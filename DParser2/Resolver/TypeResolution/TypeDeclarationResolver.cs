using System;
using System.Collections.Generic;
using System.Linq;
using D_Parser.Dom;
using D_Parser.Dom.Expressions;
using D_Parser.Dom.Statements;
using D_Parser.Parser;
using D_Parser.Resolver.ASTScanner;
using D_Parser.Resolver.ExpressionSemantics;
using D_Parser.Resolver.Templates;
using System.Collections.ObjectModel;

namespace D_Parser.Resolver.TypeResolution
{
	public static class TypeDeclarationResolver
	{
		/// <summary>
		/// Used for searching further identifier list parts.
		/// 
		/// a.b -- nextIdentifier would be 'b' whereas <param name="resultBases">resultBases</param> contained the resolution result for 'a'
		/// </summary>
		public static AbstractType[] ResolveFurtherTypeIdentifier(int nextIdentifierHash,
			AbstractType resultBases,
			ResolutionContext ctxt,
			ISyntaxRegion typeIdObject = null, bool ufcsItem = true)
		{
			MemberSymbol statProp;
			if ((resultBases = DResolver.StripMemberSymbols(resultBases)) == null)
				return null;

			var r = new List<AbstractType>();

			foreach(var b_ in AmbiguousType.TryDissolve(resultBases))
			{
				var b = TryPostDeduceAliasDefinition(b_, typeIdObject, ctxt);

				if (b is PointerType)
					b = (b as DerivedDataType).Base;

				if (b is UserDefinedType)
				{
					var udt = b as UserDefinedType;

					using (b is MixinTemplateType || udt is TemplateType ? ctxt.Push(udt) : null)
					{
						r.AddRange(SingleNodeNameScan.SearchChildrenAndResolve(ctxt, udt, nextIdentifierHash, typeIdObject));

						TemplateParameterSymbol[] dedTypes = null;
						foreach (var t in r)
						{
							var ds = t as DSymbol;
							if (ds != null && !ds.HasDeducedTypes)
							{
								if (dedTypes == null)
									dedTypes = ctxt.DeducedTypesInHierarchy.ToArray();

								if(dedTypes.Length != 0)
									ds.SetDeducedTypes(dedTypes);
							}
						}

						statProp = StaticProperties.TryEvalPropertyType(ctxt, b, nextIdentifierHash);
						if (statProp != null)
							r.Add(statProp);

						// go the opDispatch way if possible - http://dlang.org/operatoroverloading.html#Dispatch
						if (r.Count == 0 && nextIdentifierHash != OpDispatchResolution.opDispatchId)
							r.AddRange(OpDispatchResolution.TryResolveFurtherIdViaOpDispatch(ctxt, nextIdentifierHash, udt, typeIdObject));

						if (r.Count == 0 && ufcsItem)
							r.AddRange(UFCSResolver.TryResolveUFCS(b, nextIdentifierHash, ctxt.ScopedBlock != udt.Definition && typeIdObject != null ? typeIdObject.Location : ctxt.ScopedBlock.BlockStartLocation, ctxt, typeIdObject));
					}
				}
				else if (b is PackageSymbol)
				{
					var pack = (b as PackageSymbol).Package;

					var accessedModule = pack.GetModule(nextIdentifierHash);
					if (accessedModule != null)
						r.Add(new ModuleSymbol(accessedModule, b as PackageSymbol));
					else if ((pack = pack.GetPackage(nextIdentifierHash)) != null)
						r.Add(new PackageSymbol(pack));
				}
				else if (b is ModuleSymbol)
					r.AddRange(SingleNodeNameScan.SearchChildrenAndResolve(ctxt, b as ModuleSymbol, nextIdentifierHash, typeIdObject));
				else
				{
					statProp = StaticProperties.TryEvalPropertyType(ctxt, b, nextIdentifierHash);
					if (statProp != null)
						r.Add(statProp);

					if(r.Count == 0 && ufcsItem) // Only if there hasn't been a result yet?
						r.AddRange(UFCSResolver.TryResolveUFCS (b, nextIdentifierHash, typeIdObject != null ? typeIdObject.Location : ctxt.ScopedBlock.BlockStartLocation, ctxt, typeIdObject));
				}
			}

			return r.Count == 0 ? null : r.ToArray();
		}

