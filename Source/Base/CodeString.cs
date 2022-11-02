using System;
using System.Collections.Generic;
using System.Numerics;

namespace Zinnia.Base;

public class CodeFile
{
    public string Content;

    public DataList Data = new();

    private LineData[] Lines;
    public string Path;
    private readonly int TabSize;

    public CodeFile(string Path, string Content, int TabSize = 1)
    {
        this.Path = Path;
        this.Content = Content;
        this.TabSize = TabSize;

        if (Path == null) throw new ArgumentNullException("Path");
        if (Content == null) throw new ArgumentNullException("Content");
        if (TabSize < 0) throw new ArgumentOutOfRangeException("TabSize");

        InitializeLines();
    }

    public void Update()
    {
        for (var i = 0; i < Lines.Length; i++)
            Lines[i].Update();
    }

    public void Update(int Line, int Count = 1)
    {
        if (Line < 0 || Line >= Lines.Length)
            throw new ArgumentOutOfRangeException("Line");

        if (Count < 0 || Line + Count > Lines.Length)
            throw new ArgumentOutOfRangeException("Count");

        for (var i = Line; i < Line + Count; i++)
            Lines[i].Update();
    }

    public void VerifyLineIndex(int Position, int Line)
    {
        if (Line < 0 || Line >= Lines.Length)
            throw new ArgumentOutOfRangeException("Line");

        if (Lines[Line].Position > Position)
            throw new ArgumentOutOfRangeException("Line");

        if (Line + 1 < Lines.Length && Lines[Line + 1].Position <= Position)
            throw new ArgumentOutOfRangeException("Line");
    }

    public void RemoveCode(int Index, int Length)
    {
        RemoveCode(Index, Length, GetLine(Index));
    }

    public unsafe void RemoveCode(int Index, int Length, int Line)
    {
        if (Index < 0 || Index >= Content.Length)
            throw new ArgumentOutOfRangeException("Index");

        if (Length < 0 || Index + Length > Content.Length)
            throw new ArgumentOutOfRangeException("Length");

        fixed (char* Ptr = Content)
        {
            for (var i = Index; i < Index + Length; i++)
                if (Ptr[i] != '\n' && Ptr[i] != '\r')
                    Ptr[i] = ' ';
        }

        Update(Line, GetLineCount(Line, Index + Length - 1));
    }

    public void RemoveCode(CodeString String)
    {
        if (String.File != this)
            throw new ArgumentException(null, "String");

        RemoveCode(String.Index, String.Length, String.Line);
    }

    public int GetLinePosition(int Line)
    {
        if (Line < 0 || Line >= Lines.Length)
            throw new ArgumentOutOfRangeException("Line");

        return Lines[Line].Position;
    }

    public void InitializeLines()
    {
        var LineCount = Helper.GetLineCount(Content);
        Lines = new LineData[LineCount];

        LineCount = 0;
        Helper.ProcessNewLines(Content, Pos =>
        {
            if (LineCount > 0 && Lines[LineCount - 1].Position >= Pos)
                throw new ApplicationException("Invalid line position");

            Lines[LineCount].Position = Pos;
            LineCount++;
        });

        Update();
    }

    private int CalculateLineIndent(int Line)
    {
        var Indent = 0;
        for (var i = Lines[Line].Position; i < Content.Length; i++)
        {
            var Chr = Content[i];
            if (Chr == '\t') Indent += TabSize;
            else if (Chr == ' ') Indent++;
            else if (Chr == '\r' || Chr == '\n') return 0;
            else break;
        }

        return Indent;
    }

    private ConditionResult CalculateIsEmpty(int Line)
    {
        var Until = Lines[Line].Position + GetLineLength(Line);
        for (var i = Lines[Line].Position; i < Until; i++)
            if (!char.IsWhiteSpace(Content[i]))
                return ConditionResult.False;

        return ConditionResult.True;
    }

    public bool IsEmptyLine(int Line)
    {
        if (Line < 0 || Line >= Lines.Length)
            throw new ArgumentOutOfRangeException("Line");

        var Result = Lines[Line].IsEmtpy;
        if (Result == ConditionResult.Unknown)
        {
            Result = CalculateIsEmpty(Line);
            Lines[Line].IsEmtpy = Result;
        }

        return Result == ConditionResult.True;
    }

