using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Zinnia.Base;
using Zinnia.Recognizers;
using Zinnia.Languages;

namespace Zinnia.Languages.Zinnia
{
    public struct ZinniaLineData
    {
        public bool SkipScope;
        public bool SkipLine;
    }

    public class ZinniaCodeFileData
    {
        public ZinniaLineData[] Lines;
    }

    public static class ZinniaHelper
    {
        public static bool ForEachLine(ScopeNode Scope, Predicate<CodeString> Func)
        {
            var Code = Scope.Code;
            return !Code.IsValid || ForEachLine(Scope.State, Code, Func);
        }

        public static int GetNextLinePosition(CodeFile File, int Line, int Indent = -1)
        {
            var Next = GetNextLine(File, Line, Indent);
            if (Next == File.GetLineCount()) return File.Content.Length;
            else return File.GetLinePosition(Next);
        }

        public static int GetNextLine(CodeFile File, int Line, int Indent = -1)
        {
            var FileData = File.Data.Get<ZinniaCodeFileData>();
            if (Indent == -1) Indent = File.GetIndent(Line);

            var Next = Line + 1;
            while (Next < File.GetLineCount())
            {
                var Skip = File.IsEmptyLine(Next) || FileData.Lines[Next].SkipLine;
                if (!Skip && File.GetIndent(Next) <= Indent) break;
                Next++;
            }

            return Next;
        }

        public static int GetNextLinePosition(CodeString Code, int Line, int Indent = -1)
        {
            var File = Code.File;
            var Next = GetNextLine(Code, Line, Indent);

            if (Next == File.GetLineCount()) return File.Content.Length;
            else return File.GetLinePosition(Next);
        }

        public static int GetNextLine(CodeString Code, int Line, int Indent = -1)
        {
            var File = Code.File;
            var FileData = File.Data.Get<ZinniaCodeFileData>();
            if (Indent == -1) Indent = File.GetIndent(Line);

            var Next = Line + 1;
            while (Code.HasLine(Next))
            {
                var Skip = File.IsEmptyLine(Next) || FileData.Lines[Next].SkipLine;
                if (!Skip && File.GetIndent(Next) <= Indent) break;
                Next++;
            }

            return Next;
        }

        public static CodeString GetFirstScope(CompilerState State, CodeString Code, bool Trim = true)
        {
            var File = Code.File;
            var FileData = File.Data.Get<ZinniaCodeFileData>();
            var Line = Code.Line;

            while (Code.HasLine(Line))
            {
                if (File.IsEmptyLine(Line) || FileData.Lines[Line].SkipLine)
                {
                    Line++;
                    continue;
                }

                var CurrentIndent = File.GetIndent(Line);
                var Next = GetNextLine(Code, Line, CurrentIndent);
                if (!FileData.Lines[Line].SkipScope)
                {
                    var EndLine = ProcessSkipLines(File, FileData, Next);
                    var LineStr = Code.GetLines(Line, EndLine - Line + 1);
                    return Trim ? LineStr.Trim() : LineStr;
                }

                Line = Next;
            }

            return new CodeString();
        }

        public static bool ForEachLine(CompilerState State, CodeString Code, Predicate<CodeString> Func)
        {
            var RetValue = true;
            var File = Code.File;
            var FileData = File.Data.Get<ZinniaCodeFileData>();
            var Indent = -1;
            var Line = Code.Line;

            while (Code.HasLine(Line))
            {
                if (File.IsEmptyLine(Line) || FileData.Lines[Line].SkipLine)
                {
                    Line++;
                    continue;
                }

                var CurrentIndent = File.GetIndent(Line);
                if (Indent == -1)
                {
                    if (Line != Code.Line)
                    {
                        Indent = CurrentIndent;
                    }
                    else
                    {
                        Indent = File.GetDistanceOrIndent(Line, Code.Index);
                        CurrentIndent = Indent;
                    }
                }

                if (CurrentIndent == Indent)
                {
                    var Next = GetNextLine(Code, Line, CurrentIndent);
                    if (!FileData.Lines[Line].SkipScope)
                    {
                        var EndLine = ProcessSkipLines(File, FileData, Next);
                        var LineStr = Code.GetLines(Line, EndLine - Line + 1).Trim();
                        if (!Func(LineStr)) RetValue = false;
                    }

                    Line = Next;
                }
                else
                {
                    State.Messages.Add(MessageId.InvalidScopeIndent, File.GetLines(Line));
                    RetValue = false;
                    Line++;
                }
            }

            return RetValue;
        }

        public static int ProcessSkipLines(CodeFile File, ZinniaCodeFileData FileData, int Next)
        {
            var EndLine = Next - 1;
            while (File.IsEmptyLine(EndLine) || FileData.Lines[EndLine].SkipLine)
                EndLine--;

            return EndLine;
        }
    }

