﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using D_Parser.Completion;
using D_Parser.Dom;
using D_Parser.Dom.Statements;
using D_Parser.Misc;

namespace D_Parser.Resolver.ASTScanner
{
	/// <summary>
	/// A whitelisting filter for members to show in completion menus.
	/// </summary>
	[Flags]
	public enum MemberFilter
	{
		Imports = 1,
		Variables = 1 << 1,
		Methods = 1 << 2,
		Types = 1 << 3,
		Keywords = 1 << 4,

		All = Imports | Variables | Methods | Types | Keywords
	}

	public class ItemEnumeration : AbstractVisitor
	{
		protected ItemEnumeration(ResolutionContext ctxt): base(ctxt) { }

		public static IEnumerable<INode> EnumAllAvailableMembers(IBlockNode ScopedBlock
			, IStatement ScopedStatement,
			CodeLocation Caret,
			ParseCacheList CodeCache,
			MemberFilter VisibleMembers,
			ConditionalCompilationFlags compilationEnvironment = null)
		{
			var ctxt = ResolutionContext.Create(CodeCache, compilationEnvironment, ScopedBlock, ScopedStatement);
			return EnumAllAvailableMembers(ctxt, Caret, VisibleMembers);
		}

		public static IEnumerable<INode> EnumAllAvailableMembers(
			ResolutionContext ctxt,
			CodeLocation Caret,
			MemberFilter VisibleMembers)
		{
			var en = new ItemEnumeration(ctxt);

			en.IterateThroughScopeLayers(Caret, VisibleMembers);

			return en.Nodes.Count <1 ? null : en.Nodes;
		}

		List<INode> Nodes = new List<INode>();
		protected override bool HandleItem(INode n)
		{
			Nodes.Add(n);
			return false;
		}

		protected override bool HandleItems(IEnumerable<INode> nodes)
		{
			Nodes.AddRange(nodes);
			return false;
		}
		
		public static List<INode> EnumChildren(ResolutionContext ctxt, IBlockNode block, MemberFilter filter = MemberFilter.All)
		{
			var scan = new ItemEnumeration(ctxt);

			bool _unused=false;
			scan.ScanBlock(block, CodeLocation.Empty, MemberFilter.All, ref _unused);

			return scan.Nodes;
		}
	}
	
	public class MemberCompletionEnumeration : AbstractVisitor
	{
		bool isVarInst;
		readonly ICompletionDataGenerator gen;
		protected MemberCompletionEnumeration(ResolutionContext ctxt, ICompletionDataGenerator gen) : base(ctxt) 
		{
			this.gen = gen;
		}
		
		public static void EnumChildren(ICompletionDataGenerator cdgen,ResolutionContext ctxt, UserDefinedType udt, bool isVarInstance)
		{
			var scan = new MemberCompletionEnumeration(ctxt, cdgen) { isVarInst = isVarInstance };

			bool _unused=false;
			scan.DeepScanClass(udt, MemberFilter.Methods | MemberFilter.Types | MemberFilter.Variables, ref _unused);
		}
		
		public static void EnumChildren(ICompletionDataGenerator cdgen,ResolutionContext ctxt, IBlockNode block, bool isVarInstance)
		{
			var scan = new MemberCompletionEnumeration(ctxt, cdgen) { isVarInst = isVarInstance };

			bool _unused=false;
			scan.ScanBlock(block, CodeLocation.Empty, MemberFilter.Methods | MemberFilter.Types | MemberFilter.Variables, ref _unused);
		}
		
		protected override bool HandleItem(INode n)
		{
			if(isVarInst || !(n is DMethod || n is DVariable || n is TemplateParameterNode) || (n as DNode).IsStatic)
				gen.Add(n);
			return false;
		}
	}
}