    public int GetLineCount()
    {
        return Lines.Length;
    }

    public int GetLineCount(int FirstLine, int End = int.MaxValue)
    {
        if (FirstLine < 0 || FirstLine >= Lines.Length)
            throw new ArgumentOutOfRangeException("FirstLine");

        var Result = 0;
        for (var i = FirstLine; i < Lines.Length; i++)
            if (Lines[i].Position > End) break;
            else Result++;

        return Result;
    }

    public int GetIndent(int Line)
    {
        if (Line < 0 || Line >= Lines.Length)
            throw new ArgumentOutOfRangeException("Line");

        if (IsEmptyLine(Line))
            return 0;

        var Result = Lines[Line].Indent;
        if (Result == -1)
        {
            Result = CalculateLineIndent(Line);
            Lines[Line].Indent = Result;
        }

        return Result;
    }

    public int GetDistance(int Line, int Position)
    {
        if (Line < 0 || Line >= Lines.Length)
            throw new ArgumentOutOfRangeException("Line");

        if (Position < 0 || Position > Content.Length)
            throw new ArgumentOutOfRangeException("Position");

        if (TabSize == 1) return Position - Lines[Line].Position;

        var Result = 0;
        for (var i = Lines[Line].Position; i < Position; i++)
            if (Content[i] != '\t') Result++;
            else Result += TabSize;

        return Result;
    }

    public int GetDistanceOrIndent(int Line, int Position)
    {
        if (Line < 0 || Line >= Lines.Length)
            throw new ArgumentOutOfRangeException("Line");

        if (Position < 0 || Position > Content.Length)
            throw new ArgumentOutOfRangeException("Position");

        var Distance = GetDistance(Line, Position);
        var Indent = GetIndent(Line);
        return Distance < Indent ? Indent : Distance;
    }

    public int GetLineLength(int Line)
    {
        if (Line < 0 || Line >= Lines.Length)
            throw new ArgumentOutOfRangeException("Line");

        var Start = Lines[Line].Position;
        if (Line + 1 == Lines.Length) return Content.Length - Start;
        return Lines[Line + 1].Position - Start;
    }

    public int GetLineLength(int Line, int Position)
    {
        if (Line < 0 || Line >= Lines.Length)
            throw new ArgumentOutOfRangeException("Line");

        if (Position < 0 || Position > Content.Length)
            throw new ArgumentOutOfRangeException("Position");

        if (Line + 1 == Lines.Length) return Content.Length - Position;
        return Lines[Line + 1].Position - Position;
    }

    public CodeString GetLines(int Line, int Count = 1)
    {
        if (Line < 0 || Line >= Lines.Length)
            throw new ArgumentOutOfRangeException("Line");

        if (Count < 0 || Line + Count > Lines.Length)
            throw new ArgumentOutOfRangeException("Count");

        if (Line + Count == Lines.Length)
        {
            var Pos = Lines[Line].Position;
            return new CodeString(this, Pos, Content.Length - Pos, Line);
        }
        else
        {
            var Pos = Lines[Line].Position;
            var Length = Lines[Line + Count].Position - Pos;
            return new CodeString(this, Pos, Length, Line);
        }
    }

    public int GetLine(int Index)
    {
        if (Index < 0 || Index >= Content.Length)
            throw new ArgumentOutOfRangeException("Index");

        for (var i = 0; i < Lines.Length; i++)
            if (Lines[i].Position > Index)
                return i - 1;

        return Lines.Length;
    }

    private struct LineData
    {
        public int Position;
        public int Indent;
        public ConditionResult IsEmtpy;

        public void Update()
        {
            Indent = -1;
            IsEmtpy = ConditionResult.Unknown;
        }
    }
}

public struct CodeString
{
    public CodeFile File;
    public StringSlice String;
    public int Line;

    public bool IsValid => String.String != null;

    public int Length => String.Length;

    public int Index => String.Index;

    public CodeString(CodeFile File, int Index, int Length, int Line)
    {
        this.File = File;
        String = new StringSlice(File.Content, Index, Length);
        this.Line = Line;
        File.VerifyLineIndex(String.Index, Line);
    }