    public class ZinniaVarDeclRecognizer : IVarDeclRecognizer
    {
        public bool Recognize(IdContainer Container, CodeString Code, bool EnableMessages, VarDeclarationList Out)
        {
            var State = Container.State;
            Code = Code.Trim();
            if (Code.Length == 0) return true;

            var CodeToSplit = Code;
            var SkippingHandlers = State.Language.GlobalHandlers;
            var ColonPos = Code.Find(':', false, SkippingHandlers);
            if (ColonPos != -1) CodeToSplit = CodeToSplit.Substring(0, ColonPos);

            var SplStrings = RecognizerHelper.SplitToParameters(State, CodeToSplit, ',', EnableMessages);
            if (SplStrings == null) return false;

            if (ColonPos != -1)
            {
                var Last = SplStrings[SplStrings.Length - 1];
                SplStrings[SplStrings.Length - 1] = Code.Substring(Last.Index - Code.Index);
            }

            var RetValue = true;
            var Unnamed = false;
            var Type = (Identifier)null;
            var TypeName = new CodeString();

            for (var i = 0; i < SplStrings.Length; i++)
            {
                var DeclCode = SplStrings[i];
                var Modifiers = global::Zinnia.Modifiers.Recognize(Container, ref DeclCode);
                if (Modifiers == null) { RetValue = false; continue; }

                var Pos = DeclCode.Find('=', Handlers: SkippingHandlers);
                if (Pos == -1) Pos = DeclCode.Length;

                var Def = DeclCode.Substring(0, Pos).Trim();
                var Name = Def.Word(Back: true, Handlers: SkippingHandlers);

                //---------------------------------------------------------------------------------
                var TypeSpecified = false;
                if (Def.Length > 0 && Name.Length > 0)
                {
                    if (Unnamed)
                    {
                        if (EnableMessages) State.Messages.Add(MessageId.MustBeUnnamed, DeclCode);
                        return false;
                    }

                    TypeName = Def;
                    TypeSpecified = true;
                }
                else
                {
                    if (i == 0) Unnamed = true;

                    if (Unnamed)
                    {
                        if (Name.Length == 0) TypeName = Def;
                        else TypeName = Name;

                        Name = new CodeString();
                        TypeSpecified = true;
                    }/*
					else
					{
						if (EnableMessages) State.Messages.Add(MessageId.UnnamedIdentifier, e);
						return false;
					}*/
                }

                //---------------------------------------------------------------------------------
                if (TypeSpecified)
                {
                    var Options = GetIdOptions.Default;
                    Options.EnableMessages = EnableMessages;
                    Options.Func = x => x.RealId is Type;

                    if ((Type = Container.RecognizeIdentifier(TypeName, Options)) == null) RetValue = false;
                    else Type.SetUsed();
                }
                else if (!TypeName.IsValid)
                {
                    if (EnableMessages) State.Messages.Add(MessageId.TypeNotSpecified, Name);
                    RetValue = false;
                }

                //---------------------------------------------------------------------------------
                var InitString = new CodeString();
                if (Pos != DeclCode.Length)
                {
                    InitString = DeclCode.TrimmedSubstring(State, Pos + 1);
                    if (!InitString.IsValid) RetValue = false;
                }

                var TN = TypeSpecified ? TypeName : new CodeString();
                if (Out.DefaultModifiers != null) Modifiers.AddRange(Out.DefaultModifiers);
                Out.Add(new VarDeclaration(DeclCode, TN, Type, Name, InitString, Modifiers));
            }

            return RetValue;
        }
    }

    public class ZinniaDeclarationRecognizer : BasicDeclarationRecognizer
    {
        public ZinniaDeclarationRecognizer()
            : base(ZinniaHelper.ForEachLine)
        {
        }
    }

    public delegate bool ForEachLineFunc(ScopeNode Data, Predicate<CodeString> Func);

    public abstract class BasicDeclarationRecognizer : IDeclarationRecognizer
    {
        public static string[] ExplicitImplicit = new string[] { "explicit", "implicit" };
        public string[] PropertyStrings = new string[] { "get", "set" };
        public string OperatorString = "operator";
        public ForEachLineFunc ForEachLineFunc;

        public BasicDeclarationRecognizer(ForEachLineFunc ForEachLineFunc)
        {
            this.ForEachLineFunc = ForEachLineFunc;
        }

        public bool ProcPropertyLine(PropertyScope Scope, CodeString Code)
        {
            var State = Scope.State;
            var Mods = Modifiers.Recognize(Scope, ref Code);
            if (Mods == null) return false;

            var FindRes = Code.StartsWith(PropertyStrings, null, new IdCharCheck(true));
            if (FindRes.Index == -1)
            {
                State.Messages.Add(MessageId.NotExpected, Code);
                return false;
            }

            var FuncName = Code.Substring(0, FindRes.String.Length);
            Code = Code.Substring(FindRes.String.Length).Trim();

            IdentifierAccess Access;
            if (!Modifiers.GetIdAccess(State, Mods, out Access))
                return false;

            if (Access == IdentifierAccess.Unknown)
                Access = Scope.Property.Access;

            var Inner = State.GetInnerScope(Code, FuncName, false);
            if (!Inner.IsValid) return false;

            if (Inner.Length == 0)
                Inner = new CodeString();

            if (FindRes.Index == 0)
            {
                if (Scope.CreateGetter(FuncName, Inner, Access) == null)
                    return false;
            }
            else if (FindRes.Index == 1)
            {
                var ValueName = new CodeString("value");
                if (Scope.CreateSetter(FuncName, ValueName, Inner, Access) == null)
                    return false;
            }
            else
            {
                throw new ApplicationException();
            }

            return true;
        }