		class SingleResolverVisitor : TypeDeclarationVisitor<AbstractType>
		{
			readonly ResolutionContext ctxt;
			bool filterTemplates;

			public SingleResolverVisitor(ResolutionContext ctxt, bool filterTemplates)
			{
				this.filterTemplates = filterTemplates;
				this.ctxt = ctxt;
			}

			public AbstractType Visit (IdentifierDeclaration id)
			{
				return AmbiguousType.Get(ExpressionTypeEvaluation.GetOverloads (id, ctxt, null, filterTemplates));
			}

			public AbstractType Visit (DTokenDeclaration td)
			{
				var tk = td.Token;

				if (DTokens.IsBasicType(tk))
					return new PrimitiveType(tk, 0);

				return null;
			}

			public AbstractType Visit (ArrayDecl ad)
			{
				filterTemplates = true;
				var valueType = ad.ValueType != null ? ad.ValueType.Accept(this) : null;

				AbstractType keyType = null;
				int fixedArrayLength = -1;

				if (ad.KeyExpression != null)
				{
					var val = Evaluation.EvaluateValue(ad.KeyExpression, ctxt);

					if (val != null)
					{
						// It should be mostly a number only that points out how large the final array should be
						var pv = Evaluation.GetVariableContents(val, new StandardValueProvider(ctxt)) as PrimitiveValue;
						if (pv != null)
						{
							fixedArrayLength = System.Convert.ToInt32(pv.Value);

							if (fixedArrayLength < 0)
								ctxt.LogError(ad, "Invalid array size: Length value must be greater than 0");
						}
						//TODO Is there any other type of value allowed?
						else
							// Take the value's type as array key type
							keyType = val.RepresentedType;
					}
				}
				else if(ad.KeyType != null)
					keyType = ResolveSingle(ad.KeyType, ctxt);

				if (ad.KeyType == null && (ad.KeyExpression == null || fixedArrayLength >= 0)) {
					if (fixedArrayLength >= 0) {
						// D Magic: One might access tuple items directly in the pseudo array declaration - so stuff like Tup[0] i; becomes e.g. int i;
						var dtup = DResolver.StripMemberSymbols (valueType) as DTuple;
						if (dtup == null)
							return new ArrayType (valueType, fixedArrayLength);

						if (dtup.Items != null && fixedArrayLength < dtup.Items.Length)
							return AbstractType.Get(dtup.Items [fixedArrayLength]);
						else {
							ctxt.LogError (ad, "TypeTuple only consists of " + (dtup.Items != null ? dtup.Items.Length : 0) + " items. Can't access item at index " + fixedArrayLength);
							return null;
						}
					}
					return new ArrayType (valueType);
				}

				return new AssocArrayType(valueType, keyType);
			}

			public AbstractType Visit (DelegateDeclaration dg)
			{
				filterTemplates = true;
				var returnTypes = ResolveSingle(dg.ReturnType, ctxt);

				List<AbstractType> paramTypes=null;
				if(dg.Parameters!=null && 
					dg.Parameters.Count != 0)
				{	
					paramTypes = new List<AbstractType>();
					foreach(var par in dg.Parameters)
						paramTypes.Add(ResolveSingle(par.Type, ctxt));
				}

				return new DelegateType(returnTypes, dg, paramTypes);
			}

			public AbstractType Visit (PointerDecl td)
			{
				filterTemplates = true;
				var ptrBaseTypes = td.InnerDeclaration.Accept(this);

				if (ptrBaseTypes != null)
					ptrBaseTypes.NonStaticAccess = true;

				return new PointerType(ptrBaseTypes);
			}

			public AbstractType Visit (MemberFunctionAttributeDecl td)
			{
				filterTemplates = true;
				if (td.InnerType == null)
					return null;
				
				var ret = td.InnerType.Accept(this);

				if (ret == null)
					return null;// new UnknownType(attrDecl);

				ret.Modifier = td.Modifier;

				return ret;
			}