    public CodeString(CodeFile File, int Index, int Length)
    {
        this.File = File;
        String = new StringSlice(File.Content, Index, Length);
        Line = File.GetLineCount(0, Index) - 1;
    }

    public CodeString(CodeFile File, StringSlice String)
    {
        if (!ReferenceEquals(File.Content, String.String))
            throw new ArgumentException("The string can't be part of the file", "String");

        this.File = File;
        this.String = String;
        Line = File.GetLineCount(0, String.Index) - 1;
    }

    public CodeString(CodeFile File)
    {
        this.File = File;
        String = new StringSlice(File.Content);
        Line = 0;
    }

    public CodeString(StringSlice String)
    {
        File = null;
        this.String = String;
        Line = 0;
    }

    public CodeString(string String)
    {
        File = null;
        this.String = new StringSlice(String);
        Line = 0;
    }

    public bool Contains(char Char)
    {
        return Find(Char) != -1;
    }

    public bool Contains(string String)
    {
        return Find(String) != -1;
    }

    public IEnumerable<CodeString> EnumLines()
    {
        var Line = this.Line;
        while (HasLine(Line))
        {
            yield return GetLine(Line);
            Line++;
        }
    }

    public CodeString Intersection(int Index, int Length)
    {
        if (!IsValid) throw new InvalidOperationException();
        if (Index < this.Index)
        {
            Length -= this.Index - Index;
            Index = this.Index;
        }

        var MinLength = Length < this.Length ? Length : this.Length;
        var NewLine = Line;
        if (Line < File.GetLineCount() - 1)
            NewLine += File.GetLineCount(Line + 1, Index);

        return new CodeString(File, Index, MinLength, NewLine);
    }

    public CodeString GetLine(int Line)
    {
        if (!IsValid) throw new InvalidOperationException();
        if (Line < 0 || Line >= File.GetLineCount())
            throw new ArgumentOutOfRangeException("Line");

        if (this.Line == Line) return FirstLine;
        if (!HasLine(Line)) return new CodeString();

        var Start = File.GetLinePosition(Line);
        var Length = File.GetLineLength(Line);
        return Intersection(Start, Length);
    }

    public CodeString GetLines(int Line, int Count = 1)
    {
        if (!IsValid) throw new InvalidOperationException();
        var AllLines = File.GetLineCount();
        if (Line < 0 || Line >= AllLines)
            throw new ArgumentOutOfRangeException("Line");

        if (Count < 0 || Line + Count > AllLines)
            throw new ArgumentOutOfRangeException("Count");

        var Start = File.GetLinePosition(Line);
        var EndPos = Line + Count == AllLines
            ? File.Content.Length
            : File.GetLinePosition(Line + Count);

        return Intersection(Start, EndPos - Start);
    }

    public int LeftWhiteSpaces => String.LeftWhiteSpaces;

    public int RightWhiteSpaces => String.RightWhiteSpaces;

    public bool HasLine(int Line)
    {
        if (!IsValid) throw new InvalidOperationException();

        if (Length == 0) return false;
        if (this.Line > Line || File.GetLineCount() <= Line) return false;
        return File.GetLinePosition(Line) <= End;
    }

    public int LineCount
    {
        get
        {
            if (!IsValid) throw new InvalidOperationException();
            return 1 + File.GetLineCount(Line + 1, End);
        }
    }

    public char this[int Index] => String[Index];

    public int FirstLineLength
    {
        get
        {
            if (!IsValid) throw new InvalidOperationException();
            var Ret = File.GetLineLength(Line, Index);
            return Ret > Length ? Length : Ret;
        }
    }

    public CodeString FirstLine
    {
        get
        {
            if (!IsValid) throw new InvalidOperationException();
            return Substring(0, FirstLineLength);
        }
    }

    public int End
    {
        get
        {
            if (!IsValid) throw new InvalidOperationException();
            return Index + Length - 1;
        }
    }

    public CodeString Substring(StringSlice String)
    {
        if (!ReferenceEquals(this.String.String, String.String))
            throw new ArgumentException("The string must be part of the file", "String");

        return Substring(String.Index - this.String.Index, String.Length);
    }

