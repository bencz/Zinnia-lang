using System;
using System.Collections.Generic;
using System.Numerics;

namespace Zinnia.Base
{
    public enum LetterCase
    {
        Both = 0,
        OnlyLower = 1,
        OnlyUpper = 2,
    }

    public struct FindResult
    {
        public int Index;
        public int Position;
        public string String;

        public FindResult(int Index, int Position, string String)
        {
            this.Index = Index;
            this.Position = Position;
            this.String = String;
        }

        public int NextChar
        {
            get
            {
                if (Position == -1 || String == null)
                    throw new InvalidOperationException();

                return Position + String.Length;
            }
        }
    }

    public struct SplitRes
    {
        public StringSlice String;
        public bool IsSeparator;

        public SplitRes(StringSlice String, bool IsSeparator)
        {
            this.String = String;
            this.IsSeparator = IsSeparator;
        }
    }

    public struct IdCharCheck
    {
        public bool CheckIdChars;
        public Func<char, bool> IsIdChar;

        public IdCharCheck(bool CheckIdChars, Func<char, bool> IsIdChar = null)
        {
            this.CheckIdChars = CheckIdChars;
            this.IsIdChar = IsIdChar;
        }
    }

    public struct StringSlice
    {
        public string String;
        public int Index;
        public int Length;

        public bool IsValid
        {
            get { return String != null; }
        }

        public char this[int Index]
        {
            get
            {
                Helper.Verify(Length, Index);
                return String[this.Index + Index];
            }
        }

        public StringSlice(string String)
        {
            this.String = String;
            this.Index = 0;

            if (String == null) this.Length = 0;
            else this.Length = String.Length;
        }

        public StringSlice(string String, int Index, int Length)
        {
            Helper.Verify(String.Length, Index, Length, "Index");
            this.String = String;
            this.Index = Index;
            this.Length = Length;
        }

        public string GetSingleLineString()
        {
            var Ret = ToString();
            if (Ret == null) return null;

            Ret = Ret.Replace("\r\n", "\n");
            Ret = Ret.Replace('\n', ' ');
            return Ret;
        }

        public override string ToString()
        {
            if (!IsValid) return null;
            if (Index == 0 && Length == String.Length)
                return String;

            return String.Substring(Index, Length);
        }

        public int LeftWhiteSpaces
        {
            get
            {
                for (var i = Index; i < Index + Length; i++)
                {
                    var Chr = String[i];
                    if (Chr != ' ' && Chr != '\t' && !char.IsWhiteSpace(Chr))
                        return i - Index;
                }

                return Length;
            }
        }

        public int RightWhiteSpaces
        {
            get
            {
                var Start = Index + Length - 1;
                for (var i = Start; i >= Index; i--)
                {
                    var Chr = String[i];
                    if (Chr != ' ' && Chr != '\t' && !char.IsWhiteSpace(Chr))
                        return Start - i;
                }

                return Length;
            }
        }

        public StringSlice SubstringFromTo(int A, int B)
        {
            if (A < B) return Substring(A, B - A + 1);
            else return Substring(B, A - B + 1);
        }

        public StringSlice Substring(int Index, int Length)
        {
            Helper.Verify(this.Length, Index, Length);
            return new StringSlice(String, this.Index + Index, Length);
        }

        public StringSlice Substring(int Index)
        {
            Helper.Verify(this.Length, Index);
            return new StringSlice(String, this.Index + Index, this.Length - Index);
        }

        public StringSlice Substring(FindResult FindRes)
        {
            return Substring(FindRes.Index, FindRes.String.Length);
        }

        public StringSlice Trim()
        {
            var i = LeftWhiteSpaces;
            if (i == Length) return Substring(0, 0);

            var j = RightWhiteSpaces;
            return Substring(i, Length - i - j);
        }

        public StringSlice TrimEnd()
        {
            var j = RightWhiteSpaces;
            if (j == Length) return Substring(0, 0);
            else return Substring(0, Length - j);
        }

