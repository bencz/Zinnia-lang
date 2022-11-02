using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Zinnia.Base;

namespace Zinnia;

public struct ConvStrToNumberOptions
{
    public ConstValueType Type;
    public LetterCase LetterCase;
    public StringSlice Number;
    public int Radix;
    public bool EnableENotation;
    public bool Sign;

    public static ConvStrToNumberOptions Default
    {
        get
        {
            var Ret = new ConvStrToNumberOptions();
            Ret.Radix = 10;
            Ret.LetterCase = LetterCase.Both;
            Ret.EnableENotation = true;
            return Ret;
        }
    }
}

public struct ConvNumberPartsOptions
{
    public ConstValueType Type;
    public BigInteger Number;
    public BigInteger Fraction;
    public BigInteger ENotation;
    public int Radix;
    public int FractionLength;
    public bool UseFraction;

    public static ConvNumberPartsOptions Default
    {
        get
        {
            var Ret = new ConvNumberPartsOptions();
            Ret.Radix = 10;
            return Ret;
        }
    }
}

public delegate ExpressionNode ExtractTupleOpFunc(int Index, ExpressionNode[] Ch);

public interface IFind2Handler
{
    bool IsAcceptable(IdContainer Container, StringSlice String);
}

public static class RecognizerHelper
{
    public static StringSlice CutOffSign(StringSlice String)
    {
        for (var i = 0; i < String.Length; i++)
        {
            var Char = String[i];
            if (Char == '+' || Char == '-') continue;
            return String.Substring(i);
        }

        return String;
    }

    public static CodeString CutOffSign(CodeString String)
    {
        for (var i = 0; i < String.Length; i++)
        {
            var Char = String[i];
            if (Char == '+' || Char == '-') continue;
            return String.Substring(i);
        }

        return String;
    }

    public static bool GetSign(ref StringSlice String)
    {
        var Negative = false;
        for (var i = 0; i < String.Length; i++)
        {
            var Char = String[i];
            if (Char == '-')
            {
                Negative = !Negative;
            }
            else
            {
                if (Char == '+') continue;

                String = String.Substring(i);
                break;
            }
        }

        return Negative;
    }

    public static bool GetSign(ref CodeString String)
    {
        var Negative = false;
        for (var i = 0; i < String.Length; i++)
        {
            var Char = String[i];
            if (Char == '-')
            {
                Negative = !Negative;
            }
            else
            {
                if (Char == '+') continue;

                if (i != 0)
                    String = String.Substring(i);

                break;
            }
        }

        return Negative;
    }

    public static SimpleRecResult ConvStrToNumber(CompilerState State, CodeString Code,
        ConvStrToNumberOptions Options, out ConstValue Value)
    {
        Value = null;
        var Radix = Options.Radix;
        var LetterCase = Options.LetterCase;

        var Options2 = new ConvNumberPartsOptions();
        Options2.Radix = Radix;
        Options2.Type = Options.Type;

        var Number = Options.Number;
        var Negative = GetSign(ref Number);
        if (Options.Sign) Negative = !Negative;

        var PointChk = Number;
        var ENotationPos = -1;

        if (Options.EnableENotation)
        {
            if (Options.LetterCase != LetterCase.OnlyLower)
                ENotationPos = PointChk.Find('e');

            if (ENotationPos == -1 && Options.LetterCase != LetterCase.OnlyUpper)
                ENotationPos = PointChk.Find('E');
        }

        if (ENotationPos != -1)
        {
            PointChk = PointChk.Substring(0, ENotationPos);
            var ENotation = Code.Substring(ENotationPos + 1);
            var ENegative = GetSign(ref ENotation);

            if (!ENotation.ToNumber(Radix, LetterCase, out Options2.ENotation))
                return SimpleRecResult.Unknown;

            Options2.UseFraction = true;
            if (ENegative) Options2.ENotation = -Options2.ENotation;
        }

        var Point = PointChk.Find('.');
        if (Point == -1)
        {
            if (!PointChk.ToNumber(Radix, LetterCase, out Options2.Number))
                return SimpleRecResult.Unknown;

            if (Negative) Options2.Number = -Options2.Number;
        }
        else
        {
            var Left = PointChk.Substring(0, Point);
            var Right = PointChk.Substring(Point + 1);
            Options2.UseFraction = true;
            Options2.FractionLength = Right.Length;

            if (!Left.ToNumber(Radix, LetterCase, out Options2.Number))
                return SimpleRecResult.Unknown;
            if (!Right.ToNumber(Radix, LetterCase, out Options2.Fraction))
                return SimpleRecResult.Unknown;

            if (Negative)
            {
                Options2.Number = -Options2.Number;
                Options2.Fraction = -Options2.Fraction;
            }
        }

        return ConvNumberParts(State, Code, Options2, out Value);
    }

    public static SimpleRecResult ConvNumberParts(CompilerState State, CodeString Code,
        ConvNumberPartsOptions Options, out ConstValue Value)
    {
        Value = null;
        var Number = Options.Number;
        var Fraction = Options.Fraction;
        var Type = Options.Type;
        var ENotation = Options.ENotation;
        var Radix = Options.Radix;
        var UseFraction = Options.UseFraction;

        if (Type == ConstValueType.Unknown)
        {
            if (UseFraction) Type = ConstValueType.Double;
            else Type = ConstValueType.Integer;
        }
        else if (UseFraction && Type != ConstValueType.Double && Type != ConstValueType.Float)
        {
            State.Messages.Add(MessageId.ConstOutOfRange, Code);
            return SimpleRecResult.Failed;
        }

        if (Type == ConstValueType.Double || Type == ConstValueType.Float)
        {
            var Ret = (double)Number;
            if (UseFraction)
            {
                var N = BigInteger.Pow(Radix, Options.FractionLength);
                Ret += (double)Fraction / (double)N;
            }

            if (ENotation > 0)
            {
                if (ENotation > int.MaxValue)
                {
                    State.Messages.Add(MessageId.ConstOutOfRange, Code);
                    return SimpleRecResult.Failed;
                }

                for (var i = 0; i < (int)ENotation; i++)
                    Ret *= Radix;
            }
            else if (ENotation < 0)
            {
                if (ENotation < int.MinValue)
                {
                    State.Messages.Add(MessageId.ConstOutOfRange, Code);
                    return SimpleRecResult.Failed;
                }

                for (var i = (int)ENotation; i < 0; i++)
                    Ret /= Radix;
            }

            if (double.IsInfinity(Ret))
            {
                State.Messages.Add(MessageId.ConstOutOfRange, Code);
                return SimpleRecResult.Failed;
            }

            if (Type == ConstValueType.Double) Value = new DoubleValue(Ret);
            else Value = new FloatValue((float)Ret);
            return SimpleRecResult.Succeeded;
        }

        if (Type == ConstValueType.Integer)
        {
            Value = new IntegerValue(Number);
            return SimpleRecResult.Succeeded;
        }

        throw new ApplicationException();
    }

