using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using Zinnia.Base;

namespace Zinnia.NativeCode
{
    public enum NCCommandType : byte
    {
        Unknown,
        Jump,
    }

    class NCFuncScopeData
    {
        public Variable FinallyReturn;
        public int FinallyReturnLabel = -1;
    }

    public class NCCommandData
    {
        public Variable FinallyJump;
        public Variable CatchVariable;
    }

    public class NCCommandExtension : ICommandExtension
    {
        public Command Command;
        public NCCommandType Type;

        public NCCommandExtension(Command Command, NCCommandType Type)
        {
            this.Command = Command;
            this.Type = Type;
        }

        public void GetAssembly(CodeGenerator CG)
        {
            if (Type == NCCommandType.Jump)
            {
                var NCCG = CG as INCCodeGenerator;
                NCCG.Jump(Command.Expressions[0]);
            }
            else
            {
                throw new ApplicationException();
            }
        }
    }

    public interface INCCodeGenerator
    {
        void Jump(ExpressionNode Node);
    }

    public interface INCArchitecture
    {
        bool SetupPlugin(PluginRoot Plugin);
        bool ProcessContainer(IdContainer Container);
        ExpressionNode OverflowCondition(PluginRoot Plugin, ExpressionNode Node,
            CodeString Code, BeginEndMode BEMode = BeginEndMode.Both);
    }

    public class NCProcessor
    {
        public FunctionScope FuncScope;
        public INCArchitecture NCArch;
        public PluginRoot Plugin;

        NCProcessor(FunctionScope FuncScope, INCArchitecture NCArch)
        {
            this.FuncScope = FuncScope;
            this.NCArch = NCArch;
        }

        public PluginRoot CreatePlugin()
        {
            var Plugin = new PluginForCodeScope(FuncScope);
            var TypeMngrPlugin = Plugin.GetPlugin<TypeMngrPlugin>();
            TypeMngrPlugin.Flags |= TypeMngrPluginFlags.EnableAll;
            TypeMngrPlugin.CheckingMode = CheckingMode.Default;
            Plugin.CallNewNodeFrom = Plugin.Plugins.Length;
            Plugin.Plugins = Plugin.Plugins.Union(new NCPlugin(Plugin, this)).ToArray();

            if (!NCArch.SetupPlugin(Plugin)) return null;
            return Plugin;
        }

        public static bool ProcessCode(FunctionScope Scope)
        {
            var NCArch = Scope.State.Arch as INCArchitecture;
            var NCProcessor = new NCProcessor(Scope, NCArch);
            NCProcessor.Plugin = NCProcessor.CreatePlugin();

            var NSData = Scope.Data.Create<NCFuncScopeData>();
            if (!NCProcessor.ProcessRecursively(Scope))
                return false;

            Scope.Data.Remove<NCFuncScopeData>();
            return true;
        }

        bool ProcessRecursively(IdContainer Container)
        {
            if (!PreprocessContainer(Container))
                return false;

            for (var i = 0; i < Container.Children.Count; i++)
            {
                var Ch = Container.Children[i];
                if (!ProcessRecursively(Ch)) return false;
            }

            return ProcessContainer(Container);
        }

        public bool PreprocessContainer(IdContainer Container)
        {
            if (Container is Command)
            {
                var Command = Container as Command;
                if (Command.Type == CommandType.Try)
                {
                    if ((Command.Flags & CommandFlags.TryHasCatchVariable) != 0)
                    {
                        var Global = Container.GlobalContainer;
                        var ExceptionClass = Identifiers.GetByFullNameFast<ClassType>(Global, "System.Exception");
                        if (ExceptionClass == null) return false;

                        var CatchScope = Command.CatchScope;
                        var CatchVar = CatchScope.IdentifierList[0] as LocalVariable;
                        if (CatchVar == null || !CatchVar.TypeOfSelf.IsEquivalent(ExceptionClass))
                            throw new ApplicationException("Invalid catch variable");

                        CatchVar.Container = Command;
                        CatchScope.IdentifierList.RemoveAt(0);
                        Command.IdentifierList.Add(CatchVar);

                        var NCData = Command.Data.GetOrCreate<NCCommandData>();
                        NCData.CatchVariable = CatchVar;
                    }
                }
            }

            return true;
        }