        public StringSlice TrimStart()
        {
            var i = LeftWhiteSpaces;
            if (i == Length) return Substring(0, 0);
            else return Substring(i, Length - i);
        }

        public bool SubstringEqualsS(int Index, string CmpWith, IdCharCheck IdCharCheck = new IdCharCheck())
        {
            var CmpLen = CmpWith.Length;
            if (CmpLen == 1)
            {
                if (Index < 0 || Index >= Length) return false;
                if (String[this.Index + Index] != CmpWith[0]) return false;
            }
            else if (CmpLen != 0)
            {
                if (Index < 0 || CmpLen + Index > Length)
                    return false;

                var RIndex = Index + this.Index;
                for (var i = 0; i < CmpLen; i++)
                    if (String[RIndex + i] != CmpWith[i]) return false;
            }
            else
            {
                return true;
            }

            if (IdCharCheck.CheckIdChars)
            {
                var Func = IdCharCheck.IsIdChar;
                if (Index > 0)
                {
                    var SelfChar = String[this.Index + Index - 1];
                    var CmpChar = CmpWith[0];

                    if ((Func != null ? Func(SelfChar) : Helper.IsIdChar(SelfChar)) &&
                        (Func != null ? Func(CmpChar) : Helper.IsIdChar(CmpChar))) return false;
                }

                if (Index + CmpLen < Length)
                {
                    var SelfChar = String[this.Index + Index + CmpLen];
                    var CmpChar = CmpWith[CmpLen - 1];

                    if ((Func != null ? Func(SelfChar) : Helper.IsIdChar(SelfChar)) &&
                        (Func != null ? Func(CmpChar) : Helper.IsIdChar(CmpChar))) return false;
                }
            }

            return true;
        }

        public bool StartsWith(string CmpWith, IdCharCheck IdCharCheck = new IdCharCheck())
        {
            return SubstringEqualsS(0, CmpWith, IdCharCheck);
        }

        public bool EndsWith(string CmpWith, IdCharCheck IdCharCheck = new IdCharCheck())
        {
            return SubstringEqualsS(Length - CmpWith.Length, CmpWith, IdCharCheck);
        }

        public int SubstringEquals(int Index, string CmpWith, string[] Skip = null, bool Back = false,
            IdCharCheck IdCharCheck = new IdCharCheck())
        {
            var RIndex = Index;
            if (Back) RIndex -= CmpWith.Length - 1;

            if (SubstringEqualsS(RIndex, CmpWith, IdCharCheck))
            {
                if (Skip != null)
                {
                    for (var i = 0; i < Skip.Length; i++)
                    {
                        var SkipStr = Skip[i];
                        if (SkipStr.Length >= CmpWith.Length)
                        {
                            for (var SkipPos = -SkipStr.Length + 1; SkipPos < CmpWith.Length; SkipPos++)
                                if (SubstringEqualsS(RIndex + SkipPos, SkipStr)) return -1;
                        }
                    }
                }

                return RIndex;
            }

            return -1;
        }

        public FindResult SubstringEquals(int Index, string[] CmpWith, string[] Skip = null, bool Back = false,
            IdCharCheck IdCharCheck = new IdCharCheck())
        {
            for (var i = 0; i < CmpWith.Length; i++)
            {
                var e = CmpWith[i];
                var Res = SubstringEquals(Index, e, Skip, Back, IdCharCheck);
                if (Res != -1) return new FindResult(i, Res, e);
            }

            return new FindResult(-1, -1, null);
        }

        public FindResult StartsWith(string[] CmpWith, string[] Skip = null, IdCharCheck IdCharCheck = new IdCharCheck())
        {
            return SubstringEquals(0, CmpWith, Skip, false, IdCharCheck);
        }

        public FindResult EndsWith(string[] CmpWith, string[] Skip = null, IdCharCheck IdCharCheck = new IdCharCheck())
        {
            return SubstringEquals(Length - 1, CmpWith, Skip, true, IdCharCheck);
        }