        public bool ProcDeclarationLine(NonCodeScope Scope, CodeString Code)
        {
            var Mods = Modifiers.Recognize(Scope, ref Code);
            if (Mods == null) return false;

            var State = Scope.State;
            var Result = State.Language.CommandInnerSeparator.Separate(State, Code,
                CommandInnerSeparatorFlags.NoEmptyScopeWarning);

            if (!Result.Command.IsValid) return false;

            var FuncLine = Result.Command;
            var BracketStart = -1;

            CodeString LeftSide = new CodeString();
            int OpFindRes = -1;

            if (FuncLine.Length > 0 && FuncLine[FuncLine.Length - 1] == ')')
            {
                BracketStart = FuncLine.GetBracketPos(State, Back: true);
                if (BracketStart == -1) return false;

                if (BracketStart == 0)
                {
                    BracketStart = -1;
                }
                else
                {
                    var Handlers = State.Language.GlobalHandlers;
                    LeftSide = FuncLine.Substring(0, BracketStart).Trim();
                    OpFindRes = LeftSide.String.Find(OperatorString, null, false, new IdCharCheck(true), Handlers);

                    if (OpFindRes == -1)
                    {
                        if (LeftSide.Length == 0 || !Helper.IsIdChar(LeftSide[LeftSide.Length - 1]))
                            BracketStart = -1;
                    }
                }
            }

            //--------------------------------------------------------------------------------------
            if (BracketStart != -1)
            {
                var Str_Params = FuncLine.Substring(BracketStart + 1, FuncLine.Length - BracketStart - 2);
                var Params = State.GetParameters(Scope, Str_Params);
                if (Params == null) return false;

                Function Function;
                if (OpFindRes != -1)
                {
                    var OpStr = LeftSide.TrimmedSubstring(State, OpFindRes + OperatorString.Length);
                    if (!OpStr.IsValid) return false;

                    var StructuredScope = Scope as StructuredScope;
                    if (StructuredScope == null)
                    {
                        State.Messages.Add(MessageId.CannotDeclFunc, LeftSide);
                        return false;
                    }

                    var EnclosingType = StructuredScope.StructuredType;
                    LeftSide = LeftSide.Substring(0, OpFindRes).Trim();

                    CodeString Name;
                    Identifier RetType;

                    var Res = LeftSide.StartsWith(ExplicitImplicit, null, new IdCharCheck());
                    if (Res.Index != -1)
                    {
                        if (LeftSide.Length != Res.String.Length)
                        {
                            var ErrStr = LeftSide.Substring(Res.String.Length).Trim();
                            State.Messages.Add(MessageId.NotExpected);
                            return false;
                        }

                        if (Params.Length != 1)
                        {
                            var ErrStr = Str_Params;
                            if (Params.Length == 0)
                                ErrStr = FuncLine.Substring(BracketStart, FuncLine.Length - BracketStart);

                            State.Messages.Add(MessageId.ParamCount, Str_Params);
                        }

                        if (Res.Index == 0) Name = new CodeString("%Operator_Explicit");
                        else if (Res.Index == 1) Name = new CodeString("%Operator_Implicit");
                        else throw new ApplicationException();

                        RetType = Identifiers.Recognize(Scope, OpStr, GetIdOptions.DefaultForType);
                        if (RetType == null) return false;

                        if (!RetType.IsEquivalent(EnclosingType) && !Params[0].TypeOfSelf.IsEquivalent(EnclosingType))
                        {
                            State.Messages.Add(MessageId.CastOpInvalidTypes, LeftSide);
                            return false;
                        }
                    }
                    else
                    {
                        var Op = Operators.GetOperator(State, OpStr, Params.Length);
                        if (Op == Operator.Unknown) return false;

                        if (LeftSide.Length == 0)
                        {
                            State.Messages.Add(MessageId.UntypedFunction, OpStr);
                            return false;
                        }

                        Name = new CodeString("%Operator_" + Op.ToString());
                        RetType = Identifiers.Recognize(Scope, LeftSide, GetIdOptions.DefaultForType);
                        if (RetType == null) return false;

                        if (Params.TrueForAll(x => !x.TypeOfSelf.IsEquivalent(EnclosingType)))
                        {
                            State.Messages.Add(MessageId.NoncastOpInvalidTypes, LeftSide);
                            return false;
                        }
                    }

                    var Type = new TypeOfFunction(Scope, Scope.DefaultCallConv, RetType, Params);
                    Function = Scope.CreateAndDeclareFunction(Name, Type, Mods);
                }
                else
                {
                    var Name = LeftSide.Word(Back: true);
                    if (LeftSide.Length == 0)
                    {
                        var Structured = Scope as StructuredScope;
                        if (Structured == null || !Structured.StructuredType.Name.IsEqual(Name))
                        {
                            State.Messages.Add(MessageId.UntypedFunction, Name);
                            return false;
                        }

                        LeftSide = new CodeString();
                        Function = Structured.CreateDeclaredConstructor(Name, Params, Mods);
                        if (Function == null) return false;
                    }
                    else
                    {
                        Function = Scope.CreateAndDeclareFunction(Name, LeftSide, Params, Mods);
                        if (Function == null) return false;
                    }
                }

                if (Function.HasCode)
                {
                    if (!Result.Inner.IsValid && !(Function is Constructor && Params.Length == 0))
                    {
                        State.Messages.Add(MessageId.EmptyScope, Code);
                        Code = new CodeString();
                    }

                    var FuncScope = new FunctionScope(Scope, Function, Result.Inner);
                    Function.FunctionScope = FuncScope;

                    if (!FuncScope.Initialize()) return false;

                    var FSData = new AfterDeclarationData();
                    FuncScope.Data.Set(FSData);
                }

                ProcAfterDeclaration(ref Result, Function);
            }

            //--------------------------------------------------------------------------------------
            else if (Result.FindRes.Position != -1)
            {
                CodeString Parameters, TypeAndName;
                if (FuncLine[FuncLine.Length - 1] == ']')
                {
                    var Begin = FuncLine.GetBracketPos(State, true);
                    if (Begin == -1) return false;

                    Parameters = FuncLine.Substring(Begin + 1, FuncLine.Length - Begin - 2).Trim();
                    TypeAndName = FuncLine.Substring(0, Begin).Trim();

                    if (Parameters.Length == 0)
                    {
                        var ErrStr = FuncLine.Substring(Begin, FuncLine.Length - Begin);
                        State.Messages.Add(MessageId.NotExpected, ErrStr);
                        return false;
                    }
                }
                else
                {
                    Parameters = new CodeString();
                    TypeAndName = FuncLine;
                }

                if (TypeAndName.Length == 0)
                {
                    State.Messages.Add(MessageId.DeficientExpr, FuncLine);
                    return false;
                }

                var Str_Type = TypeAndName;
                var SkippingHandlers = State.Language.GlobalHandlers;
                var Name = Str_Type.Word(Back: true, Handlers: SkippingHandlers);

                if (Str_Type.Length == 0)
                {
                    State.Messages.Add(MessageId.TypeNotSpecified, TypeAndName);
                    return false;
                }
                else if (Name.Length == 0)
                {
                    State.Messages.Add(MessageId.NotValidName, TypeAndName);
                    return false;
                }

                var CodeProcessor = State.Language.CodeProcessor;
                if (Name.IsEqual(CodeProcessor.SelfName))
                {
                    Name = new CodeString();
                    if (!Parameters.IsValid)
                    {
                        State.Messages.Add(MessageId.ParamlessSelfIndexer, Name);
                        return false;
                    }
                }

                var Type = Identifiers.Recognize(Scope, Str_Type, GetIdOptions.DefaultForType);
                if (Type == null) return false;

                FunctionParameter[] Params = null;
                if (Parameters.IsValid)
                {
                    Params = State.GetParameters(Scope, Parameters);
                    if (Params == null) return false;
                }

                var Property = Scope.CreateProperty(Name, Type, Params, Mods);
                if (Property == null) return false;
                Property.Declaration = TypeAndName;

                var PropertyScope = new PropertyScope(Scope, Result.Inner, Property);
                Scope.Children.Add(PropertyScope);
                Property.PropertyScope = PropertyScope;

                var Res = ForEachLineFunc(PropertyScope, PropertyLine =>
                        ProcPropertyLine(PropertyScope, PropertyLine));

                if (!Res) return false;
                if (!PropertyScope.ProcessAutoImplementation()) return false;
                if (!Scope.DeclareIdentifier(Property)) return false;
            }

            //--------------------------------------------------------------------------------------
            else
            {
                if (!Scope.DeclareVariables(Code, Mods, IdMode: GetIdMode.Function))
                    return false;
            }

            return true;
        }