        public bool ProcessContainer(IdContainer Container, bool NoExtract = false)
        {
            if (Container is Command)
            {
                var Old = Container;
                var OldParent = Old.Parent;

                Container = ProcessCommand(Container as Command, NoExtract);
                if (Container == null) return false;

                if (Container != Old && OldParent.Children.Contains(Old))
                    throw new ApplicationException();
            }
            else if (Container is FunctionScope)
            {
                if (!ProcessFunctionScope(Container as FunctionScope))
                    return false;
            }

            if (!NCArch.ProcessContainer(Container))
                return false;

            if (Container is Command)
            {
                var Command = Container as Command;
                if (Command.Expressions != null)
                {
                    var Plugin = this.Plugin;
                    if (Plugin.CurrentlyUsing) Plugin = CreatePlugin();

                    Plugin.Container = Command;
                    for (var i = 0; i < Command.Expressions.Count; i++)
                    {
                        var Expr = Command.Expressions[i].CallNewNode(Plugin);
                        if (Expr == null) { Plugin.Reset(); return false; }

                        Command.Expressions[i] = Expr;
                    }
                }
            }

            return true;
        }

        bool ProcessFunctionScope(FunctionScope FS)
        {
            if (FS.Function is Constructor)
            {
                var StructuredScope = FS.Function.Container as StructuredScope;
                var Class = StructuredScope.StructuredType as ClassType;
                if (Class != null && (FS.Function.Flags & IdentifierFlags.Static) == 0)
                {
                    var Index = 0;
                    if (!CallInitializer(FS, ref Index, FS.Function.Declaration))
                        return false;
                }
            }

            var FSData = FS.Data.Get<NCFuncScopeData>();
            if (FSData.FinallyReturn != null)
            {
                var Code = FS.Function.Declaration;
                var List = new List<Command>();
                var SkipperReturn = new Command(FS, Code, CommandType.Return);
                SkipperReturn.Label = FS.ReturnLabel;
                List.Add(SkipperReturn);

                var FinallyReturnLabel = new Command(FS, Code, CommandType.Label);
                FinallyReturnLabel.Label = FSData.FinallyReturnLabel;
                List.Add(FinallyReturnLabel);

                var NewPlugin = FS.GetPlugin();
                if (!NewPlugin.Begin()) return false;

                var Node = NewPlugin.NewNode(new IdExpressionNode(FSData.FinallyReturn, Code));
                if (Node == null || NewPlugin.End(ref Node) == PluginResult.Failed) return false;

                var FinallyReturn = new Command(FS, Code, CommandType.Return);
                FinallyReturn.Expressions = new List<ExpressionNode>() { Node };
                FinallyReturn.Label = FS.ReturnLabel;
                List.Add(FinallyReturn);

                FS.Children.InsertRange(FS.Children.Count - 1, List);

                if (!ProcessContainer(SkipperReturn)) return false;
                if (!ProcessContainer(FinallyReturnLabel)) return false;
                if (!ProcessContainer(FinallyReturn)) return false;
            }

            return true;
        }