        bool CanContain(string[] CmpWith, bool Back)
        {
            for (var i = 0; i < CmpWith.Length; i++)
            {
                if (CmpWith[i].Length < 1)
                    return true;

                if (Back)
                {
                    var Chr = CmpWith[i][CmpWith[i].Length - 1];
                    var Start = Index + CmpWith[i].Length - 1;
                    for (var Pos = Index + Length - 1; Pos >= Index; Pos--)
                        if (String[Pos] == Chr) return true;
                }
                else
                {
                    var Chr = CmpWith[i][0];
                    var Until = Index + Length - CmpWith[i].Length + 1;
                    for (var Pos = Index; Pos < Until; Pos++)
                        if (String[Pos] == Chr) return true;
                }
            }

            return false;
        }

        public IEnumerable<FindResult> EnumFind(string[] CmpWith, string[] Skip = null, bool Back = false,
            IdCharCheck IdCharCheck = new IdCharCheck(), IList<IResultSkippingHandler> Handlers = null)
        {
            if (!CanContain(CmpWith, Back))
                yield break;

            var RSM = new ResultSkippingManager(Handlers, this, Back);
            while (RSM.Loop())
            {
                var Res = SubstringEquals(RSM.Current, CmpWith, Skip, Back, IdCharCheck);
                if (Res.Position != -1)
                {
                    yield return Res;
                    if (Back) RSM.Current -= Res.String.Length - 1;
                    else RSM.Current += Res.String.Length - 1;
                }
            }
        }

        public FindResult Find(string[] CmpWith, string[] Skip = null, bool Back = false,
            IdCharCheck IdCharCheck = new IdCharCheck(), IList<IResultSkippingHandler> Handlers = null)
        {
            if (!CanContain(CmpWith, Back))
                return new FindResult(-1, -1, null);

            var RSM = new ResultSkippingManager(Handlers, this, Back);
            while (RSM.Loop())
            {
                var Res = SubstringEquals(RSM.Current, CmpWith, Skip, Back, IdCharCheck);
                if (Res.Position != -1) return Res;
            }

            return new FindResult(-1, -1, null);
        }

        public IEnumerable<int> EnumFind(string CmpWith, string[] Skip = null, bool Back = false,
            IdCharCheck IdCharCheck = new IdCharCheck(), IList<IResultSkippingHandler> Handlers = null)
        {
            var RSM = new ResultSkippingManager(Handlers, this, Back);
            while (RSM.Loop())
            {
                var Res = SubstringEquals(RSM.Current, CmpWith, Skip, Back, IdCharCheck);
                if (Res != -1) yield return Res;
            }
        }

        public int Find(string CmpWith, string[] Skip = null, bool Back = false,
            IdCharCheck IdCharCheck = new IdCharCheck(), IList<IResultSkippingHandler> Handlers = null)
        {
            var RSM = new ResultSkippingManager(Handlers, this, Back);
            while (RSM.Loop())
            {
                var Res = SubstringEquals(RSM.Current, CmpWith, Skip, Back, IdCharCheck);
                if (Res != -1) return Res;
            }

            return -1;
        }

        public int Find(char CmpWith, bool Back = false, IList<IResultSkippingHandler> Handlers = null)
        {
            var Found = false;
            if (Back)
            {
                for (var i = Index + Length - 1; i >= Index; i--)
                    if (String[i] == CmpWith)
                    {
                        if (Handlers == null)
                            return i - Index;

                        Found = true;
                        break;
                    }
            }
            else
            {
                for (var i = Index; i < Index + Length; i++)
                    if (String[i] == CmpWith)
                    {
                        if (Handlers == null)
                            return i - Index;

                        Found = true;
                        break;
                    }
            }

            if (Found)
            {
                var RSM = new ResultSkippingManager(Handlers, this, Back);
                while (RSM.Loop())
                {
                    if (RSM.CurrentChar == CmpWith)
                        return RSM.Current;
                }
            }

            return -1;
        }