        private static void ProcAfterDeclaration(ref CommandInnerSeparatorResult Result, Function Func)
        {
            var FS = Func.FunctionScope;
            if (FS == null || !Result.Inner.IsValid)
                return;

            var Data = FS.Data.Get<AfterDeclarationData>();
            if (Result.FindRes.Position != -1)
            {
                Data.AfterDeclaration = ZinniaHelper.GetFirstScope(FS.State, Result.Inner);
                FS.Code = FS.Code.Substring(Data.AfterDeclaration.Length).Trim();
            }
            else
            {
                var FirstScope = ZinniaHelper.GetFirstScope(FS.State, Result.Inner);
                if (FirstScope.Length > 0 && FirstScope[0] == ':')
                {
                    Data.AfterDeclaration = FirstScope.Substring(1).Trim();
                    FS.Code = FS.Code.Substring(FirstScope.Length).Trim();
                }
            }
        }

        public bool Recognize(NonCodeScope Scope)
        {
            return ForEachLineFunc(Scope, Code => ProcDeclarationLine(Scope, Code));
        }
    }

    public class ZinniaConstDeclRecognizer : IConstDeclRecognizer
    {
        public bool Recognize(NonCodeScope Scope, ConstDeclarationList Out)
        {
            var State = Scope.State;
            var RetValue = true;

            if (Scope is EnumScope)
            {
                var EScope = Scope as EnumScope;
                RetValue = ZinniaHelper.ForEachLine(Scope, Code =>
                {
                    var Rec = State.Language.ParameterRecognizer;
                    var List = Rec.SplitToParameters(State, Code);
                    if (List == null) return false;

                    for (var i = 0; i < List.Length; i++)
                    {
                        var DeclStr = List[i];
                        var Pos = DeclStr.Find('=', true);
                        var Name = Pos == -1 ? DeclStr : DeclStr.TrimmedSubstring(State, 0, Pos);
                        var Value = Pos == -1 ? new CodeString() : DeclStr.TrimmedSubstring(State, Pos + 1);

                        if (!Name.IsValidIdentifierName)
                        {
                            State.Messages.Add(MessageId.NotValidName, Name);
                            return false;
                        }

                        Out.Add(new ConstDeclaration(Scope, Name, EScope.EnumType, Value, null));
                    }

                    return true;
                });
            }
            else
            {
                var VarRec = State.Language.VarDeclRecognizer;
                var File = Scope.Code.File;
                var FileData = File.Data.Get<ZinniaCodeFileData>();

                RetValue = ZinniaHelper.ForEachLine(Scope, Code =>
                {
                    var Mods = Modifiers.Recognize(Scope, ref Code);
                    if (Mods != null && Modifiers.Contains<ConstModifier>(Mods))
                    {
                        var VarDecls = VarDeclarationList.Create(Scope, Code);
                        if (VarDecls == null || !VarDecls.VerifyInitVal(Scope))
                            return false;

                        Out.AddRange(VarDecls.ToConstDecls(Scope, Mods));
                        FileData.Lines[Code.Line].SkipScope = true;
                    }

                    return true;
                });
            }

            return RetValue;
        }
    }