			public AbstractType Visit (TypeOfDeclaration typeOf)
			{
				filterTemplates = true;
				// typeof(return)
				if (typeOf.Expression is TokenExpression && (typeOf.Expression as TokenExpression).Token == DTokens.Return)
				{
					return HandleNodeMatch(ctxt.ScopedBlock, ctxt, null, typeOf);
				}
				// typeOf(myInt)  =>  int
				else if (typeOf.Expression != null)
				{
					var wantedTypes = ExpressionTypeEvaluation.EvaluateType(typeOf.Expression, ctxt);
					return DResolver.StripMemberSymbols(wantedTypes);
				}

				return null;
			}

			public AbstractType Visit (VectorDeclaration td)
			{
				filterTemplates = true;
				return null;
			}

			public AbstractType Visit (VarArgDecl td)
			{
				filterTemplates = true;
				return td.InnerDeclaration != null ? td.InnerDeclaration.Accept(this) : null;
			}

			public AbstractType Visit (TemplateInstanceExpression td)
			{
				if (filterTemplates)
					return ExpressionTypeEvaluation.EvaluateType (td, ctxt);
				else
					return AmbiguousType.Get (ExpressionTypeEvaluation.GetOverloads (td, ctxt));
			}
		}

		public static AbstractType ResolveSingle(ITypeDeclaration declaration, ResolutionContext ctxt, bool filterTemplates = true)
		{
			return declaration == null ? null : declaration.Accept (new SingleResolverVisitor (ctxt, filterTemplates));
		}


		#region Intermediate methods
		[ThreadStatic]
		static Dictionary<INode, int> stackCalls;

		internal static void ResetDeducedSymbols(AbstractType b)
		{
			var ds = b as DSymbol;
			if (ds != null && 
				ds.HasDeducedTypes &&
				ds.Definition.TemplateParameters != null)
			{
				var remainingTemplateSymbols = new List<TemplateParameterSymbol>(ds.DeducedTypes);

				foreach (var tp in ds.Definition.TemplateParameters)
				{
					if (tp != null)
					{
						foreach (var sym in remainingTemplateSymbols)
							if (sym.Parameter == tp)
							{
								remainingTemplateSymbols.Remove(sym);
								break;
							}
					}
				}

				ds.SetDeducedTypes(remainingTemplateSymbols);
			}
		}

		[ThreadStatic]
		static Stack<ISyntaxRegion> aliasDeductionStack = new Stack<ISyntaxRegion>();

		internal static AbstractType TryPostDeduceAliasDefinition(AbstractType b, ISyntaxRegion typeBase, ResolutionContext ctxt)
		{
			if (typeBase != null &&
				b != null && 
				b.Tag<AliasTag>(AliasTag.Id) != null && 
				(ctxt.Options & ResolutionOptions.DontResolveAliases) == 0)
			{
				if (aliasDeductionStack == null)
					aliasDeductionStack = new Stack<ISyntaxRegion>();
				else if (aliasDeductionStack.Contains(typeBase))
					return b;
				aliasDeductionStack.Push(typeBase);

				var bases = AmbiguousType.TryDissolve(b);

				//TODO: Declare alias-level context? 

				if (typeBase is TemplateInstanceExpression)
				{
					// Reset 
					foreach (var bas in bases)
						ResetDeducedSymbols(bas);

					b = AmbiguousType.Get(TemplateInstanceHandler.DeduceParamsAndFilterOverloads(bases, typeBase as TemplateInstanceExpression, ctxt, false));
				}
				else 
					b = AmbiguousType.Get(TemplateInstanceHandler.DeduceParamsAndFilterOverloads(bases, null, false, ctxt));

				aliasDeductionStack.Pop();
			}

			return b;
		}

		public class AliasTag
		{
			public const string Id = "AliasTag";
			public DVariable aliasDefinition;
			public ISyntaxRegion typeBase;
		}

