﻿//
// NodeTooltipRepresentationGen.Headers.cs
//
// Author:
//       Alexander Bothe <info@alexanderbothe.com>
//
// Copyright (c) 2014 Alexander Bothe
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
using D_Parser.Dom;
using D_Parser.Parser;
using D_Parser.Resolver;
using D_Parser.Resolver.Templates;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace D_Parser.Completion.ToolTips
{
	[Flags]
	public enum TooltipSignatureFlags
	{
		NoLineBreakedMethodParameters = 1<<0,
		NoDefaultParams = 1<<1,
		NoEnsquaredDefaultParams = 1<<2,
		NoTemplateConstraints = 1<<3,
	}

	public partial class NodeTooltipRepresentationGen
    {
		public TooltipSignatureFlags SignatureFlags;

		public string GenTooltipSignature(AbstractType t, bool templateParamCompletion = false, int currentMethodParam = -1)
		{
			var ds = t as DSymbol;
			if (ds != null)
			{
				if (currentMethodParam >= 0 && !templateParamCompletion && ds.Definition is DVariable && ds.Base != null)
					return GenTooltipSignature(ds.Base, false, currentMethodParam);

				var aliasedSymbol = ds.Tag as D_Parser.Resolver.TypeResolution.TypeDeclarationResolver.AliasTag;
				return GenTooltipSignature(aliasedSymbol == null || currentMethodParam >= 0 ? ds.Definition : aliasedSymbol.aliasDefinition, templateParamCompletion, currentMethodParam, DTypeToTypeDeclVisitor.GenerateTypeDecl(ds.Base), ds.DeducedTypes != null ? new DeducedTypeDictionary(ds) : null);
			}

			if (t is PackageSymbol)
			{
				var pack = (t as PackageSymbol).Package;
				return "<i>(Package)</i> " + pack.ToString();
			}

			if (t is DelegateType)
			{
				var dt = t as DelegateType;
				//TODO
			}

			return DCodeToMarkup(t != null ? t.ToCode(true) : "null");
		}

		public string GenTooltipSignature(DNode dn, bool templateParamCompletion = false,
			int currentMethodParam = -1, ITypeDeclaration baseType = null, DeducedTypeDictionary deducedType = null)
		{
			var sb = new StringBuilder();

			if (dn is DMethod)
				S(dn as DMethod, sb, templateParamCompletion, currentMethodParam, baseType, deducedType);
			else if (dn is DModule)
			{
				sb.Append("<i>(Module)</i> ").Append((dn as DModule).ModuleName);
			}
			else if (dn is DClassLike)
				S(dn as DClassLike, sb, deducedType);
			else if (dn != null)
				AttributesTypeAndName(dn, sb, baseType, -1, deducedType);

			return sb.ToString();
		}

		/*void S(DelegateType dt, StringBuilder sb, bool templArgs = false, int curArg = -1)
		{
			if (dt.ReturnType != null)
				sb.Append(DCodeToMarkup(dt.ReturnType.ToCode(true))).Append(' ');

			// Parameters
			sb.Append('(');

			if (dt.Parameters != null)
			{
				for (int i = 0; i < dt.Parameters.Length; i++)
				{
					sb.AppendLine();
					sb.Append("  ");

					var parm = dt.Parameters[i];

					var indexBackup = sb.Length;

					//TODO: Show deduced parameters
					AttributesTypeAndName(parm, sb);

					if (!templArgs && curArg == i)
					{
						//TODO: Optimize
						var contentToUnderline = sb.ToString(indexBackup, sb.Length - indexBackup);
						sb.Remove(indexBackup, contentToUnderline.Length);
						AppendFormat(contentToUnderline, sb, FormatFlags.Underline);
					}

					if (parm is DVariable && (parm as DVariable).Initializer != null)
						sb.Append(" = ").Append((parm as DVariable).Initializer.ToString());

					sb.Append(',');
				}

				RemoveLastChar(sb, ',');
				sb.AppendLine();
			}

			sb.Append(')');
		}*/

		void S(DMethod dm, StringBuilder sb, bool templArgs = false, int curArg = -1, ITypeDeclaration baseType = null,
			DeducedTypeDictionary deducedTypes = null)
		{
			AttributesTypeAndName(dm, sb, baseType, templArgs ? curArg : -1, deducedTypes);

			// Parameters
			sb.Append('(');

			if (dm.Parameters.Count != 0)
			{
				for (int i = 0; i < dm.Parameters.Count; i++)
				{
					if((SignatureFlags & TooltipSignatureFlags.NoLineBreakedMethodParameters) == 0)
						sb.AppendLine().Append("  ");

					var parm = dm.Parameters[i] as DNode;

					var indexBackup = sb.Length;

					var addSqareBrackets = (this.SignatureFlags & TooltipSignatureFlags.NoEnsquaredDefaultParams) == 0;

					var isOpt = (this.SignatureFlags & TooltipSignatureFlags.NoDefaultParams) == 0 && parm is DVariable && (parm as DVariable).Initializer != null;

					if (isOpt && addSqareBrackets)
						sb.Append('[');

					//TODO: Show deduced parameters
					AttributesTypeAndName(parm, sb);

					if (!templArgs && curArg == i)
					{
						//TODO: Optimize
						var contentToUnderline = sb.ToString(indexBackup, sb.Length - indexBackup);
						sb.Remove(indexBackup, contentToUnderline.Length);
						AppendFormat(contentToUnderline, sb, FormatFlags.Underline);
					}

					if (isOpt)
					{
						sb.Append(" = ").Append(DCodeToMarkup((parm as DVariable).Initializer.ToString()));
						if(addSqareBrackets)
							sb.Append(']');
					}

					sb.Append(',');
				}

				RemoveLastChar(sb, ',');
				if((SignatureFlags & TooltipSignatureFlags.NoLineBreakedMethodParameters) == 0)
					sb.AppendLine();
			}

			sb.Append(')');

			AppendConstraint(dm, sb);
		}

		void S(DClassLike dc, StringBuilder sb, DeducedTypeDictionary deducedTypes = null)
		{
			AppendAttributes(dc, sb);

			sb.Append(DCodeToMarkup(DTokens.GetTokenString(dc.ClassType))).Append(' ');

			sb.Append(DCodeToMarkup(dc.Name));

			AppendTemplateParams(dc, sb, -1, deducedTypes);

			if (dc.BaseClasses != null && dc.BaseClasses.Count != 0)
			{
				sb.AppendLine(" : ");
				sb.Append(" ");
				foreach (var bc in dc.BaseClasses)
					sb.Append(' ').Append(DCodeToMarkup(bc.ToString())).Append(',');

				RemoveLastChar(sb, ',');
			}

			AppendConstraint(dc, sb);
		}

		void AttributesTypeAndName(DNode dn, StringBuilder sb,
			ITypeDeclaration baseType = null, int highlightTemplateParam = -1,
			DeducedTypeDictionary deducedTypes = null)
		{
			AppendAttributes(dn, sb, baseType == null);

			if (dn.Type != null || baseType != null)
				sb.Append(DCodeToMarkup((baseType ?? dn.Type).ToString(true))).Append(' ');

			// Maybe highlight variables/method names?
			sb.Append(dn.Name);

			AppendTemplateParams(dn, sb, highlightTemplateParam, deducedTypes);
		}

		const int MaxTemplateConstraintExprLength = 200;
		void AppendConstraint(DNode n, StringBuilder sb)
		{
			if (n.TemplateConstraint == null || (SignatureFlags & TooltipSignatureFlags.NoTemplateConstraints) != 0)
				return;

			if ((SignatureFlags & TooltipSignatureFlags.NoLineBreakedMethodParameters) == 0)
				sb.AppendLine();
			else
				sb.Append(' ');
			
			var constr = n.TemplateConstraint.ToString();
			if (constr.Length > MaxTemplateConstraintExprLength)
				constr = constr.Substring(0, MaxTemplateConstraintExprLength) + "...";
			sb.AppendLine(DCodeToMarkup("if(" + constr + ")"));
		}

		void AppendTemplateParams(DNode dn, StringBuilder sb, int highlightTemplateParam = -1, DeducedTypeDictionary deducedTypes = null)
		{
			if (dn.TemplateParameters != null && dn.TemplateParameters.Length > 0)
			{
				sb.Append('(');

				for (int i = 0; i < dn.TemplateParameters.Length; i++)
				{
					var param = dn.TemplateParameters[i];
					if (param != null)
					{
						var tps = deducedTypes != null ? deducedTypes[param] : null;

						string str;

						if (tps == null)
							str = param.ToString();
						else if (tps.ParameterValue != null)
							str = tps.ParameterValue.ToCode();
						else if (tps.Base != null)
							str = tps.Base.ToCode();
						else
							str = param.ToString();

						AppendFormat(DCodeToMarkup(str), sb, i != highlightTemplateParam ? FormatFlags.None : FormatFlags.Underline);
						sb.Append(',');
					}
				}

				RemoveLastChar(sb, ',');

				sb.Append(')');
			}
		}

		static bool CanShowAttribute(DAttribute attr, bool showStorageClasses)
		{
			if (attr is DeclarationCondition)
				return false;

			var mod = attr as Modifier;
			if (showStorageClasses || mod == null)
				return true;

			switch (mod.Token)
			{
				case DTokens.Auto:
				case DTokens.Enum:
					return false;
				default:
					return true;
			}
		}

		void AppendAttributes(DNode dn, StringBuilder sb, bool showStorageClasses = true)
		{
			if (dn.Attributes != null && dn.Attributes.Count != 0)
			{
				foreach (var attr in dn.Attributes)
					if (CanShowAttribute(attr, showStorageClasses))
						sb.Append(DCodeToMarkup(attr.ToString())).Append(' ');
			}

			var dv = dn as DVariable;
			if (dv != null && dv.IsAlias)
				sb.Append("alias ");
		}

		static void RemoveLastChar(StringBuilder sb, char c)
		{
			if (sb[sb.Length - 1] == c)
				sb.Remove(sb.Length - 1, 1);
		}
    }
}