    public static CodeString ExtractBracket(CompilerState State, CodeString ErrCode,
        char Bracket, ref CodeString Code, bool Back = false, bool EnableMessages = true)
    {
        if (Code.Length == 0)
        {
            State.Messages.Add(MessageId.DeficientExpr, ErrCode);
            return new CodeString();
        }

        if (Back)
        {
            if (Code[Code.Length - 1] != Bracket)
            {
                State.Messages.Add(MessageId.DeficientExpr, ErrCode);
                return new CodeString();
            }

            var BracketPos = Code.GetBracketPos(State, true, EnableMessages);
            if (BracketPos == -1) return new CodeString();

            var InsideBracket = Code.Substring(BracketPos + 1, Code.Length - BracketPos - 2).Trim();
            if (!InsideBracket.IsValid) return new CodeString();

            Code = Code.Substring(0, BracketPos).Trim();
            return InsideBracket;
        }
        else
        {
            if (Code[0] != Bracket)
            {
                State.Messages.Add(MessageId.DeficientExpr, ErrCode);
                return new CodeString();
            }

            var BracketPos = Code.GetBracketPos(State, false, EnableMessages);
            if (BracketPos == -1) return new CodeString();

            var InsideBracket = Code.Substring(1, BracketPos - 1).Trim();
            if (!InsideBracket.IsValid) return new CodeString();

            Code = Code.Substring(BracketPos + 1).Trim();
            return InsideBracket;
        }
    }

    public static SimpleRecResult ExtractBracket(CompilerState State, char Bracket, ref CodeString Code,
        out CodeString Word, out CodeString InsideBracket, bool EnableMessages = true)
    {
        Word = Code.Word(Handlers: State.Language.GlobalHandlers);
        if (Word.Length == 0)
        {
            InsideBracket = new CodeString();
            return SimpleRecResult.Unknown;
        }

        InsideBracket = ExtractBracket(State, Word, Bracket, ref Code, false, EnableMessages);
        return InsideBracket.IsValid ? SimpleRecResult.Succeeded : SimpleRecResult.Failed;
    }

    public static SimpleRecResult ExtractBracket(CompilerState State, string[] Find, string[] Skip,
        char Bracket, ref CodeString Code, out FindResult Result, out CodeString InsideBracket,
        out CodeString CuttedCode, bool EnableMessages = true)
    {
        var OldCode = Code;
        Result = Code.StartsWith(Find, Skip);
        if (Result.Index == -1)
        {
            InsideBracket = new CodeString();
            CuttedCode = new CodeString();
            return SimpleRecResult.Unknown;
        }

        var Word = Code.Substring(0, Result.String.Length);
        Code = Code.Substring(Result.String.Length).Trim();

        InsideBracket = ExtractBracket(State, Word, Bracket, ref Code, false, EnableMessages);
        CuttedCode = Code.Substring(OldCode.Length - Code.Length);
        return InsideBracket.IsValid ? SimpleRecResult.Succeeded : SimpleRecResult.Failed;
    }

    public static SimpleRecResult ExtractBracket(CompilerState State, string Find, char Bracket,
        ref CodeString Code, out CodeString InsideBracket, out CodeString CuttedCode, bool EnableMessages = true)
    {
        var OldCode = Code;
        if (!Code.StartsWith(Find))
        {
            InsideBracket = new CodeString();
            CuttedCode = new CodeString();
            return SimpleRecResult.Unknown;
        }

        var Word = Code.Substring(0, Find.Length);
        Code = Code.Substring(Find.Length).Trim();

        InsideBracket = ExtractBracket(State, Word, Bracket, ref Code, false, EnableMessages);
        CuttedCode = OldCode.Substring(OldCode.Length - Code.Length);
        return InsideBracket.IsValid ? SimpleRecResult.Succeeded : SimpleRecResult.Failed;
    }

    public static string ProcessString(CodeString Code, PluginRoot Plugin, char Char)
    {
        var Ret = "";
        var RetValue = true;
        var LastPos = 0;
        var String = Code.ToString();

        for (var i = 0; i < String.Length; i++)
            if (String[i] == Char)
            {
                var Length = -1;
                Ret += String.Substring(LastPos, i - LastPos);

                if (i < String.Length - 1)
                {
                    var NextChar = Code[i + 1];
                    if (char.IsDigit(NextChar))
                    {
                        var CharCode = NextChar - '0';
                        Length = 1;

                        for (var j = i + 2; j < String.Length; j++)
                            if (char.IsDigit(Code[j]))
                            {
                                Length++;
                                if (Length < 6)
                                {
                                    CharCode *= 10;
                                    CharCode += Code[j] - '0';
                                }
                            }
                            else
                            {
                                break;
                            }

                        if (Length > 5 || CharCode > char.MaxValue)
                        {
                            var PStr = Code.Substring(i + 1, Length);
                            Plugin.State.Messages.Add(MessageId.ConstOutOfRange, PStr);
                            RetValue = false;
                        }

                        Ret += (char)CharCode;
                    }
                    else
                    {
                        Length = 1;
                        if (NextChar == 't') Ret += "\t";
                        else if (NextChar == 'q') Ret += "\"";
                        else if (NextChar == 'r') Ret += "\r";
                        else if (NextChar == 'n') Ret += "\n";
                        else if (NextChar == Char) Ret += Char;
                        else Length = -1;
                    }
                }

                if (Length == -1)
                {
                    var PStrChar = Code.Substring(i, 1);
                    Plugin.State.Messages.Add(MessageId.InvalidEscapeSequence, PStrChar);
                    RetValue = false;
                }
                else
                {
                    LastPos = i + Length + 1;
                    i += Length;
                }
            }

        if (!RetValue) return null;
        Ret += String.Substring(LastPos);
        return Ret;
    }

    public static bool SplitToParameters(CompilerState State, CodeString Self, string Separator,
        List<CodeString> Ret, bool EnableMessages = true, bool EnableEmpty = false)
    {
        Self = Self.Trim();
        if (Self.Length == 0)
            return true;

        var SkipHandlers = State.Language.GlobalHandlers;
        var RetValue = true;
        var WasSeparator = true;
        var Sep = new CodeString();

        foreach (var e in Self.EnumSplit(Separator, StringSplitOptions.RemoveEmptyEntries, true, SkipHandlers))
            if (e.IsSeparator)
            {
                if (WasSeparator)
                {
                    if (!EnableEmpty)
                    {
                        if (EnableMessages) State.Messages.Add(MessageId.MissingParam, Sep);
                        RetValue = false;
                    }

                    Ret.Add(new CodeString());
                }

                Sep = Self.Substring(e.String);
                WasSeparator = true;
            }
            else
            {
                Ret.Add(Self.Substring(e.String));
                Sep = new CodeString();
                WasSeparator = false;
            }

        if (WasSeparator)
        {
            if (!EnableEmpty)
            {
                if (EnableMessages) State.Messages.Add(MessageId.MissingParam, Sep);
                RetValue = false;
            }

            Ret.Add(new CodeString());
        }

        return RetValue;
    }

