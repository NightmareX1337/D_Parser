﻿using System.Collections.Generic;
using System.Linq;
using D_Parser.Dom;
using D_Parser.Dom.Expressions;
using D_Parser.Resolver.Templates;
using D_Parser.Resolver.ExpressionSemantics;
using System.Diagnostics;

namespace D_Parser.Resolver.TypeResolution
{
	public static class TemplateInstanceHandler
	{
		public static List<ISemantic> PreResolveTemplateArgs(TemplateInstanceExpression tix, ResolutionContext ctxt)
		{
			// Resolve given argument expressions
			var templateArguments = new List<ISemantic>();

			if (tix != null && tix.Arguments!=null)
				foreach (var arg in tix.Arguments)
				{
					if (arg is TypeDeclarationExpression)
					{
						var tde = (TypeDeclarationExpression)arg;

						var res = TypeDeclarationResolver.ResolveSingle(tde.Declaration, ctxt);

						// Might be a simple symbol without any applied template arguments that is then passed to an template alias parameter
						if (res == null && tde.Declaration is IdentifierDeclaration)
							res = TypeDeclarationResolver.ResolveSingle(tde.Declaration, ctxt, false);

						var amb = res as AmbiguousType;
						if (amb != null)
						{
							// Error
							res = amb.Overloads[0];
						}

						var mr = res as MemberSymbol;
						if (mr != null && mr.Definition is DVariable)
						{
							var dv = (DVariable)mr.Definition;

							if (dv.IsAlias || dv.Initializer == null)
							{
								templateArguments.Add(mr);
								continue;
							}

							ISemantic eval = null;

							try
							{
								eval = new StandardValueProvider(ctxt)[dv];
							}
							catch(System.Exception ee) // Should be a non-const-expression error here only
							{
								ctxt.LogError(dv.Initializer, ee.Message);
							}

							templateArguments.Add(eval ?? (ISemantic)mr);
						}
						else
							templateArguments.Add(res);
					}
					else
					{
						ISemantic v = Evaluation.EvaluateValue(arg, ctxt, true);
						if (v is VariableValue)
						{
							var vv = v as VariableValue;
							if (vv.Variable.IsConst && vv.Variable.Initializer != null)
								v = Evaluation.EvaluateValue(vv, new StandardValueProvider(ctxt));
						}
						
						v = DResolver.StripValueTypeWrappers(v);
						templateArguments.Add(v);
					}
				}

			return templateArguments;
		}
		
		internal static bool IsNonFinalArgument(ISemantic v)
		{
			return (v is TypeValue && (v as TypeValue).RepresentedType is TemplateParameterSymbol) ||
				(v is TemplateParameterSymbol && (v as TemplateParameterSymbol).Base == null) ||
				v is ErrorValue;
		}

		public static List<AbstractType> DeduceParamsAndFilterOverloads(IEnumerable<AbstractType> rawOverloadList,
			TemplateInstanceExpression templateInstanceExpr,
			ResolutionContext ctxt, bool isMethodCall = false)
		{
			var args = PreResolveTemplateArgs(templateInstanceExpr, ctxt);
			return DeduceParamsAndFilterOverloads(rawOverloadList, args, isMethodCall, ctxt);
		}

