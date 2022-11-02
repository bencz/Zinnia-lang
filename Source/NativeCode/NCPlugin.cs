using System;
using System.Collections.Generic;
using System.Numerics;
using Zinnia.Base;

namespace Zinnia.NativeCode;

public class NCPlugin : ExpressionPlugin
{
    private readonly Identifier[] _ConcatFunctions = new Identifier[3];
    public IdContainer DeclContainer;
    public NCProcessor NCProcessor;
    public List<CodeScopeNode> RunBefore;
    public CodeScopeNode RunBeforeParent;

    public NCPlugin(PluginRoot Parent, NCProcessor NCProcessor)
        : base(Parent)
    {
        this.NCProcessor = NCProcessor;
    }

    public CodeScopeNode GetRunBeforeScope()
    {
        var Ret = RunBefore[0];
        RunBefore.RemoveAt(0);
        return Ret;
    }

    public override bool Begin()
    {
        if (!base.Begin()) return false;
        DeclContainer = Container;

        if (Container.Parent is CodeScopeNode)
        {
            var Parent = Container.Parent;
            Predicate<IdContainer> Func = x => x == Container || (x is CodeScopeNode && x.Children.Count == 0);

            if (Parent.GetChildIndex(Container) == Parent.Children.Count - 1 && Parent.Children.TrueForAll(Func))
            {
                RunBeforeParent = Container.Parent as CodeScopeNode;
                RunBefore = new List<CodeScopeNode>();
                for (var i = 0; i < RunBeforeParent.Children.Count - 1; i++)
                    RunBefore.Add(RunBeforeParent.Children[i] as CodeScopeNode);

                DeclContainer = RunBeforeParent;
            }
        }

        return true;
    }

    public override PluginResult End(ref ExpressionNode Node)
    {
        if (RunBefore != null && RunBefore.Count != 0)
            throw new ApplicationException();

        return PluginResult.Succeeded;
    }

    private ExpressionNode ObjectIs(ExpressionNode Node, Identifier Type)
    {
        var UnderOrReal = Type.UnderlyingStructureOrRealId;
        var FuncName = "Internals.ObjectHelper.ObjectIs";
        if (UnderOrReal != null && UnderOrReal.DeclaredIdType != DeclaredIdType.Unknown)
        {
            FuncName += "Fast";
            Type = UnderOrReal;
        }

        var Func = Identifiers.GetByFullNameFast<Function>(State, FuncName);
        if (Func == null) return null;

        var NewCh = new[]
        {
            Parent.NewNode(new IdExpressionNode(Func, Node.Code)),
            Node,
            Parent.NewNode(new DataPointerNode(Node.Code, Type))
        };

        if (NewCh[0] == null || NewCh[2] == null) return null;
        return Parent.NewNode(new OpExpressionNode(Operator.Call, NewCh, Node.Code));
    }

    private Command ThrowExceptionCommand(IdContainer If, CodeString Code, string ExceptionName)
    {
        var Then = new Command(If, Code, CommandType.Throw);
        If.Children.Add(Then);

        var NewPlugin = Then.GetPlugin();
        if (!NewPlugin.Begin()) return null;

        var ExceptionType = Identifiers.GetByFullNameFast<ClassType>(State, ExceptionName);
        if (ExceptionType == null) return null;

        var ExceptionTypeNode = NewPlugin.NewNode(new IdExpressionNode(ExceptionType, Code));
        if (ExceptionTypeNode == null) return null;

        var ExceptionCh = new[] { ExceptionTypeNode };
        var Exception = NewPlugin.NewNode(new OpExpressionNode(Operator.NewObject, ExceptionCh, Code));
        if (Exception == null) return null;

        Then.Expressions = new List<ExpressionNode> { Exception };
        if (!NCProcessor.ProcessContainer(Then)) return null;
        return Then;
    }

    private Identifier AssignToVariable(ExpressionNode Node, CodeScopeNode Scope, CodeString Code)
    {
        if (Node is IdExpressionNode)
        {
            var IdNode = Node as IdExpressionNode;
            if (IdNode.Identifier.RealId is LocalVariable)
                return IdNode.Identifier;
        }

        Node.ForEach(x =>
        {
            if (x.CheckingMode == CheckingMode.Checked)
                x.CheckingMode = CheckingMode.Default;
        });

        var Var = RunBeforeParent.CreateAndDeclareVariable(State.AutoVarName, Node.Type);
        if (Var == null) return null;

        var Plugin = Scope.GetPlugin();
        if (!Plugin.Begin()) return null;

        var VarNode = Plugin.NewNode(new IdExpressionNode(Var, Code));
        if (VarNode == null) return null;

        var Ch = new[] { VarNode, Node };
        Node = Plugin.NewNode(new OpExpressionNode(Operator.Assignment, Ch, Code));
        if (Node == null) return null;

        var Command = new Command(Scope, Code, CommandType.Expression);
        Command.Expressions = new List<ExpressionNode> { Node };
        Scope.Children.Add(Command);

        if (!NCProcessor.ProcessContainer(Command))
            return null;

        return Var;
    }