    public static bool SplitToParameters(CompilerState State, CodeString Self, char Separator,
        List<CodeString> Ret, bool EnableMessages = true, bool EnableEmpty = false)
    {
        Self = Self.Trim();
        if (Self.Length == 0)
            return true;

        if (Self.Find(Separator) == -1)
        {
            Ret.Add(Self);
            return true;
        }

        var SkipHandlers = State.Language.GlobalHandlers;
        var RetValue = true;
        var WasSeparator = true;
        var Sep = new CodeString();

        foreach (var e in Self.EnumSplit(Separator, StringSplitOptions.RemoveEmptyEntries, true, SkipHandlers))
            if (e.IsSeparator)
            {
                if (WasSeparator)
                {
                    if (!EnableEmpty)
                    {
                        if (EnableMessages) State.Messages.Add(MessageId.MissingParam, Sep);
                        RetValue = false;
                    }

                    Ret.Add(new CodeString());
                }

                Sep = Self.Substring(e.String);
                WasSeparator = true;
            }
            else
            {
                Ret.Add(Self.Substring(e.String));
                Sep = new CodeString();
                WasSeparator = false;
            }

        if (WasSeparator)
        {
            if (!EnableEmpty)
            {
                if (EnableMessages) State.Messages.Add(MessageId.MissingParam, Sep);
                RetValue = false;
            }

            Ret.Add(new CodeString());
        }

        return RetValue;
    }

    public static CodeString[] SplitToParameters(CompilerState State, CodeString Self,
        char Separator, bool EnableMessages = true, bool EnableEmpty = false)
    {
        var Ret = new List<CodeString>();
        if (!SplitToParameters(State, Self, Separator, Ret, EnableMessages, EnableEmpty))
            return null;

        return Ret.ToArray();
    }

    public static CodeString[] SplitToParameters(CompilerState State, CodeString Self,
        string Separator, bool EnableMessages = true, bool EnableEmpty = false)
    {
        var Ret = new List<CodeString>();
        if (!SplitToParameters(State, Self, Separator, Ret, EnableMessages, EnableEmpty))
            return null;

        return Ret.ToArray();
    }

    public static CodeString[] GetParamList(CompilerState State, CodeString Params, int Count = -1)
    {
        Params = Params.Trim();
        if (Params.Length == 0) return new CodeString[0];

        var Recognizer = State.Language.ParameterRecognizer;
        var Ret = Recognizer.SplitToParameters(State, Params);
        if (Ret == null) return null;

        if (Count != -1 && Count != Ret.Length)
        {
            State.Messages.Add(MessageId.ParamCount, Params);
            return null;
        }

        return Ret;
    }

    public static bool IsAcceptable(IdContainer Container, StringSlice String)
    {
        String = String.Trim();
        if (String.Length == 0) return false;

        var Lang = Container.State.Language;
        var Res = String.EndsWith(Lang.Root.NewLineRight, Lang.Root.NewLineRightSkip, new IdCharCheck(true));
        if (Res.Position != -1) return false;

        var Handlers = Lang.Root.GetObjectsStoreable<IFind2Handler>();
        for (var i = 0; i < Handlers.Length; i++)
            if (!Handlers[i].IsAcceptable(Container, String))
                return false;

        return true;
    }

    public static bool IsAcceptable(IdContainer Container, CodeString String)
    {
        var Lang = Container.State.Language;
        String = String.Trim();

        if (!String.IsValid || String.Length == 0) return false;
        if (String.EndsWith(Lang.Root.Operators, Lang.Root.OnlyLeft, new IdCharCheck(true)).Position != -1)
            return false;

        var Handlers = Lang.Root.GetObjectsStoreable<IFind2Handler>();
        var StringRange = String.String;
        for (var i = 0; i < Handlers.Length; i++)
            if (!Handlers[i].IsAcceptable(Container, StringRange))
                return false;

        return true;
    }

    public static FindResult Find(LanguageNode Node, CompilerState State, StringSlice String)
    {
        return String.Find(Node.Operators, Node.Skip, true, new IdCharCheck(true), Node.SkippingHandlers);
    }

    public static IEnumerable<FindResult> EnumFind(LanguageNode Node, CompilerState State, StringSlice String)
    {
        return String.EnumFind(Node.Operators, Node.Skip, true, new IdCharCheck(true), Node.SkippingHandlers);
    }

    public static IEnumerable<FindResult> EnumFind2(LanguageNode Node, IdContainer Container, StringSlice String)
    {
        foreach (var Result in EnumFind(Node, Container.State, String))
            if (IsAcceptable(Container, String.Substring(0, Result.Position).Trim()))
                yield return Result;
    }

    public static FindResult Find2(LanguageNode Node, IdContainer Container, StringSlice String)
    {
        foreach (var e in EnumFind2(Node, Container, String))
            return e;

        return new FindResult(-1, -1, null);
    }

    public static ExpressionNode[] GetLeftRightNode(CodeString String, FindResult Result, PluginRoot Plugin)
    {
        var State = Plugin.State;
        var LeftStr = String.TrimmedSubstring(State, 0, Result.Position);
        var RightStr = String.TrimmedSubstring(State, Result.NextChar);
        if (!LeftStr.IsValid || !RightStr.IsValid) return null;

        var Left = Expressions.Recognize(LeftStr, Plugin, true);
        var Right = Expressions.Recognize(RightStr, Plugin, true);
        if (Left == null || Right == null) return null;

        return new[] { Left, Right };
    }

    private static CodeString CheckTrmSubStr(CompilerState State, CodeString String, int StrSize = 1, bool Back = false)
    {
        if (!Back) return String.TrimmedSubstring(State, StrSize);
        return String.TrimmedSubstring(State, 0, String.Length - StrSize);
    }

    public static ExpressionNode OneParamOpNode(CodeString String, PluginRoot Plugin,
        Operator Op, int StrSize = 1)
    {
        var ps = CheckTrmSubStr(Plugin.State, String, StrSize);
        if (!ps.IsValid) return null;

        var Node = Expressions.Recognize(ps, Plugin, true);
        if (Node != null) return new OpExpressionNode(Op, new[] { Node }, String);
        return null;
    }
}

public static class Expressions
{
    public static ExpressionNode GetSelfNode(ExpressionNode Node)
    {
        return IsSelfSpecified(Node) ? Node.Children[0] : null;
    }

    public static bool IsSelfSpecified(ExpressionNode Node)
    {
        if (GetOperator(Node) == Operator.Member)
        {
            var Ch1Id = GetIdentifier(Node.Children[1]);
            if (Ch1Id.RealId is Function && (Ch1Id.RealId.Flags & IdentifierFlags.Static) == 0)
                return true;
        }

        return false;
    }

    public static ExpressionNode Index(PluginRoot Plugin, ExpressionNode Node, ArrayIndices Indices, CodeString Code,
        bool End = false)
    {
        var Ch = new ExpressionNode[Indices.Indices.Length + 1];
        Ch[0] = Node;

        var RIndices = Indices.Indices;
        for (var i = 0; i < RIndices.Length; i++)
        {
            var ChNode = Plugin.NewNode(Constants.GetIntValue(Plugin.Container, RIndices[i], Code));
            if (ChNode == null) return null;

            Ch[i + 1] = ChNode;
        }

        Node = Plugin.NewNode(new OpExpressionNode(Operator.Index, Ch, Code));
        return Node == null || !End ? Node : Plugin.End(Node);
    }

