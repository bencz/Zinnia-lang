using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Zinnia.Base;

namespace Zinnia.Recognizers
{
	public delegate ExprRecResult KeywordExprRecognizerFunc(CodeString Code, PluginRoot Plugin, ref ExpressionNode Ret);
	public class KeywordExprRecognizer : LanguageNode, IExprRecognizer
	{
		public Dictionary<string, KeywordExprRecognizerFunc> List;

		public KeywordExprRecognizer(LanguageNode Parent, Dictionary<string, KeywordExprRecognizerFunc> List)
			: base(Parent)
		{
			this.List = List;
		}

		public KeywordExprRecognizer(LanguageNode Parent)
			: base(Parent)
		{
			List = new Dictionary<string, KeywordExprRecognizerFunc>();
			List.Add("true", (CodeString Code, PluginRoot Plugin, ref ExpressionNode Ret) =>
			{
				Ret = Constants.GetBoolValue(Plugin.Container, true, Code);
				return ExprRecResult.Succeeded;
			});

			List.Add("false", (CodeString Code, PluginRoot Plugin, ref ExpressionNode Ret) =>
			{
				Ret = Constants.GetBoolValue(Plugin.Container, false, Code);
				return ExprRecResult.Succeeded;
			});

			List.Add("null", (CodeString Code, PluginRoot Plugin, ref ExpressionNode Ret) =>
			{
				Ret = Constants.GetNullValue(Plugin.Container, Code);
				return ExprRecResult.Succeeded;
			});

			List.Add("default", (CodeString Code, PluginRoot Plugin, ref ExpressionNode Ret) =>
			{
				Ret = Constants.GetDefaultValue(Plugin.Container, Code);
				return ExprRecResult.Succeeded;
			});
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Ret)
		{
			if (Code.IsValidIdentifierName)
			{
				foreach (var e in List)
					if (Code.IsEqual(e.Key))
						return e.Value(Code, Plugin, ref Ret);
			}

			return ExprRecResult.Unknown;
		}
	}

	public class StringRecognizer : SkippedBetweenRecognizer, IExprRecognizer
	{
		public StringRecognizer(LanguageNode Parent)
			: base(Parent, LanguageNodeFlags.NotOpRecongizer)
		{
			Operators = new string[] { "\"" };
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Ret)
		{
			var InnerCode = BetweenOperatos(Code);
			if (!InnerCode.IsValid) return ExprRecResult.Unknown;

			var String = RecognizerHelper.ProcessString(InnerCode, Plugin, '~');
			if (String == null) return ExprRecResult.Failed;

			var Global = Plugin.Container.GlobalContainer;
			Ret = new ConstExpressionNode(Global.CommonIds.String, new StringValue(String), Code);
			return ExprRecResult.Succeeded;
		}
	}

	public class CharRecognizer : SkippedBetweenRecognizer, IExprRecognizer
	{
		public CharRecognizer(LanguageNode Parent)
			: base(Parent, LanguageNodeFlags.NotOpRecongizer)
		{
			Operators = new string[] { "\'" };
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Ret)
		{
			var InnerCode = BetweenOperatos(Code);
			if (!InnerCode.IsValid) return ExprRecResult.Unknown;

			var String = RecognizerHelper.ProcessString(InnerCode, Plugin, '~');
			if (String == null) return ExprRecResult.Failed;

			if (String.Length != 1)
			{
				Plugin.State.Messages.Add(MessageId.CharInvalidLength, Code);
				return ExprRecResult.Failed;
			}

			var Global = Plugin.Container.GlobalContainer;
			Ret = new ConstExpressionNode(Global.CommonIds.Char, new CharValue(String[0]), Code);
			return ExprRecResult.Succeeded;
		}
	}

	public struct NumberTypeCodeDefinition
	{
		public string String;
		public Identifier Identifier;

