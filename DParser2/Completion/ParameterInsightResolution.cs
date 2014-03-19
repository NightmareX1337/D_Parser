using System.Collections.Generic;
using D_Parser.Dom;
using D_Parser.Dom.Expressions;
using D_Parser.Dom.Statements;
using D_Parser.Resolver;
using D_Parser.Resolver.ExpressionSemantics;
using D_Parser.Resolver.TypeResolution;

namespace D_Parser.Completion
{
	public class ArgumentsResolutionResult
	{
		public bool IsMethodArguments;
		public bool IsTemplateInstanceArguments;

		public IExpression ParsedExpression;

		/// <summary>
		/// Usually some part of the ParsedExpression.
		/// For instance in a PostfixExpression_MethodCall it'd be the PostfixForeExpression.
		/// </summary>
		public object MethodIdentifier;

		public AbstractType[] ResolvedTypesOrMethods;

		public readonly Dictionary<IExpression, AbstractType> TemplateArguments = new Dictionary<IExpression, AbstractType>();
		/// <summary>
		/// Stores the already typed arguments (Expressions) + their resolved types.
		/// The value part will be null if nothing could get returned.
		/// </summary>
		public readonly Dictionary<IExpression, AbstractType> Arguments = new Dictionary<IExpression, AbstractType>();

		/// <summary>
		///	Identifies the currently called method overload. Is an index related to <see cref="ArgumentsResolutionResult.ResolvedTypesOrMethods"/>
		/// </summary>
		public int CurrentlyCalledMethod;
		public IExpression CurrentlyTypedArgument
		{
			get
			{
				if (Arguments != null && Arguments.Count > CurrentlyTypedArgumentIndex)
				{
					int i = 0;
					foreach (var kv in Arguments)
					{
						if (i == CurrentlyTypedArgumentIndex)
							return kv.Key;
						i++;
					}
				}
				return null;
			}
		}
		public int CurrentlyTypedArgumentIndex;
	}

	public static class ParameterInsightResolution
	{
		
		/// <summary>
		/// Reparses the given method's fucntion body until the cursor position,
		/// searches the last occurring method call or template instantiation,
		/// counts its already typed arguments
		/// and returns a wrapper containing all the information.
		/// </summary>
		public static ArgumentsResolutionResult ResolveArgumentContext(IEditorData Editor)
		{
			IBlockNode curBlock = null;
			bool inNonCode;
			var sr = CodeCompletion.FindCurrentCaretContext(Editor, ref curBlock, out inNonCode);

			IExpression lastParamExpression = null;

			var paramInsightVis = new ParamInsightVisitor ();
			if (sr is INode)
				(sr as INode).Accept (paramInsightVis);
			else if (sr is IStatement)
				(sr as IStatement).Accept (paramInsightVis);
			else if (sr is IExpression)
				(sr as IExpression).Accept (paramInsightVis);

			lastParamExpression = paramInsightVis.LastCallExpression;

			/*
			 * Then handle the lastly found expression regarding the following points:
			 * 
			 * 1) foo(			-- normal arguments only
			 * 2) foo!(...)(	-- normal arguments + template args
			 * 3) foo!(		-- template args only
			 * 4) new myclass(  -- ctor call
			 * 5) new myclass!( -- ditto
			 * 6) new myclass!(...)(
			 * 7) mystruct(		-- opCall call
			 */

			var res = new ArgumentsResolutionResult() { 
				ParsedExpression = lastParamExpression
			};

			var ctxt = ResolutionContext.Create(Editor, false);				

			CodeCompletion.DoTimeoutableCompletionTask(null, ctxt, () =>
			{
				ctxt.Push(Editor);

				ctxt.CurrentContext.ContextDependentOptions |= ResolutionOptions.DontResolveAliases;

				// 1), 2)
				if (lastParamExpression is PostfixExpression_MethodCall)
				{
					res.IsMethodArguments = true;
					var call = (PostfixExpression_MethodCall)lastParamExpression;

					res.MethodIdentifier = call.PostfixForeExpression;
					res.ResolvedTypesOrMethods = ExpressionTypeEvaluation.GetUnfilteredMethodOverloads(call.PostfixForeExpression, ctxt, call);

					if (call.Arguments != null)
						res.CurrentlyTypedArgumentIndex = call.ArgumentCount;
				}
				// 3)
				else if (lastParamExpression is TemplateInstanceExpression)
					HandleTemplateInstance(lastParamExpression as TemplateInstanceExpression, res, Editor, ctxt, curBlock);
				else if (lastParamExpression is NewExpression)
					HandleNewExpression((NewExpression)lastParamExpression, res, Editor, ctxt, curBlock);
			});

			/*
			 * alias int function(int a, bool b) myDeleg;
			 * alias myDeleg myDeleg2;
			 * 
			 * myDeleg dg;
			 * 
			 * dg( -- it's not needed to have myDeleg but the base type for what it stands for
			 * 
			 * ISSUE:
			 * myDeleg( -- not allowed though
			 * myDeleg2( -- allowed neither!
			 */

			return res;
		}