		struct NodeMatchHandleVisitor : NodeVisitor<AbstractType>
		{
			public ResolutionContext ctxt;
			public bool CanResolveBase(INode m)
			{
				int stkC;
				stackCalls.TryGetValue(m, out stkC);
				return ((ctxt.Options & ResolutionOptions.DontResolveBaseTypes) != ResolutionOptions.DontResolveBaseTypes) &&
						stkC < 4 && 
						(!(m.Type is IdentifierDeclaration) || (m.Type as IdentifierDeclaration).IdHash != m.NameHash || m.Type.InnerDeclaration != null); // pretty rough and incomplete SO prevention hack
			}
			public ISyntaxRegion typeBase;
			public AbstractType resultBase;


			public NodeMatchHandleVisitor(ResolutionContext ctxt, ISyntaxRegion typeBase, AbstractType resultBase)
			{
				this.ctxt = ctxt;
				this.typeBase = typeBase;
				this.resultBase = resultBase;
			}


			public AbstractType Visit(DEnumValue n)
			{
				return new MemberSymbol(n, resultBase ?? HandleNodeMatch(n.Parent, ctxt));
			}

			AbstractType VisitAliasDefinition(DVariable v)
			{
				AbstractType bt;

				// Ignore explicitly set resolution restrictions - aliases must be resolved!
				if (!CanResolveBase(v) &&
					(ctxt.Options & ResolutionOptions.DontResolveBaseTypes) == 0)
					return new AliasedType(v, null);

				// Is it really that easy?
				var optBackup = ctxt.CurrentContext.ContextDependentOptions;
				ctxt.CurrentContext.ContextDependentOptions |= ResolutionOptions.ReturnMethodReferencesOnly;
				if (v.Type is IdentifierDeclaration)
					ctxt.CurrentContext.ContextDependentOptions |= ResolutionOptions.NoTemplateParameterDeduction;



				bt = TypeDeclarationResolver.ResolveSingle(v.Type, ctxt);

				ctxt.CurrentContext.ContextDependentOptions = optBackup;

				// For auto variables, use the initializer to get its type
				if (bt == null && v.Initializer != null)
					bt = DResolver.StripMemberSymbols(ExpressionTypeEvaluation.EvaluateType(v.Initializer, ctxt));

				// Check if inside an foreach statement header
				if (bt == null)
					bt = GetForeachIteratorType(v);

				if (bt == null)
					return new AliasedType(v, null);

				bt.Tag(AliasTag.Id, new AliasTag { aliasDefinition = v, typeBase = typeBase });
				return bt;
			}

			public AbstractType Visit(DVariable variable)
			{
				if (variable.IsAlias)
					return VisitAliasDefinition(variable);
				AbstractType bt;

				if (CanResolveBase(variable))
				{
					bt = TypeDeclarationResolver.ResolveSingle(variable.Type, ctxt);

					// For auto variables, use the initializer to get its type
					if (bt == null && variable.Initializer != null)
						bt = DResolver.StripMemberSymbols(ExpressionTypeEvaluation.EvaluateType(variable.Initializer, ctxt));

					// Check if inside an foreach statement header
					if (bt == null)
						bt = GetForeachIteratorType(variable);
				}
				else
					bt = null;

				return new MemberSymbol(variable, bt);
			}