        public bool ValidIdentifierName
        {
            get
            {
                if (Length == 0) return false;

                var FirstChar = this[0];
                if (HasNonIdChar || (FirstChar >= '0' && FirstChar <= '9')) return false;
                return FirstChar != '_' || Length > 1;
            }
        }

        public bool IsNumber
        {
            get
            {
                for (var i = Index; i < Index + Length; i++)
                    if (!Char.IsDigit(String[i])) return false;

                return Length != 0;
            }
        }

        public bool ToNumber(int Radix, LetterCase Case, out BigInteger Ret)
        {
            Ret = 0;
            if (Length == 0)
                return false;

            for (var i = Index; i < Index + Length; i++)
            {
                var Chr = String[i];

                var Num = -1;
                if (Chr >= '0' && Chr <= '9')
                    Num = Chr - '0';
                else if (Chr >= 'a' && Chr <= 'z' && Case != LetterCase.OnlyUpper)
                    Num = Chr - 'a' + 10;
                else if (Chr >= 'A' && Chr <= 'Z' && Case != LetterCase.OnlyLower)
                    Num = Chr - 'A' + 10;

                if (Num == -1 || Num >= Radix) return false;
                else Ret = Ret * Radix + Num;
            }

            return true;
        }

        public int StrEndLetterCount(LetterCase Case = LetterCase.Both)
        {
            var Ret = 0;
            for (var i = Index + Length - 1; i >= Index; i--)
            {
                var C = String[i];
                if (Char.IsLetter(C))
                {
                    if ((Char.IsUpper(C) && Case != LetterCase.OnlyLower) ||
                        (Char.IsLower(C) && Case != LetterCase.OnlyUpper))
                    {
                        Ret++;
                        continue;
                    }
                }

                break;
            }

            return Ret;
        }

        public StringSlice CutEndStr(LetterCase Case = LetterCase.Both)
        {
            var C = StrEndLetterCount(Case);
            return C == 0 ? this : Substring(0, String.Length - C);
        }

        public StringSlice EndStr(LetterCase Case = LetterCase.Both)
        {
            var C = StrEndLetterCount(Case);
            return C == 0 ? Substring(Length) : Substring(String.Length - C);
        }

        public bool HasNonIdChar
        {
            get
            {
                for (var i = Index; i < Index + Length; i++)
                {
                    if (!Helper.IsIdChar(String[i]))
                        return true;
                }

                return false;
            }
        }

        public int LeftBracketPos(int Depth)
        {
            var b = 0;
            for (var i = Index; i < Index + Length; i++)
                if (String[i] == '(' && ++b == Depth) return i - Index;

            return -1;
        }

        public int RightBracketPos(int Depth)
        {
            var b = 0;
            for (var i = Index + Length - 1; i >= Index; i--)
                if (String[i] == ')' && ++b == Depth) return i - Index;

            return -1;
        }

        public int TrimmableBracketCount(IList<IResultSkippingHandler> Handlers = null)
        {
            var RSM = new ResultSkippingManager(Handlers, this);
            RSM.DoNotSkipBrackets = true;

            var Left = 0;
            var Right = 0;
            var Depth = 0;
            var MinDepth = int.MaxValue;
            var EndLeft = false;

            while (RSM.Loop())
            {
                var Chr = RSM.CurrentChar;
                if (Chr == ')')
                {
                    Depth--;
                    Right++;

                    if (Left == 0) return 0;
                    EndLeft = true;
                }
                else
                {
                    if (EndLeft && MinDepth > Depth)
                    {
                        MinDepth = Depth;
                        if (MinDepth == 0) return 0;
                    }

                    if (Chr == '(')
                    {
                        Depth++;
                        Right = 0;
                        if (!EndLeft) Left++;
                    }
                    else if (!char.IsWhiteSpace(Chr))
                    {
                        Right = 0;

                        if (Left == 0) return 0;
                        EndLeft = true;
                    }
                }
            }

            var LRMin = Left < Right ? Left : Right;
            return LRMin < MinDepth ? LRMin : MinDepth;
        }