        IdContainer ProcessCommand(Command Command, bool NoExtract = false)
        {
            var State = Command.State;
            var GlobalContainer = State.GlobalContainer;
            var Code = Command.Code;

            if (!NoExtract && Command.Expressions != null && Command.Expressions.Count > 0)
            {
                var RunBefores = NCExpressions.GetCommandRunBefores(Command);
                if (!RunBefores.TrueForAll(x => x.NeededRunBefores.Length == 0))
                    return ExtractCommand(Command, RunBefores);
            }

            if (Command.Type == CommandType.Try)
            {
                var NCData = Command.Data.GetOrCreate<NCCommandData>();
                if (NCData.FinallyJump != null && !InitializeFinallyJump(Command, Code))
                    return null;

                var TryScope = Command.Children[0] as CodeScopeNode;
                var CatchVar = GetCatchVariable(Command);
                if (CatchVar == null) return null;

                var Pos = NCData.FinallyJump != null ? 1 : 0;
                if (!EnterTryBlock(TryScope, Pos, Code, CatchVar, Command.CatchLabel))
                    return null;

                if (!LeaveTryBlock(TryScope, Code)) return null;

                if (Command.CatchScope == null)
                {
                    if (!FinallyRethrow(Command, Command.FinallyScope, Code))
                        return null;
                }

                if (Command.FinallyScope != null && NCData.FinallyJump != null)
                {
                    if (!FinallyJump(Command.FinallyScope, NCData.FinallyJump, Command.Code))
                        return null;
                }
            }

            else if (Command.Type == CommandType.Throw || Command.Type == CommandType.Rethrow)
            {
                var NewPlugin = Command.GetPlugin();
                if (!NewPlugin.Begin()) return null;

                ExpressionNode Node;
                if (Command.Expressions != null && Command.Expressions.Count > 0)
                {
                    Node = Command.Expressions[0];
                }
                else
                {
                    var TryComm = Command.GetParent(CommandType.Try);
                    var CatchVar = GetCatchVariable(TryComm);

                    Node = NewPlugin.NewNode(new IdExpressionNode(CatchVar, Command.Code));
                    if (Node == null) return null;

                    Command.Expressions = new List<ExpressionNode>() { null };
                }

                Node = NCExpressions.Throw(NewPlugin, Node, Code);
                if (Node == null) return null;

                Command.Expressions[0] = Node;
                Command.Type = CommandType.Expression;
            }

            else if (Commands.IsJumpCommand(Command.Type))
            {
                var TryComms = new AutoAllocatedList<Command>();
                Command.ForEachJumpedOver<Command>(x =>
                {
                    if (x.Type == CommandType.Try && x.FinallyScope != null)
                        TryComms.Add(x);
                });

                if (TryComms.Count > 0)
                {
                    var NewScope = new CodeScopeNode(Command.Parent, Code);
                    Command.Parent.ReplaceChild(Command, NewScope);

                    var LastLabel = Command.Label;
                    if (Command.Type == CommandType.Return)
                    {
                        if (!SetFinallyReturn(NewScope, Command.Expressions[0], Code))
                            return null;

                        var FS = Command.FunctionScope;
                        var FSData = FS.Data.Get<NCFuncScopeData>();
                        LastLabel = FSData.FinallyReturnLabel;
                    }

                    for (var i = 0; i < TryComms.Count - 1; i++)
                    {
                        var Label = TryComms[i + 1].FinallyLabel;
                        if (!SetFinallyJump(TryComms[i], NewScope, Label, Code))
                            return null;
                    }

                    var Last = TryComms[TryComms.Count - 1];
                    if (!SetFinallyJump(Last, NewScope, LastLabel, Code)) return null;
                    if (!CreateJump(NewScope, TryComms[0].FinallyLabel, Code)) return null;
                    Command.FunctionScope.NeverSkippedLabels.Add(Command.Label);
                    return NewScope;
                }
            }

            return Command;
        }

        #region CommandExtraction
        bool AddFreeScopes(CodeScopeNode Scope, int Count, CodeString Code)
        {
            for (var i = 0; i < Count; i++)
                Scope.Children.Add(new CodeScopeNode(Scope, Code));

            return true;
        }

        CodeScopeNode CreateScopeForCommand(Command Command)
        {
            var Scope = new CodeScopeNode(Command.Parent, Command.Code);
            Command.Parent.ReplaceChild(Command, Scope);
            CopyIdentifiers(Scope, Command);
            return Scope;
        }

        static void CopyIdentifiers(IdContainer Dst, IdContainer Src)
        {
            Dst.IdentifierList = Src.IdentifierList;
            for (var i = 0; i < Dst.IdentifierList.Count; i++)
                Dst.IdentifierList[i].Container = Dst;
        }

