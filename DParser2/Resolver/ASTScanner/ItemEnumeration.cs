//
// ItemEnumeration.cs
//
// Author:
//       Alexander Bothe <info@alexanderbothe.com>
//
// Copyright (c) 2013 Alexander Bothe
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
using System.Collections.Generic;
using D_Parser.Dom;

namespace D_Parser.Resolver.ASTScanner
{
	class ItemEnumeration : AbstractVisitor
	{
		protected ItemEnumeration (ResolutionContext ctxt) : base (ctxt)
		{
		}

		public static List<INode> EnumScopedBlockChildren (ResolutionContext ctxt,MemberFilter VisibleMembers)
		{
			var en = new ItemEnumeration (ctxt);

			en.ScanBlock(ctxt.ScopedBlock, ctxt.ScopedBlock.EndLocation, new ItemCheckParameters(VisibleMembers));

			return en.Nodes;
		}

		public static List<INode> EnumChildren(UserDefinedType ds,ResolutionContext ctxt, MemberFilter VisibleMembers)
		{
			var en = new ItemEnumeration(ctxt);

			en.DeepScanClass(ds, new ItemCheckParameters(VisibleMembers));

			return en.Nodes;
		}

		List<INode> Nodes = new List<INode> ();

		protected override void HandleItem (INode n, AbstractType resolvedCurrentScope)
		{
			Nodes.Add (n);
		}

		protected override void HandleItem(PackageSymbol pack) { }
	}
}