    public static ExpressionNode Index(PluginRoot Plugin, Identifier Id, ArrayIndices Indices,
        CodeString Code, BeginEndMode BEMode = BeginEndMode.Begin)
    {
        if ((BEMode & BeginEndMode.Begin) != 0 && !Plugin.Begin())
            return null;

        var Node = Plugin.NewNode(new IdExpressionNode(Id, Code));
        if (Node == null) return null;

        return Index(Plugin, Node, Indices, Code, (BEMode & BeginEndMode.End) != 0);
    }

    public static ExpressionNode ChainedRelation(PluginRoot Plugin, IList<ExpressionNode> Nodes,
        IList<Operator> Operators, CodeString Code)
    {
        var LinkedNodes = new List<LinkedExprNode>();
        for (var i = 1; i < Nodes.Count - 1; i++)
            LinkedNodes.Add(new LinkedExprNode(Nodes[i]));

        var RetNodes = new List<ExpressionNode>();
        for (var i = 0; i < Operators.Count; i++)
        {
            var Op = Operators[i];
            var Ch = new[]
            {
                i != 0 ? Plugin.NewNode(new LinkingNode(LinkedNodes[i - 1], Code)) : Nodes[i],
                i != Operators.Count - 1 ? Plugin.NewNode(new LinkingNode(LinkedNodes[i], Code)) : Nodes[i + 1]
            };

            if (Ch[0] == null || Ch[1] == null) return null;
            var NewNode = Plugin.NewNode(new OpExpressionNode(Op, Ch, Code));
            if (NewNode == null) return null;
            RetNodes.Add(NewNode);
        }

        while (RetNodes.Count > 1)
        {
            var Ch = new[] { RetNodes[0], RetNodes[1] };
            var Node = (ExpressionNode)new OpExpressionNode(Operator.And, Ch, Code);

            if (LinkedNodes.Count > 0 && RetNodes.Count == 2)
                Node.LinkedNodes.Set(LinkedNodes);

            if ((Node = Plugin.NewNode(Node)) == null)
                return null;

            RetNodes.RemoveAt(0);
            RetNodes[0] = Node;
        }

        return RetNodes[0];
    }

    public static ExpressionNode CallToString(PluginRoot Plugin, ExpressionNode Node, CodeString Code)
    {
        var Member = Plugin.NewNode(new StrExpressionNode(new CodeString("ToString")));
        if (Member == null) return null;

        var FuncCh = new[] { Node, Member };
        var Func = Plugin.NewNode(new OpExpressionNode(Operator.Member, FuncCh, Code));
        if (Func == null) return null;

        var CallCh = new[] { Func };
        return Plugin.NewNode(new OpExpressionNode(Operator.Call, CallCh, Code));
    }

    public static ExpressionNode CallToStringSafe(PluginRoot Plugin, ExpressionNode Node, CodeString Code)
    {
        var Container = Plugin.Container;
        var Type = Node.Type.RealId as Type;

        if ((Type.TypeFlags & TypeFlags.ReferenceValue) == 0) return CallToString(Plugin, Node, Code);

        var LinkedNode = new LinkedExprNode(Node);
        var FuncCh0 = Plugin.NewNode(new LinkingNode(LinkedNode, Node.Code));
        if (FuncCh0 == null) return null;

        var CallNode = CallToString(Plugin, FuncCh0, Code);
        if (CallNode == null) return null;

        var CmpNull = Plugin.NewNode(Constants.GetNullValue(Container, Node.Code));
        var CmpSrc = Plugin.NewNode(new LinkingNode(LinkedNode, Node.Code));
        if (CmpNull == null || CmpSrc == null) return null;

        var CmpCh = new[] { CmpSrc, CmpNull };
        var Cmp = Plugin.NewNode(new OpExpressionNode(Operator.Inequality, CmpCh, Node.Code));
        if (Cmp == null) return null;

        var CondElse = Plugin.NewNode(Constants.GetNullValue(Container, Node.Code));
        if (CondElse == null) return null;

        var CondCh = new[] { Cmp, CallNode, CondElse };
        var Cond = new OpExpressionNode(Operator.Condition, CondCh, Node.Code);
        Cond.LinkedNodes.Add(LinkedNode);

        return Plugin.NewNode(Cond);
    }

    public static ExpressionNode GetMember(PluginRoot Plugin, CodeString Code,
        ExpressionNode DstCh0, Identifier Member, bool End = false)
    {
        var DstCh1 = Plugin.NewNode(new IdExpressionNode(Member, Code));
        if (DstCh0 == null || DstCh1 == null) return null;

        var DstCh = new[] { DstCh0, DstCh1 };
        var Node = Plugin.NewNode(new OpExpressionNode(Operator.Member, DstCh, Code));
        return Node == null || !End ? Node : Plugin.End(Node);
    }

    public static ExpressionNode GetMember(PluginRoot Plugin, CodeString Code, Identifier Id, Identifier Member,
        BeginEndMode BEMode = BeginEndMode.Begin)
    {
        if ((BEMode & BeginEndMode.Begin) != 0 && !Plugin.Begin())
            return null;

        var DstCh0 = Plugin.NewNode(new IdExpressionNode(Id, Code));
        return GetMember(Plugin, Code, DstCh0, Member, (BEMode & BeginEndMode.End) != 0);
    }

    public static ExpressionNode GetMember(PluginRoot Plugin, CodeString Code, Identifier Id, string Member,
        BeginEndMode BEMode = BeginEndMode.Begin)
    {
        var MemberId = Identifiers.GetMember(Plugin.State, Id.TypeOfSelf, Member, Code);
        if (MemberId == null) return null;

        return GetMember(Plugin, Code, Id, MemberId, BEMode);
    }

    public static ExpressionNode SetDefaultValue(PluginRoot Plugin, CodeString Code, Identifier Id,
        BeginEndMode Mode = BeginEndMode.Both)
    {
        return BeginEnd(Plugin, () =>
        {
            var Dst = Plugin.NewNode(new IdExpressionNode(Id, Code));
            var Src = Constants.GetDefaultValue(Plugin, Id, Code);
            if (Dst == null || Src == null) return null;

            var Ch = new[] { Dst, Src };
            return Plugin.NewNode(new OpExpressionNode(Operator.Assignment, Ch, Code));
        }, Mode);
    }

    public static bool GetArrayLengths(CompilerState State, NodeGroup Group, List<int> List, int MaxDepth,
        int Depth = 0)
    {
        if (Depth >= MaxDepth)
        {
            State.Messages.Add(MessageId.NotExpected, Group.Code);
            return false;
        }

        var Count = Group.Children.Count;
        if (List.Count > Depth)
        {
            if (List[Depth] != Count)
            {
                State.Messages.Add(MessageId.ArrayInvalidLength, Group.Code);
                return false;
            }
        }
        else
        {
            List.Add(Count);
        }

        var NDepth = Depth + 1;
        for (var i = 0; i < Group.Children.Count; i++)
        {
            var Obj = Group.Children[i] as NodeGroup;
            if (Obj != null && !GetArrayLengths(State, Obj, List, MaxDepth, NDepth))
                return false;
        }

        return true;
    }