		public NumberTypeCodeDefinition(string String, Identifier Identifier)
		{
			this.String = String;
			this.Identifier = Identifier;
		}
	}

	public struct NumberRadixDefinition
	{
		public string String;
		public int Radix;

		public NumberRadixDefinition(string String, int Radix)
		{
			this.String = String;
			this.Radix = Radix;
		}
	}

	public class ENotationSkipper : IResultSkippingHandler
	{
		public LetterCase HEXLetterCase;

		public ENotationSkipper(LetterCase HEXLetterCase = LetterCase.OnlyUpper)
		{
			this.HEXLetterCase = HEXLetterCase;
		}

		public SkippingHandlerResult SkipResult(ref ResultSkippingManager RSM)
		{
			var Pos = RSM.Current;
			if (Pos <= 0) return new SkippingHandlerResult(-1);

			var Chr = RSM.String[Pos];
			while (Chr == '-' || Chr == '+')
			{
				Pos--;
				if (Pos == -1) return new SkippingHandlerResult(-1);
				else Chr = RSM.String[Pos];
			}

			if (Chr != 'e' && Chr != 'E')
				return new SkippingHandlerResult(-1);

			if (HEXLetterCase != LetterCase.Both)
			{
				if (Chr == 'e' && HEXLetterCase == LetterCase.OnlyLower) 
					return new SkippingHandlerResult(-1);

				if (Chr == 'E' && HEXLetterCase == LetterCase.OnlyUpper) 
					return new SkippingHandlerResult(-1);
			}

			var LeftSide = RSM.String.Substring(0, Pos);
			if (LeftSide.Word(Back: true).ValidIdentifierName)
				return new SkippingHandlerResult(-1);

			var Ret = RSM.Back ? RSM.Current - 1 : RSM.Current + 1;
			return new SkippingHandlerResult(Ret, true);
		}
	}

	public class NumberRecognizer : LanguageNode, IExprRecognizer
	{
		public NumberRadixDefinition[] AtStart;
		public NumberRadixDefinition[] AtEnd;
		public NumberTypeCodeDefinition[] TypeCodes;

		public string[] AtStartStrings;
		public string[] AtEndStrings;

		string[] GetStrings(NumberRadixDefinition[] List)
		{
			if (List == null || List.Length == 0)
				return null;

			var Ret = new string[List.Length];
			for (var i = 0; i < List.Length; i++)
				Ret[i] = List[i].String;

			return Ret;
		}

		public NumberRecognizer(LanguageNode Parent, NumberRadixDefinition[] AtStart,
			NumberRadixDefinition[] AtEnd, NumberTypeCodeDefinition[] TypeCodes)
			: base(Parent)
		{
			this.AtStart = AtStart;
			this.AtEnd = AtEnd;
			this.TypeCodes = TypeCodes;
		}

		public NumberRecognizer(LanguageNode Parent)
			: base(Parent)
		{
			AtStart = new NumberRadixDefinition[]
			{
				new NumberRadixDefinition("$", 16),
				new NumberRadixDefinition("%", 2),
			};
		}

        public override void Init(LanguageInitData InitData)
		{
			base.Init(InitData);
			AtStartStrings = GetStrings(AtStart);
			AtEndStrings = GetStrings(AtEnd);

			if (AtStartStrings == null) Operators = Helper.GetStrings(AtEndStrings);
			else if (AtEndStrings == null) Operators = Helper.GetStrings(AtStartStrings);
			else Operators = AtStartStrings.Union(AtEndStrings).ToArray();

            foreach (var e in Language.Root.GetObjects<AdditiveRecognizer>())
				e.SkippingHandlers.Add(new ENotationSkipper());
		}