    public class ZinniaAliasDeclRecognizer : IAliasDeclRecognizer
    {
        public static string String = "alias";

        public bool Recognize(NonCodeScope Scope, AliasDeclarationList Out)
        {
            var State = Scope.State;
            var File = Scope.Code.File;
            var FileData = File.Data.Get<ZinniaCodeFileData>();
            var SkipHandlers = State.Language.GlobalHandlers;

            var RetValue = ZinniaHelper.ForEachLine(Scope, Code =>
            {
                var Mods = Modifiers.Recognize(Scope, ref Code);
                if (Mods == null) return false;

                if (!Code.StartsWith(String, IdCharCheck: new IdCharCheck(true)))
                    return true;

                Code = Code.Substring(String.Length).Trim();
                FileData.Lines[Code.Line].SkipScope = true;

                var SplString = RecognizerHelper.SplitToParameters(State, Code, ',');
                if (SplString == null) return false;

                for (var i = 0; i < SplString.Length; i++)
                {
                    var e = SplString[i];
                    var p = e.Find('=', Handlers: SkipHandlers);
                    if (p == -1)
                    {
                        State.Messages.Add(MessageId.DeficientExpr, e);
                        return false;
                    }

                    var Val = Code.TrimmedSubstring(State, p + 1);
                    if (!Val.IsValid) return false;

                    Code = Code.Substring(0, p).Trim();
                    var Name = Code.Word(Back: true);

                    if (Code.Length > 0)
                    {
                        State.Messages.Add(MessageId.TypeCannotBeSpecified, Code);
                        return false;
                    }

                    Out.Add(new AliasDeclaration(Scope, Name, Val, Mods));
                }

                return true;
            });

            return RetValue;
        }
    }

    public class ZinniaTypeDeclRecognizer : ITypeDeclRecognizer
    {
        public static string[] Strings = new string[] { "class", "struct", "enum", "flag" };

        StructureBase[] GetBaseList(CompilerState State, CodeString Code)
        {
            var Splitted = RecognizerHelper.SplitToParameters(State, Code, ',');
            if (Splitted == null) return null;

            var Succeeded = true;
            var Ret = new StructureBase[Splitted.Length];
            for (var i = 0; i < Splitted.Length; i++)
            {
                var Name = Splitted[i];
                var Reti = new StructureBase(Name);

                var LSucceeded = Modifiers.CustomRecognize(ref Name, (ref CodeString xCode) =>
                {
                    if (xCode.StartsWith("virtual", new IdCharCheck(true)))
                    {
                        Reti.Flags |= StructureBaseFlags.Virtual;
                        return SimpleRecResult.Succeeded;
                    }

                    return SimpleRecResult.Unknown;
                });

                if (!LSucceeded)
                {
                    Succeeded = false;
                    continue;
                }

                Reti.Name = Name;
                Ret[i] = Reti;
            }

            return Succeeded ? Ret : null;
        }

