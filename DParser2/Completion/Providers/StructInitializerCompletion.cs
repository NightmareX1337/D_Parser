﻿using D_Parser.Dom;
using D_Parser.Dom.Expressions;
using D_Parser.Resolver;
using D_Parser.Resolver.ASTScanner;
using D_Parser.Resolver.TypeResolution;
using System.Collections.Generic;

namespace D_Parser.Completion.Providers
{
	class StructInitializerCompletion : AbstractCompletionProvider
	{
		public readonly DVariable initedVar;
		public readonly StructInitializer init;

		public StructInitializerCompletion(ICompletionDataGenerator gen,DVariable initializedVariable, StructInitializer init) : base(gen)
		{
			this.initedVar = initializedVariable;
			this.init = init;
		}

		protected override void BuildCompletionDataInternal(IEditorData Editor, char enteredChar)
		{
			var ctxt = ResolutionContext.Create(Editor, true);
			var resolvedVariable = TypeDeclarationResolver.HandleNodeMatch(initedVar, ctxt) as DSymbol;

			if (resolvedVariable == null)
				return;

			while (resolvedVariable is TemplateParameterSymbol)
				resolvedVariable = resolvedVariable.Base as DSymbol;

			var structType = resolvedVariable.Base as TemplateIntermediateType;

			if (structType == null)
				return;

			var alreadyTakenNames = new List<int>();
			foreach (var m in init.MemberInitializers)
				alreadyTakenNames.Add(m.MemberNameHash);

			new StructVis(structType,alreadyTakenNames,CompletionDataGenerator,ctxt);
		}

		class StructVis : AbstractVisitor
		{
			readonly List<int> alreadyTakenNames;
			readonly ICompletionDataGenerator gen;

			public StructVis(TemplateIntermediateType structType,List<int> tkn,ICompletionDataGenerator gen,ResolutionContext ctxt)
				: base(ctxt)
			{
				this.alreadyTakenNames = tkn;
				this.gen = gen;

				if (ctxt.CompletionOptions.ShowStructMembersInStructInitOnly)
					this.DeepScanClass(structType, new ItemCheckParameters(MemberFilter.Variables), false);
				else
					IterateThroughScopeLayers(CodeLocation.Empty, MemberFilter.All);
			}

			protected override bool PreCheckItem (INode n) => !alreadyTakenNames.Contains (n.NameHash);

			protected override void HandleItem(INode n, AbstractType resolvedCurrentScope) => gen.Add(n);
			protected override void HandleItem(PackageSymbol pack) { }
		}
	}
}