        public IdContainer ExtractCommand(Command Command, NCExpressionRunBefores[] RunBefores)
        {
            var State = Command.State;
            var Code = Command.Code;

            if (Command.Type == CommandType.For)
            {
                var Scope = CreateScopeForCommand(Command);
                if (Command.Expressions[0] != null)
                {
                    var Init = new Command(Scope, Code, CommandType.Expression);
                    Init.Expressions = new List<ExpressionNode>() { Command.Expressions[0] };
                    Scope.Children.Add(Init);
                    if (!ProcessContainer(Init)) return null;
                }

                var While = new Command(Scope, Code, CommandType.While);
                While.Expressions = new List<ExpressionNode>() { Command.Expressions[1] };
                Scope.Children.Add(While);

                var WhileScope = new CodeScopeNode(While, Code);
                While.Children.Add(WhileScope);
                WhileScope.Children.Add(Command.Children[0]);
                Command.Children[0].Parent = WhileScope;

                var Loop = new Command(WhileScope, Code, CommandType.Expression);
                Loop.Expressions = new List<ExpressionNode>() { Command.Expressions[2] };
                WhileScope.Children.Add(Loop);
                if (!ProcessContainer(Loop)) return null;

                if (!ProcessContainer(WhileScope)) return null;
                if (!ProcessContainer(While)) return null;
                if (!ProcessContainer(Scope)) return null;
                return Scope;
            }

            else if (Command.Type == CommandType.While || Command.Type == CommandType.DoWhile)
            {
                var Cycle = new Command(Command.Parent, Code, CommandType.Cycle);
                Command.Parent.ReplaceChild(Command, Cycle);
                CopyIdentifiers(Cycle, Command);

                var CycleScope = new CodeScopeNode(Cycle, Code);
                Cycle.Children.Add(CycleScope);

                var If = new Command(CycleScope, Code, CommandType.If);
                CycleScope.Children.Add(If);

                var Condition = NCExpressions.Negate(If.GetPlugin(), Command.Expressions[0], Code);
                If.Expressions = new List<ExpressionNode>() { Condition };
                if (Condition == null) return null;

                var Then = new Command(If, Code, CommandType.Break);
                Then.Label = Cycle.BreakLabel;
                If.Children.Add(Then);

                if (!ProcessContainer(Then)) return null;
                if (!ProcessContainer(If)) return null;

                if (Command.Type == CommandType.While)
                    CycleScope.Children.Add(Command.Children[0]);
                else CycleScope.Children.Insert(0, Command.Children[0]);
                Command.Children[0].Parent = CycleScope;

                if (!ProcessContainer(CycleScope)) return null;
                if (!ProcessContainer(Cycle)) return null;
                return Cycle;
            }

            else if (Command.Type == CommandType.If)
            {
                var Scope = CreateScopeForCommand(Command);
                if (Command.Expressions.Count == 1)
                {
                    var FreeScopeCount = RunBefores[0].NeededRunBefores.Length;
                    if (!AddFreeScopes(Scope, FreeScopeCount, Code)) return null;

                    Scope.Children.Add(Command);
                    Command.Parent = Scope;

                    if (!ProcessContainer(Command, true))
                        return null;
                }
                else
                {
                    var Label = State.AutoLabel;
                    var LabelComm = new Command(Scope, Code, CommandType.Label);
                    LabelComm.Label = Label;

                    for (var i = 0; i < Command.Expressions.Count; i++)
                    {
                        var If = new Command(Scope, Code, CommandType.If);
                        If.Expressions = new List<ExpressionNode>() { Command.Expressions[i] };
                        Scope.Children.Add(If);

                        if (i == Command.Expressions.Count - 1)
                        {
                            If.Children.Add(Command.Children[i]);
                            Command.Children[i].Parent = If;

                            for (var j = i + 1; j < Command.Children.Count; j++)
                            {
                                If.Children.Add(Command.Children[j]);
                                Command.Children[j].Parent = If;
                            }
                        }
                        else
                        {
                            var ThenScope = new CodeScopeNode(If, Code);
                            If.Children.Add(ThenScope);

                            ThenScope.Children.Add(Command.Children[i]);
                            Command.Children[i].Parent = ThenScope;

                            var Goto = new Command(ThenScope, Code, CommandType.Goto);
                            Goto.JumpTo = LabelComm;
                            Goto.Label = Label;
                            ThenScope.Children.Add(Goto);

                            if (!ProcessContainer(Goto)) return null;
                            if (!ProcessContainer(ThenScope)) return null;
                        }

                        if (!ProcessContainer(If))
                            return null;
                    }

                    Scope.Children.Add(LabelComm);

                    if (!ProcessContainer(LabelComm))
                        return null;
                }

                if (!ProcessContainer(Scope)) return null;
                return Scope;
            }

            else
            {
                if (RunBefores.Length != 1) throw new ApplicationException();

                var Scope = CreateScopeForCommand(Command);
                var FreeScopeCount = RunBefores[0].NeededRunBefores.Length;
                if (!AddFreeScopes(Scope, FreeScopeCount, Code)) return null;

                Scope.Children.Add(Command);
                Command.Parent = Scope;

                if (!ProcessContainer(Command, true)) return null;
                if (!ProcessContainer(Scope)) return null;
                return Scope;
            }
        }
        #endregion

        #region ClassConstructor
        public bool CallInitializer(CodeScopeNode Scope, ref int Index, CodeString Code)
        {
            var FS = Scope.FunctionScope;
            var Structured = FS.Parent as StructuredScope;
            var Type = Structured.StructuredType;
            var Class = Type as ClassType;

            var Plugin = Scope.GetPlugin();
            if (!Plugin.Begin()) return false;

            ExpressionNode SizeN;
            if (FS.ObjectSize == null)
            {
                SizeN = Plugin.NewNode(Constants.GetIntValue(Scope, Class.InstanceSize, Code, true));
                if (SizeN == null) return false;
            }
            else
            {
                SizeN = FS.ObjectSize.Copy(Plugin, Mode: BeginEndMode.None);
                if (SizeN == null) return false;
            }

            return CallInitializer(Scope, ref Index, Plugin, SizeN, Code);
        }