        public bool Recognize(NonCodeScope Scope, TypeDeclarationList Out)
        {
            var State = Scope.State;
            var File = Scope.Code.File;
            var FileData = File.Data.Get<ZinniaCodeFileData>();

            var RetValue = ZinniaHelper.ForEachLine(Scope, Code =>
            {
                var Mods = Modifiers.Recognize(Scope, ref Code);
                if (Mods == null) return false;

                var Result = Code.StartsWith(Strings, IdCharCheck: new IdCharCheck(true));
                if (Result.Index == -1) return true;

                TypeDeclType Type;
                if (Result.Index == 0) Type = TypeDeclType.Class;
                else if (Result.Index == 1) Type = TypeDeclType.Struct;
                else if (Result.Index == 2) Type = TypeDeclType.Enum;
                else if (Result.Index == 3) Type = TypeDeclType.Flag;
                else throw new ApplicationException();

                var Command = Code.Substring(0, Result.String.Length).Trim();
                Code = Code.Substring(Result.NextChar).Trim();
                FileData.Lines[Code.Line].SkipScope = true;

                var Name = Code.Word();
                if (Name.Length == 0)
                {
                    State.Messages.Add(MessageId.UnnamedIdentifier, Command);
                    return false;
                }

                StructureBase[] Bases;
                var FirstLine = Code.FirstLine;

                if (FirstLine.IsValid && FirstLine.Length > 0 && FirstLine[0] == ':')
                {
                    Code = Code.Substring(FirstLine.Length).Trim();
                    FirstLine = FirstLine.TrimmedSubstring(State, 1);
                    if (!FirstLine.IsValid) return false;

                    Bases = GetBaseList(State, FirstLine);
                    if (Bases == null) return false;
                }
                else
                {
                    Bases = new StructureBase[0];
                }

                var Inner = State.GetInnerScope(Code, Name);
                if (!Inner.IsValid) return false;

                Out.Add(new TypeDeclaration(Scope, Name, Type, Bases, Inner, Mods));
                return true;
            });

            return RetValue;
        }
    }

    public class ZinniaExprRecognizers : LanguageNode
    {
        public ZinniaExprRecognizers(LanguageNode Parent)
            : base(Parent)
        {
            Children = new LanguageNode[]
            {
                new TestScopeRecognizer(this),
                new KeywordExprRecognizer(this),
                new NumberRecognizer(this),
                new StringRecognizer(this),
                new CharRecognizer(this),
                new RefRecognizer(this),
                new ZinniaNewRecognizer(this),
                new IfThenRecognizer(this),
                new VarDeclRecignizer(this),
                new CheckedUncExprRecognizer(this),
                new IsAsToRecognizer(this),
                new LogicalRecognizer(this),
                new NotRecognizer(this),
                new AssignmentRecognizer(this),
                new RelEquRecognizer(this, RelEquRecognizerFlags.DisableOpposed),
                new RefEqualityRecognizer(this),
                new TupleCreatingRecognizer(this),
                new ArrayCreatingRecognizer(this),
                new RangeRecognizer(this),
                new AdditiveRecognizer(this),
                new MultiplicativeRecognizer(this),
                new BitwiseRecognizer(this),
                new ShiftRecognizer(this),
                new NullCoalescingRecognizer(this),

                new NegateRecognizer(this),
                new ComplementRecognizer(this),
                new AddressRecognizer(this),
                new IncDecRecognizer(this),
                new IndirectionRecognizer(this),
				//new CastRecognizer(this),
				new IndexRecognizer(this),

                new CallRecognizer(this, new List<IBuiltinFuncRecognizer>()
                {
                    new DefaultRecognizer(),
                    new StackAllocRecognizer(),
                    new IsDefinedRecognizer(),
                    new SizeOfRecognizer(),
                    new ReinterpretCastRecognizer(),
                    new DataPointerRecognizer(),
                    new IncBinRecognizer(),
                }),

                new SafeNavigationRecognizer(this),
                new MemberRecognizer(this, MemberRecognizerFlags.AllowScopeResolution),
                new ExprIdRecognizer(this),
            };
        }
    }

    public class ZinniaCommRecognizers : LanguageNode
    {
        public ZinniaCommRecognizers(LanguageNode Parent)
            : base(Parent)
        {
            Children = new LanguageNode[]
            {
                new CtorCallRecognizer(this),
                new ExtraStorageRecognizer(this),
                new NonAfterDeclBlocker(this),

                new IfCommRecognizer(this),
				//new ForCommRecognizer(this),
				new ForInCommRecognizer(this),
                new ElseCommRecognizer(this),
                new BreakCommRecognizer(this),
                new ContinueCommRecognizer(this),
                new SwitchCommRecognizer(this),
                new WhileCommRecognizer(this),
                new DoCommRecognizer(this),
                new RepeatCommRecognizer(this),
                new CycleCommRecognizer(this),
                new TryCommRecognizer(this),
                new CheckedUncheckedCommRecognizer(this),

                new LineSplittingRecognizer(this),
                new ReturnCommRecognizer(this),
                new WithCommRecognizer(this),
                new GotoCommRecognizer(this),
                new LabelCommRecognizer(this),
                new ThrowCommRecognizer(this),

                new VarDeclCommRecognizer(this),
                new ExprCommRecognizer(this),
            };
        }
    }

    public class ZinniaCodeProcessor : CodeProcessor
    {
        public override string SelfName
        {
            get { return "this"; }
        }

        public override string BaseName
        {
            get { return "base"; }
        }

        public override bool Process(CodeScopeNode Scope)
        {
            var State = Scope.State;
            if (Scope is FunctionScope)
            {
                var FSData = Scope.Data.Get<AfterDeclarationData>();
                if (FSData != null && FSData.AfterDeclaration.IsValid)
                {
                    FSData.InAfterDeclaration = true;
                    if (!ZinniaHelper.ForEachLine(State, FSData.AfterDeclaration, Code =>
                        Scope.RecognizeCommand(Code))) return false;

                    FSData.InAfterDeclaration = false;
                }
            }

            return ZinniaHelper.ForEachLine(Scope, Code =>
                Scope.RecognizeCommand(Code));
        }
    }