			/// <summary>
			/// string[] s;
			/// 
			/// foreach(i;s)
			/// {
			///		// i is of type 'string'
			///		writeln(i);
			/// }
			/// </summary>
			public AbstractType GetForeachIteratorType(DVariable i)
			{
				var r = new List<AbstractType>();
				var curMethod = ctxt.ScopedBlock as DMethod;
				var loc = ctxt.CurrentContext.Caret;
				loc = new CodeLocation(loc.Column-1, loc.Line); // SearchStmtDeeplyAt only checks '<' EndLocation, we may need to have '<=' though due to completion offsets.
				var curStmt = curMethod != null ? DResolver.SearchStatementDeeplyAt(curMethod.GetSubBlockAt(ctxt.CurrentContext.Caret), loc) : null;

				if (curStmt == null)
					return null;

				bool init = true;
				// Walk up statement hierarchy -- note that foreach loops can be nested
				while (curStmt != null)
				{
					if (init)
						init = false;
					else
						curStmt = curStmt.Parent;

					if (curStmt is ForeachStatement)
					{
						var fe = (ForeachStatement)curStmt;

						if (fe.ForeachTypeList == null)
							continue;

						// If the searched variable is declared in the header
						int iteratorIndex = -1;

						for (int j = 0; j < fe.ForeachTypeList.Length; j++)
							if (fe.ForeachTypeList[j] == i)
							{
								iteratorIndex = j;
								break;
							}

						if (iteratorIndex == -1)
							continue;

						bool keyIsSearched = iteratorIndex == 0 && fe.ForeachTypeList.Length > 1;


						// foreach(var k, var v; 0 .. 9)
						if (keyIsSearched && fe.IsRangeStatement)
						{
							// -- it's static type int, of course(?)
							return new PrimitiveType(DTokens.Int);
						}

						if (fe.Aggregate == null)
							return null;

						var aggregateType = ExpressionTypeEvaluation.EvaluateType(fe.Aggregate, ctxt);

						aggregateType = DResolver.StripMemberSymbols(aggregateType);

						if (aggregateType == null)
							return null;

						// The most common way to do a foreach
						if (aggregateType is AssocArrayType)
						{
							var ar = (AssocArrayType)aggregateType;

							return keyIsSearched ? ar.KeyType : ar.ValueType;
						}
						else if (aggregateType is PointerType)
							return keyIsSearched ? TypeDeclarationResolver.ResolveSingle(new IdentifierDeclaration("size_t"), ctxt, false /* Generally, size_t isn't templated or such..so for performance, step through additional filtering */) : (aggregateType as PointerType).Base;
						else if (aggregateType is UserDefinedType)
						{
							var tr = (UserDefinedType)aggregateType;

							if (keyIsSearched || !(tr.Definition is IBlockNode))
								continue;

							bool foundIterPropertyMatch = false;
							#region Foreach over Structs and Classes with Ranges

							// Enlist all 'back'/'front' members
							var iterPropertyTypes = new List<AbstractType>();

							foreach (var n in (IBlockNode)tr.Definition)
								if (fe.IsReverse ? n.Name == "back" : n.Name == "front")
									iterPropertyTypes.Add(HandleNodeMatch(n, ctxt));

							foreach (var iterPropType in iterPropertyTypes)
								if (iterPropType is MemberSymbol)
								{
									foundIterPropertyMatch = true;

									var itp = (MemberSymbol)iterPropType;

									// Only take non-parameterized methods
									if (itp.Definition is DMethod && ((DMethod)itp.Definition).Parameters.Count != 0)
										continue;

									// Handle its base type [return type] as iterator type
									if (itp.Base != null)
										r.Add(itp.Base);

									foundIterPropertyMatch = true;
								}

							if (foundIterPropertyMatch)
								continue;
							#endregion

							#region Foreach over Structs and Classes with opApply
							iterPropertyTypes.Clear();
							r.Clear();

							foreach (var n in (IBlockNode)tr.Definition)
								if (n is DMethod &&
									(fe.IsReverse ? n.Name == "opApplyReverse" : n.Name == "opApply"))
									iterPropertyTypes.Add(HandleNodeMatch(n, ctxt));

							foreach (var iterPropertyType in iterPropertyTypes)
								if (iterPropertyType is MemberSymbol)
								{
									var mr = (MemberSymbol)iterPropertyType;
									var dm = mr.Definition as DMethod;

									if (dm == null || dm.Parameters.Count != 1)
										continue;

									var dg = dm.Parameters[0].Type as DelegateDeclaration;

									if (dg == null || dg.Parameters.Count != fe.ForeachTypeList.Length)
										continue;

									var paramType = ResolveSingle(dg.Parameters[iteratorIndex].Type, ctxt);

									if (paramType != null)
										r.Add(paramType);
								}
							#endregion
						}

						return AmbiguousType.Get(r);
					}
				}

				return null;
			}

