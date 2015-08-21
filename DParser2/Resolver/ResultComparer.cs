﻿using D_Parser.Dom;
using D_Parser.Parser;
using D_Parser.Resolver.Templates;
using D_Parser.Resolver.TypeResolution;
using D_Parser.Resolver.ExpressionSemantics;
using System.Collections.Generic;

namespace D_Parser.Resolver
{
	/// <summary>
	/// Provides methods to check if resolved types are matching each other and/or can be converted into each other.
	/// Used for UFCS completion, argument-parameter matching, template parameter deduction
	/// </summary>
	public static class ResultComparer
	{
		/// <summary>
		/// Checks given results for type equality
		/// </summary>
		public static bool IsEqual(ISemantic r1, ISemantic r2)
		{
			if (r1 == r2)
				return true;

			if (r1 is TemplateParameterSymbol)
			{
				var tps1 = r1 as TemplateParameterSymbol;
				var tps2 = r2 as TemplateParameterSymbol;
				if (tps2 != null && tps1.Parameter == tps2.Parameter)
					return (tps1.Base == null && tps2.Base == null) || IsEqual(tps1.Base, tps2.Base);

				if (TemplateInstanceHandler.IsNonFinalArgument(r2))
					return true;
				r1 = tps1.Base;
			}
			if(r2 is TemplateParameterSymbol)
				r2 = ((TemplateParameterSymbol)r2).Base;
			
			if (r1 is ISymbolValue && r2 is ISymbolValue)
				return SymbolValueComparer.IsEqual((ISymbolValue)r1, (ISymbolValue)r2);

			else if (r1 is TemplateIntermediateType && r2 is TemplateIntermediateType)
			{
				var tr1 = (TemplateIntermediateType)r1;
				var tr2 = (TemplateIntermediateType)r2;

				if (tr1.Definition != tr2.Definition)
					return false;

				//TODO: Compare deduced types
				return true;
			}
			else if (r1 is PrimitiveType && r2 is PrimitiveType)
				return ((PrimitiveType)r1).TypeToken == ((PrimitiveType)r2).TypeToken;
			else if (r1 is ArrayType && r2 is ArrayType)
			{
				var ar1 = (ArrayType)r1;
				var ar2 = (ArrayType)r2;

				if (!IsEqual(ar1.KeyType, ar2.KeyType))
					return false;

				return IsEqual(ar1.Base, ar2.Base);
			}
			else if(r1 is DelegateType && r2 is DelegateType)
			{
				var dg1 = r1 as DelegateType;
				var dg2 = r2 as DelegateType;
				
				if(dg1.IsFunctionLiteral != dg2.IsFunctionLiteral || 
				   !IsEqual(dg1.ReturnType, dg2.ReturnType))
					return false;

				var it1 = dg1.Parameters.GetEnumerator();
				var it2 = dg2.Parameters.GetEnumerator();

				while (it1.MoveNext() && it2.MoveNext())
					if (!IsEqual(it1.Current, it2.Current))
						return false;

				return !it1.MoveNext() && !it2.MoveNext();
			}

			//TODO: Handle other types

			return false;
		}

		static readonly List<int> ImplicitConvertabilityTable = new List<int> { 
			{(DTokens.Long << 8) + DTokens.Int},
			{(DTokens.Ulong << 8) + DTokens.Uint},
		};

		public static bool IsPrimitiveTypeImplicitlyConvertible(byte fromType, byte toType)
		{
			return fromType == toType || ImplicitConvertabilityTable.Contains((fromType << 8) + toType);
		}