    public static int[] GetArrayLengths(CompilerState State, NodeGroup Group)
    {
        var List = new List<int>();
        if (!GetArrayLengths(State, Group, List, Group.MinDepth))
            return null;

        return List.ToArray();
    }

    public static bool IsLValue(ExpressionNode Node)
    {
        Node = Node.RealNode;

        var Op = GetOperator(Node);
        if (Op == Operator.Assignment || Op == Operator.Index || Op == Operator.Member)
            return true;

        if (Op == Operator.Unknown && Node is IdExpressionNode)
            return true;

        return false;
    }

    private static ExpressionNode GetArrayDataSize(PluginRoot Plugin,
        ExpressionNode[] Ch, int SizeOfValues, CodeString Code)
    {
        var Ret = Ch[0];
        ExpressionNode[] MulCh;

        for (var i = 1; i < Ch.Length; i++)
        {
            MulCh = new[] { Ret, Ch[i] };
            Ret = Plugin.NewNode(new OpExpressionNode(Operator.Multiply, MulCh, Code));
            if (Ret == null) return null;
        }

        var SizeOfValuesNode = Plugin.NewNode(Constants.GetIntValue(Plugin.Container, SizeOfValues, Code));
        if (SizeOfValuesNode == null) return null;

        MulCh = new[] { Ret, SizeOfValuesNode };
        return Plugin.NewNode(new OpExpressionNode(Operator.Multiply, MulCh, Code));
    }

    public static bool FinishParameters(PluginRoot Plugin, ExpressionNode[] Ch)
    {
        for (var i = 1; i < Ch.Length; i++)
        {
            Ch[i] = Plugin.FinishNode(Ch[i]);
            if (Ch[i] == null) return false;
        }

        return true;
    }

    public static ExpressionNode ConstructArray(PluginRoot Plugin, ExpressionNode Node)
    {
        if (GetOperator(Node) != Operator.NewArray)
            throw new ApplicationException();

        return ConstructArray(Plugin, Node.Type, Node.Children, Node.Code);
    }

    public static ExpressionNode ConstructArray(PluginRoot Plugin, Identifier RefArrayType,
        int[] Lengths, CodeString Code, ExpressionNode InitialValue = null)
    {
        var Container = Plugin.Container;
        var LengthNodes = new ExpressionNode[Lengths.Length];
        for (var i = 0; i < Lengths.Length; i++)
        {
            LengthNodes[i] = Plugin.NewNode(Constants.GetIntValue(Container, Lengths[i], Code));
            if (LengthNodes[i] == null) return null;
        }

        return ConstructArray(Plugin, RefArrayType, LengthNodes, Code, InitialValue);
    }

    public static ExpressionNode ConstructArray(PluginRoot Plugin, Identifier RefArrayType,
        ExpressionNode[] Lengths, CodeString Code, ExpressionNode InitialValue = null)
    {
        var State = Plugin.State;
        var Container = Plugin.Container;
        var RRefArrayType = RefArrayType.RealId as Type;
        var BaseType = RRefArrayType.Children[0];
        var RBaseType = BaseType.RealId as Type;

        var UINT_PTR = Container.GlobalContainer.CommonIds.UIntPtr;
        var ArrayClass = Identifiers.GetByFullNameFast<ClassType>(State, "System.Array");
        if (ArrayClass == null) return null;

        var DimensionCh = new ExpressionNode[Lengths.Length];
        for (var i = 0; i < Lengths.Length; i++)
        {
            DimensionCh[i] = Lengths[i];
            if (!DimensionCh[i].Type.IsEquivalent(UINT_PTR))
            {
                DimensionCh[i] = Convert(DimensionCh[i], UINT_PTR, Plugin);
                if (DimensionCh[i] == null) return null;
            }
        }

        var ArrayTypeNode = Plugin.NewNode(new IdExpressionNode(ArrayClass, Code));
        var BaseTypeNode = Plugin.NewNode(new DataPointerNode(Code, RefArrayType));
        var Dimensions = Plugin.NewNode(new OpExpressionNode(Operator.Array, DimensionCh, Code));
        var ItemSize = Plugin.NewNode(Constants.GetUIntValue(Container, (uint)RBaseType.Size, Code));
        if (ArrayTypeNode == null || BaseTypeNode == null || Dimensions == null || ItemSize == null)
            return null;

        ExpressionNode[] Ch;
        if (InitialValue == null)
            Ch = new[] { ArrayTypeNode, BaseTypeNode, Dimensions, ItemSize };
        else Ch = new[] { ArrayTypeNode, BaseTypeNode, Dimensions, ItemSize, InitialValue };

        var Node = Plugin.NewNode(new OpExpressionNode(Operator.NewObject, Ch, Code));
        return Node == null ? null : Reinterpret(Node, RefArrayType, Plugin);
    }

    public static bool CheckNodeMultipleUsage(ExpressionNode Node)
    {
        if (!Node.CheckNodes(x => x != Node, false)) return false;
        return Node.CheckChildren(x => CheckNodeMultipleUsage(x));
    }

    public static bool CheckLinkedNodes(ExpressionNode Node, List<LinkedExprNode> LinkedNodes = null)
    {
        if (LinkedNodes == null)
            LinkedNodes = new List<LinkedExprNode>();

        for (var i = 0; i < Node.LinkedNodes.Count; i++)
            LinkedNodes.Add(Node.LinkedNodes[i]);

        var LNode = Node as LinkingNode;
        if (LNode != null && !LinkedNodes.Contains(LNode.LinkedNode))
            return false;

        if (!Node.CheckChildren(x => CheckLinkedNodes(x, LinkedNodes)))
            return false;

        for (var i = 0; i < Node.LinkedNodes.Count; i++)
            LinkedNodes.Remove(Node.LinkedNodes[i]);

        return true;
    }

    public static ExpressionNode[] GetAllMember(PluginRoot Plugin, ExpressionNode Node, out LinkedExprNode LinkedNode)
    {
        var Type = Node.Type.UnderlyingClassOrRealId as StructuredType;
        if (Type == null) throw new ApplicationException();

        var Members = Type.StructuredScope.IdentifierList;
        LinkedNode = new LinkedExprNode(Node);
        var Ret = new ExpressionNode[Members.Count];

        for (var i = 0; i < Members.Count; i++)
        {
            var Ch = new[]
            {
                Plugin.NewNode(new LinkingNode(LinkedNode, Node.Code)),
                Plugin.NewNode(new IdExpressionNode(Members[i], Node.Code))
            };

            if (Ch[0] == null || Ch[1] == null)
                return null;

            Ret[i] = Plugin.NewNode(new OpExpressionNode(Operator.Member, Ch, Node.Code));
            if (Ret[i] == null) return null;
        }

        return Ret;
    }

    public static Identifier GetTupleMemberType(ExpressionNode Node, int Index)
    {
        if (Index == -1)
            return Node.Type;

        if (GetOperator(Node) == Operator.Tuple) return Node.Children[Index].Type;

        var Type = Node.Type.RealId as TupleType;
        var Members = Type.StructuredScope.IdentifierList;
        return Members[Index].Children[0];
    }