    public bool NewInitializationNode(CodeScopeNode Scope, PluginRoot Plugin,
        ExpressionNode Node, Identifier Var, CodeString Code)
    {
        if (Node is ObjectInitNode)
        {
            var InitNode = Node as ObjectInitNode;
            for (var i = 0; i < InitNode.Members.Length; i++)
            {
                if (!Plugin.Begin()) return false;

                var Value = InitNode.Children[i];
                var Member = InitNode.Members[i].Identifier;
                var Assignment = Expressions.SetValue(Var, Member, Value, Plugin, Code, true);
                if (Assignment == null) return false;

                var Command = new Command(Scope, Code, CommandType.Expression);
                Command.Expressions = new List<ExpressionNode> { Assignment };
                Scope.Children.Add(Command);

                if (!NCProcessor.ProcessContainer(Command)) return false;
            }
        }
        else if (Node is ArrayInitNode)
        {
            var InitNode = Node as ArrayInitNode;
            for (var i = 0; i < InitNode.Indices.Length; i++)
            {
                if (!Plugin.Begin()) return false;

                var Value = InitNode.Children[i];
                var Indices = InitNode.Indices[i];
                var Assignment = Expressions.SetValue(Var, Indices, Value, Plugin, Code, true);
                if (Assignment == null) return false;

                var Command = new Command(Scope, Code, CommandType.Expression);
                Command.Expressions = new List<ExpressionNode> { Assignment };
                Scope.Children.Add(Command);

                if (!NCProcessor.ProcessContainer(Command)) return false;
            }
        }
        else
        {
            throw new ApplicationException();
        }

        return true;
    }

    private void MoveUsedIdentifiers(ExpressionNode Node, IdContainer To)
    {
        var Comm = Container as Command;
        NCExpressions.GetUsedCommandIdentifiers(Comm, Node).Foreach(x => Identifiers.MoveIdentifier(x, To));
    }

