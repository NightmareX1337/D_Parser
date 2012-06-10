﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using D_Parser.Dom.Expressions;
using D_Parser.Resolver;
using D_Parser.Resolver.TypeResolution;
using D_Parser.Dom;

namespace D_Parser.Evaluation
{
	public partial class ExpressionEvaluator
	{
		#region Properties / Ctor
		ISymbolValueProvider vp;

		private ExpressionEvaluator() { }
		#endregion

		#region Outer interaction
		public static ResolveResult Resolve(IExpression arg, ResolverContextStack ctxt)
		{
			var ev=Evaluate(arg, new StandardValueProvider(ctxt));

			if (ev == null)
				return null;

			return new ExpressionValueResult{ 
				DeclarationOrExpressionBase=arg, 
				Value=ev
			};
		}

		public static ISymbolValue Evaluate(IExpression expression, ISymbolValueProvider vp)
		{
			return new ExpressionEvaluator { vp=vp }.Evaluate(expression);
		}
		
		/// <summary>
		/// Tries to evaluate a const initializer of the const/enum variable passed in by r
		/// </summary>
		/// <param name="r">Contains a member result that holds a const'ed variable with a static initializer</param>
		public static ISymbolValue TryToEvaluateConstInitializer(
			IEnumerable<ResolveResult> r,
			ResolverContextStack ctxt)
		{
			// But: If it's a variable that represents a const value..
			var r_noAlias = DResolver.TryRemoveAliasesFromResult(r);
			if (r_noAlias != null)
				foreach (var r_ in r_noAlias)
				{
					if (r_ is MemberResult)
					{
						var n = ((MemberResult)r_).Node as DVariable;

						if (n != null && n.IsConst)
						{
							// .. resolve it's pre-compile time value and make the returned value the given argument
							var val = Evaluate(n.Initializer, new StandardValueProvider(ctxt));

							if (val != null)
								return val;
						}
					}
				}
			return null;
		}
		#endregion

		public ISymbolValue Evaluate(IExpression x)
		{
			if (x is PrimaryExpression)
				return Evaluate((PrimaryExpression)x);
			if (x is TypeDeclarationExpression)
				return Evaluate((TypeDeclarationExpression)x);

			return null;
		}

		public ISymbolValue Evaluate(TypeDeclarationExpression x)
		{
			var r=TypeDeclarationResolver.Resolve(x.Declaration, vp.ResolutionContext);

			if(r!=null)
				return TryToEvaluateConstInitializer(r, vp.ResolutionContext);
			return null;
		}

		#region Helpers
		public static bool ToBool(object value)
		{
			bool b = false;

			try
			{
				b = Convert.ToBoolean(value);
			}
			catch { }

			return b;
		}

		public static double ToDouble(object value)
		{
			double d = 0;

			try
			{
				d = Convert.ToDouble(value);
			}
			catch { }

			return d;
		}

		public static long ToLong(object value)
		{
			long d = 0;

			try
			{
				d = Convert.ToInt64(value);
			}
			catch { }

			return d;
		}
		#endregion
	}
}