        public int GetBracketPos(bool Back = false, IList<IResultSkippingHandler> Handlers = null)
        {
            if (String.Length < 2) return -1;

            var RSM = new ResultSkippingManager(Handlers, this, Back);
            RSM.DoNotSkipBrackets = true;
            char BeginChar, EndChar;
            var Depth = 1;

            if (Back)
            {
                var Chr = String[Index + Length - 1];
                if (Chr == ')')
                {
                    BeginChar = ')';
                    EndChar = '(';
                }
                else if (Chr == ']')
                {
                    BeginChar = ']';
                    EndChar = '[';
                }
                else if (Chr == '}')
                {
                    BeginChar = '}';
                    EndChar = '{';
                }
                else if (Chr == '>')
                {
                    BeginChar = '>';
                    EndChar = '<';
                }
                else
                {
                    throw new ApplicationException();
                }
            }
            else
            {
                var Chr = String[Index];
                if (Chr == '(')
                {
                    BeginChar = '(';
                    EndChar = ')';
                }
                else if (Chr == '[')
                {
                    BeginChar = '[';
                    EndChar = ']';
                }
                else if (Chr == '{')
                {
                    BeginChar = '{';
                    EndChar = '}';
                }
                else if (Chr == '<')
                {
                    BeginChar = '<';
                    EndChar = '>';
                }
                else
                {
                    throw new ApplicationException();
                }
            }

            if (!RSM.Loop()) return -1;
            while (RSM.Loop())
            {
                var Chr = RSM.CurrentChar;
                if (Chr == BeginChar)
                {
                    Depth++;
                }
                else if (Chr == EndChar)
                {
                    Depth--;
                    if (Depth == 0)
                        return RSM.Current;
                }
            }

            return -1;
        }

        public StringSlice TrimBrackets(int Count)
        {
            if (Count == 0) return Trim();
            var Left = LeftBracketPos(Count);
            var Right = RightBracketPos(Count);

            if (Left == -1 || Right == -1) return Substring(Length).Trim();
            else return Substring(Left + 1, Right - Left - 1).Trim();
        }

        public StringSlice TrimBrackets(IList<IResultSkippingHandler> Handlers = null)
        {
            var Count = TrimmableBracketCount(Handlers);
            if (Count == 0) return Substring(Length).Trim();
            return TrimBrackets(Count);
        }

        public StringSlice TrimOneBracket(IList<IResultSkippingHandler> Handlers = null)
        {
            if (CanTrimOneBracket())
                return Substring(1, String.Length - 2).Trim();

            return this;
        }

        public bool CanTrimOneBracket(IList<IResultSkippingHandler> Handlers = null)
        {
            if (Length > 0 && String[Index] == '(')
                return GetBracketPos(Handlers: Handlers) == Length - 1;

            return false;
        }

        public int WordEnd(bool WordStart = false, bool Back = false, Func<char, bool> Func = null,
            IList<IResultSkippingHandler> Handlers = null)
        {
            if (Length == 0) return -1;
            var RSM = new ResultSkippingManager(Handlers, this, Back);
            var Exit = false;
            var Prev = -1;

            while (RSM.Loop())
            {
                var Chr = RSM.CurrentChar;
                var Res = Func == null ? Helper.IsIdChar(Chr) : Func(Chr);

                if (Prev != -1)
                {
                    if (WordStart)
                    {
                        if (!Res) Exit = true;
                        else if (Exit) return Prev;
                    }
                    else
                    {
                        if (!Res) return Prev;
                    }
                }
                else if (!Res)
                {
                    if (WordStart) Exit = true;
                    else return -1;
                }

                Prev = RSM.Current;
            }

            return Back ? 0 : Length - 1;
        }

