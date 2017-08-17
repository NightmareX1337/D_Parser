﻿using System;

namespace D_Parser.Dom
{
	public abstract class AbstractNode : INode
	{
		protected AbstractNode()
		{
			Description = null;
		}

		public CodeLocation EndLocation
		{
			get;
			set;
		}

		public CodeLocation Location
		{
			get;
			set;
		}

		public virtual string Description
		{
			get;
			set;
		}

		public virtual ITypeDeclaration Type
		{
			get;
			set;
		}

		public string Name
		{
			get { return Strings.TryGet(NameHash); }
			set { NameHash = value != null ? value.GetHashCode() : 0; Strings.Add (value); }
		}

		public int NameHash {
			get;
			set;
		}

		public CodeLocation NameLocation
		{
			get;
			set;
		}

		public bool IsAnonymous { get { return NameHash == 0; } }

		public INode Parent { get;set; }

		public override string ToString()
		{
			return ToString(true,true);
		}

		public string ToString(bool IncludePath)
		{
			return ToString(true, IncludePath);
		}

		public static string GetNodePath(INode n,bool includeActualNodesName)
		{
			string path = "";
			var curParent = includeActualNodesName?n:n.Parent;
			while (curParent != null)
			{
				// Also include module path
				if (curParent is DModule)
					path = (curParent as DModule).ModuleName + "." + path;
				else if (!String.IsNullOrEmpty(curParent.Name))
					path = curParent.Name + "." + path;

				curParent = curParent.Parent;
			}
			return path.Trim('.');
		}

		public virtual string ToString(bool Attributes,bool IncludePath)
		{
			string s = "";
			// Type
			if (Type != null)
				s += Type.ToString() + " ";

			// Path + Name
			if (IncludePath)
				s += GetNodePath(this, true);
			else
				s += Name;

			return s.Trim();
		}

		public virtual void AssignFrom(INode other)
		{
			Type = other.Type;
			NameHash = other.NameHash;
			NameLocation = other.NameLocation;

			Parent = other.Parent;
			Description = other.Description;
			Location = other.Location;
			EndLocation = other.EndLocation;
		}

		public INode NodeRoot
		{
			get
			{
				return Parent != null ? Parent.NodeRoot : this;
			}
		}

		public abstract void Accept(NodeVisitor vis);
		public abstract R Accept<R>(NodeVisitor<R> vis);
	}
}