    public class ZinniaIdRecognizers : LanguageNode
    {
        public ZinniaIdRecognizers(LanguageNode Parent)
            : base(Parent)
        {
            Children = new LanguageNode[]
            {
                new FunctionTypeRecognizer(this),
                new OldFunctionTypeRecognizer(this),
                new TupleTypeRecognizer(this),
                new RefTypeRecognizer(this),
                new ArrayRecognizer(this),
                new MemberTypeRecognizer(this),
                new PointerTypeRecognizer(this),
                new SimpleIdRegocnizer(this),
            };
        }
    }

    public class ZinniaModRecognizers : LanguageNode
    {
        public ZinniaModRecognizers(LanguageNode Parent)
            : base(Parent)
        {
            Children = new LanguageNode[]
            {
                new AlignModifierRecognizer(this),
                new CallingConventionRecognizer(this),
                new AccessModifierRecognizer(this),
                new FlagModifierRecognizer(this),
                new ParamFlagModifierRecognizer(this),
                new ConstModifierRecognizer(this),
                new AssemblyNameRecognizer(this),
                new NoBaseModifierRecognizer(this),
            };
        }
    }

    public class ZinniaCommentRecognizer : ICommentRecognizer
    {
        public static string[] Strings = new string[] { "''", "rem" };

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
                    var Index = LineStr.Index + Result.Position;
                    var Length = LineStr.FirstLineLength - Result.Position;
                    File.RemoveCode(Index, Length, Line);
                }
                else if (Result.Index == 1)
                {
                    var Index = LineStr.Index + Result.Position;
                    var Length = ZinniaHelper.GetNextLinePosition(File, Line) - Index;
                    File.RemoveCode(Index, Length, Line);
                }