    public CodeString Substring(int Index, int Length)
    {
        if (!IsValid) throw new InvalidOperationException();
        Helper.Verify(this.Length, Index, Length);

        if (File == null)
            return new CodeString(String.Substring(Index, Length));

        var NewPosition = String.Index + Index;
        var NewLine = Line;
        if (Line < File.GetLineCount() - 1)
            NewLine += File.GetLineCount(Line + 1, NewPosition);

        return new CodeString(File, NewPosition, Length, NewLine);
    }

    public CodeString Substring(int Index)
    {
        if (!IsValid) throw new InvalidOperationException();
        Helper.Verify(Length, Index);

        if (File == null)
            return new CodeString(String.Substring(Index));

        var NewPosition = String.Index + Index;
        var NewLine = Line;
        if (Line < File.GetLineCount() - 1)
            NewLine += File.GetLineCount(Line + 1, NewPosition);

        return new CodeString(File, NewPosition, Length - Index, NewLine);
    }

    public CodeString Substring(FindResult FindRes)
    {
        return Substring(FindRes.Index, FindRes.String.Length);
    }

    public CodeString TrimmedSubstring(CompilerState State, int Index, int Length, bool EnableMessages = true)
    {
        var Ret = Substring(Index, Length).Trim();
        if (Ret.Length == 0)
        {
            if (EnableMessages)
                State.Messages.Add(MessageId.DeficientExpr, this);

            return new CodeString();
        }

        return Ret;
    }

    public bool VerifyNotEmpty(CompilerState State, CodeString Code, bool EnableMessages = true)
    {
        if (Length == 0)
        {
            if (EnableMessages)
                State.Messages.Add(MessageId.DeficientExpr, Code);

            return false;
        }

        return true;
    }

    public CodeString TrimmedSubstring(CompilerState State, int Index, bool EnableMessages = true)
    {
        var Ret = Substring(Index).Trim();
        if (!Ret.VerifyNotEmpty(State, this, EnableMessages)) return new CodeString();
        return Ret;
    }

    public CodeString Trim()
    {
        var i = LeftWhiteSpaces;
        if (i == Length) return Substring(0, 0);

        var j = RightWhiteSpaces;
        return Substring(i, Length - i - j);
    }

    public IEnumerable<SplitRes> EnumSplit(string Separator, StringSplitOptions SplitOptions = StringSplitOptions.None,
        bool Trim = false, IList<IResultSkippingHandler> Handlers = null)
    {
        return String.EnumSplit(Separator, SplitOptions, Trim, Handlers);
    }

    public IEnumerable<SplitRes> EnumSplit(char Separator, StringSplitOptions SplitOptions = StringSplitOptions.None,
        bool Trim = false, IList<IResultSkippingHandler> Handlers = null)
    {
        return String.EnumSplit(Separator, SplitOptions, Trim, Handlers);
    }

    public List<CodeString> Split(string Separator, StringSplitOptions SplitOptions = StringSplitOptions.None,
        bool Trim = false, IList<IResultSkippingHandler> Handlers = null)
    {
        var Ret = new List<CodeString>();
        foreach (var e in EnumSplit(Separator, SplitOptions, Trim, Handlers))
            if (!e.IsSeparator)
                Ret.Add(Substring(e.String));

        return Ret;
    }

    public List<CodeString> Split(char Separator, StringSplitOptions SplitOptions = StringSplitOptions.None,
        bool Trim = false, IList<IResultSkippingHandler> Handlers = null)
    {
        var Ret = new List<CodeString>();
        foreach (var e in EnumSplit(Separator, SplitOptions, Trim, Handlers))
            if (!e.IsSeparator)
                Ret.Add(Substring(e.String));

        return Ret;
    }

    public int WordEnd(bool WordStart = false, bool Back = false, Func<char, bool> Func = null,
        IList<IResultSkippingHandler> Handlers = null)
    {
        return String.WordEnd(WordStart, Back, Func, Handlers);
    }