        bool CallInitializer(CodeScopeNode Scope, ref int Index, PluginRoot Plugin,
            ExpressionNode Size, CodeString Code)
        {
            var FS = Scope.FunctionScope;
            var Structured = FS.Parent as StructuredScope;
            var Type = Structured.StructuredType;

            var FType = Scope.FunctionScope.Function.Children[0];
            var NoCondition = FType.Children[0].RealId is VoidType;
            if (NoCondition) return CallInitializerWithoutCmp(Scope, ref Index, Plugin, Size, Code);
            else return CallInitializerWithCmp(Scope, ref Index, Plugin, Size, Code);
        }

        bool CallInitializerWithCmp(CodeScopeNode Scope, ref int Index, PluginRoot Plugin, ExpressionNode Size, CodeString Code)
        {
            var CondComm = new Command(Scope, Code, CommandType.If);
            Scope.Children.Insert(Index, CondComm);
            Index++;

            var ThenScope = new CodeScopeNode(CondComm, new CodeString());
            CondComm.Children = new List<IdContainer>() { ThenScope };

            var CmpPlugin = ThenScope.GetPlugin();
            if (!CmpPlugin.Begin()) return false;

            var FS = Scope.FunctionScope;
            var Self = CmpPlugin.NewNode(new IdExpressionNode(FS.SelfVariable, Code));
            var Null = CmpPlugin.NewNode(Constants.GetNullValue(Scope, Code));
            if (Self == null || Null == null) return false;

            var CmpCh = new ExpressionNode[] { Self, Null };
            var CmpNode = CmpPlugin.NewNode(new OpExpressionNode(Operator.RefEquality, CmpCh, Code));
            if (CmpNode == null || (CmpNode = CmpPlugin.End(CmpNode)) == null) return false;
            CondComm.Expressions = new List<ExpressionNode>() { CmpNode };

            var NewIndex = ThenScope.Children.Count;
            if (!CallInitializerWithoutCmp(ThenScope, ref NewIndex, Plugin, Size, Code)) return false;
            if (!ProcessContainer(ThenScope)) return false;
            return ProcessContainer(CondComm);
        }

        bool CallInitializerWithoutCmp(CodeScopeNode Scope, ref int Index, PluginRoot Plugin, ExpressionNode Size, CodeString Code)
        {
            var Self = Scope.FunctionScope.SelfVariable;
            if (!AllocateObject(Scope, ref Index, Self, Size, Plugin, Code)) return false;

            var StructuredType = Scope.StructuredScope.StructuredType;
            var ObjectType = Identifiers.GetByFullNameFast<ClassType>(Scope.GlobalContainer, "System.Object");
            if (Identifiers.IsSubtypeOrEquivalent(StructuredType, ObjectType))
            {
                if (!SetFunctionTable(Scope, ref Index, Self, Code)) return false;
                if (!SetTypePointer(Scope, ref Index, Self, Code)) return false;
            }

            return true;
        }

        private bool SetFunctionTable(CodeScopeNode Scope, ref int Index, SelfVariable Self, CodeString Code)
        {
            var Class = Self.TypeOfSelf.UnderlyingClassOrRealId as ClassType;
            var Plugin = Scope.GetPlugin();
            if (!Plugin.Begin()) return false;

            ExpressionNode Value;
            if (Class.HasFunctionTable)
            {
                Value = Plugin.NewNode(new LabelExpressionNode(Code, Class.FunctionTableLabel));
                if (Value == null) return false;
            }
            else
            {
                Value = Constants.GetNullValue(Scope, Code);
                if (Value == null) return false;
            }

            var Assignment = Expressions.SetValue(Self, "_objFunctionTable", Value, Plugin, Code, true);
            if (Assignment == null) return false;

            var Command = new Command(Scope, Code, CommandType.Expression);
            Command.Expressions = new List<ExpressionNode>() { Assignment };
            Scope.Children.Insert(Index, Command);
            Index++;

            return ProcessContainer(Command);
        }

