using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Zinnia.Base;

namespace Zinnia.Recognizers
{
	public abstract class SkippedBetweenRecognizer : LanguageNode, IResultSkippingHandler
	{
		public char Marker;
		public bool UseMarker;

		public SkippedBetweenRecognizer(LanguageNode Parent, LanguageNodeFlags Flags = LanguageNodeFlags.None)
			: base(Parent, Flags)
		{
		}

        public override void Init(LanguageInitData InitData)
		{
			if (Operators.Length == 1 && Operators[0].Length > 0)
			{
				Marker = Operators[0][0];
				UseMarker = true;
			}
			else
			{
				UseMarker = false;
			}

			base.Init(InitData);
		}

		public SkippingHandlerResult SkipResult(ref ResultSkippingManager RSM)
		{
			if (RSM.CurrentChar == Marker || !UseMarker)
			{
				var Result = RSM.String.SubstringEquals(RSM.Current, Operators, Skip);
				if (Result.Position != -1)
				{
					if (RSM.Back)
					{
						var NewString = RSM.String.Substring(0, RSM.Current);
						var Res = NewString.Find(Operators, Skip, Back: true);
						if (Res.Position != -1) return new SkippingHandlerResult(Res.Position);
					}
					else
					{
						var NewString = RSM.String.Substring(RSM.Current + Result.String.Length);
						var Res = NewString.Find(Operators, Skip, Back: false);

						if (Res.Position != -1)
						{
							var P = Res.Position + RSM.Current + Result.String.Length;
							return new SkippingHandlerResult(P);
						}
					}
				}
			}

			return new SkippingHandlerResult(-1);
		}

		public CodeString BetweenOperatos(CodeString Code)
		{
			if (UseMarker)
			{
				var StrLength = Code.Length;
				if (StrLength < 2 || Code[0] != Marker || Code[StrLength - 1] != Marker)
					return new CodeString();
			}

			var StartRes = Code.StartsWith(Operators, Skip);
			var EndRes = Code.EndsWith(Operators, Skip);
			if (StartRes.Index == -1 || EndRes.Index == -1) return new CodeString();
			if (StartRes.Index != EndRes.Index) return new CodeString();
			
			var Length = StartRes.String.Length;
			if (EndRes.Position < Length) return new CodeString();

			Code = Code.Substring(Length, Code.Length - Length * 2);
			if (Code.Find(Operators, Skip).Index != -1) return new CodeString();
			return Code;
		}
	}

}