    public CodeString Word(bool WordStart = false, bool Back = false, Func<char, bool> Func = null,
        bool ModThis = true, IList<IResultSkippingHandler> Handlers = null)
    {
        var p = WordEnd(WordStart, Back, Func, Handlers);
        if (p == -1) return Substring(0, 0);

        CodeString Ret;
        if (!Back) Ret = Substring(0, p + 1).Trim();
        else Ret = Substring(p).Trim();

        if (ModThis)
        {
            CodeString String;
            if (!Back) String = Substring(p + 1).Trim();
            else String = Substring(0, p).Trim();

            File = String.File;
            this.String = String.String;
            Line = String.Line;
        }

        return Ret;
    }

    public bool SubstringEqualsS(int Index, string CmpWith, IdCharCheck IdCharCheck = new())
    {
        return String.SubstringEqualsS(Index, CmpWith, IdCharCheck);
    }

    public bool StartsWith(string CmpWith, IdCharCheck IdCharCheck = new())
    {
        return String.StartsWith(CmpWith, IdCharCheck);
    }

    public bool EndsWith(string CmpWith, IdCharCheck IdCharCheck = new())
    {
        return String.EndsWith(CmpWith, IdCharCheck);
    }

    public int SubstringEquals(int Index, string CmpWith, string[] Skip = null, bool Back = false,
        IdCharCheck IdCharCheck = new())
    {
        return String.SubstringEquals(Index, CmpWith, Skip, Back, IdCharCheck);
    }

    public FindResult SubstringEquals(int Index, string[] CmpWith, string[] Skip = null,
        bool Back = false, IdCharCheck IdCharCheck = new())
    {
        return String.SubstringEquals(Index, CmpWith, Skip, Back, IdCharCheck);
    }

    public FindResult StartsWith(string[] CmpWith, string[] Skip = null, IdCharCheck IdCharCheck = new())
    {
        return String.StartsWith(CmpWith, Skip, IdCharCheck);
    }

    public FindResult EndsWith(string[] CmpWith, string[] Skip = null, IdCharCheck IdCharCheck = new())
    {
        return String.EndsWith(CmpWith, Skip, IdCharCheck);
    }

    public IEnumerable<FindResult> EnumFind(string[] CmpWith, string[] Skip = null, bool Back = false,
        IdCharCheck IdCharCheck = new(), IList<IResultSkippingHandler> Handlers = null)
    {
        return String.EnumFind(CmpWith, Skip, Back, IdCharCheck, Handlers);
    }

    public FindResult Find(string[] CmpWith, string[] Skip = null, bool Back = false,
        IdCharCheck IdCharCheck = new(), IList<IResultSkippingHandler> Handlers = null)
    {
        return String.Find(CmpWith, Skip, Back, IdCharCheck, Handlers);
    }

    public IEnumerable<int> EnumFind(string CmpWith, string[] Skip = null, bool Back = false,
        IdCharCheck IdCharCheck = new(), IList<IResultSkippingHandler> Handlers = null)
    {
        return String.EnumFind(CmpWith, Skip, Back, IdCharCheck, Handlers);
    }

    public int Find(string CmpWith, string[] Skip = null, bool Back = false,
        IdCharCheck IdCharCheck = new(), IList<IResultSkippingHandler> Handlers = null)
    {
        return String.Find(CmpWith, Skip, Back, IdCharCheck, Handlers);
    }

    public int Find(char CmpWith, bool Back = false, IList<IResultSkippingHandler> Handlers = null)
    {
        return String.Find(CmpWith, Back, Handlers);
    }

    public static bool operator ==(CodeString Str1, CodeString Str2)
    {
        return Str1.IsEqual(Str2);
    }

    public static bool operator !=(CodeString Str1, CodeString Str2)
    {
        return !Str1.IsEqual(Str2);
    }

    public static bool operator ==(CodeString Str1, string Str2)
    {
        return Str1.IsEqual(Str2);
    }

    public static bool operator !=(CodeString Str1, string Str2)
    {
        return !Str1.IsEqual(Str2);
    }

    public static bool operator ==(string Str1, CodeString Str2)
    {
        return Str2.IsEqual(Str1);
    }

    public static bool operator !=(string Str1, CodeString Str2)
    {
        return !Str2.IsEqual(Str1);
    }

    public bool IsEqual(string String)
    {
        return this.String.IsEqual(String);
    }

    public bool IsEqual(StringSlice String)
    {
        return this.String.IsEqual(String);
    }