		/// <summary>
		/// Associates the given arguments with the template parameters specified in the type/method declarations 
		/// and filters out unmatching overloads.
		/// </summary>
		/// <param name="rawOverloadList">Can be either type results or method results</param>
		/// <param name="givenTemplateArguments">A list of already resolved arguments passed explicitly 
		/// in the !(...) section of a template instantiation 
		/// or call arguments given in the (...) appendix 
		/// that follows a method identifier</param>
		/// <param name="isMethodCall">If true, arguments that exceed the expected parameter count will be ignored as far as all parameters could be satisfied.</param>
		/// <param name="ctxt"></param>
		/// <returns>A filtered list of overloads which mostly fit to the specified arguments.
		/// Usually contains only 1 element.
		/// The 'TemplateParameters' property of the results will be also filled for further usage regarding smart completion etc.</returns>
		public static List<AbstractType> DeduceParamsAndFilterOverloads(IEnumerable<AbstractType> rawOverloadList,
					IEnumerable<ISemantic> givenTemplateArguments,
					bool isMethodCall,
					ResolutionContext ctxt)
		{
			if (rawOverloadList == null)
				return null;

			var unfilteredOverloads = DeduceOverloads(rawOverloadList, givenTemplateArguments, isMethodCall, ctxt);

			IEnumerable<AbstractType> preFilteredOverloads;

			// If there are >1 overloads, filter from most to least specialized template param
			if (unfilteredOverloads.Count > 1)
				preFilteredOverloads = SpecializationOrdering.FilterFromMostToLeastSpecialized(unfilteredOverloads, ctxt);
			else if (unfilteredOverloads.Count == 1)
				preFilteredOverloads = unfilteredOverloads;
			else
				return null;

			var templateConstraintFilteredOverloads = new List<AbstractType>();
			foreach (var overload in preFilteredOverloads)
			{
				var ds = overload as DSymbol;
				if (ds != null && ds.Definition.TemplateConstraint != null)
				{
					ctxt.CurrentContext.IntroduceTemplateParameterTypes(ds);
					try
					{
						var v = Evaluation.EvaluateValue(ds.Definition.TemplateConstraint, ctxt);
						if (v is VariableValue)
							v = new StandardValueProvider(ctxt)[((VariableValue)v).Variable];
						if (!Evaluation.IsFalseZeroOrNull(v))
							templateConstraintFilteredOverloads.Add(ds);
					}
					catch { } //TODO: Handle eval exceptions
					ctxt.CurrentContext.RemoveParamTypesFromPreferredLocals(ds);
				}
				else
					templateConstraintFilteredOverloads.Add(overload);
			}
			if (templateConstraintFilteredOverloads.Count == 0)
				return null;

			var implicitPropertiesOrEponymousTemplatesOrOther = new List<AbstractType>();
			foreach(var t in templateConstraintFilteredOverloads)
			{
				if (t is TemplateType)
				{
					var implicitProperties = TryGetImplicitProperty(t as TemplateType, ctxt);
					if (implicitProperties.Count > 0)
						implicitPropertiesOrEponymousTemplatesOrOther.AddRange(implicitProperties);
					else
						implicitPropertiesOrEponymousTemplatesOrOther.AddRange(templateConstraintFilteredOverloads);
				}
				else if (t is EponymousTemplateType)
				{
					var eponymousResolvee = DeduceEponymousTemplate(t as EponymousTemplateType, ctxt);
					if(eponymousResolvee != null)
						implicitPropertiesOrEponymousTemplatesOrOther.Add(eponymousResolvee);
				}
				else
					implicitPropertiesOrEponymousTemplatesOrOther.Add(t);
			}

			return implicitPropertiesOrEponymousTemplatesOrOther;
		}

		static List<AbstractType> TryGetImplicitProperty(TemplateType template, ResolutionContext ctxt)
		{
			// Get actual overloads
			var matchingChild = TypeDeclarationResolver.ResolveFurtherTypeIdentifier( template.NameHash, template, ctxt, null, false);

			if (matchingChild != null) // Currently requried for proper UFCS resolution - sustain template's Tag
				foreach (var ch in matchingChild)
				{
					var ds = ch as DSymbol;
					if (ds != null)
					{
						var newDeducedTypes = new DeducedTypeDictionary(ds);
						foreach (var tps in template.DeducedTypes)
							newDeducedTypes[tps.Parameter] = tps;
						ds.SetDeducedTypes(newDeducedTypes);
					}
					ch.AssignTagsFrom(template);
				}

			return matchingChild;
		}