        public StringSlice Word(bool WordStart = false, bool Back = false, Func<char, bool> Func = null,
            bool ModThis = true, IList<IResultSkippingHandler> Handlers = null)
        {
            var p = WordEnd(WordStart, Back, Func, Handlers);
            if (p == -1) return Trim();

            StringSlice Ret;
            if (!Back) Ret = Substring(0, p + 1).Trim();
            else Ret = Substring(p).Trim();

            if (ModThis)
            {
                StringSlice Self;
                if (!Back) Self = Substring(p + 1).Trim();
                else Self = Substring(0, p).Trim();

                this.Index = Self.Index;
                this.Length = Self.Length;
            }

            return Ret;
        }

        public IEnumerable<SplitRes> EnumSplit(string Separator, StringSplitOptions SplitOptions = StringSplitOptions.None,
            bool Trim = false, IList<IResultSkippingHandler> Handlers = null)
        {
            if (string.IsNullOrEmpty(Separator))
                throw new ArgumentException("Separator");

            var RSM = new ResultSkippingManager(Handlers, this);
            var SeparatorLen = Separator.Length;
            RSM.String.Length -= SeparatorLen - 1;

            var BeginOfLast = 0;
            var SubStr = new StringSlice();

            while (RSM.Loop())
            {
                var i = RSM.Current;
                if (SubstringEqualsS(i, Separator))
                {
                    if (SplitOptions != StringSplitOptions.RemoveEmptyEntries || i > BeginOfLast)
                    {
                        SubStr = Substring(BeginOfLast, i - BeginOfLast);
                        if (Trim) SubStr = SubStr.Trim();
                        if (SubStr.Length > 0) yield return new SplitRes(SubStr, false);
                    }

                    yield return new SplitRes(Substring(i, SeparatorLen), true);
                    BeginOfLast = i + SeparatorLen;
                    i += SeparatorLen - 1;
                }
            }

            SubStr = Substring(BeginOfLast);
            if (Trim) SubStr = SubStr.Trim();
            if (SubStr.Length > 0) yield return new SplitRes(SubStr, false);
        }

        public IEnumerable<SplitRes> EnumSplit(char Separator, StringSplitOptions SplitOptions = StringSplitOptions.None,
            bool Trim = false, IList<IResultSkippingHandler> Handlers = null)
        {
            var RSM = new ResultSkippingManager(Handlers, this);
            var BeginOfLast = 0;
            var SubStr = new StringSlice();

            while (RSM.Loop())
                if (RSM.CurrentChar == Separator)
                {
                    var i = RSM.Current;
                    if (SplitOptions != StringSplitOptions.RemoveEmptyEntries || i > BeginOfLast)
                    {
                        SubStr = Substring(BeginOfLast, i - BeginOfLast);
                        if (Trim) SubStr = SubStr.Trim();
                        if (SubStr.Length > 0) yield return new SplitRes(SubStr, false);
                    }

                    yield return new SplitRes(Substring(i, 1), true);
                    BeginOfLast = i + 1;
                }

            SubStr = Substring(BeginOfLast);
            if (Trim) SubStr = SubStr.Trim();
            if (SubStr.Length > 0) yield return new SplitRes(SubStr, false);
        }

        public IEnumerable<StringSlice> EnumWords(Func<char, bool> Func = null)
        {
            var WordBegin = -1;
            for (var i = 0; i < Length; i++)
            {
                var Chr = String[Index + i];
                var Res = Func == null ? Helper.IsIdChar(Chr) : Func(Chr);

                if (!Res)
                {
                    if (WordBegin != -1)
                    {
                        yield return Substring(WordBegin, i - WordBegin);
                        WordBegin = -1;
                    }
                }
                else if (WordBegin == -1)
                {
                    WordBegin = i;
                }
            }
        }

        public bool IsEqual(string String)
        {
            if (Length != String.Length) return false;
            return SubstringEqualsS(0, String);
        }

        public bool IsEqual(StringSlice String)
        {
            if (Length != String.Length) return false;
            for (var i = 0; i < Length; i++)
            {
                if (String[i] != this.String[Index + i])
                    return false;
            }

            return true;
        }
    }
}