    public bool IsEqual(CodeString String)
    {
        return this.String.IsEqual(String.String);
    }

    public override bool Equals(object obj)
    {
        if (obj is CodeString)
        {
            var CodeStr = (CodeString)obj;
            return CodeStr.File == File && CodeStr.Index == Index &&
                   CodeStr.Length == Length;
        }

        return false;
    }

    public override int GetHashCode()
    {
        return Index ^ Length;
    }

    public bool IsValidIdentifierName => String.ValidIdentifierName;

    public bool HasNonIdChar => String.HasNonIdChar;

    public bool IsNumber => String.IsNumber;

    public bool ToNumber(int Radix, LetterCase Case, out BigInteger Ret)
    {
        return String.ToNumber(Radix, Case, out Ret);
    }

    public int StrEndLetterCount(LetterCase Case = LetterCase.Both)
    {
        return String.StrEndLetterCount(Case);
    }

    public CodeString CutEndStr(LetterCase Case = LetterCase.Both)
    {
        var C = StrEndLetterCount(Case);
        return C == 0 ? this : Substring(0, String.Length - C);
    }

    public CodeString EndStr(LetterCase Case = LetterCase.Both)
    {
        var C = StrEndLetterCount(Case);
        return C == 0 ? Substring(Length) : Substring(String.Length - C);
    }

    public int TrimmableBracketCount(IList<IResultSkippingHandler> Handlers = null)
    {
        return String.TrimmableBracketCount();
    }

    public int GetBracketPos(bool Back = false, IList<IResultSkippingHandler> Handlers = null)
    {
        return String.GetBracketPos(Back, Handlers);
    }

    public int GetBracketPos(CompilerState State, bool Back = false, bool EnableMessages = true)
    {
        var Handlers = State.Language.GlobalHandlers;
        var Res = String.GetBracketPos(Back, Handlers);
        if (Res == -1)
        {
            var ErrStr = Back ? Substring(Length - 1) : Substring(0, 1);
            if (EnableMessages) State.Messages.Add(MessageId.ZNumErr, ErrStr);
        }

        return Res;
    }

    public bool CanTrimOneBracket(IList<IResultSkippingHandler> Handlers = null)
    {
        return String.CanTrimOneBracket(Handlers);
    }

    public CodeString TrimOneBracket(IList<IResultSkippingHandler> Handlers = null)
    {
        if (CanTrimOneBracket())
            return Substring(1, String.Length - 2).Trim();

        return this;
    }

    public int LeftBracketPos(int Depth)
    {
        return String.LeftBracketPos(Depth);
    }

    public int RightBracketPos(int Depth)
    {
        return String.RightBracketPos(Depth);
    }

    public CodeString TrimBrackets(int Count)
    {
        if (Count == 0) return Trim();
        var Left = LeftBracketPos(Count);
        var Right = RightBracketPos(Count);

        if (Left == -1 || Right == -1) return Substring(Length).Trim();
        return Substring(Left + 1, Right - Left - 1).Trim();
    }

    public CodeString TrimBrackets(IList<IResultSkippingHandler> Handlers = null)
    {
        var Count = TrimmableBracketCount(Handlers);
        return Count == 0 ? this : TrimBrackets(Count);
    }

    public CodeString TrimBrackets(CompilerState State, int Count, bool Messages = true)
    {
        var Ret = TrimBrackets(Count);
        if (!Ret.IsValid)
        {
            State.Messages.Add(MessageId.ZNumErr, this);
            return new CodeString();
        }

        if (Ret.Length == 0)
        {
            State.Messages.Add(MessageId.DeficientExpr, this);
            return new CodeString();
        }

        return Ret;
    }

    public CodeString TrimBrackets(CompilerState State, bool Messages = true)
    {
        var Handlers = State.Language.GlobalHandlers;
        var Count = TrimmableBracketCount(Handlers);
        return TrimBrackets(State, Count, Messages);
    }

    public override string ToString()
    {
        return String.ToString();
    }

    public IEnumerable<CodeString> EnumWords(Func<char, bool> Func = null)
    {
        foreach (var e in String.EnumWords(Func))
            yield return Substring(e);
    }
}