    public PluginResult NewOpNode(ref ExpressionNode Node)
    {
        var OpNode = Node as OpExpressionNode;
        var Ch = OpNode.Children;
        var Op = OpNode.Operator;

        if (NCExpressions.IsOverflowableOp(Op) && Op != Operator.Cast)
            if (Node.CheckingMode == CheckingMode.Checked && NCExpressions.GetEquivalentNumberType(Node.Type) != null)
            {
                var Scope = GetRunBeforeScope();
                var Var = AssignToVariable(Node, Scope, Node.Code);
                if (Var == null) return PluginResult.Failed;

                var If = new Command(Scope, Node.Code, CommandType.If);
                Scope.Children.Add(If);

                var NCArch = NCProcessor.NCArch;
                var IsOverflow = NCArch.OverflowCondition(If.GetPlugin(), Node, Node.Code);
                if (IsOverflow == null) return PluginResult.Failed;

                If.Expressions = new List<ExpressionNode> { IsOverflow };
                if (ThrowExceptionCommand(If, Node.Code, "System.OverflowException") == null)
                    return PluginResult.Failed;

                if (!NCProcessor.ProcessContainer(If)) return PluginResult.Failed;
                if (!NCProcessor.ProcessContainer(Scope)) return PluginResult.Failed;

                Node = Parent.NewNode(new IdExpressionNode(Var, Node.Code));
                return Node == null ? PluginResult.Failed : PluginResult.Ready;
            }

        //--------------------------------------------------------------------------------------
        if (Operators.IsNewOp(Op))
        {
            if (!Node.LinkedNodes.TrueForAll(x => !(x.Node is InitializationNode)))
            {
                var Scope = GetRunBeforeScope();
                var LinkedNodes = new List<ExpressionNode>();
                for (var i = 0; i < Node.LinkedNodes.Count; i++)
                {
                    var LNode = Node.LinkedNodes[i].Node;
                    if (LNode is InitializationNode)
                    {
                        LNode.Children.Foreach(x => MoveUsedIdentifiers(x, Scope));

                        LinkedNodes.Add(LNode);
                        Node.LinkedNodes.RemoveAt(i);
                        i--;
                    }
                }

                var Var = AssignToVariable(Node, Scope, Node.Code);
                if (Var == null) return PluginResult.Failed;

                var Plugin = Scope.GetPlugin();
                for (var i = 0; i < LinkedNodes.Count; i++)
                {
                    var LNode = LinkedNodes[i];
                    if (!NewInitializationNode(Scope, Plugin, LNode, Var, Node.Code))
                        return PluginResult.Failed;
                }

                if (!NCProcessor.ProcessContainer(Scope)) return PluginResult.Failed;

                Node = Parent.NewNode(new IdExpressionNode(Var, Node.Code));
                return Node == null ? PluginResult.Failed : PluginResult.Ready;
            }

            if (Op == Operator.NewArray)
            {
                Node = Expressions.ConstructArray(Parent, Node);
                return Node == null ? PluginResult.Failed : PluginResult.Ready;
            }

            if (Op == Operator.NewObject)
            {
                if (Ch == null || Ch.Length == 0)
                {
                    if (Node.Type.UnderlyingClassOrRealId is ClassType)
                        throw new ApplicationException("Class instantiation must have constructor");

                    Node = Parent.NewNode(Constants.GetDefaultValue(Node.Type, Node.Code));
                    return Node != null ? PluginResult.Ready : PluginResult.Failed;
                }
            }
            else
            {
                throw new ApplicationException();
            }
        }

        //--------------------------------------------------------------------------------------
        else if (Op == Operator.Call)
        {
            if (Ch[0].Type.RealId is NonstaticFunctionType)
            {
                var Type = Ch[0].Type.RealId as NonstaticFunctionType;
                var FType = Type.Child.RealId as TypeOfFunction;

                var LinkedCh = new LinkedExprNode[Ch.Length];
                for (var i = 0; i < Ch.Length; i++)
                    LinkedCh[i] = new LinkedExprNode(Ch[i]);

                //--------------------------------------------------------------------------------------
                var CondMember0 = Parent.NewNode(new LinkingNode(LinkedCh[0], Node.Code));
                var CondMember1 = Parent.NewNode(new StrExpressionNode(new CodeString("Self")));
                if (CondMember0 == null || CondMember1 == null) return PluginResult.Failed;

                var CondMembers = new[] { CondMember0, CondMember1 };
                var CondCh0 = Parent.NewNode(new OpExpressionNode(Operator.Member, CondMembers, Node.Code));
                var CondCh1 = Parent.NewNode(Constants.GetNullValue(Container, Node.Code));
                if (CondCh0 == null || CondCh1 == null) return PluginResult.Failed;

                var CondCh = new[] { CondCh0, CondCh1 };
                var Cond = Parent.NewNode(new OpExpressionNode(Operator.Equality, CondCh, Node.Code));
                if (Cond == null) return PluginResult.Failed;

                //--------------------------------------------------------------------------------------
                var ThenMember0 = Parent.NewNode(new LinkingNode(LinkedCh[0], Node.Code));
                var ThenMember1 = Parent.NewNode(new StrExpressionNode(new CodeString("Pointer")));
                if (ThenMember0 == null || ThenMember1 == null) return PluginResult.Failed;

                var ThenMembers = new[] { ThenMember0, ThenMember1 };
                var ThenCastCh0 = Parent.NewNode(new OpExpressionNode(Operator.Member, ThenMembers, Node.Code));
                var ThenCastCh1 = Parent.NewNode(new IdExpressionNode(Type.Child, Node.Code));
                if (ThenCastCh0 == null || ThenCastCh1 == null) return PluginResult.Failed;

                var ThenCastMembers = new[] { ThenCastCh0, ThenCastCh1 };
                var ThenCast = Parent.NewNode(new OpExpressionNode(Operator.Reinterpret, ThenCastMembers, Node.Code));
                if (ThenCast == null) return PluginResult.Failed;

                var ThenCh = new ExpressionNode[Ch.Length];
                ThenCh[0] = ThenCast;

                for (var i = 1; i < Ch.Length; i++)
                {
                    ThenCh[i] = Parent.NewNode(new LinkingNode(LinkedCh[i], Node.Code));
                    if (ThenCh[i] == null) return PluginResult.Failed;
                }

                var Then = Parent.NewNode(new OpExpressionNode(Operator.Call, ThenCh, Node.Code));
                if (Then == null) return PluginResult.Failed;

                //--------------------------------------------------------------------------------------
                var ElseMember0 = Parent.NewNode(new LinkingNode(LinkedCh[0], Node.Code));
                var ElseMember1 = Parent.NewNode(new StrExpressionNode(new CodeString("Pointer")));
                if (ElseMember0 == null || ElseMember1 == null) return PluginResult.Failed;

                var Object = Container.GlobalContainer.CommonIds.Object;
                var ElseCastCh1Id = Identifiers.AddSelfParameter(FType, Object);
                if (ElseCastCh1Id == null) return PluginResult.Failed;

                var ElseMembers = new[] { ElseMember0, ElseMember1 };
                var ElseCastCh0 = Parent.NewNode(new OpExpressionNode(Operator.Member, ElseMembers, Node.Code));
                var ElseCastCh1 = Parent.NewNode(new IdExpressionNode(ElseCastCh1Id, Node.Code));
                if (ElseCastCh0 == null || ElseCastCh1 == null) return PluginResult.Failed;

                var ElseCastMembers = new[] { ElseCastCh0, ElseCastCh1 };
                var ElseCast = Parent.NewNode(new OpExpressionNode(Operator.Reinterpret, ElseCastMembers, Node.Code));
                if (ElseCast == null) return PluginResult.Failed;

                var ElseSelfCh0 = Parent.NewNode(new LinkingNode(LinkedCh[0], Node.Code));
                var ElseSelfCh1 = Parent.NewNode(new StrExpressionNode(new CodeString("Self")));
                if (ElseSelfCh0 == null || ElseSelfCh1 == null) return PluginResult.Failed;

                var ElseSelfCh = new[] { ElseSelfCh0, ElseSelfCh1 };
                var ElseSelf = Parent.NewNode(new OpExpressionNode(Operator.Member, ElseSelfCh, Node.Code));
                if (ElseSelf == null) return PluginResult.Failed;

                var ElseCh = new ExpressionNode[Ch.Length + 1];
                ElseCh[0] = ElseCast;
                ElseCh[1] = ElseSelf;

                for (var i = 1; i < Ch.Length; i++)
                {
                    ElseCh[i + 1] = Parent.NewNode(new LinkingNode(LinkedCh[i], Node.Code));
                    if (ElseCh[i + 1] == null) return PluginResult.Failed;
                }

                var Else = Parent.NewNode(new OpExpressionNode(Operator.Call, ElseCh, Node.Code));
                if (Else == null) return PluginResult.Failed;

                //--------------------------------------------------------------------------------------
                var NewCh = new[] { Cond, Then, Else };
                var NewNode = new OpExpressionNode(Operator.Condition, NewCh, Node.Code);
                NewNode.LinkedNodes.AddRange(LinkedCh);

                Node = Parent.NewNode(NewNode);
                return Node == null ? PluginResult.Failed : PluginResult.Ready;
            }
        }

        //--------------------------------------------------------------------------------------
        else if (Op == Operator.Address || Operators.IsReference(Op))
        {
            OpNode.Operator = Op = Operator.Address;

            if (!Expressions.IsLValue(Ch[0]))
            {
                /*
                                    if (Ch[0] is ConstExpressionNode)
                                    {
                                        var Global = Container.GlobalContainer;
                                        var ConstCh0 = Ch[0] as ConstExpressionNode;
                                        var Var = Global.CreateExprConst(ConstCh0);
                                        if (Var == null) return PluginResult.Failed;
                
                                        Ch[0] = Parent.NewNode(new IdExpressionNode(Var, Node.Code));
                                        if (Ch[0] == null) return PluginResult.Failed;
                
                                        if (Parent.NewNode(ref Node) == PluginResult.Failed)
                                            return PluginResult.Failed;
                
                                        return PluginResult.Ready;
                                    }
                                    else
                                    {*/
                var Var = DeclContainer.CreateAndDeclareVariable(State.AutoVarName, Ch[0].Type);
                if (Var == null) return PluginResult.Failed;

                var AssignmentDst = Parent.NewNode(new IdExpressionNode(Var, Node.Code));
                if (AssignmentDst == null) return PluginResult.Failed;

                var AssignmentCh = new[] { AssignmentDst, Ch[0] };
                var Assignment = Parent.NewNode(new OpExpressionNode(Operator.Assignment, AssignmentCh, Node.Code));
                if (Assignment == null) return PluginResult.Failed;

                Ch[0] = Parent.NewNode(new IdExpressionNode(Var, Node.Code));
                if (Ch[0] == null) return PluginResult.Failed;

                Node.LinkedNodes.Add(new LinkedExprNode(Assignment, LinkedNodeFlags.NotRemovable));
                if (Parent.NewNode(ref Node) == PluginResult.Failed)
                    return PluginResult.Failed;

                return PluginResult.Ready;
                //}
            }
        }

        //--------------------------------------------------------------------------------------
        else if (Op == Operator.Is)
        {
            var Ch1Id = Expressions.GetIdentifier(Ch[1]);
            if (Identifiers.IsSubtypeOrEquivalent(Ch[0].Type, Ch1Id))
            {
                Node = Parent.NewNode(Constants.GetBoolValue(Container, true, Node.Code));
                return Node == null ? PluginResult.Failed : PluginResult.Ready;
            }

            if (!Identifiers.IsSubtypeOf(Ch1Id, Ch[0].Type))
            {
                Node = Parent.NewNode(Constants.GetBoolValue(Container, false, Node.Code));
                return Node == null ? PluginResult.Failed : PluginResult.Ready;
            }

            Node = ObjectIs(Ch[0], Ch1Id);
            return Node == null ? PluginResult.Failed : PluginResult.Ready;
        }

        //--------------------------------------------------------------------------------------
        else if (Op == Operator.As)
        {
            var Ch1Id = Expressions.GetIdentifier(Ch[1]);
            if (Identifiers.IsSubtypeOrEquivalent(Ch[0].Type, Ch1Id))
            {
                Node = Ch[0];
                Node.Type = Ch[0].Type;
                return PluginResult.Ready;
            }

            if (!Identifiers.IsSubtypeOf(Ch1Id, Ch[0].Type))
            {
                Node = Parent.NewNode(new ConstExpressionNode(Ch1Id, new NullValue(), Node.Code));
                return Node == null ? PluginResult.Failed : PluginResult.Ready;
            }

            var LinkedNode = new LinkedExprNode(Ch[0]);
            var CheckLinking = Parent.NewNode(new LinkingNode(LinkedNode, Node.Code));
            var ThenLinking = Parent.NewNode(new LinkingNode(LinkedNode, Node.Code));
            var ThenLinkingType = Parent.NewNode(new IdExpressionNode(Ch1Id, Node.Code));
            if (CheckLinking == null || ThenLinking == null || ThenLinkingType == null)
                return PluginResult.Failed;

            var ThenCastCh = new[] { ThenLinking, ThenLinkingType };

            var NewCh = new[]
            {
                ObjectIs(CheckLinking, Ch1Id),
                Parent.NewNode(new OpExpressionNode(Operator.Reinterpret, ThenCastCh, Node.Code)),
                Parent.NewNode(new ConstExpressionNode(Ch1Id, new NullValue(), Node.Code))
            };

            if (!NewCh.TrueForAll(x => x != null))
                return PluginResult.Failed;

            Node = new OpExpressionNode(Operator.Condition, NewCh, Node.Code);
            Node.LinkedNodes.Add(LinkedNode);
            Node = Parent.NewNode(Node);
            return Node == null ? PluginResult.Failed : PluginResult.Ready;
        }

        //--------------------------------------------------------------------------------------
        else if (Op == Operator.Member)
        {
            var Ch0Type = Ch[0].Type.RealId as Type;
            if (Ch0Type.RealId is StructType)
            {
                var Ch1 = Expressions.GetIdentifier(Ch[1]) as MemberFunction;
                var Struct = Ch[0].Type.RealId as StructType;
                var Base = Identifiers.GetByFullNameFast<ClassType>(State, "System.ValueType", false);

                if (Ch1 != null && Base != null && Ch1.Container != Struct.StructuredScope)
                {
                    var NewCh1 = Parent.NewNode(new IdExpressionNode(Base, Node.Code));
                    if (NewCh1 == null) return PluginResult.Failed;

                    var NewCh = new[] { Ch[0], NewCh1 };
                    Ch[0] = Parent.NewNode(new OpExpressionNode(Operator.Cast, NewCh, Node.Code));
                    if (Ch[0] == null) return PluginResult.Failed;
                }
            }
        }

        //--------------------------------------------------------------------------------------
        else if (Op == Operator.Cast)
        {
            var To = Expressions.GetIdentifier(Ch[1]);
            var RFrom = Ch[0].Type.RealId as Type;
            var RTo = To.RealId as Type;

            if (Node.CheckingMode == CheckingMode.Checked)
            {
                var NFrom = NCExpressions.GetEquivalentNumberType(Ch[0].Type);
                var NTo = NCExpressions.GetEquivalentNumberType(To);
                if (NFrom != null && NTo != null)
                    if (NFrom.Size > NTo.Size || (NFrom is SignedType && NTo is UnsignedType))
                    {
                        var Scope = GetRunBeforeScope();
                        var Var = AssignToVariable(Ch[0], Scope, Node.Code);
                        if (Var == null) return PluginResult.Failed;

                        var If = new Command(Scope, Node.Code, CommandType.If);
                        Scope.Children.Add(If);

                        var NewPlugin = If.GetPlugin();
                        if (!NewPlugin.Begin()) return PluginResult.Failed;

                        var IdNode = Parent.NewNode(new IdExpressionNode(Var, Node.Code));
                        if (IdNode == null) return PluginResult.Failed;

                        var IsOverflow = NCExpressions.IsInRange(NewPlugin, IdNode, NTo, Node.Code);
                        if (IsOverflow == null) return PluginResult.Failed;

                        IsOverflow = NCExpressions.Negate(NewPlugin, IsOverflow, Node.Code, true);
                        if (IsOverflow == null) return PluginResult.Failed;

                        If.Expressions = new List<ExpressionNode> { IsOverflow };

                        if (ThrowExceptionCommand(If, Node.Code, "System.OverflowException") == null)
                            return PluginResult.Failed;

                        if (!NCProcessor.ProcessContainer(If)) return PluginResult.Failed;
                        if (!NCProcessor.ProcessContainer(Scope)) return PluginResult.Failed;

                        Ch[0] = Parent.NewNode(new IdExpressionNode(Var, Node.Code));
                        if (Ch[0] == null) return PluginResult.Failed;

                        Node.CheckingMode = CheckingMode.Default;
                        Node = Parent.NewNode(Node);
                        return Node == null ? PluginResult.Failed : PluginResult.Ready;
                    }
            }

            if (RTo is PointerType && RFrom is ArrayType)
            {
                if (RFrom is NonrefArrayType)
                {
                    var NewCh = new[] { Ch[0] };
                    Node = Parent.NewNode(new OpExpressionNode(Operator.Address, NewCh, Node.Code));
                    if (Node == null) return PluginResult.Failed;

                    Node = Expressions.Convert(Node, RTo, Parent, Node.Code);
                    return Node == null ? PluginResult.Failed : PluginResult.Ready;
                }

                if (RFrom is RefArrayType)
                {
                    var RefArray = RFrom as RefArrayType;
                    var NewCh0Type = Container.GlobalContainer.CommonIds.BytePtr;
                    var NewCh0TypeNode = Parent.NewNode(new IdExpressionNode(NewCh0Type, Node.Code));
                    if (NewCh0TypeNode == null) return PluginResult.Failed;

                    var NewCh0Ch = new[] { Ch[0], NewCh0TypeNode };
                    var NewCh0 = Parent.NewNode(new OpExpressionNode(Operator.Reinterpret, NewCh0Ch, Node.Code));
                    var NewCh1 = Parent.NewNode(Constants.GetIntValue(Container, RefArray.OffsetToData, Node.Code));
                    if (NewCh0 == null || NewCh1 == null) return PluginResult.Failed;

                    var NewCh = new[] { NewCh0, NewCh1 };
                    Node = Parent.NewNode(new OpExpressionNode(Operator.Add, NewCh, Node.Code));
                    if (Node == null) return PluginResult.Failed;

                    Node = Expressions.Convert(Node, RTo, Parent, Node.Code);
                    return Node == null ? PluginResult.Failed : PluginResult.Ready;
                }

                throw new ApplicationException();
            }

            if (RFrom is NonrefArrayType && RTo is RefArrayType)
            {
                var ArrFrom = RFrom as NonrefArrayType;
                Node = Expressions.ConstructArray(Parent, To, ArrFrom.Lengths, Node.Code, Ch[0]);
                return Node == null ? PluginResult.Failed : PluginResult.Ready;
            }

            if (RFrom is NonrefArrayType && RTo is PointerAndLength)
            {
                var ArrFrom = RFrom as NonrefArrayType;
                var PAndLTo = RTo as PointerAndLength;

                var PtrType = PAndLTo.StructuredScope.IdentifierList[0].TypeOfSelf;
                var PtrTypeNode = Parent.NewNode(new IdExpressionNode(PtrType, Node.Code));
                if (PtrTypeNode == null) return PluginResult.Failed;

                var FromAsPtrCh = new[] { Ch[0], PtrTypeNode };
                var FromAsPtr = Parent.NewNode(new OpExpressionNode(Operator.Cast, FromAsPtrCh, Node.Code));
                var Length = Parent.NewNode(Constants.GetUIntValue(Container, (uint)ArrFrom.Lengths[0], Node.Code));
                if (FromAsPtr == null || Length == null) return PluginResult.Failed;

                var TupleCh = new[] { FromAsPtr, Length };
                var Tuple = Parent.NewNode(new OpExpressionNode(Operator.Tuple, TupleCh, Node.Code));
                var TypeNode = Parent.NewNode(new IdExpressionNode(To, Node.Code));
                if (Tuple == null || TypeNode == null) return PluginResult.Failed;

                var NewCh = new[] { Tuple, TypeNode };
                Node = Parent.NewNode(new OpExpressionNode(Operator.Cast, NewCh, Node.Code));
                return Node == null ? PluginResult.Failed : PluginResult.Ready;
            }

            if (RFrom is RefArrayType && RTo is PointerAndLength)
            {
                var PAndLTo = RTo as PointerAndLength;
                var Linked = new LinkedExprNode(Ch[0]);

                var PointerCh0 = Parent.NewNode(new LinkingNode(Linked, Node.Code));
                var PointerTypeNode = Parent.NewNode(new IdExpressionNode(PAndLTo.PointerType, Node.Code));
                if (PointerCh0 == null || PointerTypeNode == null) return PluginResult.Failed;

                var PointerCh = new[] { PointerCh0, PointerTypeNode };
                var Pointer = Parent.NewNode(new OpExpressionNode(Operator.Cast, PointerCh, Node.Code));
                if (Pointer == null) return PluginResult.Failed;

                var LengthMember = Identifiers.GetMember(State, RFrom, "Length", Node.Code);
                if (LengthMember == null) return PluginResult.Failed;

                var LengthCh0 = Parent.NewNode(new LinkingNode(Linked, Node.Code));
                var LengthCh1 = Parent.NewNode(new IdExpressionNode(LengthMember, Node.Code));
                if (LengthCh0 == null || LengthCh1 == null) return PluginResult.Failed;

                var LengthCh = new[] { LengthCh0, LengthCh1 };
                var Length = Parent.NewNode(new OpExpressionNode(Operator.Member, LengthCh, Node.Code));
                if (Length == null) return PluginResult.Failed;

                var TupleCh = new[] { Pointer, Length };
                ExpressionNode Tuple = new OpExpressionNode(Operator.Tuple, TupleCh, Node.Code);
                Tuple.LinkedNodes.Add(Linked);

                var TypeNode = Parent.NewNode(new IdExpressionNode(To, Node.Code));
                if (TypeNode == null || Parent.NewNode(ref Tuple) == PluginResult.Failed)
                    return PluginResult.Failed;

                var NewCh = new[] { Tuple, TypeNode };
                Node = Parent.NewNode(new OpExpressionNode(Operator.Cast, NewCh, Node.Code));
                return Node == null ? PluginResult.Failed : PluginResult.Ready;
            }

            if (RFrom is TypeOfFunction && RTo is NonstaticFunctionType)
            {
                var VoidPtr = Container.GlobalContainer.CommonIds.VoidPtr;
                var CastTo = Parent.NewNode(new IdExpressionNode(VoidPtr, Node.Code));
                if (CastTo == null) return PluginResult.Failed;

                var CastCh = new[] { Ch[0], CastTo };
                var Cast = Parent.NewNode(new OpExpressionNode(Operator.Cast, CastCh, Node.Code));
                if (Cast == null) return PluginResult.Failed;

                var Self = Expressions.GetSelfNode(Ch[0]);
                if (Self == null)
                {
                    Self = Parent.NewNode(Constants.GetNullValue(Container, Node.Code));
                    if (Self == null) return PluginResult.Failed;
                }

                var TupleCh = new[] { Self, Cast };
                var Tuple = Parent.NewNode(new OpExpressionNode(Operator.Tuple, TupleCh, Node.Code));
                if (Tuple == null) return PluginResult.Failed;

                Ch[0] = Tuple;
                Node = Parent.NewNode(Node);
                return Node == null ? PluginResult.Failed : PluginResult.Ready;
            }

            if (Identifiers.IsBoxing(RFrom, RTo))
            {
                var Global = Container.GlobalContainer;
                var Class = Identifiers.GetBoxClass(RFrom);

                var NewCh = new ExpressionNode[4]
                {
                    Parent.NewNode(new IdExpressionNode(Class, Node.Code)),
                    Parent.NewNode(new OpExpressionNode(Operator.Address,
                        new[] { Ch[0] }, Node.Code)),

                    Parent.NewNode(new DataPointerNode(Node.Code, RFrom)),
                    Parent.NewNode(Constants.GetUIntPtrValue(Container, RFrom.Size, Node.Code))
                };

                if (NewCh[0] == null || NewCh[1] == null || NewCh[2] == null || NewCh[3] == null)
                    return PluginResult.Failed;

                Node = Parent.NewNode(new OpExpressionNode(Operator.NewObject, NewCh, Node.Code));
                var TypeNode = Parent.NewNode(new IdExpressionNode(To, Node.Code));
                if (Node == null || TypeNode == null) return PluginResult.Failed;

                var ReinterpretCh = new[] { Node, TypeNode };
                Node = Parent.NewNode(new OpExpressionNode(Operator.Reinterpret, ReinterpretCh, Node.Code));
                return Node == null ? PluginResult.Failed : PluginResult.Ready;
            }

            if (Identifiers.IsBoxing(RTo, RFrom))
            {
                var Func = Identifiers.GetByFullNameFast<Function>(State, "Internals.ObjectHelper.Unbox");
                var Var = DeclContainer.CreateAndDeclareVariable(State.AutoVarName, RTo);
                if (Func == null || Var == null) return PluginResult.Failed;

                var AddressIdNode = Parent.NewNode(new IdExpressionNode(Var, Node.Code));
                if (AddressIdNode == null) return PluginResult.Failed;

                var NewCh = new[]
                {
                    Parent.NewNode(new IdExpressionNode(Func, Node.Code)),
                    Ch[0],
                    Parent.NewNode(new OpExpressionNode(Operator.Address, new[] { AddressIdNode }, Node.Code)),
                    Parent.NewNode(new DataPointerNode(Node.Code, RTo.UnderlyingStructureOrRealId))
                };

                var Linked = Parent.NewNode(new OpExpressionNode(Operator.Call, NewCh, Node.Code));
                if (Linked == null) return PluginResult.Failed;

                Node = new IdExpressionNode(Var, Node.Code);
                Node.LinkedNodes.Add(new LinkedExprNode(Linked, LinkedNodeFlags.NotRemovable));
                Node = Parent.NewNode(Node);
                return Node == null ? PluginResult.Failed : PluginResult.Ready;
            }

            if ((RTo.TypeFlags & TypeFlags.ReferenceValue) != 0)
            {
                if ((RFrom.TypeFlags & TypeFlags.ReferenceValue) != 0)
                {
                    if (Identifiers.IsSubtypeOrEquivalent(RFrom, RTo))
                    {
                        Node = Ch[0];
                        Node.Type = To;
                        Node.Flags |= ExpressionFlags.FixedType;
                        return PluginResult.Ready;
                    }

                    var FuncName = "Internals.ObjectHelper.Cast";
                    if (RTo.UnderlyingStructureOrRealId.DeclaredIdType != DeclaredIdType.Unknown)
                        FuncName += "Fast";

                    var Func = Identifiers.GetByFullNameFast<Function>(State, FuncName);
                    if (Func == null) return PluginResult.Failed;

                    var FuncNode = Parent.NewNode(new IdExpressionNode(Func, Node.Code));
                    var TypePtr = Parent.NewNode(new DataPointerNode(Node.Code, RTo));
                    if (FuncNode == null || TypePtr == null) return PluginResult.Failed;

                    Ch = new[] { FuncNode, Ch[0], TypePtr };
                    Node = Parent.NewNode(new OpExpressionNode(Operator.Call, Ch, Node.Code));
                    if (Node == null) return PluginResult.Failed;

                    Node.Type = To;
                    Node.Flags |= ExpressionFlags.FixedType;
                    return PluginResult.Ready;
                }

                if (RTo is StringType && RFrom is CharType)
                {
                    var CharArrEnding = Parent.NewNode(Constants.GetCharValue(Container, '\0', Node.Code));
                    if (CharArrEnding == null) return PluginResult.Failed;

                    var CharArrCh = new[] { Ch[0], CharArrEnding };
                    var CharArr = Parent.NewNode(new OpExpressionNode(Operator.Array, CharArrCh, Node.Code));
                    if (CharArr == null) return PluginResult.Failed;

                    var StringClass = Container.GlobalContainer.CommonIds.String;
                    var StringNode = Parent.NewNode(new IdExpressionNode(StringClass, Node.Code));
                    if (StringNode == null) return PluginResult.Failed;

                    var NewCh = new[] { StringNode, CharArr };
                    Node = Parent.NewNode(new OpExpressionNode(Operator.NewObject, NewCh, Node.Code));
                    return Node == null ? PluginResult.Failed : PluginResult.Ready;
                }
            }

            else if (RTo is TupleType && !(RFrom is TupleType))
            {
                var TupleTo = RTo as TupleType;
                var ToMembers = TupleTo.StructuredScope.IdentifierList;
                var TupleCh = new ExpressionNode[ToMembers.Count];
                var Linked = new LinkedExprNode(Ch[0]);

                for (var i = 0; i < ToMembers.Count; i++)
                {
                    TupleCh[i] = Parent.NewNode(new LinkingNode(Linked, Node.Code));
                    if (TupleCh[i] == null) return PluginResult.Failed;

                    var ToType = ToMembers[i].Children[0];
                    if (!Linked.Node.Type.IsEquivalent(ToType))
                    {
                        var ToTypeNode = Parent.NewNode(new IdExpressionNode(ToType, Node.Code));
                        if (ToTypeNode == null) return PluginResult.Failed;

                        var CastCh = new[] { TupleCh[i], ToTypeNode };
                        TupleCh[i] = Parent.NewNode(new OpExpressionNode(Operator.Cast, CastCh, Node.Code));
                        if (TupleCh[i] == null) return PluginResult.Failed;
                    }
                }

                Node = new OpExpressionNode(Operator.Tuple, TupleCh, Node.Code);
                Node.LinkedNodes.Add(Linked);
                Node.Type = To;
                Node = Parent.NewNode(Node);
                return Node == null ? PluginResult.Failed : PluginResult.Ready;
            }
        }

        //--------------------------------------------------------------------------------------
        else if (Op == Operator.Add)
        {
            if (Node.Type.RealId is StringType)
            {
                var Ch0Ch = Ch[0].Children;
                var Ch1Ch = Ch[1].Children;

                var Ch0StringConcatNode = IsStringConcatNode(Ch[0]);
                var Ch1StringConcatNode = IsStringConcatNode(Ch[1]);

                if (Ch0StringConcatNode && Ch0Ch.Length <= 3 && Ch1StringConcatNode && Ch1Ch.Length <= 3)
                {
                    var NewLength = Ch0Ch.Length + Ch1Ch.Length - 1;
                    var NewCh = new ExpressionNode[NewLength];

                    for (var i = 1; i < Ch0Ch.Length; i++)
                        NewCh[i] = Ch0Ch[i];

                    for (var i = 1; i < Ch1Ch.Length; i++)
                        NewCh[Ch0Ch.Length + i - 1] = Ch1Ch[i];

                    return ConcatStrings(ref Node, NewCh);
                }

                if (Ch0StringConcatNode && Ch0Ch.Length <= 4)
                {
                    var NewCh = new ExpressionNode[Ch0Ch.Length + 1];
                    for (var i = 1; i < Ch0Ch.Length; i++)
                        NewCh[i] = Ch0Ch[i];

                    NewCh[Ch0Ch.Length] = Ch[1];
                    return ConcatStrings(ref Node, NewCh);
                }

                if (Ch1StringConcatNode && Ch1Ch.Length <= 4)
                {
                    var NewCh = new ExpressionNode[Ch1Ch.Length + 1];
                    NewCh[1] = Ch[0];

                    for (var i = 1; i < Ch1Ch.Length; i++)
                        NewCh[i + 1] = Ch1Ch[i];

                    return ConcatStrings(ref Node, NewCh);
                }
                else
                {
                    var NewCh = new[] { null, Ch[0], Ch[1] };
                    return ConcatStrings(ref Node, NewCh);
                }
            }
        }

        //--------------------------------------------------------------------------------------
        else if (Op == Operator.Index)
        {
            if (Ch[0].Type.RealId is ArrayType)
            {
                var ArrayType = Ch[0].Type.RealId as ArrayType;
                var Global = Container.GlobalContainer;
                var BytePtr = Global.CommonIds.BytePtr;
                var UIntPtr = Container.GlobalContainer.CommonIds.UIntPtr;

                var Offset = NCExpressions.FlattenIndicesWithoutTempVar(Parent, Node, Node.Code, UIntPtr);
                if (Offset == null) return PluginResult.Failed;

                if (ArrayType.TypeOfValues.Size != 1)
                {
                    var MulByValue = new IntegerValue(new BigInteger(ArrayType.TypeOfValues.Size));
                    var MulBy = Parent.NewNode(new ConstExpressionNode(UIntPtr, MulByValue, Node.Code));
                    if (MulBy == null) return PluginResult.Failed;

                    var MulCh = new[] { Offset, MulBy };
                    Offset = Parent.NewNode(new OpExpressionNode(Operator.Multiply, MulCh, Node.Code));
                    if (Offset == null) return PluginResult.Failed;
                }

                ExpressionNode Pointer;
                if (ArrayType is RefArrayType)
                {
                    var RefArray = ArrayType as RefArrayType;
                    var PointerCh1 = Parent.NewNode(new IdExpressionNode(BytePtr, Node.Code));
                    if (PointerCh1 == null) return PluginResult.Failed;

                    var PointerCh = new[] { Ch[0], PointerCh1 };
                    Pointer = Parent.NewNode(new OpExpressionNode(Operator.Reinterpret, PointerCh, Node.Code));
                    if (Pointer == null) return PluginResult.Failed;

                    var DataOffset = new IntegerValue(RefArray.OffsetToData);
                    var OffsetCh1 = Parent.NewNode(new ConstExpressionNode(UIntPtr, DataOffset, Node.Code));
                    if (OffsetCh1 == null) return PluginResult.Failed;

                    var OffsetCh = new[] { Offset, OffsetCh1 };
                    Offset = Parent.NewNode(new OpExpressionNode(Operator.Add, OffsetCh, Node.Code));
                    if (Offset == null) return PluginResult.Failed;
                }
                else if (ArrayType is NonrefArrayType)
                {
                    var PointerCh = new[] { Ch[0] };
                    Pointer = Parent.NewNode(new OpExpressionNode(Operator.Address, PointerCh, Node.Code));
                    if (Pointer == null) return PluginResult.Failed;

                    var PointerCh1 = Parent.NewNode(new IdExpressionNode(BytePtr, Node.Code));
                    if (PointerCh1 == null) return PluginResult.Failed;

                    PointerCh = new[] { Pointer, PointerCh1 };
                    Pointer = Parent.NewNode(new OpExpressionNode(Operator.Cast, PointerCh, Node.Code));
                    if (Pointer == null) return PluginResult.Failed;
                }
                else
                {
                    throw new NotImplementedException();
                }

                var RetCh = new[] { Pointer, Offset };
                var Ret = Parent.NewNode(new OpExpressionNode(Operator.Add, RetCh, Node.Code));
                if (Ret == null) return PluginResult.Failed;

                var RetType = new PointerType(Container, Node.Type);
                var RetTypeNode = Parent.NewNode(new IdExpressionNode(RetType, Node.Code));
                if (RetTypeNode == null) return PluginResult.Failed;

                RetCh = new[] { Ret, RetTypeNode };
                Ret = Parent.NewNode(new OpExpressionNode(Operator.Cast, RetCh, Node.Code));
                if (Ret == null) return PluginResult.Failed;

                var Zero = Parent.NewNode(new ConstExpressionNode(UIntPtr, new IntegerValue(0), Node.Code));
                if (Zero == null) return PluginResult.Failed;

                RetCh = new[] { Ret, Zero };
                Ret = Parent.NewNode(new OpExpressionNode(Operator.Index, RetCh, Node.Code));
                if (Ret == null) return PluginResult.Failed;

                Node = Ret;
                return PluginResult.Ready;
            }
        }

        return PluginResult.Succeeded;
    }