		static AbstractType DeduceEponymousTemplate(EponymousTemplateType ept, ResolutionContext ctxt)
		{
			if (ept.Definition.Initializer == null &&
				ept.Definition.Type == null) {
				ctxt.LogError(ept.Definition, "Can't deduce type from empty initializer!");
				return null;
			}

			// Introduce the deduced params to the current resolution context
			ctxt.CurrentContext.IntroduceTemplateParameterTypes(ept);

			// Get actual overloads
			AbstractType deducedType = null;
			var def = ept.Definition;
			deducedType = new MemberSymbol(def, def.Type != null ? 
				TypeDeclarationResolver.ResolveSingle(def.Type, ctxt) :
				ExpressionTypeEvaluation.EvaluateType(def.Initializer, ctxt), ept.DeducedTypes); //ept; //ExpressionTypeEvaluation.EvaluateType (ept.Definition.Initializer, ctxt);

			deducedType.AssignTagsFrom(ept); // Currently requried for proper UFCS resolution - sustain ept's Tags

			// Undo context-related changes
			ctxt.CurrentContext.RemoveParamTypesFromPreferredLocals(ept);

			return deducedType;
		}

		private static List<AbstractType> DeduceOverloads(
			IEnumerable<AbstractType> rawOverloadList, 
			IEnumerable<ISemantic> givenTemplateArguments, 
			bool isMethodCall,
			ResolutionContext ctxt)
		{
			bool hasTemplateArgsPassed = givenTemplateArguments != null && givenTemplateArguments.FirstOrDefault() != null;

			var filteredOverloads = new List<AbstractType>();

			if (rawOverloadList == null)
				return filteredOverloads;

			foreach (var o in rawOverloadList)
			{
				TypeDeclarationResolver.AliasTag aliasTag;

				var overload = o as DSymbol;
				while (overload is TemplateParameterSymbol)
					overload = overload.Base as DSymbol;
				if (overload == null)
				{
					if (!hasTemplateArgsPassed)
						filteredOverloads.Add(o);
					continue;
				}
				else if ((aliasTag = overload.Tag<TypeDeclarationResolver.AliasTag>(TypeDeclarationResolver.AliasTag.Id)) != null && 
					(hasTemplateArgsPassed || !(aliasTag.aliasDefinition.Type is TemplateInstanceExpression)))
					TypeDeclarationResolver.ResetDeducedSymbols(overload);

				var tplNode = overload.Definition;

				// Generically, the node should never be null -- except for TemplateParameterNodes that encapsule such params
				if (tplNode == null)
				{
					filteredOverloads.Add(o);
					continue;
				}

				bool ignoreOtherOverloads;
				var hook = D_Parser.Resolver.ResolutionHooks.HookRegistry.TryDeduce(overload, givenTemplateArguments, out ignoreOtherOverloads);
				if (hook != null)
				{
					filteredOverloads.Add(hook);
					if (ignoreOtherOverloads)
						break;
					continue;
				}

				// If the type or method has got no template parameters and if there were no args passed, keep it - it's legit.
				if (tplNode.TemplateParameters == null)
				{
					if (!hasTemplateArgsPassed || isMethodCall)
						filteredOverloads.Add(o);
					continue;
				}

				var deducedTypes = new DeducedTypeDictionary(overload);

				if (deducedTypes.AllParamatersSatisfied) // Happens e.g. after resolving a class/interface definition
					filteredOverloads.Add(o);
				else if (DeduceParams(givenTemplateArguments, isMethodCall, ctxt, overload, tplNode, deducedTypes))
				{
					overload.SetDeducedTypes(deducedTypes); // Assign calculated types to final result
					filteredOverloads.Add(o);
				}
				else
					overload.SetDeducedTypes(null);
			}
			return filteredOverloads;
		}

		internal static bool DeduceParams(IEnumerable<ISemantic> givenTemplateArguments, 
			bool isMethodCall, 
			ResolutionContext ctxt, 
			DSymbol overload, 
			DNode tplNode,
			DeducedTypeDictionary deducedTypes)
		{
			bool isLegitOverload = true;

			var argEnum = givenTemplateArguments.GetEnumerator();
			if(tplNode.TemplateParameters != null)
				foreach (var expectedParam in tplNode.TemplateParameters)
					if (!DeduceParam(ctxt, overload, deducedTypes, argEnum, expectedParam))
					{
						isLegitOverload = false;
						break; // Don't check further params if mismatch has been found
					}

			if (!isMethodCall && argEnum.MoveNext())
			{
				// There are too many arguments passed - discard this overload
				isLegitOverload = false;
			}
			return isLegitOverload;
		}