		public Identifier TypeExtractor(IdContainer Container, ref CodeString Code)
		{
			if (TypeCodes == null) return null;

			var OldCode = Code;
			var Global = Container.GlobalContainer;
			var State = Container.State;

			var EndStr = Code.EndStr(LetterCase.OnlyLower);
			Code = Code.Substring(0, Code.Length - EndStr.Length);

			if (EndStr.Length > 0)
			{
				for (var i = 0; i < TypeCodes.Length; i++)
				{
					if (TypeCodes[i].String == EndStr)
						return TypeCodes[i].Identifier;
				}
			}

			Code = OldCode;
			return null;
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Ret)
		{
			if (Code.Length == 0)
				return ExprRecResult.Unknown;

			var OldCode = Code;
			var State = Plugin.State;
			var Container = Plugin.Container;
			var Global = Container.GlobalContainer;

			var RadixSpecified = false;
			var Radix = 10;

			var Sign = RecognizerHelper.GetSign(ref Code);
			if (Code.Length == 0) return ExprRecResult.Unknown;

			if (AtStartStrings != null)
			{
				var Result = Code.StartsWith(AtStartStrings, Skip);
				if (Result.Index != -1)
				{
					Code = Code.TrimmedSubstring(State, Result.String.Length);
					if (!Code.IsValid) return ExprRecResult.Failed;

					RadixSpecified = true;
					Radix = AtStart[Result.Index].Radix;
				}
			}

			if (AtEndStrings != null)
			{
				var Result = Code.EndsWith(AtEndStrings, Skip);
				if (Result.Index != -1)
				{
					if (Radix != -1)
					{
						State.Messages.Add(MessageId.MultipleRadix, Code);
						return ExprRecResult.Failed;
					}

					Code = Code.TrimmedSubstring(State, Result.String.Length);
					if (!Code.IsValid) return ExprRecResult.Failed;

					RadixSpecified = true;
					Radix = AtStart[Result.Index].Radix;
				}
			}

			if (RecognizerHelper.GetSign(ref Code)) Sign = !Sign;
			if (Code.Length == 0 || (!char.IsDigit(Code[0]) && !RadixSpecified)) 
				return ExprRecResult.Unknown;

			var Options = new ConvStrToNumberOptions();
			var Type = TypeExtractor(Container, ref Code);
			if (Type == null)
			{
				Options.Type = ConstValueType.Unknown;
			}
			else
			{
				var RType = Type.RealId as Type;
				Options.Type = RType.ConstValueType;
			}

			Code = Code.Trim();
			if (Code.Length == 0)
				return ExprRecResult.Unknown;

			Options.Radix = Radix;
			Options.Number = Code.String;
			Options.LetterCase = LetterCase.OnlyUpper;
			Options.EnableENotation = true;
			Options.Sign = Sign;

			ConstValue Value;
			var Res = RecognizerHelper.ConvStrToNumber(State, Code, Options, out Value);
			if (Res == SimpleRecResult.Unknown) return ExprRecResult.Unknown;
			else if (Res == SimpleRecResult.Failed) return ExprRecResult.Failed;

			if (Type == null)
			{
				Type = Constants.GetDefaultType(Container, Value.Type);
				if (Type.RealId is NumberType && !Value.CheckBounds(Type))
				{
					var SystemType = Type.GetType();

					do
					{
						var RType = Type.RealId as Type;
						Type = Global.CommonIds.GetIdentifier(SystemType, RType.Size * 2);

						if (Type == null)
						{
							if (typeof(SignedType).IsEquivalentTo(SystemType))
								Type = Global.CommonIds.GetLargestType(typeof(UnsignedType));
							else Type = Global.CommonIds.GetLargestType(typeof(SignedType));
							break;
						}

					} while (!Value.CheckBounds(Type));
				}
			}

			if (!Value.CheckBounds(State, Type, OldCode)) return ExprRecResult.Failed;

			var Flags = Options.Type == ConstValueType.Unknown ? 
				ExpressionFlags.AutoConvert : ExpressionFlags.None;

			Ret = new ConstExpressionNode(Type, Value, OldCode, Flags);
			return ExprRecResult.Succeeded;
		}
	}

}