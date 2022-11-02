using System;
using System.Collections.Generic;
using Zinnia.Base;

namespace Zinnia.Recognizers
{
	public class CCommentRecognizer : ICommentRecognizer
	{
		public static string[] Strings = new string[] { "//", "/*" };
		public static string[] EndStrings = new string[] { "*/" };

		public bool Process(CompilerState State, CodeString Code)
		{
			var File = Code.File;
			var StringRecognizer = State.Language.Root.GetObject<StringRecognizer>();
			var SkippingHandlers = new IResultSkippingHandler[] { StringRecognizer };

			var Line = Code.Line;
			while (Code.HasLine(Line))
			{
				var LineStr = Code.GetLine(Line);
				var Result = LineStr.Find(Strings, Handlers: SkippingHandlers);
				if (Result.Index == 0)
				{
					var Index =  LineStr.Index + Result.Position;
					var Length = LineStr.FirstLineLength - Result.Position;
					File.RemoveCode(Index, Length, Line);
				}
				else if (Result.Index == 1)
				{
					var Index = LineStr.Index + Result.Position;
					var Right = Code.Substring(LineStr.Index + Result.NextChar);
					var EndRes = Right.Find(EndStrings);

					if (EndRes.Position == -1)
					{
						State.Messages.Add(MessageId.DeficientExpr, Right.Substring(EndRes));
						return false;
					}

					var Length = Right.Index + EndRes.NextChar - Index;
					File.RemoveCode(Index, Length, Line);
				}

				Line++;
			}

			return true;
		}
	}
}