		private static bool DeduceParam(ResolutionContext ctxt, 
			DSymbol overload, 
			DeducedTypeDictionary deducedTypes,
			IEnumerator<ISemantic> argEnum, 
			TemplateParameter expectedParam)
		{
			if (expectedParam is TemplateThisParameter && overload != null && overload.Base != null)
			{
				var ttp = (TemplateThisParameter)expectedParam;

				// Get the type of the type of 'this' - so of the result that is the overload's base
				var t = DResolver.StripMemberSymbols(overload.Base);

				if (t == null)
					return false;

				//TODO: Still not sure if it's ok to pass a type result to it 
				// - looking at things like typeof(T) that shall return e.g. const(A) instead of A only.

				if (!CheckAndDeduceTypeAgainstTplParameter(ttp, t, deducedTypes, ctxt))
					return false;

				return true;
			}

			// Used when no argument but default arg given
			bool useDefaultType = false;
			if (argEnum.MoveNext() || (useDefaultType = HasDefaultType(expectedParam)))
			{
				if (!useDefaultType)
				{
					// On tuples, take all following arguments and pass them to the check function
					if (expectedParam is TemplateTupleParameter)
					{
						var tupleItems = new List<ISemantic>();
						// A tuple must at least contain one item!
						tupleItems.Add(argEnum.Current);
						while (argEnum.MoveNext())
							tupleItems.Add(argEnum.Current);

						if (!CheckAndDeduceTypeTuple((TemplateTupleParameter)expectedParam, tupleItems, deducedTypes, ctxt))
							return false;
					}
					else if (argEnum.Current != null)
					{
						if (!CheckAndDeduceTypeAgainstTplParameter(expectedParam, argEnum.Current, deducedTypes, ctxt))
							return false;
					}
					else
						return false;
				}
				else if (CheckAndDeduceTypeAgainstTplParameter(expectedParam, null, deducedTypes, ctxt))
				{
					// It's legit - just do nothing
				}
				else
					return false;
			}
			else if(expectedParam is TemplateTupleParameter)
			{
				if(!CheckAndDeduceTypeTuple(expectedParam as TemplateTupleParameter, null, deducedTypes, ctxt))
					return false;
			}
			// There might be too few args - but that doesn't mean that it's not correct - it's only required that all parameters got satisfied with a type
			else if (!deducedTypes.AllParamatersSatisfied)
				return false;

			return true;
		}

		public static bool HasDefaultType(TemplateParameter p)
		{
			if (p is TemplateTypeParameter)
				return ((TemplateTypeParameter)p).Default != null;
			else if (p is TemplateAliasParameter)
			{
				var ap = (TemplateAliasParameter)p;
				return ap.DefaultExpression != null || ap.DefaultType != null;
			}
			else if (p is TemplateThisParameter)
				return HasDefaultType(((TemplateThisParameter)p).FollowParameter);
			else if (p is TemplateValueParameter)
				return ((TemplateValueParameter)p).DefaultExpression != null;
			return false;
		}

		static bool CheckAndDeduceTypeAgainstTplParameter(TemplateParameter handledParameter, 
			ISemantic argumentToCheck,
			DeducedTypeDictionary deducedTypes,
			ResolutionContext ctxt)
		{
			return new Templates.TemplateParameterDeduction(deducedTypes, ctxt).Handle(handledParameter, argumentToCheck);
		}

		static bool CheckAndDeduceTypeTuple(TemplateTupleParameter tupleParameter, 
			IEnumerable<ISemantic> typeChain,
			DeducedTypeDictionary deducedTypes,
			ResolutionContext ctxt)
		{
			return new Templates.TemplateParameterDeduction(deducedTypes,ctxt).Handle(tupleParameter,typeChain);
		}
	}
}