			/// <summary>
			/// Add 'superior' template parameters to the current symbol because 
			/// the parameters might be re-used in the nested class.
			/// Only pays attention to those parameter symbols that are located in the current resolution scope's AST hierarchy.
			/// </summary>
			IEnumerable<TemplateParameterSymbol> GetInvisibleTypeParameters(DNode n)
			{
				ContextFrame prev = null;
				foreach (var cf in ctxt.ContextStack) {
					// Only stay in the same AST hierarchy
					if (prev != null && cf.ScopedBlock != null && cf.ScopedBlock.Parent != prev.ScopedBlock)
						yield break;
					prev = cf;

					foreach (var kv in cf.DeducedTemplateParameters)
						if (!n.ContainsTemplateParameter (kv.Value.Parameter))
							yield return kv.Value;
				}
			}

			public AbstractType Visit(EponymousTemplate ep)
			{
				return new EponymousTemplateType(ep, new ReadOnlyCollection<TemplateParameterSymbol>(new List<TemplateParameterSymbol>(GetInvisibleTypeParameters(ep))));
			}

			public AbstractType Visit(DMethod m)
			{
				return new MemberSymbol(m, CanResolveBase(m) ? GetMethodReturnType(m, ctxt) : null, GetInvisibleTypeParameters(m));
			}

			public AbstractType Visit(DClassLike dc)
			{
				var invisibleTypeParams = GetInvisibleTypeParameters(dc);

				switch (dc.ClassType)
				{
					case DTokens.Struct:
						return new StructType(dc, null, invisibleTypeParams);

					case DTokens.Union:
						return new UnionType(dc, invisibleTypeParams);

					case DTokens.Interface:
					case DTokens.Class:
					return DResolver.ResolveClassOrInterface(dc, ctxt, typeBase, false, invisibleTypeParams.ToList());

					case DTokens.Template:
						if (dc.ContainsAttribute(DTokens.Mixin))
							return new MixinTemplateType(dc, invisibleTypeParams);
						return new TemplateType(dc, invisibleTypeParams);

					default:
						ctxt.LogError(new NothingFoundError(dc, "Unknown type (" + DTokens.GetTokenString(dc.ClassType) + ")"));
						return null;
				}
			}

			public AbstractType Visit(DEnum de)
			{
				AbstractType bt;

				if (de.Type == null)
					bt = new PrimitiveType(DTokens.Int);
				else
				{
					using(ctxt.Push(de.Parent))
						bt = TypeDeclarationResolver.ResolveSingle(de.Type, ctxt);
				}

				return new EnumType(de, bt);
			}

			public AbstractType Visit(DModule mod)
			{
				if (typeBase != null && typeBase.ToString() != mod.ModuleName)
				{
					var pack = ctxt.ParseCache.LookupPackage(ctxt.ScopedBlock, typeBase.ToString()).FirstOrDefault();
					if (pack != null)
						return new PackageSymbol(pack);
				}
				
				return new ModuleSymbol(mod);
			}

			public AbstractType Visit(DBlockNode dBlockNode)
			{
				throw new NotImplementedException();
			}

			public AbstractType Visit(TemplateParameter.Node tpn)
			{
				TemplateParameterSymbol tpnBase;

				if (ctxt.GetTemplateParam(tpn.NameHash, out tpnBase) && tpnBase.Parameter == tpn.TemplateParameter)
					return tpnBase;

				AbstractType baseType;
				//TODO: What if there are like nested default constructs like (T = U*, U = int) ?
				var ttp = tpn.TemplateParameter as TemplateTypeParameter;
				if (CanResolveBase(tpn) && ttp != null && (ttp.Default != null || ttp.Specialization != null))
					baseType = TypeDeclarationResolver.ResolveSingle(ttp.Default ?? ttp.Specialization, ctxt);
				else
					baseType = null;

				return new TemplateParameterSymbol(tpn, baseType);
			}

			public AbstractType Visit(NamedTemplateMixinNode n)
			{
				return Visit(n as DVariable);
			}