    public static ExpressionNode GetTupleMember(PluginRoot Plugin, ref ExpressionNode Node, int Index,
        out LinkedExprNode LinkedNode)
    {
        LinkedNode = null;
        if (Index == -1)
            return Node;

        if (GetOperator(Node) == Operator.Tuple) return Node.Children[Index];

        var Members = GetAllMember(Plugin, Node, out LinkedNode);
        if (Members == null) return null;

        Node = Plugin.NewNode(new OpExpressionNode(Operator.Tuple, Members, Node.Code));
        return Node == null ? null : Members[Index];
    }

    public static void ProcessTuple(ExpressionNode Node, Action<ExpressionNode> Func)
    {
        if (GetOperator(Node) == Operator.Tuple)
            for (var i = 0; i < Node.Children.Length; i++)
                Func(Node.Children[i]);
        else
            Func(Node);
    }

    public static bool ProcessTuple(ExpressionNode Node, Predicate<ExpressionNode> Func)
    {
        if (GetOperator(Node) == Operator.Tuple)
        {
            var Ch = Node.Children;
            for (var i = 0; i < Ch.Length; i++)
                if (!Func(Ch[i]))
                    return false;
        }
        else
        {
            if (!Func(Node)) return false;
        }

        return true;
    }

    public static bool ProcessTuple(ExpressionNode Node, Func<ExpressionNode, int, bool> Func)
    {
        if (GetOperator(Node) == Operator.Tuple)
        {
            var Ch = Node.Children;
            for (var i = 0; i < Ch.Length; i++)
                if (!Func(Ch[i], i))
                    return false;
        }
        else
        {
            if (!Func(Node, -1)) return false;
        }

        return true;
    }

    public static PluginResult ProcessTuple(PluginRoot Plugin, ref ExpressionNode Node,
        Func<ExpressionNode, int, ExpressionNode> Func)
    {
        if (GetOperator(Node) == Operator.Tuple)
        {
            var Ch = Node.Children;
            var NewCreated = false;

            for (var i = 0; i < Ch.Length; i++)
            {
                var OldNode = Ch[i];
                Ch[i] = Func(OldNode, i);

                if (Ch[i] == null) return PluginResult.Failed;
                if (Ch[i] != OldNode) NewCreated = true;
            }

            if (NewCreated)
            {
                Node = Plugin.NewNode(Node);
                return Node == null ? PluginResult.Failed : PluginResult.Ready;
            }
        }
        else
        {
            var OldNode = Node;
            Node = Func(OldNode, -1);

            if (Node == null) return PluginResult.Failed;
            if (Node != OldNode) return PluginResult.Ready;
        }

        return PluginResult.Succeeded;
    }

    public static ExpressionNode CreateReference(IdContainer Container, Identifier Id,
        PluginRoot Plugin, CodeString Code, bool EnableMessages = true)
    {
        var Node = Plugin.NewNode(new IdExpressionNode(Id, Code));
        if (Node is IdExpressionNode)
        {
            var IdNode = Node as IdExpressionNode;
            if (IdNode.Identifier.IsInstanceIdentifier)
            {
                var MemVar = IdNode.Identifier as MemberVariable;
                var FS = Container.FunctionScope;
                var Self = FS != null ? FS.SelfVariable : null;

                var StructuredScope = IdNode.Identifier.Container.RealContainer as StructuredScope;
                if (Self != null && Self.Container.StructuredScope == StructuredScope)
                {
                    var SelfNode = Plugin.NewNode(new IdExpressionNode(Self, Code));
                    if (SelfNode == null) return null;

                    var Ch = new[] { SelfNode, IdNode };
                    return Plugin.NewNode(new OpExpressionNode(Operator.Member, Ch, Code));
                }

                if (EnableMessages)
                    Plugin.State.Messages.Add(MessageId.NonStatic, Code);

                return null;
            }
        }

        return Node;
    }

    public static ExpressionNode SetValue(ExpressionNode Destination, ExpressionNode Value,
        PluginRoot Plugin, CodeString Code = new(), bool End = false)
    {
        var Ch = new[]
        {
            Destination,
            Value
        };

        var Ret = Plugin.NewNode(new OpExpressionNode(Operator.Assignment, Ch, Code));

        if (End && Ret != null)
        {
            Ret = Plugin.End(Ret);
            if (Ret == null) return null;
        }

        return Ret;
    }

    public static ExpressionNode SetValue(Identifier Variable, ExpressionNode Value,
        PluginRoot Plugin, CodeString Code = new(), bool End = false)
    {
        var Destination = Plugin.NewNode(new IdExpressionNode(Variable, Code));
        if (Destination == null) return null;

        return SetValue(Destination, Value, Plugin, Code, End);
    }

    public static ExpressionNode SetValue(Identifier Self, Identifier Member, ExpressionNode Value,
        PluginRoot Plugin, CodeString Code = new(), bool End = false)
    {
        var Dst = GetMember(Plugin, Code, Self, Member, BeginEndMode.None);
        if (Dst == null) return null;

        return SetValue(Dst, Value, Plugin, Code, End);
    }

    public static ExpressionNode SetValue(Identifier Self, CodeString Member, ExpressionNode Value,
        PluginRoot Plugin, CodeString Code = new(), bool End = false)
    {
        var MemberId = Identifiers.GetMember(Plugin.State, Self.TypeOfSelf, Member);
        if (MemberId == null) return null;

        return SetValue(Self, MemberId, Value, Plugin, Code, End);
    }

    public static ExpressionNode SetValue(Identifier Self, string Member, ExpressionNode Value,
        PluginRoot Plugin, CodeString Code = new(), bool End = false)
    {
        var MemberId = Identifiers.GetMember(Plugin.State, Self.TypeOfSelf, Member, Code);
        if (MemberId == null) return null;

        return SetValue(Self, MemberId, Value, Plugin, Code, End);
    }

    public static ExpressionNode SetValue(Identifier Self, ArrayIndices Indices, ExpressionNode Value,
        PluginRoot Plugin, CodeString Code = new(), bool End = false)
    {
        var Dst = Index(Plugin, Self, Indices, Code, BeginEndMode.None);
        if (Dst == null) return null;

        return SetValue(Dst, Value, Plugin, Code, End);
    }

    public static ExpressionNode Convert(ExpressionNode Node, Identifier To, PluginRoot Plugin, CodeString Code = new())
    {
        var TypeNode = Plugin.NewNode(new IdExpressionNode(To, Code));
        if (TypeNode == null) return null;

        var Ch = new[] { Node, TypeNode };
        return Plugin.NewNode(new OpExpressionNode(Operator.Cast, Ch, Code));
    }

    public static ExpressionNode Reinterpret(ExpressionNode Node, Identifier To, PluginRoot Plugin,
        CodeString Code = new())
    {
        var TypeNode = Plugin.NewNode(new IdExpressionNode(To, Code));
        if (TypeNode == null) return null;

        var Ch = new[] { Node, TypeNode };
        return Plugin.NewNode(new OpExpressionNode(Operator.Reinterpret, Ch, Code));
    }