        private bool SetTypePointer(CodeScopeNode Scope, ref int Index, SelfVariable Self, CodeString Code)
        {
            var Plugin = Scope.GetPlugin();
            if (!Plugin.Begin()) return false;

            var Value = Plugin.NewNode(new DataPointerNode(Code, Self.TypeOfSelf));
            if (Value == null) return false;

            var Assignment = Expressions.SetValue(Self, "_objTypePointer", Value, Plugin, Code, true);
            if (Assignment == null) return false;

            var Command = new Command(Scope, Code, CommandType.Expression);
            Command.Expressions = new List<ExpressionNode>() { Assignment };
            Scope.Children.Insert(Index, Command);
            Index++;

            return ProcessContainer(Command);
        }

        private bool AllocateObject(CodeScopeNode Scope, ref int Index, SelfVariable Self, ExpressionNode Size, PluginRoot Plugin, CodeString Code)
        {
            var TypeMngrPlugin = Plugin.GetPlugin<TypeMngrPlugin>();
            TypeMngrPlugin.Flags |= TypeMngrPluginFlags.NoWarningOnCastingToSameType;
            TypeMngrPlugin.Flags |= TypeMngrPluginFlags.EnableReadonlyWriting;

            var Func = Identifiers.GetByFullNameFast<Function>(Scope.State, "Internals.ObjectHelper.Allocate");
            if (Func == null) return false;

            var FuncNode = Plugin.NewNode(new IdExpressionNode(Func, Code));
            if (Func == null) return false;

            var FuncType = Func.Children[0] as TypeOfFunction;
            var SizeParam = FuncType.Children[1] as FunctionParameter;
            if (Size.Type == null || !Size.Type.IsEquivalent(SizeParam.TypeOfSelf))
            {
                Size = Expressions.Convert(Size, SizeParam.TypeOfSelf, Plugin, Code);
                if (Size == null) return false;
            }

            var CallCh = new ExpressionNode[] { FuncNode, Size };
            var Call = Plugin.NewNode(new OpExpressionNode(Operator.Call, CallCh, Code));
            if (Call == null) return false;

            var Node = Expressions.Reinterpret(Call, Self.TypeOfSelf, Plugin, Code);
            if (Node == null) return false;

            var FS = Scope.FunctionScope;
            var Assignment = Expressions.SetValue(FS.SelfVariable, Node, Plugin, Code, true);
            if (Assignment == null) return false;

            var Command = new Command(Scope, Code, CommandType.Expression);
            Command.Expressions = new List<ExpressionNode>() { Assignment };
            Scope.Children.Insert(Index, Command);
            Index++;

            return ProcessContainer(Command);
        }
        #endregion

        #region ExceptionHandling
        static bool CreateFinallyReturn(FunctionScope Scope)
        {
            var Data = Scope.Data.Get<NCFuncScopeData>();
            var State = Scope.State;
            var RetType = Scope.Type.RetType;

            var Name = new CodeString("_" + State.AutoLabel.ToString());
            Data.FinallyReturn = Scope.CreateAndDeclareVariable(Name, RetType);
            Data.FinallyReturnLabel = State.AutoLabel;
            return Data.FinallyReturn != null;
        }

        bool SetFinallyReturn(CodeScopeNode Scope, ExpressionNode Value, CodeString Code)
        {
            var FS = Scope.FunctionScope;
            var FSData = FS.Data.Get<NCFuncScopeData>();
            if (!CreateFinallyReturn(FS)) return false;

            var Plugin = Scope.GetPlugin();
            if (!Plugin.Begin()) return false;

            var Node = Expressions.SetValue(FSData.FinallyReturn, Value, Plugin, Code, true);
            if (Node == null) return false;

            var Comm = new Command(Scope, Code, CommandType.Expression);
            Comm.Expressions = new List<ExpressionNode>() { Node };
            Scope.Children.Add(Comm);
            return ProcessContainer(Comm);
        }

