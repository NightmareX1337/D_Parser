﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using D_Parser.Dom.Expressions;
using D_Parser.Resolver;

namespace D_Parser.Evaluation
{
	public partial class ExpressionEvaluator
	{
		ResolverContextStack ctxt;

		private ExpressionEvaluator() { }

		public static bool IsEqual(IExpression ex, IExpression ex2, ResolverContextStack ctxt)
		{
			var val_x1 = Evaluate(ex, ctxt);
			var val_x2 = Evaluate(ex2, ctxt);

			//TEMPORARILY: Remove the string comparison
			if (val_x1 == null && val_x2 == null)
				return ex.ToString() == ex2.ToString();

			return val_x1!=null && val_x2 != null && val_x1.Value == val_x2.Value;
		}

		public static IExpressionValue Evaluate(IExpression expression, ResolverContextStack ctxt)
		{
			return new ExpressionEvaluator { ctxt = ctxt }.Evaluate(expression);
		}

		public IExpressionValue Evaluate(IExpression x)
		{
			//if (x is PrimaryExpression)
				//return Evaluate((PrimaryExpression)x);

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
