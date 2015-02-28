﻿using System.Collections.Generic;
using System.Linq;
using D_Parser.Dom;
using D_Parser.Dom.Expressions;

namespace D_Parser.Resolver
{
	public class ResolutionError
	{
		public readonly object SyntacticalContext;
		public readonly string Message;

		public ResolutionError(object syntacticalObj, string message)
		{
			this.SyntacticalContext = syntacticalObj;
			this.Message = message;
		}
	}

	public class AmbiguityError : ResolutionError
	{
		public readonly ISemantic[] DetectedOverloads;

		public AmbiguityError(ISyntaxRegion syntaxObj, IEnumerable<ISemantic> results, string msg=null)
			: base(syntaxObj, msg ?? "Resolution returned too many results")
		{
			if (results is ISemantic[])
				this.DetectedOverloads = (ISemantic[])results;
			else if(results!=null)
				this.DetectedOverloads = results.ToArray();
		}
	}

	public class NothingFoundError : ResolutionError
	{
		public NothingFoundError(ISyntaxRegion syntaxObj, string msg =null)
			: base(syntaxObj, msg ?? ((syntaxObj is IExpression ? "Expression" : "Declaration") + " could not be resolved."))
		{ }
	}

	public class TemplateParameterDeductionError : ResolutionError
	{
		public TemplateParameter Parameter { get { return SyntacticalContext as TemplateParameter; } }
		public readonly ISemantic Argument;

		public TemplateParameterDeductionError(TemplateParameter parameter, ISemantic argument, string msg)
			: base(parameter, msg)
		{
			this.Argument = argument;
		}
	}

	public class AmbigousSpecializationError : ResolutionError
	{
		public readonly AbstractType[] ComparedOverloads;

		public AmbigousSpecializationError(AbstractType[] comparedOverloads)
			: base(comparedOverloads[comparedOverloads.Length - 1], "Could not distinguish a most specialized overload. Both overloads seem to be equal.")
		{
			this.ComparedOverloads = comparedOverloads;
		}
	}
}