        bool FinallyRethrow(Command Command, CodeScopeNode Scope, CodeString Code)
        {
            var CatchVar = GetCatchVariable(Command);
            if (CatchVar == null) return false;

            var Plugin = Command.GetPlugin();
            if (!Plugin.Begin()) return false;

            var Cmp_Dst = Plugin.NewNode(new IdExpressionNode(CatchVar, Code));
            var Cmp_Src = Plugin.NewNode(Constants.GetNullValue(Command, Code));
            if (Cmp_Dst == null || Cmp_Src == null) return false;

            var Cmp_Ch = new ExpressionNode[] { Cmp_Dst, Cmp_Src };
            var Cmp_Node = Plugin.NewNode(new OpExpressionNode(Operator.Inequality, Cmp_Ch, Code));
            if (Cmp_Node == null || Plugin.End(ref Cmp_Node) == PluginResult.Failed)
                return false;

            var Cmp_If = new Command(Scope, Code, CommandType.If);
            Cmp_If.Expressions = new List<ExpressionNode>() { Cmp_Node };
            Scope.Children.Add(Cmp_If);

            var Then = new Command(Cmp_If, Code, CommandType.Rethrow);
            Cmp_If.Children.Add(Then);

            if (!ProcessContainer(Then)) return false;
            if (!ProcessContainer(Cmp_If)) return false;
            return true;
        }

        static Variable GetCatchVariable(Command Command)
        {
            var NCData = Command.Data.GetOrCreate<NCCommandData>();
            var Global = Command.GlobalContainer;
            var ExceptionClass = Identifiers.GetByFullNameFast<ClassType>(Global, "System.Exception");
            if (ExceptionClass == null) return null;

            if (NCData.CatchVariable == null)
            {
                var CatchVarName = new CodeString("_" + Command.State.AutoLabel);
                NCData.CatchVariable = Command.CreateAndDeclareVariable(CatchVarName, ExceptionClass);
                if (NCData.CatchVariable == null) return null;
            }

            return NCData.CatchVariable;
        }

        bool EnterTryBlock(CodeScopeNode Scope, int Index, CodeString Code, Identifier CatchVariable, int Label)
        {
            var FScope = Scope.FunctionScope;
            FScope.NeverSkippedLabels.Add(Label);

            var Global = Scope.State.GlobalContainer;
            var Plugin = Scope.GetPlugin();
            if (!Plugin.Begin()) return false;

            var CatchVarNode = Plugin.NewNode(new IdExpressionNode(CatchVariable, Code));
            if (CatchVarNode == null) return false;

            var Ch = new ExpressionNode[] { CatchVarNode };
            CatchVarNode = Plugin.NewNode(new OpExpressionNode(Operator.Address, Ch, Code));
            var JumpTo = Plugin.NewNode(new LabelExpressionNode(Code, "_" + Label.ToString()));
            var Func = Identifiers.GetByFullNameFast<Function>(Global, "Internals.EnterTryBlock");
            if (Func == null || CatchVarNode == null || JumpTo == null)
                return false;

            var Node = Expressions.Call(Code, Plugin, Func, CatchVarNode, JumpTo);
            if (Node == null || Plugin.End(ref Node) == PluginResult.Failed)
                return false;

            var Command = new Command(Plugin.Container, Code, CommandType.Expression);
            Command.Expressions = new List<ExpressionNode>() { Node };
            Scope.Children.Insert(0, Command);
            return ProcessContainer(Command);
        }

        bool LeaveTryBlock(CodeScopeNode Scope, CodeString Code)
        {
            var Global = Scope.State.GlobalContainer;
            var Plugin = Scope.GetPlugin();
            if (!Plugin.Begin()) return false;

            var Func = Identifiers.GetByFullNameFast<Function>(Global, "Internals.LeaveTryBlock");
            if (Func == null) return false;

            var Node = Expressions.Call(Code, Plugin, Func);
            if (Node == null || Plugin.End(ref Node) == PluginResult.Failed)
                return false;

            var Command = new Command(Scope, Code, CommandType.Expression);
            Command.Expressions = new List<ExpressionNode>() { Node };
            Scope.Children.Add(Command);
            return ProcessContainer(Command);
        }

        bool FinallyJump(CodeScopeNode Scope, Identifier JumpTo, CodeString Code)
        {
            var Plugin = Scope.GetPlugin();
            if (!Plugin.Begin()) return false;

            var Cmp_IdNode = Plugin.NewNode(new IdExpressionNode(JumpTo, Code));
            var Cmp_Null = Plugin.NewNode(Constants.GetNullValue(Scope, Code));
            if (Cmp_IdNode == null || Cmp_Null == null) return false;

            var Cmp_Ch = new ExpressionNode[] { Cmp_IdNode, Cmp_Null };
            var Cmp_Node = Plugin.NewNode(new OpExpressionNode(Operator.Inequality, Cmp_Ch, Code));
            if (Cmp_Node == null || Plugin.End(ref Cmp_Node) == PluginResult.Failed) return false;

            var Cmp_If = new Command(Scope, Code, CommandType.If);
            Cmp_If.Expressions = new List<ExpressionNode>() { Cmp_Node };
            Scope.Children.Add(Cmp_If);

            if (!Plugin.Begin()) return false;
            var Then_Node = Plugin.NewNode(new IdExpressionNode(JumpTo, Code));
            if (Then_Node == null || Plugin.End(ref Then_Node) == PluginResult.Failed)
                return false;

            var Then_Jump = CreateJump(Cmp_If, Then_Node, Code);
            if (Then_Jump == null) return false;

            Cmp_If.Children.Add(Then_Jump);
            if (!ProcessContainer(Then_Jump)) return false;
            if (!ProcessContainer(Cmp_If)) return false;
            return true;
        }