			// Only import symbol aliases are allowed to search in the parse cache
			public AbstractType Visit(ImportSymbolNode importSymbolNode)
			{
				return VisitAliasDefinition(importSymbolNode);

				AbstractType ret = null;

				var modAlias = importSymbolNode is ModuleAliasNode;
				if (modAlias ? importSymbolNode.Type != null : importSymbolNode.Type.InnerDeclaration != null)
				{
					var mods = new List<DModule>();
					var td = modAlias ? importSymbolNode.Type : importSymbolNode.Type.InnerDeclaration;
					foreach (var mod in ctxt.ParseCache.LookupModuleName(importSymbolNode, td.ToString()))
						mods.Add(mod);
					if (mods.Count == 0)
						ctxt.LogError(new NothingFoundError(importSymbolNode.Type));
					else
						if (mods.Count > 1)
						{
							var m__ = new List<ISemantic>();
							foreach (var mod in mods)
								m__.Add(new ModuleSymbol(mod));
							ctxt.LogError(new AmbiguityError(importSymbolNode.Type, m__));
						}
					var bt = mods.Count != 0 ? (AbstractType)new ModuleSymbol(mods[0]) : null;
					//TODO: Is this correct behaviour?
					if (!modAlias)
					{
						bt = AmbiguousType.Get(ResolveFurtherTypeIdentifier(importSymbolNode.Type.ToString(false).GetHashCode(), bt, ctxt, importSymbolNode.Type));
					}
					ret = new AliasedType(importSymbolNode, bt);
				}
				return ret;
			}

			public AbstractType Visit(ModuleAliasNode moduleAliasNode)
			{
				return Visit(moduleAliasNode as ImportSymbolNode);
			}

			public AbstractType Visit(ImportSymbolAlias importSymbolAlias)
			{
				return Visit(importSymbolAlias as ImportSymbolNode);
			}

			#region Attributes etc.
			public AbstractType VisitAttribute(Modifier attr)
			{
				throw new NotImplementedException();
			}

			public AbstractType VisitAttribute(DeprecatedAttribute a)
			{
				throw new NotImplementedException();
			}

			public AbstractType VisitAttribute(PragmaAttribute attr)
			{
				throw new NotImplementedException();
			}

			public AbstractType VisitAttribute(BuiltInAtAttribute a)
			{
				throw new NotImplementedException();
			}

			public AbstractType VisitAttribute(UserDeclarationAttribute a)
			{
				throw new NotImplementedException();
			}

			public AbstractType VisitAttribute(VersionCondition a)
			{
				throw new NotImplementedException();
			}

			public AbstractType VisitAttribute(DebugCondition a)
			{
				throw new NotImplementedException();
			}

			public AbstractType VisitAttribute(StaticIfCondition a)
			{
				throw new NotImplementedException();
			}

			public AbstractType VisitAttribute(NegatedDeclarationCondition a)
			{
				throw new NotImplementedException();
			}
			#endregion
		}

		/// <summary>
		/// The variable's or method's base type will be resolved (if auto type, the intializer's type will be taken).
		/// A class' base class will be searched.
		/// etc..
		/// </summary>
		public static AbstractType HandleNodeMatch(
			INode m,
			ResolutionContext ctxt,
			AbstractType resultBase = null,
			ISyntaxRegion typeBase = null)
		{
			// See https://github.com/aBothe/Mono-D/issues/161
			int stkC;

			if (stackCalls == null)
			{
				stackCalls = new Dictionary<INode, int>();
				stackCalls[m] = 1;
			}
			else
				stackCalls[m] = stackCalls.TryGetValue(m, out stkC) ? ++stkC : 1;

			/*
			 * Pushing a new scope is only required if current scope cannot be found in the handled node's hierarchy.
			 * Edit: No, it is required nearly[1] every time because of nested type declarations - then, we do need the
			 * current block scope.
			 * [1] Edit2: Not when we are currently handling method parameters.
			 */
			var options = ctxt.CurrentContext.ContextDependentOptions;
			var applyOptions = ctxt.ScopedBlockIsInNodeHierarchy(m);
			IDisposable disp;
			CodeLocation loc = typeBase != null ? typeBase.Location : m.Location;

			if (resultBase is DSymbol)
				disp = ctxt.Push (resultBase as DSymbol, loc);
			else
				disp = ctxt.Push (m, loc);

			AbstractType ret;
			using (disp)
			{
				if (applyOptions)
					ctxt.CurrentContext.ContextDependentOptions = options;

				ret = m.Accept(new NodeMatchHandleVisitor(ctxt, typeBase, resultBase));
			}

			stackCalls.TryGetValue(m, out stkC);
			if (stkC == 1)
				stackCalls.Remove(m);
			else
				stackCalls[m] = stkC-1;

			return ret;
		}