                Line++;
            }

            return true;
        }
    }

    public class ZinniaCodeFileProcessor : ICodeFileProcessor
    {
        public ICommentRecognizer CommentRecognizer;

        public ZinniaCodeFileProcessor()
        {
            CommentRecognizer = new CCommentRecognizer();
        }

        public bool Process(AssemblyScope Scope)
        {
            if (!Scope.Code.IsValid)
                return true;

            var State = Scope.State;
            var File = Scope.Code.File;
            var LineCount = File.GetLineCount();

            var FileData = new ZinniaCodeFileData();
            FileData.Lines = new ZinniaLineData[LineCount];
            File.Data.Set(FileData);

            if (!CommentRecognizer.Process(State, Scope.Code))
                return false;

            var RetValue = true;
            var Preprocessor = State.GlobalContainer.Preprocessor;
            for (var i = 0; i < LineCount; i++)
            {
                if (File.IsEmptyLine(i)) continue;

                var Res = Preprocessor.ProcessLine(File.GetLines(i));
                if (Res != SimpleRecResult.Unknown)
                {
                    if (Res == SimpleRecResult.Failed) RetValue = false;
                    else FileData.Lines[i].SkipLine = true;
                }
            }

            if (!Preprocessor.CheckConditions())
                RetValue = false;

            return RetValue;
        }
    }

    public class ZinniaNamespaceDeclRecognizer : INamespaceDeclRecognizer
    {
        public static string[] Strings = new string[] { "namespace", "using" };

        public bool Recognize(NamespaceScope Scope, NamespaceDeclList Out)
        {
            var State = Scope.State;
            var File = Scope.Code.File;
            var FileData = File.Data.Get<ZinniaCodeFileData>();

            return ZinniaHelper.ForEachLine(Scope, Code =>
            {
                var Result = Code.StartsWith(Strings, null, new IdCharCheck(true));
                if (Result.Index == -1) return true;

                var Command = Code.Substring(0, Result.String.Length).Trim();
                Code = Code.Substring(Result.NextChar).Trim();
                FileData.Lines[Code.Line].SkipScope = true;

                var Name = Code.FirstLine;
                if (Name.Length == 0)
                {
                    State.Messages.Add(MessageId.UnnamedIdentifier, Command);
                    return false;
                }

                var Names = new List<CodeString>();
                if (!RecognizerHelper.SplitToParameters(State, Name, '.', Names))
                    return false;

                for (var i = 0; i < Names.Count; i++)
                    Names[i] = Names[i].Trim();

                if (Result.Index == 0)
                {
                    Code = Code.Substring(Name.Length).Trim();
                    var Inner = State.GetInnerScope(Code, Name);
                    if (!Inner.IsValid) return false;

                    Out.Add(new NamespaceDecl(Scope, NamespaceDeclType.Declare, Names, Inner));
                }
                else if (Result.Index == 1)
                {
                    Out.Add(new NamespaceDecl(Scope, NamespaceDeclType.Use, Names));
                }
                else
                {
                    throw new ApplicationException();
                }

                return true;
            });
        }
    }

    public class ZinniaCommandInnerSeparator : LanguageNode, ICommandInnerSeparator
    {
        public ZinniaCommandInnerSeparator(LanguageNode Parent)
            : base(Parent, LanguageNodeFlags.FindSkipFromAll)
        {
            Operators = new string[] { ":" };
        }

        public static CodeString GetFirstExpression(Language Lang, CodeString Code)
        {
            var File = Code.File;
            var FileData = File.Data.Get<ZinniaCodeFileData>();
            var Line = Code.Line;

            while (Code.HasLine(Line))
            {
                if (!File.IsEmptyLine(Line) && !FileData.Lines[Line].SkipLine)
                {
                    var StrLine = Code.GetLine(Line).Trim();
                    var R = StrLine.EndsWith(Lang.Root.NewLineRight, Lang.Root.NewLineRightSkip, new IdCharCheck(true));
                    if (R.Index == -1) return Code.GetLines(Code.Line, Line - Code.Line + 1);
                }

                Line++;
            }

            return Code;
        }

        public CommandInnerSeparatorResult Separate(CompilerState State, CodeString Code,
            CommandInnerSeparatorFlags Flags = CommandInnerSeparatorFlags.None)
        {
            var Res = new CommandInnerSeparatorResult();
            var SkippingHandlers = State.Language.GlobalHandlers;
            var Line = GetFirstExpression(State.Language, Code);

            Res.FindRes = Line.Find(Operators, Skip, Handlers: SkippingHandlers);
            if (Res.FindRes.Position != -1)
            {
                var ResLength = Res.FindRes.String != null ? Res.FindRes.String.Length : 1;
                var InnerStart = Res.FindRes.Position + ResLength;
                var InnerSrc = Code.Substring(InnerStart).Trim();
                if (InnerSrc.Length == 0)
                {
                    var Do = Code.Substring(Res.FindRes.Position, ResLength);
                    State.Messages.Add(MessageId.DeficientExpr, Do);
                    return new CommandInnerSeparatorResult();
                }

                Res.Command = Code.Substring(0, Res.FindRes.Position).Trim();
                Res.Inner = State.GetInnerScope(InnerSrc, Res.Command,
                    (Flags & CommandInnerSeparatorFlags.NoEmptyScopeWarning) == 0);

                if (!Res.Inner.IsValid) return new CommandInnerSeparatorResult();
                if (Res.Inner.Length == 0) Res.Inner = new CodeString();
                return Res;
            }
            else if ((Flags & CommandInnerSeparatorFlags.InnerIsOptional) == 0)
            {
                Res.Command = Line.Trim();

                var InnerSrc = Code.Substring(Line.Length).Trim();
                Res.Inner = State.GetInnerScope(InnerSrc, Res.Command,
                    (Flags & CommandInnerSeparatorFlags.NoEmptyScopeWarning) == 0);

                if (!Res.Inner.IsValid) return new CommandInnerSeparatorResult();
                if (Res.Inner.Length == 0) Res.Inner = new CodeString();
                return Res;
            }
            else
            {
                Res.Command = Code;
                return Res;
            }
        }
    }

    public class ZinniaLanguage : Language
    {
        public ZinniaLanguage()
        {
            Root = new LanguageNode(this);

            //LanguageFlags |= LangaugeFlags.ConvertParametersToTuple;
            Flags |= LangaugeFlags.AllowMemberFuncStaticRef;
            var RetValLessRec = new RetValLessRecognizer(Root);

            Root.Children = new LanguageNode[]
            {
                RetValLessRec,
                new ZinniaCommandInnerSeparator(Root),
                new ZinniaCommRecognizers(Root),

                new BracketRecognizer(Root),
                new ZinniaModRecognizers(Root),
                new ZinniaExprRecognizers(Root),
                new ZinniaIdRecognizers(Root),

                new SimpleArgRecognizer(Root),
                new GenericRecognizer(Root),
                new BracketGroupRecognizer(Root, '(', ')')
            };

            RetValLessRecognizer = RetValLessRec;
            RetValLessRec.RunBefore = new IExprRecognizer[]
            {
                Root.GetObject<AssignmentRecognizer>(),
                Root.GetObject<IncDecRecognizer>(),
            };

            VarDeclRecognizer = new ZinniaVarDeclRecognizer();
            NamespaceDeclRecognizer = new ZinniaNamespaceDeclRecognizer();
            TypeDeclRecognizer = new ZinniaTypeDeclRecognizer();
            ConstDeclRecognizer = new ZinniaConstDeclRecognizer();
            DeclarationRecognizer = new ZinniaDeclarationRecognizer();
            AliasDeclRecognizer = new ZinniaAliasDeclRecognizer();
            CommandInnerSeparator = Root.GetObject<ICommandInnerSeparator>();
            FullNameGenerator = new FullNameGenerator(".");

            CodeProcessor = new ZinniaCodeProcessor();
            CodeFileProcessor = new ZinniaCodeFileProcessor();
            Init();

            foreach (var e in Root.GetObjects<BracketRecognizer>())
                if (e.GenericBracketSkipOptions.Enabled)
                {
                    var Old = e.GenericBracketSkipOptions.DisableFind;
                    e.GenericBracketSkipOptions.DisableFind = Old.Union(":").ToArray();
                }
        }
    }
}