		/// <summary>
		/// Checks results for implicit type convertability 
		/// </summary>
		public static bool IsImplicitlyConvertible(ISemantic resultToCheck, AbstractType targetType, ResolutionContext ctxt=null)
		{
			var resToCheck = AbstractType.Get(resultToCheck);
			bool isVariable = resToCheck is MemberSymbol;

			// Initially remove aliases from results
			var _r=DResolver.StripMemberSymbols(resToCheck);
			if(_r==null)
				return IsEqual(resToCheck,targetType);
			resToCheck = _r;
			
			if (targetType is DSymbol)
			{
				var tpn = ((DSymbol)targetType).Definition as TemplateParameter.Node;

				if (tpn!=null)
				{
					var par = tpn.Parent as DNode;

					if (par != null && par.TemplateParameters != null)
					{
						var dedParam = new DeducedTypeDictionary(par);

						return TemplateParameterDeduction.Deduce(tpn.TemplateParameter, resToCheck, dedParam, ctxt);
					}
				}
			}

			_r = DResolver.StripMemberSymbols(targetType);
			if (_r == null)
				return false;
			targetType = _r;

			if (resToCheck is PrimitiveType && targetType is PrimitiveType)
			{
				var sr1 = (PrimitiveType)resToCheck;
				var sr2 = (PrimitiveType)targetType;
				
				//if (sr1.TypeToken == sr2.TypeToken /*&& sr1.Modifier == sr2.Modifier*/)
				//	return true;
				
				return IsPrimitiveTypeImplicitlyConvertible(sr1.TypeToken, sr2.TypeToken);
			}
			else if (resToCheck is UserDefinedType && targetType is UserDefinedType)
				return IsImplicitlyConvertible((UserDefinedType)resToCheck, (UserDefinedType)targetType);
			else if (resToCheck is DelegateType && targetType is DelegateType)
				return IsEqual(resToCheck, targetType); //TODO: Can non-equal delegates be converted into each other?
			else if (resToCheck is ArrayType && targetType is ArrayType)
			{
				var ar1 = (ArrayType)resToCheck;
				var ar2 = (ArrayType)targetType;

				// Key as well as value types must be matching!
				var ar1_n = ar1.KeyType == null;
				var ar2_n = ar2.KeyType == null;

				if (ar1_n != ar2_n)
					return false;

				if (ar1_n || IsImplicitlyConvertible(ar1.KeyType, ar2.KeyType, ctxt))
					return IsImplicitlyConvertible(ar1.Base, ar2.Base, ctxt);
			}
			else if (resToCheck is DSymbol && targetType is DSymbol)
			{
				var r1 = resToCheck as DSymbol;
				var r2 = targetType as DSymbol;

				if (r1.Definition == r2.Definition)
				{
					//TODO: Compare template param deductions
					return true;
				}
			}
			else if (resToCheck is DTuple && targetType is DTuple)
			{
				var tup1 = resToCheck as DTuple;
				var tup2 = resToCheck as DTuple;

				//TODO
				return true;
			}
			/*else if (resultToCheck is ExpressionValueResult && targetType is ExpressionValue)
			{
				return ((ExpressionValueResult)resultToCheck).Value.Equals(((ExpressionValueResult)targetType).Value);
			}*/

			// http://dlang.org/type.html
			//TODO: Pointer to non-pointer / vice-versa checkability? -- Can it really be done implicitly?
			else if (!isVariable &&
				resToCheck is ArrayType &&
				targetType is PointerType && ((targetType = (targetType as PointerType).Base) is PrimitiveType) &&
				DTokens.IsBasicType_Character((targetType as PrimitiveType).TypeToken))
				return (resultToCheck as ArrayType).IsString;


			return false;
		}

		public static bool IsImplicitlyConvertible(UserDefinedType r, UserDefinedType target)
		{
			if (r == null || target == null)
				return false;

			if (r.Definition == target.Definition)
				return true;

			if (r.Base != null && IsImplicitlyConvertible(r.Base, target))
				return true;

			if (r is TemplateIntermediateType)
			{
				var templateType = (TemplateIntermediateType)r;

				if (target is InterfaceType)
					foreach (var I in templateType.BaseInterfaces)
						if (IsImplicitlyConvertible(I, target))
							return true;
			}

			return false;
		}
	}
}