    public static ExpressionNode Indirection(PluginRoot Plugin, ExpressionNode Node, CodeString Code)
    {
        var Zero = Plugin.NewNode(Constants.GetIntValue(Plugin.Container, 0, Code, true));
        if (Zero == null) return null;

        var Ch = new[] { Node, Zero };
        return Plugin.NewNode(new OpExpressionNode(Operator.Index, Ch, Code));
    }

    public static ExpressionNode GetAddress(PluginRoot Plugin, ExpressionNode Node, CodeString Code)
    {
        var Zero = Plugin.NewNode(Constants.GetIntValue(Plugin.Container, 0, Code, true));
        if (Zero == null) return null;

        var Ch = new[] { Node, Zero };
        return Plugin.NewNode(new OpExpressionNode(Operator.Address, Ch, Code));
    }

    public static Identifier GetIdentifier(ExpressionNode Node)
    {
        var IdNode = Node as IdExpressionNode;
        return IdNode != null ? IdNode.Identifier : null;
    }

    public static Identifier GetMemberIdentifier(ExpressionNode Node)
    {
        if (Node is IdExpressionNode)
        {
            var IdNode = Node as IdExpressionNode;
            return IdNode.Identifier;
        }

        if (Node is OpExpressionNode)
        {
            var OpNode = Node as OpExpressionNode;
            var Op = OpNode.Operator;
            var Ch = OpNode.Children;

            if (Op != Operator.Member) return null;
            return GetMemberIdentifier(Ch[1]);
        }

        return null;
    }

    public static Operator GetOperator(ExpressionNode Node)
    {
        var OpNode = Node as OpExpressionNode;
        return OpNode != null ? OpNode.Operator : Operator.Unknown;
    }

    public static OverloadSelectionData GetOperatorSelectData(ExpressionNode[] Children)
    {
        var Ret = new OverloadSelectionData();
        Ret.Unnamed = new List<Identifier>();
        Ret.Specified = true;

        for (var i = 0; i < Children.Length; i++)
            Ret.Unnamed.Add(Children[i].Type);

        return Ret;
    }

    public static OverloadSelectionData GetOverloadSelectData(ExpressionNode[] Children)
    {
        var Ret = new OverloadSelectionData();
        Ret.Specified = true;

        for (var i = 1; i < Children.Length; i++)
            if (Children[i] is NamedParameterNode)
            {
                var Node = Children[i] as NamedParameterNode;
                if (Ret.Named == null)
                    Ret.Named = new Dictionary<string, Identifier>();

                Ret.Named.Add(Node.Name.ToString(), Node.Children[0].Type);
            }
            else
            {
                if (Ret.Unnamed == null)
                    Ret.Unnamed = new List<Identifier>();

                Ret.Unnamed.Add(Children[i].Type);
            }

        return Ret;
    }

    public static NodeGroup GetGroups(PluginRoot Plugin, CodeString Code)
    {
        var Rec = Plugin.State.Language.GroupRecognizer;
        return Rec.GetGroups(Plugin, Code);
    }

    public static ExpressionNode Increase(PluginRoot Plugin, ExpressionNode Src, ExpressionNode Val,
        CodeString Code, Operator Op = Operator.Add, bool End = true)
    {
        var Linked = new LinkedExprNode(Src);
        var Dst = Plugin.NewNode(new LinkingNode(Linked, Code));
        Src = Plugin.NewNode(new LinkingNode(Linked, Code));
        if (Dst == null || Src == null) return null;

        var Ch = new[] { Src, Val };
        var AddNode = Plugin.NewNode(new OpExpressionNode(Op, Ch, Code));
        if (AddNode == null) return null;

        var RetVal = new OpExpressionNode(Operator.Assignment, Ch, Code);
        RetVal.Children = new[] { Dst, AddNode };
        RetVal.LinkedNodes.Add(Linked);

        var Loop = Plugin.NewNode(RetVal);
        if (Loop == null) return null;

        if (End) return Plugin.End(Loop);
        return Loop;
    }

    public static ExpressionNode Increase(PluginRoot Plugin, ExpressionNode Src, int Val,
        CodeString Code, Operator Op = Operator.Add, bool End = true)
    {
        var ValNode = Plugin.NewNode(Constants.GetIntValue(Plugin.Container, Val, Code, true));
        if (ValNode == null) return null;

        return Increase(Plugin, Src, ValNode, Code, Op, End);
    }

    public static ExpressionNode Increase(PluginRoot Plugin, Identifier Id, ExpressionNode Val,
        CodeString Code, Operator Op = Operator.Add, bool End = true)
    {
        var Src = Plugin.NewNode(new IdExpressionNode(Id, Code));
        if (Src == null) return null;

        return Increase(Plugin, Src, Val, Code, Op, End);
    }

    public static ExpressionNode Increase(PluginRoot Plugin, Identifier Id, int Val,
        CodeString Code, Operator Op = Operator.Add, BeginEndMode BEMode = BeginEndMode.Both)
    {
        if ((BEMode & BeginEndMode.Begin) != 0 && !Plugin.Begin()) return null;
        var ValNode = Plugin.NewNode(Constants.GetIntValue(Plugin.Container, Val, Code, true));
        if (ValNode == null) return null;

        return Increase(Plugin, Id, ValNode, Code, Op, (BEMode & BeginEndMode.End) != 0);
    }

    public static ExpressionNode Zero(PluginRoot Plugin, Identifier Id, CodeString Code)
    {
        if (!Plugin.Begin()) return null;
        var Zero = Plugin.NewNode(new ConstExpressionNode(Id.TypeOfSelf, new IntegerValue(0), Code));
        var Dst = Plugin.NewNode(new IdExpressionNode(Id, Code));
        if (Zero == null || Dst == null) return null;

        var Ch = new[] { Dst, Zero };
        var Assignment = Plugin.NewNode(new OpExpressionNode(Operator.Assignment, Ch, Code));
        if (Assignment == null) return null;

        return Plugin.End(Assignment);
    }