    private ExpressionNode GetConcatFunction(int ParamCount)
    {
        Identifier Concat;
        if (_ConcatFunctions[ParamCount - 2] != null)
        {
            Concat = _ConcatFunctions[ParamCount - 2];
        }
        else
        {
            var StringClass = Identifiers.GetByFullNameFast<ClassType>(State, "System.String");
            if (StringClass == null) return null;

            var Options = GetIdOptions.Default;
            Options.Func = x =>
            {
                var Func = x as Function;
                if (Func == null) return false;

                var FuncType = Func.Children[0] as TypeOfFunction;
                return FuncType.Children.Length - 1 == ParamCount;
            };

            Concat = Identifiers.GetMember(State, StringClass, new CodeString("Concat"), Options);
            if (Concat == null) return null;

            _ConcatFunctions[ParamCount - 2] = Concat;
        }

        return Parent.NewNode(new IdExpressionNode(Concat, new CodeString()));
    }

    private PluginResult ConcatStrings(ref ExpressionNode Node, ExpressionNode[] NewCh)
    {
        NewCh[0] = GetConcatFunction(NewCh.Length - 1);
        if (NewCh[0] == null) return PluginResult.Failed;

        var NewNode = new OpExpressionNode(Operator.Call, NewCh, Node.Code);
        NewNode.LinkedNodes.AddRange(Node.LinkedNodes);
        Node = Parent.NewNode(NewNode);
        return Node == null ? PluginResult.Failed : PluginResult.Ready;
    }

    private static bool IsStringConcatNode(ExpressionNode Node)
    {
        if (Expressions.GetOperator(Node) != Operator.Call)
            return false;

        var IdCh0 = Node.Children[0] as IdExpressionNode;
        if (IdCh0 == null) return false;

        return IdCh0.Identifier.AssemblyNameWithoutDecorations == "_System_String_Concat";
    }

    public override PluginResult NewNode(ref ExpressionNode Node)
    {
        if (Node is OpExpressionNode)
        {
            var Result = NewOpNode(ref Node);
            if (Result != PluginResult.Succeeded)
                return Result;
        }

        return PluginResult.Succeeded;
    }
}