		public static AbstractType[] HandleNodeMatches(
			IEnumerable<INode> matches,
			ResolutionContext ctxt,
			AbstractType resultBase = null,
			ISyntaxRegion typeDeclaration = null)
		{
			// Abbreviate a foreach-loop + List alloc
			var ll = matches as IList<INode>;
			if (ll != null && ll.Count == 1)
				return new[] { ll[0] == null ? null : HandleNodeMatch(ll[0], ctxt, resultBase, typeDeclaration) };

			if (matches == null)
				return new AbstractType[0];

			var rl = new List<AbstractType>();

			foreach (var m in matches)
			{
				if (m == null)
					continue;

				var res = HandleNodeMatch(m, ctxt, resultBase, typeDeclaration);
				if (res != null)
					rl.Add(res);
			}
			
			return rl.ToArray();
		}

		public static AbstractType GetMethodReturnType(DelegateType dg, ResolutionContext ctxt)
		{
			if (dg == null || ctxt == null)
				return null;

			if (dg.IsFunctionLiteral)
				return GetMethodReturnType(((FunctionLiteral)dg.delegateTypeBase).AnonymousMethod, ctxt);
			
			return ResolveSingle(((DelegateDeclaration)dg.delegateTypeBase).ReturnType, ctxt);
		}

		public static AbstractType GetMethodReturnType(DMethod method, ResolutionContext ctxt)
		{
			AbstractType returnType;

			if ((ctxt.Options & ResolutionOptions.DontResolveBaseTypes) == ResolutionOptions.DontResolveBaseTypes)
				return null;

			/*
			 * If a method's type equals null, assume that it's an 'auto' function..
			 * 1) Search for a return statement
			 * 2) Resolve the returned expression
			 * 3) Use that one as the method's type
			 */
			if (method.Type != null)
			{
				using (ctxt.Push(method)) //FIXME: Is it legal to explicitly return a nested type?
					returnType = TypeDeclarationResolver.ResolveSingle(method.Type, ctxt);

				if (returnType != null)
					returnType.NonStaticAccess = true;

				return returnType;
			}
			else if (method.Body != null)
			{
				ReturnStatement returnStmt = null;
				var list = new List<IStatement> { method.Body };
				var list2 = new List<IStatement>();

				bool foundMatch = false;
				while (!foundMatch && list.Count > 0)
				{
					foreach (var stmt in list)
					{
						if (stmt is ReturnStatement)
						{
							returnStmt = stmt as ReturnStatement;

							var te = returnStmt.ReturnExpression as TokenExpression;
							if (te == null || te.Token != DTokens.Null)
							{
								foundMatch = true;
								break;
							}
						}

						var statementContainingStatement = stmt as StatementContainingStatement;
						if (statementContainingStatement != null)
							list2.AddRange(statementContainingStatement.SubStatements);
					}

					list = list2;
					list2 = new List<IStatement>();
				}

				if (returnStmt != null && returnStmt.ReturnExpression != null)
				{
					using (ctxt.Push(method, returnStmt.Location, true))
						returnType = DResolver.StripMemberSymbols(ExpressionTypeEvaluation.EvaluateType(returnStmt.ReturnExpression, ctxt));

					if (returnType != null)
						returnType.NonStaticAccess = true;

					return returnType;
				}

				return new PrimitiveType (DTokens.Void);
			}

			return null;
		}
		#endregion
	}
}