    public static ExpressionNode ExtractTupleOp(ExpressionNode Node, PluginRoot Plugin, ExtractTupleOpFunc Func,
        bool[] AlwaysLink = null)
    {
        var DstType = Node.Type.RealId as TupleType;
        var LinkedNodes = new LinkedExprNode[Node.Children.Length];

        for (var i = 0; i < Node.Children.Length; i++)
            if (!(Node.Children[i].Type.RealId is TypeOfType))
            {
                var OpCh = Node.Children[i] as OpExpressionNode;
                if (OpCh == null || OpCh.Operator != Operator.Tuple || (AlwaysLink != null && AlwaysLink[i]))
                    LinkedNodes[i] = new LinkedExprNode(Node.Children[i]);
            }

        var DstMembers = DstType.StructuredScope.IdentifierList;
        var NewCh = new ExpressionNode[DstMembers.Count];

        for (var i = 0; i < DstMembers.Count; i++)
        {
            var Member = DstMembers[i] as MemberVariable;
            var Args = new ExpressionNode[Node.Children.Length];

            for (var j = 0; j < Node.Children.Length; j++)
            {
                var Ch = Node.Children[j];
                if (Ch.Type.RealId is TypeOfType)
                {
                    Args[j] = ExtractTupleChild(Plugin, Ch, i, Node.Code);
                    if (Args[j] == null) return null;
                }
                else if (LinkedNodes[j] != null)
                {
                    var Linking = Plugin.NewNode(new LinkingNode(LinkedNodes[j], Node.Code));
                    if (Linking == null || Plugin.FinishNode(ref Linking) == PluginResult.Failed)
                        return null;

                    Args[j] = ExtractTupleChild(Plugin, Linking, i, Node.Code);
                    if (Args[j] == null) return null;
                }
                else
                {
                    Args[j] = Ch.Children[i];
                }
            }

            NewCh[i] = Func(i, Args);
            if (NewCh[i] == null) return null;
        }

        var NewNode = new OpExpressionNode(Operator.Tuple, NewCh, Node.Code);
        NewNode.LinkedNodes.AddRange(Node.LinkedNodes);
        if (LinkedNodes != null) NewNode.LinkedNodes.AddRange(LinkedNodes.Where(x => x != null));

        for (var i = 0; i < Node.Children.Length; i++)
            if (LinkedNodes[i] == null)
                NewNode.LinkedNodes.AddRange(Node.Children[i].LinkedNodes);

        NewNode.Type = Node.Type;
        return Plugin.NewNode(NewNode);
    }

    public static ExpressionNode ExtractTupleChild(PluginRoot Plugin, ExpressionNode Node, int Index, CodeString Code)
    {
        if (Node.Type.RealId is TypeOfType)
        {
            var SrcType = GetIdentifier(Node).RealId as TupleType;
            var SrcMembers = SrcType.StructuredScope.IdentifierList;
            return Plugin.NewNode(new IdExpressionNode(SrcMembers[Index], Code));
        }
        else
        {
            var SrcType = Node.Type.RealId as TupleType;
            var SrcMembers = SrcType.StructuredScope.IdentifierList;
            return GetMember(Plugin, Code, Node, SrcMembers[Index]);
        }
    }

    public static bool IsDouble(int Size)
    {
        bool Double;
        if (Size == 8) Double = true;
        else if (Size == 4) Double = false;
        else throw new ApplicationException();
        return Double;
    }

    public static bool IsDouble(Type Type)
    {
        return IsDouble(Type.Size);
    }

    public static bool IsDouble(ExpressionNode Node)
    {
        return IsDouble(Node.Type.RealId as Type);
    }

    public static ExpressionNode CreateConstNode(CodeString Code,
        ConstValue Data, Type Type, PluginRoot Plugin = null)
    {
        var Ret = new ConstExpressionNode(Type, Data, Code);
        if (Plugin == null) return Ret;
        return Plugin.NewNode(Ret);
    }

    public static ExpressionNode CallIndex(CodeString Code, PluginRoot Plugin,
        Operator Op, CodeString FuncName, params ExpressionNode[] Ch)
    {
        var Func = Recognize(FuncName, Plugin);
        if (Func == null) return null;

        var NewCh = new ExpressionNode[Ch.Length + 1];
        NewCh[0] = Func;
        Ch.CopyTo(NewCh, 1);

        return Plugin.NewNode(new OpExpressionNode(Op, NewCh, Code));
    }

    public static ExpressionNode Call(CodeString Code, PluginRoot Plugin, Function Function, params ExpressionNode[] Ch)
    {
        var Func = Plugin.NewNode(new IdExpressionNode(Function, Code));
        if (Func == null) return null;

        var NewCh = new ExpressionNode[Ch.Length + 1];
        NewCh[0] = Func;
        Ch.CopyTo(NewCh, 1);
        return Plugin.NewNode(new OpExpressionNode(Operator.Call, NewCh, Code));
    }

    public static ExpressionNode CreateExpression(CodeString Code, PluginRoot Plugin,
        BeginEndMode Mode = BeginEndMode.Both)
    {
        return BeginEnd(Plugin, () => Recognize(Code, Plugin), Mode);
    }

    public static ExpressionNode CreateExpression_RetValLess(CodeString Code, PluginRoot Plugin,
        BeginEndMode Mode = BeginEndMode.Both)
    {
        var Rec = Plugin.State.Language.RetValLessRecognizer;
        if (Rec == null) return CreateExpression(Code, Plugin, Mode);

        Code = Code.Trim();
        return BeginEnd(Plugin, () => Rec.Recognize(Code, Plugin), Mode);
    }

    public static ExpressionNode BeginEnd(PluginRoot Plugin, Func<ExpressionNode> Func,
        BeginEndMode Mode = BeginEndMode.Both)
    {
        if ((Mode & BeginEndMode.Begin) != 0 && !Plugin.Begin())
            return null;

        var Ret = Func();
        if ((Mode & BeginEndMode.End) != 0 && Ret != null)
            Ret = Plugin.End(Ret);

        return Ret;
    }

    public static ExpressionNode[] Recognize(IList<CodeString> Code, PluginRoot Plugin)
    {
        var RetValue = true;
        var Ret = new ExpressionNode[Code.Count];

        for (var i = 0; i < Code.Count; i++)
        {
            Ret[i] = Recognize(Code[i], Plugin);
            if (Ret[i] == null) RetValue = false;
        }

        return RetValue ? Ret : null;
    }

    public static ExpressionNode Recognize(CodeString Code, PluginRoot Plugin,
        bool Trimmed = false, IList<IExprRecognizer> Recognizers = null)
    {
        if (!Trimmed) Code = Code.Trim();
#if DEBUG
        else if (Code.LeftWhiteSpaces != 0 || Code.RightWhiteSpaces != 0)
            throw new ApplicationException("Not Trimmed");

        if (!Code.IsValid || Code.Length == 0)
            throw new ApplicationException("Deficient");
#endif
        if (Recognizers == null)
            Recognizers = Plugin.State.Language.ExprRecognizers;

        ExpressionNode Out;
        var Res = Recognize(Code, Plugin, Recognizers, out Out);
        if (Res != SimpleRecResult.Unknown)
            return Res == SimpleRecResult.Succeeded ? Out : null;

        Plugin.State.Messages.Add(MessageId.UnknownOp, Code);
        return null;
    }

    public static SimpleRecResult Recognize(CodeString Code, PluginRoot Plugin,
        IList<IExprRecognizer> Recognizers, out ExpressionNode Out)
    {
        Out = null;
        for (var i = 0; i < Recognizers.Count; i++)
        {
            var Res = Recognizers[i].Recognize(Code, Plugin, ref Out);
            if (Res != ExprRecResult.Unknown)
            {
                if (Res == ExprRecResult.Failed)
                {
                    Out = null;
                    return SimpleRecResult.Failed;
                }

                if (Out == null)
                    throw new ApplicationException("Recognized expression name cannot be null");

                if (Res == ExprRecResult.Succeeded && (Out = Plugin.NewNode(Out)) == null)
                    return SimpleRecResult.Failed;

                return SimpleRecResult.Succeeded;
            }
        }

        return SimpleRecResult.Unknown;
    }
}