		static void HandleTemplateInstance(TemplateInstanceExpression tix,
			ArgumentsResolutionResult res,
			IEditorData Editor,
			ResolutionContext ctxt,
			IBlockNode curBlock,
			IEnumerable<AbstractType> resultBases = null)
		{
			res.IsTemplateInstanceArguments = true;

			res.MethodIdentifier = tix;
			res.ResolvedTypesOrMethods = ExpressionTypeEvaluation.GetOverloads(tix, ctxt, resultBases, false);

			if (tix.Arguments != null)
				res.CurrentlyTypedArgumentIndex = tix.Arguments.Length;
			else
				res.CurrentlyTypedArgumentIndex = 0;
		}

		static void HandleNewExpression(NewExpression nex, 
			ArgumentsResolutionResult res, 
			IEditorData Editor, 
			ResolutionContext ctxt,
			IBlockNode curBlock,
			IEnumerable<AbstractType> resultBases = null)
		{
			res.MethodIdentifier = nex;
			CalculateCurrentArgument(nex, res, Editor.CaretLocation, ctxt);

			var type = TypeDeclarationResolver.ResolveSingle(nex.Type, ctxt);

			var _ctors = new List<AbstractType>();

			if (type is AmbiguousType)
				foreach (var t in (type as AmbiguousType).Overloads)
					HandleNewExpression_Ctor(nex, curBlock, _ctors, t);
			else
				HandleNewExpression_Ctor(nex, curBlock, _ctors, type);

			res.ResolvedTypesOrMethods = _ctors.ToArray();
		}

		private static void HandleNewExpression_Ctor(NewExpression nex, IBlockNode curBlock, List<AbstractType> _ctors, AbstractType t)
		{
			var udt = t as TemplateIntermediateType;
			if (udt is ClassType || udt is StructType)
			{
				bool explicitCtorFound = false;
				var constructors = new List<DMethod>();

				//TODO: Mixed-in ctors? --> Convert to AbstractVisitor/use NameScan
				foreach (var member in udt.Definition)
				{
					var dm = member as DMethod;

					if (dm != null && dm.SpecialType == DMethod.MethodType.Constructor)
					{
						explicitCtorFound = true;
						if (!dm.IsPublic)
						{
							var curNode = curBlock;
							bool pass = false;
							do
							{
								if (curNode == udt.Definition)
								{
									pass = true;
									break;
								}
							}
							while ((curNode = curNode.Parent as IBlockNode) != curNode);

							if (!pass)
								continue;
						}

						constructors.Add(dm);
					}
				}

				if (constructors.Count == 0)
				{
					if (explicitCtorFound)
					{
						// TODO: Somehow inform the user that the current class can't be instantiated
					}
					else
					{
						// Introduce default constructor
						constructors.Add(new DMethod(DMethod.MethodType.Constructor)
						{
							Description = "Default constructor for " + udt.Name,
							Parent = udt.Definition
						});
					}
				}

				// Wrapp all ctor members in MemberSymbols
				foreach (var ctor in constructors)
					_ctors.Add(new MemberSymbol(ctor, t, nex.Type));
			}
		}

		static void CalculateCurrentArgument(NewExpression nex, 
			ArgumentsResolutionResult res, 
			CodeLocation caretLocation, 
			ResolutionContext ctxt,
			IEnumerable<AbstractType> resultBases=null)
		{
			if (nex.Arguments != null)
				res.CurrentlyTypedArgumentIndex = nex.Arguments.Length;
				/*{
				int i = 0;
				foreach (var arg in nex.Arguments)
				{
					if (caretLocation >= arg.Location && caretLocation <= arg.EndLocation)
					{
						res.CurrentlyTypedArgumentIndex = i;
						break;
					}
					i++;
				}
			}*/
		}
	}
}