        static bool CreateFinallyJump(Command TryComm, CodeString Code)
        {
            var NCData = TryComm.Data.GetOrCreate<NCCommandData>();
            if (NCData.FinallyJump == null)
            {
                var FinallyJumpName = new CodeString("_" + TryComm.State.AutoLabel);
                var FinallyJumpType = TryComm.GlobalContainer.CommonIds.VoidPtr;
                NCData.FinallyJump = TryComm.CreateAndDeclareVariable(FinallyJumpName, FinallyJumpType);
                return NCData.FinallyJump != null;
            }

            return true;
        }

        bool InitializeFinallyJump(Command TryComm, CodeString Code)
        {
            var Scope = TryComm.Children[0];
            var NCData = TryComm.Data.GetOrCreate<NCCommandData>();
            var Node = Expressions.SetDefaultValue(Scope.GetPlugin(), Code, NCData.FinallyJump);
            if (Node == null) return false;

            var Command = new Command(Scope, Code, CommandType.Expression);
            Command.Expressions = new List<ExpressionNode>() { Node };
            Scope.Children.Insert(0, Command);
            return ProcessContainer(Command);
        }

        bool SetFinallyJump(Command TryComm, CodeScopeNode Scope, int Label, CodeString Code)
        {
            Scope.FunctionScope.NeverSkippedLabels.Add(Label);
            return SetFinallyJump(TryComm, Scope, "_" + Label, Code);
        }

        bool SetFinallyJump(Command TryComm, CodeScopeNode Scope, string Label, CodeString Code)
        {
            var NCData = TryComm.Data.GetOrCreate<NCCommandData>();
            if (!CreateFinallyJump(TryComm, Code)) return false;

            var Plugin = Scope.GetPlugin();
            if (!Plugin.Begin()) return false;

            var Dst = Plugin.NewNode(new IdExpressionNode(NCData.FinallyJump, Code));
            var Src = Plugin.NewNode(new LabelExpressionNode(Code, Label));
            if (Dst == null || Src == null) return false;

            var Ch = new ExpressionNode[] { Dst, Src };
            var Node = Plugin.NewNode(new OpExpressionNode(Operator.Assignment, Ch, Code));
            if (Node == null || Plugin.End(ref Node) == PluginResult.Failed)
                return false;

            var Command = new Command(Scope, Code, CommandType.Expression);
            Command.Expressions = new List<ExpressionNode>() { Node };
            Scope.Children.Add(Command);
            return ProcessContainer(Command);
        }

        bool CreateJump(CodeScopeNode Scope, int Label, CodeString Code)
        {
            Scope.FunctionScope.NeverSkippedLabels.Add(Label);
            return CreateJump(Scope, "_" + Label, Code);
        }

        bool CreateJump(CodeScopeNode Scope, string Label, CodeString Code)
        {
            var Comm = CreateJump((IdContainer)Scope, Label, Code);
            if (Comm == null) return false;

            Scope.Children.Add(Comm);
            return ProcessContainer(Comm);
        }

        static Command CreateJump(IdContainer Container, string Label, CodeString Code)
        {
            var Plugin = Container.GetPlugin();
            if (!Plugin.Begin()) return null;

            var Node = Plugin.NewNode(new LabelExpressionNode(Code, Label));
            if (Node == null || Plugin.End(ref Node) == PluginResult.Failed)
                return null;

            return CreateJump(Container, Node, Code);
        }

        static Command CreateJump(IdContainer Container, ExpressionNode To, CodeString Code)
        {
            var Then_Jump = new Command(Container, Code, CommandType.Unknown);
            Then_Jump.Extension = new NCCommandExtension(Then_Jump, NCCommandType.Jump);
            Then_Jump.Expressions = new List<ExpressionNode>() { To };
            return Then_Jump;
        }
        #endregion
    }
}