using System;
using System.Collections.Generic;
using System.Linq;
using Zinnia.Recognizers;

namespace Zinnia
{
    [Flags]
    public enum TypeMngrPluginFlags : byte
    {
        None = 0,
        NoWarningOnCastingToSameType = 1,
        AllowConstructorCalls = 2,
        EnableUntypedNodes = 4,
        EnableReadonlyWriting = 8,
        CalculateLayouts = 16,
        EnableAll = NoWarningOnCastingToSameType | AllowConstructorCalls |
            EnableUntypedNodes | EnableReadonlyWriting,
    }

    public class TypeMngrPlugin : ExpressionPlugin
    {
        public Type RetType;
        public TypeMngrPluginFlags Flags;
        public CheckingMode CheckingMode;

        public TypeMngrPlugin(PluginRoot Parent, Type RetType, TypeMngrPluginFlags Flags = TypeMngrPluginFlags.None)
            : base(Parent)
        {
            this.RetType = RetType;
            this.Flags = Flags;

            var Scope = Container.GetParent<CodeScopeNode>(Container.FunctionScope);
            if (Scope != null) CheckingMode = Scope.CheckingMode;
        }

        public override PluginResult End(ref ExpressionNode Node)
        {
            if (Node != null && RetType != null && !Node.Type.IsEquivalent(RetType))
            {
                Node = Convert(Node, RetType, Node.Code);
                if (Node == null) return PluginResult.Failed;
            }

            if (!CheckPropertyIndices(Node)) return PluginResult.Failed;
            return PluginResult.Succeeded;
        }

        bool IsProcessed(Identifier Type)
        {
            var Tuple = Type.UnderlyingStructureOrRealId as TupleType;
            if (Tuple != null && !Tuple.TrueForAllMembers(x => IsProcessed(x.Children[0])))
                return false;

            if (Type.RealId is NonrefArrayType)
            {
                var Array = Type.RealId as NonrefArrayType;
                return !(Array.TypeOfValues is AutomaticType);
            }

            return !(Type.RealId is AutomaticType);
        }

        public bool ProcessType(ExpressionNode Node)
        {
            if ((Flags & TypeMngrPluginFlags.CalculateLayouts) != 0)
            {
                if (Node.Type != null && IsProcessed(Node.Type))
                {
                    if (!Node.Type.CalculateLayout())
                        return false;
                }
            }

            return true;
        }

        public TypeConversion CanConvert(ExpressionNode Node, Identifier To)
        {
            var RType = Node.Type.RealId as Type;
            var Ret = RType.CanConvert(To);

            if (Ret == TypeConversion.Convertable)
            {
                var TupleTo = To.RealId as TupleType;
                if ((Node.Flags & ExpressionFlags.AutoConvert) != 0)
                {
                    Ret = TypeConversion.Automatic;
                }
                else if (TupleTo != null && Expressions.GetOperator(Node) == Operator.Tuple)
                {
                    var Found = false;
                    var ToMembers = TupleTo.StructuredScope.IdentifierList;

                    for (var i = 0; i < Node.Children.Length; i++)
                    {
                        var Ch = Node.Children[i];
                        if ((Ch.Flags & ExpressionFlags.AutoConvert) != 0)
                            continue;

                        var RChType = Ch.Type.RealId as Type;
                        var ToVar = ToMembers[i] as MemberVariable;
                        var ToType = ToVar.Children[0];
                        if (RChType.CanConvert(ToType) != TypeConversion.Automatic)
                            Found = true;
                    }

                    if (!Found)
                        Ret = TypeConversion.Automatic;
                }
            }

            return Ret;
        }

        public ExpressionNode Convert(ExpressionNode Node, Identifier To, CodeString Code)
        {
            var OpFuncRes = ProcessCastOpFunc(ref Node, To, Code, true);
            if (OpFuncRes != SimpleRecResult.Unknown)
            {
                if (OpFuncRes == SimpleRecResult.Succeeded) return Node;
                else if (OpFuncRes == SimpleRecResult.Failed) return null;
                else throw new ApplicationException();
            }

            var ConvRes = CanConvert(Node, To);
            if (ConvRes == TypeConversion.Automatic)
            {
                Node = Expressions.Convert(Node, To, Parent, Code);
                if (Node == null) return null;
            }
            else
            {
                var Message = MessageId.ImplicitlyCast;
                if (ConvRes == TypeConversion.Nonconvertable)
                    Message = MessageId.CannotConvert;

                var Types = new string[]
                {
                    Node.Type.Name.ToString(),
                    To.Name.ToString()
                };

                State.Messages.Add(Message, Code, Types);
                return null;
            }

            return Node;
        }

        private ExpressionNode CheckCanBeIndexerType(ExpressionNode Node, CodeString Code)
        {
            var SType = Node.Type.RealId as NonFloatType;
            if (SType == null)
            {
                var CastTo = Container.GlobalContainer.CommonIds.GetIdentifier<SignedType>(4);
                return Convert(Node, CastTo, Code);
            }

            return Node;
        }

        TupleType CreateTupleTypeForNodes(ExpressionNode Node)
        {
            var Members = new List<Identifier>();
            for (var i = 0; i < Node.Children.Length; i++)
            {
                var Type = Node.Children[i].Type;
                var MemVar = new MemberVariable(Container, new CodeString(), Type);
                MemVar.Access = Type.Access;
                Members.Add(MemVar);
            }

            return new TupleType(Container, Members);
        }

        bool CheckTupleType(ExpressionNode Node)
        {
            var TupleType = Node.Type.RealId as TupleType;
            var Members = TupleType.StructuredScope.IdentifierList;
            if (Members.Count != Node.Children.Length) return false;

            for (var i = 0; i < Node.Children.Length; i++)
                if (Node.Children[i].Type != Members[i].Children[0]) return false;

            return true;
        }

        PluginResult CheckFunctionCall(ref ExpressionNode Node)
        {
            var OpNode = Node as OpExpressionNode;
            var Ch = OpNode.Children;
            var Op = OpNode.Operator;

            TypeOfFunction FuncType;
            if (Ch[0].Type.RealId is NonstaticFunctionType)
            {
                var NFuncType = Ch[0].Type.RealId as NonstaticFunctionType;
                FuncType = NFuncType.Child.RealId as TypeOfFunction;
            }
            else if (Ch[0].Type.RealId is TypeOfFunction)
            {
                FuncType = Ch[0].Type.RealId as TypeOfFunction;
            }
            else
            {
                State.Messages.Add(MessageId.CallingNotFunc, Node.Code);
                return PluginResult.Failed;
            }

            if (FuncType.Children.Length != Ch.Length)
            {
                State.Messages.Add(MessageId.ParamCount, Node.Code);
                return PluginResult.Failed;
            }

            var OpCh0 = Ch[0] as OpExpressionNode;
            if (OpCh0 != null && OpCh0.Operator == Operator.Member)
            {
                var BaseId = Expressions.GetIdentifier(Ch[0].Children[0]);
                var FuncId = Expressions.GetIdentifier(Ch[0].Children[1]);
                if (BaseId is BaseVariable && FuncId != null && (FuncId.Flags & IdentifierFlags.Abstract) != 0)
                {
                    State.Messages.Add(MessageId.StaticCallAbstract, OpCh0.Code);
                    return PluginResult.Failed;
                }
            }

            var ConvertDone = false;
            for (var i = 1; i < FuncType.Children.Length; i++)
            {
                var DstType = FuncType.Children[i].TypeOfSelf;
                if (Ch[i].Type.IsEquivalent(DstType)) continue;

                Ch[i] = Convert(Ch[i], DstType, Node.Code);
                if (Ch[i] == null) return PluginResult.Failed;

                ConvertDone = true;
            }

            if (ConvertDone)
            {
                Node = Parent.NewNode(Node);
                return Node == null ? PluginResult.Failed : PluginResult.Ready;
            }

            if (Op == Operator.Call && (Node.Flags & ExpressionFlags.FixedType) == 0)
                Node.Type = FuncType.RetType;

            return PluginResult.Succeeded;
        }

        public SimpleRecResult ProcessCastOpFunc(ref ExpressionNode Node, Identifier To, CodeString Code, bool OnlyImplicit)
        {
            var Global = Container.GlobalContainer;
            if ((Global.Flags & GlobalContainerFlags.StructureMembersParsed) == 0)
                return SimpleRecResult.Unknown;

            var ExplicitFound = false;
            Predicate<Identifier> Func = x =>
            {
                var xFunction = x as Function;
                if (xFunction == null) return false;

                var NameOk = !OnlyImplicit && x.Name.IsEqual("%Operator_Explicit");
                if (NameOk) ExplicitFound = true;

                if (NameOk || x.Name.IsEqual("%Operator_Implicit"))
                {
                    var xFuncType = xFunction.Children[0].RealId as TypeOfFunction;
                    var xRetType = xFuncType.Children[0].RealId as Type;
                    return xRetType.CanConvert(To) != TypeConversion.Nonconvertable;
                }

                return false;
            };

            var List = new List<IdentifierFound>();
            Identifiers.SearchMember(Container, To, null, List, Func);
            Identifiers.SearchMember(Container, Node.Type, null, List, Func);

            if (List.Count > 0)
            {
                if (ExplicitFound)
                {
                    for (var i = 0; i < List.Count; i++)
                        if (List[i].Identifier.Name.IsEqual("%Operator_Implicit"))
                        {
                            List.RemoveAt(i);
                            i--;
                        }
                }

                if (List.Count != 1)
                {
                    State.Messages.Add(MessageId.AmbiguousReference, Code);
                    return SimpleRecResult.Failed;
                }

                var CallCh = new ExpressionNode[2];
                CallCh[0] = Parent.NewNode(new IdExpressionNode(List[0].Identifier, Code));
                CallCh[1] = Node;

                if (CallCh[0] == null)
                    return SimpleRecResult.Failed;

                Node = Parent.NewNode(new OpExpressionNode(Operator.Call, CallCh, Code));
                return Node == null ? SimpleRecResult.Failed : SimpleRecResult.Succeeded;
            }

            return SimpleRecResult.Unknown;
        }

        public PluginResult ProcessOperatorFunctions(ref ExpressionNode Node)
        {
            var Global = Container.GlobalContainer;
            if ((Global.Flags & GlobalContainerFlags.StructureMembersParsed) == 0)
                return PluginResult.Succeeded;

            if ((Node.Flags & ExpressionFlags.DisableOpFunc) != 0)
                return PluginResult.Succeeded;

            var OpNode = Node as OpExpressionNode;
            var Op = OpNode.Operator;
            var Ch = OpNode.Children;

            if (Operators.CanBeOpFunction(Op) && !Ch.TrueForAll(x => x.Type.RealId is NumberType))
            {
                if (Op == Operator.Equality || Op == Operator.Inequality)
                {
                    var CIndex = Ch[0] is ConstExpressionNode ? 0 : 1;
                    var CNode = Ch[CIndex] as ConstExpressionNode;
                    var Type = Ch[1 - CIndex].Type.RealId;

                    if (CNode != null && CNode.Value is NullValue && Type is StringType)
                        return PluginResult.Succeeded;
                }

                string Name = null;
                Predicate<Identifier> Func = x =>
                    {
                        if (x.Name.Length > 0 && x.Name[0] == '%')
                        {
                            if (Name == null)
                                Name = "%Operator_" + Op.ToString();

                            return x.Name.IsEqual(Name);
                        }

                        return false;
                    };

                var List = new List<IdentifierFound>();
                Identifiers.SearchMember(Container, Ch[0].Type, null, List, Func);
                if (Ch.Length == 2 && !Ch[1].Type.IsEquivalent(Ch[0].Type))
                    Identifiers.SearchMember(Container, Ch[1].Type, null, List, Func);

                if (List.Count > 0)
                {
                    var Options = GetIdOptions.Default;
                    Options.OverloadData = Expressions.GetOperatorSelectData(Ch);

                    var Id = Identifiers.SelectIdentifier(State, List, Node.Code, Options);
                    if (Id == null) return PluginResult.Failed;

                    var CallCh = new ExpressionNode[Ch.Length + 1];
                    Ch.CopyTo(CallCh, 1);

                    CallCh[0] = Parent.NewNode(new IdExpressionNode(Id, Node.Code));
                    if (CallCh[0] == null) return PluginResult.Failed;

                    Node = Parent.NewNode(new OpExpressionNode(Operator.Call, CallCh, Node.Code));
                    return Node == null ? PluginResult.Failed : PluginResult.Ready;
                }
            }

            if (Op == Operator.Cast)
            {
                var Type = Expressions.GetIdentifier(Ch[1]);
                var CNode = Node;
                Node = Ch[0];

                var Res = ProcessCastOpFunc(ref Node, Type, CNode.Code, false);
                if (Res != SimpleRecResult.Unknown)
                {
                    if (Res == SimpleRecResult.Succeeded) return PluginResult.Ready;
                    else if (Res == SimpleRecResult.Failed) return PluginResult.Failed;
                    else throw new ApplicationException();
                }

                Node = CNode;
            }

            return PluginResult.Succeeded;
        }

        bool CalculateIdentifiers(Identifier Type, ObjectInitNode Node)
        {
            var RetValue = true;
            var Members = Node.Members;
            var Children = Node.Children;

            for (var i = 0; i < Members.Length; i++)
            {
                if (Members[i].Identifier == null)
                {
                    var Id = Identifiers.GetMember(State, Type, Members[i].Code);
                    if (Id == null) { RetValue = false; continue; }

                    Members[i].Identifier = Id;
                }

                var MemberType = Members[i].Identifier.Children[0];
                if (!MemberType.IsEquivalent(Children[i].Type))
                {
                    Children[i] = Convert(Children[i], MemberType, Node.Code);
                    if (Children[i] == null) RetValue = false;
                }
            }

            return RetValue;
        }

        bool CheckArrayInitNode(Identifier Type, ArrayInitNode Node)
        {
            var RetValue = true;
            var Indices = Node.Indices;
            var Children = Node.Children;
            var ArrayType = Type.RealId as ArrayType;
            var BaseType = ArrayType.Children[0];

            for (var i = 0; i < Indices.Length; i++)
            {
                var ChNode = Children[i] as ExpressionNode;
                if (!ChNode.Type.IsEquivalent(BaseType))
                {
                    var OldChNode = ChNode;
                    ChNode = Convert(ChNode, BaseType, Node.Code);
                    if (ChNode == null) { RetValue = false; continue; }

                    Children[i] = ChNode;
                    Node.ReplaceChildren(OldChNode, ChNode, false);
                }
            }

            return RetValue;
        }

        bool NeedConvertToNonstaticFunction(ExpressionNode Node)
        {
            return Expressions.IsSelfSpecified(Node) && (Node.Flags & ExpressionFlags.FixedType) == 0;
        }

        ExpressionNode ConvertToNonstaticFunction(ExpressionNode Node, CodeString Code)
        {
            var NewType = new NonstaticFunctionType(Container, Node.Type);
            var TypeNode = Parent.NewNode(new IdExpressionNode(NewType, Code));
            if (TypeNode == null) return null;

            var CastCh = new ExpressionNode[] { Node, TypeNode };
            return Parent.NewNode(new OpExpressionNode(Operator.Cast, CastCh, Code));
        }

        bool ConvertChildrenToNonstaticFunction(ExpressionNode Node)
        {
            for (var i = 0; i < Node.LinkedNodes.Count; i++)
            {
                var LNode = Node.LinkedNodes[i];
                if (NeedConvertToNonstaticFunction(LNode.Node))
                {
                    LNode.Node = ConvertToNonstaticFunction(LNode.Node, Node.Code);
                    if (LNode.Node == null) return false;
                }
            }

            if (Node.Children != null)
            {
                var Op = Expressions.GetOperator(Node);
                var Ch = Node.Children;

                var Start = 0;
                if (Op == Operator.Call || Op == Operator.NewObject)
                    Start = 1;

                if (Op == Operator.Cast)
                {
                    var Global = Container.GlobalContainer;
                    var ChiId = Expressions.GetIdentifier(Ch[1]);

                    if (ChiId.IsEquivalent(Global.CommonIds.VoidPtr) ||
                        ChiId.RealId is NonstaticFunctionType)
                    {
                        Start = int.MaxValue;
                    }
                }

                for (var i = Start; i < Ch.Length; i++)
                    if (NeedConvertToNonstaticFunction(Ch[i]))
                    {
                        Ch[i] = ConvertToNonstaticFunction(Ch[i], Node.Code);
                        if (Ch[i] == null) return false;
                    }
            }

            return true;
        }

        public PluginResult NewOpNode(ref ExpressionNode Node)
        {
            var OpNode = Node as OpExpressionNode;
            var Op = OpNode.Operator;
            var Ch = OpNode.Children;

            if (Ch != null)
            {
                for (var i = 0; i < Ch.Length; i++)
                    if (Ch[i].Type == null)
                    {
                        if ((Flags & TypeMngrPluginFlags.EnableUntypedNodes) == 0)
                            throw new ApplicationException();

                        return PluginResult.Succeeded;
                    }
            }

            var OpRes = ProcessOperatorFunctions(ref Node);
            if (OpRes != PluginResult.Succeeded) return OpRes;

            //-------------------------------------------------------------------------------------
            if (Op == Operator.StackAlloc)
            {
                var Global = Container.GlobalContainer;
                if (!(Ch[0].Type.RealId is NonFloatType))
                {
                    Ch[0] = Convert(Ch[0], Global.CommonIds.Int32, Node.Code);
                    if (Ch[0] == null) return PluginResult.Failed;
                }

                if ((Node.Flags & ExpressionFlags.FixedType) == 0)
                    Node.Type = Global.CommonIds.VoidPtr;
            }

            //-------------------------------------------------------------------------------------
            else if (Op == Operator.Is)
            {
                var Ch1Id = Expressions.GetIdentifier(Ch[1]);
                if (Ch1Id == null || !(Ch1Id.RealId is Type))
                {
                    State.Messages.Add(MessageId.MustBeType, Ch[1].Code);
                    return PluginResult.Failed;
                }

                if (Identifiers.IsSubtypeOrEquivalent(Ch[0].Type, Ch1Id))
                    State.Messages.Add(MessageId.ConstExpression, Node.Code, "true");
                else if (!Identifiers.IsSubtypeOf(Ch1Id, Ch[0].Type))
                    State.Messages.Add(MessageId.ConstExpression, Node.Code, "false");

                if ((Node.Flags & ExpressionFlags.FixedType) == 0)
                    Node.Type = Container.GlobalContainer.CommonIds.Boolean;
            }

            //-------------------------------------------------------------------------------------
            else if (Op == Operator.As)
            {
                var Ch0Type = Ch[0].Type.RealId as Type;
                var Ch1Id = Expressions.GetIdentifier(Ch[1]);
                if (Ch1Id == null || !Identifiers.IsNullableType(Ch1Id))
                {
                    State.Messages.Add(MessageId.MustBeClass, Ch[1].Code);
                    return PluginResult.Failed;
                }

                var Ch1Type = Ch1Id.RealId as Type;
                if (!Identifiers.IsSubtypeOrEquivalent(Ch1Type, Ch0Type))
                    State.Messages.Add(MessageId.ConstExpression, Node.Code, "null");

                if ((Node.Flags & ExpressionFlags.FixedType) == 0)
                    Node.Type = Ch1Id;
            }

            //-------------------------------------------------------------------------------------
            else if (Operators.IsRefEquality(Op))
            {
                if (!CanOpApplied(OpNode)) return PluginResult.Failed;

                var Res = CastToAppropriateTypes(ref Node);
                if (Res != PluginResult.Succeeded) return Res;

                if (Op == Operator.RefEquality) Op = Operator.Equality;
                else if (Op == Operator.RefInequality) Op = Operator.Inequality;
                else throw new ApplicationException();

                OpNode.Operator = Op;
                Node.Flags |= ExpressionFlags.DisableOpFunc;
                Node = Parent.NewNode(Node);
                return Node == null ? PluginResult.Failed : PluginResult.Ready;
            }

            //-------------------------------------------------------------------------------------
            else if (Operators.IsIncDec(Op))
            {
                if (!CanOpApplied(OpNode)) return PluginResult.Failed;

                var Code = Node.Code;
                if (!Expressions.ProcessTuple(Ch[0], (x, Index) => CheckAssignVar(x, Code)))
                    return PluginResult.Failed;

                if ((Node.Flags & ExpressionFlags.FixedType) == 0)
                    Node.Type = Ch[0].Type;
            }

            //--------------------------------------------------------------------------------------
            else if (Operators.IsCast(Op))
            {
                var Ch1Id = Expressions.GetIdentifier(Ch[1]);
                if (Ch1Id == null || !(Ch1Id.RealId is Type))
                {
                    State.Messages.Add(MessageId.MustBeType, Ch[1].Code);
                    return PluginResult.Failed;
                }

                var RFrom = Ch[0].Type.RealId as Type;
                var RTo = Ch1Id.RealId as Type;

                if ((RTo.TypeFlags & TypeFlags.CanBeVariable) == 0)
                {
                    State.Messages.Add(MessageId.InvalidCast, Node.Code);
                    return PluginResult.Failed;
                }

                if (RFrom.IsEquivalent(RTo) && (Flags & TypeMngrPluginFlags.NoWarningOnCastingToSameType) == 0)
                {
                    if (Ch[0].Type == Ch1Id)
                        State.Messages.Add(MessageId.CastToSameType, Node.Code);

                    Node = OpNode.Children[0];
                    return PluginResult.Ready;
                }

                if (Op == Operator.Reinterpret)
                {
                    if (RTo.Size != RFrom.Size)
                    {
                        State.Messages.Add(MessageId.ReinterpretSize, Node.Code);
                        return PluginResult.Failed;
                    }
                }
                else if (Op == Operator.Cast)
                {
                    if (RFrom is PointerAndLength && RTo is PointerType)
                    {
                        Ch[0] = ExtractMember(Ch[0], 0, Node.Code);
                        if (Ch[0] == null) return PluginResult.Failed;

                        var TupleFrom = RFrom as TupleType;
                        var Members = TupleFrom.StructuredScope.IdentifierList;
                        if (Members[0].TypeOfSelf.IsEquivalent(RTo))
                        {
                            Node = Ch[0];
                            return PluginResult.Ready;
                        }

                        return Parent.NewNode(ref Node);
                    }

                    if (RFrom.CanConvert(RTo) == TypeConversion.Nonconvertable)
                    {
                        var Params = new[] { Ch[0].Type.Name.ToString(), Ch1Id.Name.ToString() };
                        State.Messages.Add(MessageId.CannotConvert, Node.Code, Params);
                        return PluginResult.Failed;
                    }

                    var Ch0 = Ch[0];
                    var NodeCopy = Node;
                    var Res = Expressions.ProcessTuple(Parent, ref Ch0, (x, Index) =>
                    {
                        var xFrom = x.Type;
                        Identifier xTo;
                        if (Index == -1)
                        {
                            xTo = Ch1Id;
                        }
                        else
                        {
                            var TupleTo = RTo as TupleType;
                            var TupleMembers = TupleTo.StructuredScope.IdentifierList;
                            xTo = TupleMembers[Index].Children[0];
                        }

                        if (xFrom.RealId is ReferenceType && xTo.RealId is ReferenceType)
                        {
                            var RefxFrom = xFrom.RealId as ReferenceType;
                            var RefxTo = xTo.RealId as ReferenceType;
                            if (RefxFrom.Child is AutomaticType && RefxFrom.Mode == RefxTo.Mode)
                            {
                                SetAutoNodeType(x.Children[0], RefxTo.Children[0]);
                                x.Type = xTo;
                            }
                        }

                        Identifier BasexTo = null;
                        if (xTo.RealId is PointerAndLength)
                        {
                            var PAndLxTo = xTo.RealId as PointerAndLength;
                            BasexTo = PAndLxTo.Child;
                        }
                        else if (xTo.RealId is ArrayType)
                        {
                            var ArrxTo = xTo.RealId as ArrayType;
                            BasexTo = ArrxTo.Children[0];
                        }
                        else if (xTo.RealId is PointerType)
                        {
                            var ArrxTo = xTo.RealId as PointerType;
                            BasexTo = ArrxTo.Children[0];
                        }

                        var ArrxFrom = xFrom.RealId as ArrayType;
                        if (BasexTo != null && ArrxFrom != null && ArrxFrom.TypeOfValues is AutomaticType &&
                            ((BasexTo.RealId as Type).TypeFlags & TypeFlags.CanBeArrayType) != 0)
                        {
                            var Old = x.Type.RealId;
                            if (Old is NonrefArrayType)
                            {
                                var NonrefOld = Old as NonrefArrayType;
                                x.Type = new NonrefArrayType(Container, BasexTo, NonrefOld.Lengths);
                            }
                            else if (Old is RefArrayType)
                            {
                                var RefOld = Old as RefArrayType;
                                x.Type = new RefArrayType(Container, BasexTo, RefOld.Dimensions);
                            }
                            else
                            {
                                throw new ApplicationException();
                            }

                            return Parent.NewNode(x);
                        }

                        if (xFrom.RealId is AutomaticType && !(xTo.RealId is AutomaticType))
                        {
                            if (Expressions.GetOperator(x) == Operator.NewObject)
                            {
                                var TypeNode = Parent.NewNode(new IdExpressionNode(xTo, x.Code));
                                if (TypeNode == null) return null;

                                x.Type = xTo;
                                x.Children[0] = TypeNode;
                                return Parent.NewNode(x);
                            }

                            SetAutoNodeType(x, xTo);
                        }
                        else if (xFrom.RealId is TupleType && xTo.RealId is TupleType)
                        {
                            var TupleFrom = xFrom.RealId as TupleType;
                            var TupleTo = xTo.RealId as TupleType;

                            var FromMembers = TupleFrom.StructuredScope.IdentifierList;
                            var ToMembers = TupleTo.StructuredScope.IdentifierList;

                            var Perform = false;
                            for (var i = 0; i < FromMembers.Count; i++)
                            {
                                var FromType = FromMembers[i].TypeOfSelf;
                                var ToType = ToMembers[i].TypeOfSelf;

                                if (FromType is AutomaticType && !(ToType is AutomaticType))
                                    Perform = true;
                            }

                            if (Perform)
                            {
                                var NewMembers = new List<Identifier>();
                                for (var i = 0; i < FromMembers.Count; i++)
                                {
                                    var FromType = FromMembers[i].TypeOfSelf;
                                    var ToType = ToMembers[i].TypeOfSelf;

                                    var NewType = FromType.RealId is AutomaticType ? ToType : FromType;
                                    NewMembers.Add(Identifiers.CreateTupleMember(Container, NewType));
                                }

                                x.Type = new TupleType(Container, NewMembers);
                            }
                        }

                        return x;
                    });

                    if (Res == PluginResult.Failed) return Res;
                    Ch[0] = Ch0;

                    if (Ch0.Type.IsEquivalent(Ch1Id))
                    {
                        Node = Node.DetachChild(0);
                        if ((Node.Flags & ExpressionFlags.FixedType) == 0)
                            Node.Type = Ch1Id;

                        return PluginResult.Ready;
                    }

                    if (Res != PluginResult.Succeeded)
                    {
                        Node = Parent.NewNode(Node);
                        return Node == null ? PluginResult.Failed : PluginResult.Ready;
                    }
                }
                else
                {
                    throw new ApplicationException();
                }

                if ((Node.Flags & ExpressionFlags.FixedType) == 0)
                    Node.Type = Ch1Id;
            }

            //--------------------------------------------------------------------------------------
            else if (Op == Operator.Tuple)
            {
                for (var i = 0; i < Ch.Length; i++)
                    if (!Operators.IsRange(Expressions.GetOperator(Ch[i])))
                    {
                        var Type = Ch[i].Type.RealId as Type;
                        if ((Type.TypeFlags & TypeFlags.CanBeVariable) == 0)
                        {
                            State.Messages.Add(MessageId.CannotBeThisType, Node.Code);
                            return PluginResult.Failed;
                        }
                    }

                if ((Node.Flags & ExpressionFlags.FixedType) == 0)
                {
                    if (!Ch.TrueForAll(x => !(x.Type.RealId is VoidType)))
                        Node.Type = Container.GlobalContainer.CommonIds.Void;

                    else if (Node.Type == null || !CheckTupleType(Node))
                        Node.Type = CreateTupleTypeForNodes(Node);
                }
            }

            //--------------------------------------------------------------------------------------
            else if (Op == Operator.Array)
            {
                if (Node.Type != null)
                {
                    var ArrayType = Node.Type.RealId as NonrefArrayType;
                    var BaseType = ArrayType.Children[0];

                    if (!(BaseType is AutomaticType))
                    {
                        for (var i = 0; i < Ch.Length; i++)
                            if (!Ch[i].Type.IsEquivalent(BaseType))
                            {
                                Ch[i] = Convert(Ch[i], BaseType, Node.Code);
                                if (Ch[i] == null) return PluginResult.Failed;
                            }
                    }
                }
                else
                {
                    var Groups = Node.Data.Get<NodeGroup>();
                    int[] Lengths;

                    if (Groups != null)
                    {
                        Lengths = Expressions.GetArrayLengths(State, Groups);
                        if (Lengths == null) return PluginResult.Failed;
                    }
                    else
                    {
                        Lengths = new int[] { Ch.Length };
                    }

                    var Auto = Container.GlobalContainer.CommonIds.Auto;
                    Node.Type = new NonrefArrayType(Container, Auto, Lengths);
                }
            }

            //--------------------------------------------------------------------------------------
            else if (Operators.IsLogical(Op))
            {
                var BoolType = Container.GlobalContainer.CommonIds.Boolean;
                if ((Node.Flags & ExpressionFlags.FixedType) == 0)
                    Node.Type = BoolType;

                for (var i = 0; i < Ch.Length; i++)
                    if (!(Ch[i].Type is BooleanType))
                    {
                        Ch[i] = Convert(Ch[1], BoolType, Node.Code);
                        if (Ch[i] == null) return PluginResult.Failed;
                    }
            }

            //--------------------------------------------------------------------------------------
            else if (Op == Operator.Assignment)
            {
                if (!CanOpApplied(OpNode)) return PluginResult.Failed;

                var Code = Node.Code;
                var Ch0 = Ch[0].RealNode;
                var Ch1 = Ch[1];
                var NewCheck = false;
                var Res = Expressions.ProcessTuple(Parent, ref Ch1, (x, Index) =>
                {
                    if (Index == -1)
                    {
                        if (Expressions.GetOperator(Ch0) == Operator.Tuple)
                        {
                            for (var i = 0; i < Ch0.Children.Length; i++)
                            {
                                var Type = Expressions.GetTupleMemberType(x, i);
                                if (!CheckAssignVar(Ch0.Children[i], Type, Code))
                                    return null;
                            }
                        }

                        return CheckAssignVar(Ch0, x, Code);
                    }
                    else if (Expressions.GetOperator(Ch0) == Operator.Tuple)
                    {
                        return CheckAssignVar(Ch0.Children[Index], x, Code);
                    }
                    else
                    {
                        NewCheck = true;
                    }

                    return x;
                });

                if (Res == PluginResult.Failed)
                    return PluginResult.Failed;

                Ch[1] = Ch1;
                if (NewCheck)
                {
                    Ch[1] = CheckAssignVar(Ch[0], Ch[1], Node.Code);
                    if (Ch[1] == null) return PluginResult.Failed;
                }

                if (Res != PluginResult.Succeeded) return Parent.NewNode(ref Node);

                if ((Node.Flags & ExpressionFlags.FixedType) == 0)
                {
                    if (Expressions.GetOperator(Ch[0]) == Operator.Tuple && !CheckTupleType(Ch[0]))
                        Ch[0].Type = CreateTupleTypeForNodes(Ch[0]);

                    Node.Type = Ch[0].Type;
                }
            }

            //--------------------------------------------------------------------------------------
            else if (Operators.IsNewOp(Op))
            {
                if (Op == Operator.NewObject)
                {
                    if (!(Node.Type.RealId is AutomaticType))
                    {
                        for (var i = 0; i < Node.LinkedNodes.Count; i++)
                        {
                            var LinkedNode = Node.LinkedNodes[i];
                            var LNode = LinkedNode.Node;

                            if (LNode is ObjectInitNode)
                            {
                                var InitNode = LNode as ObjectInitNode;
                                if (!CalculateIdentifiers(Node.Type, InitNode))
                                    return PluginResult.Failed;
                            }
                        }

                        if (Ch != null && Ch.Length != 0)
                        {
                            var Res = CheckFunctionCall(ref Node);
                            if (Res != PluginResult.Succeeded) return Res;
                        }
                    }
                }

                else if (Op == Operator.NewArray)
                {
                    var ArrayType = Node.Type.RealId as RefArrayType;
                    if (!(ArrayType.Children[0].RealId is AutomaticType))
                    {
                        for (var i = 0; i < Node.LinkedNodes.Count; i++)
                        {
                            var LinkedNode = Node.LinkedNodes[i];
                            var LNode = LinkedNode.Node;

                            if (LNode is ObjectInitNode)
                            {
                                var InitNode = LNode as ObjectInitNode;
                                if (!CalculateIdentifiers(Node.Type, InitNode))
                                    return PluginResult.Failed;
                            }
                            else if (LNode is ArrayInitNode)
                            {
                                var InitNode = LNode as ArrayInitNode;
                                if (!CheckArrayInitNode(Node.Type, InitNode))
                                    return PluginResult.Failed;
                            }
                        }
                    }

                    for (var i = 0; i < Ch.Length; i++)
                    {
                        if (!(Ch[i].Type.RealId is NonFloatType))
                        {
                            var To = Container.GlobalContainer.CommonIds.Int32;
                            Ch[i] = Convert(Ch[i], To, Node.Code);
                            if (Ch[i] == null) return PluginResult.Failed;
                        }
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
                var Res = CheckFunctionCall(ref Node);
                if (Res != PluginResult.Succeeded) return Res;
            }

            //--------------------------------------------------------------------------------------
            else if (Op == Operator.Member)
            {
                if ((Node.Flags & ExpressionFlags.FixedType) == 0)
                {
                    if (!CanOpApplied(OpNode))
                        return PluginResult.Failed;

                    Node.Type = Ch[1].Type;
                }

                var IdNode = Ch[1] as IdExpressionNode;
                if (IdNode != null && IdNode.Identifier is Constructor && (Flags & TypeMngrPluginFlags.AllowConstructorCalls) == 0)
                {
                    State.Messages.Add(MessageId.CantUseConstructors, Node.Code);
                    return PluginResult.Failed;
                }
            }

            //--------------------------------------------------------------------------------------
            else if (Op == Operator.Index)
            {
                var Type = Ch[0].Type.RealId;
                if (Type is PointerAndLength)
                {
                    Ch[0] = ExtractMember(Ch[0], 0, Node.Code);
                    if (Ch[0] == null) return PluginResult.Failed;

                    return Parent.NewNode(ref Node);
                }

                if (Type is StringType)
                {
                    var StringType = Type as StringType;
                    Type = StringType.UnderlyingType;
                }

                if (Type is StructuredType)
                {
                    var Options = GetIdOptions.Default;
                    Options.EnableMessages = false;
                    Options.Func = x => x is Property && !x.Name.IsValid && x.Children.Length > 1;

                    var Indexer = Identifiers.GetMember(State, Type, new CodeString(), Options);
                    if (Indexer != null)
                    {
                        var IndexerNode = Parent.NewNode(new IdExpressionNode(Indexer, Node.Code));
                        if (IndexerNode == null) return PluginResult.Failed;

                        var NewCh = new ExpressionNode[] { Ch[0], IndexerNode };
                        Ch[0] = Parent.NewNode(new OpExpressionNode(Operator.Member, NewCh, Node.Code));
                        if (Ch[0] == null) return PluginResult.Failed;

                        Node = Parent.NewNode(Node);
                        return Node == null ? PluginResult.Failed : PluginResult.Ready;
                    }
                }

                var MemberId = Expressions.GetMemberIdentifier(Ch[0]);
                var Property = MemberId as Property;
                int IndexCount;

                if (Property != null)
                {
                    IndexCount = Property.Children.Length - 1;
                    if ((Node.Flags & ExpressionFlags.FixedType) == 0)
                        Node.Type = Property.Children[0];

                    var BaseId = Expressions.GetIdentifier(Ch[0].Children[0]);
                    if (BaseId is BaseVariable && (Property.Flags & IdentifierFlags.Abstract) != 0)
                    {
                        State.Messages.Add(MessageId.StaticCallAbstract, Ch[0].Code);
                        return PluginResult.Failed;
                    }
                }
                else if (Type is PointerType)
                {
                    var pType = Type as PointerType;
                    IndexCount = 1;

                    if ((Node.Flags & ExpressionFlags.FixedType) == 0)
                        Node.Type = pType.Child;
                }
                else if (Type is ArrayType)
                {
                    var Array = Type as ArrayType;
                    IndexCount = Array.Dimensions;

                    if ((Node.Flags & ExpressionFlags.FixedType) == 0)
                        Node.Type = Array.TypeOfValues;
                }
                else
                {
                    State.Messages.Add(MessageId.CantOpApplied, Node.Code, Type.Name.ToString());
                    return PluginResult.Failed;
                }

                if (IndexCount + 1 != Ch.Length)
                {
                    State.Messages.Add(MessageId.ParamCount, Node.Code);
                    return PluginResult.Failed;
                }

                for (var i = 1; i <= IndexCount; i++)
                {
                    if (Property != null)
                    {
                        var Param = Property.Children[i] as FunctionParameter;
                        var ParamType = Param.TypeOfSelf;

                        if (!Ch[i].Type.IsEquivalent(ParamType))
                        {
                            Ch[i] = Convert(Ch[i], ParamType, Node.Code);
                            if (Ch[i] == null) return PluginResult.Failed;
                        }
                    }
                    else
                    {
                        Ch[i] = CheckCanBeIndexerType(Ch[i], Node.Code);
                        if (Ch[i] == null) return PluginResult.Failed;

                        if (Ch[i] is ConstExpressionNode && Type is NonrefArrayType)
                        {
                            var FArr = Type as NonrefArrayType;
                            var Const = Ch[i] as ConstExpressionNode;
                            var Val = Const.Value as IntegerValue;
                            var Int = Val.Value;

                            if (FArr.Lengths != null && (Int < 0 || Int >= FArr.Lengths[i - 1]))
                            {
                                State.Messages.Add(MessageId.IndexOutOfRange, Ch[i].Code);
                                return PluginResult.Failed;
                            }
                        }
                    }
                }

                var ConstCh1 = Ch[1] as ConstExpressionNode;
                if (Expressions.GetOperator(Ch[0]) == Operator.Address && ConstCh1 != null && ConstCh1.Integer == 0)
                {
                    Ch[0].Children[0].Type = Node.Type;
                    Node = Ch[0].Children[0];
                    return PluginResult.Ready;
                }
            }

            //--------------------------------------------------------------------------------------
            else if (Op == Operator.Address)
            {
                var ChildType = Ch[0].Type.RealId as Type;
                if (ChildType is VoidType || (ChildType.TypeFlags & TypeFlags.CanBePointer) == 0)
                {
                    State.Messages.Add(MessageId.InvalidAddressType, Node.Code);
                    return PluginResult.Failed;
                }

                if ((Node.Flags & ExpressionFlags.FixedType) == 0)
                    Node.Type = new PointerType(Container, Ch[0].Type);
            }

            //--------------------------------------------------------------------------------------
            else if (Op == Operator.Not)
            {
                var Type = Ch[0].Type;
                if (!(Type is BooleanType))
                {
                    Type = Container.GlobalContainer.CommonIds.Boolean;
                    Ch[0] = Convert(Ch[0], Type, Node.Code);
                    if (Ch[0] == null) return PluginResult.Failed;
                }

                if ((Node.Flags & ExpressionFlags.FixedType) == 0)
                    Node.Type = Type;
            }

            //--------------------------------------------------------------------------------------
            else if (Op == Operator.Condition)
            {
                var Type0 = Ch[0].Type;
                if (!(Type0 is BooleanType))
                {
                    Type0 = Container.GlobalContainer.CommonIds.Boolean;
                    Ch[0] = Convert(Ch[0], Type0, Node.Code);
                    if (Ch[0] == null) return PluginResult.Failed;
                }

                if (!Ch[1].Type.IsEquivalent(Ch[2].Type))
                {
                    var RetType = Container.GetRetType(Ch[1].Type, Ch[2].Type);
                    if (RetType == null) return PluginResult.Failed;

                    if (!Ch[1].Type.IsEquivalent(RetType))
                    {
                        if ((Ch[1] = Convert(Ch[1], RetType, Node.Code)) == null)
                            return PluginResult.Failed;
                    }

                    if (!Ch[2].Type.IsEquivalent(RetType))
                    {
                        if ((Ch[2] = Convert(Ch[2], RetType, Node.Code)) == null)
                            return PluginResult.Failed;
                    }
                }

                if ((Node.Flags & ExpressionFlags.FixedType) == 0)
                {
                    Node.Type = Ch[1].Type;
                    if (Node.Type is AutomaticType)
                    {
                        State.Messages.Add(MessageId.Untyped, Node.Code);
                        return PluginResult.Failed;
                    }
                }
            }

            //--------------------------------------------------------------------------------------
            else if (Operators.IsReference(Op))
            {
                ReferenceMode Mode;
                if (Op == Operator.Reference_Unsafe) Mode = ReferenceMode.Unsafe;
                else if (Op == Operator.Reference_IdMustBeAssigned) Mode = ReferenceMode.IdMustBeAssigned;
                else if (Op == Operator.Reference_IdGetsAssigned) Mode = ReferenceMode.IdGetsAssigned;
                else throw new ApplicationException();

                var ChildType = Ch[0].Type.RealId as Type;
                if ((Node.Flags & ExpressionFlags.FixedType) == 0)
                    Node.Type = new ReferenceType(Container, Ch[0].Type, Mode);

                if (!(ChildType is AutomaticType) && (ChildType.TypeFlags & TypeFlags.CanBeReference) == 0)
                {
                    State.Messages.Add(MessageId.InvalidAddressType, Node.Code);
                    return PluginResult.Failed;
                }
            }

            //--------------------------------------------------------------------------------------
            else if (Op == Operator.UnaryPlus)
            {
                Node = Ch[0];
                return PluginResult.Ready;
            }

            //--------------------------------------------------------------------------------------
            else if (Op == Operator.Negation || Op == Operator.Complement)
            {
                if (!CanOpApplied(OpNode)) return PluginResult.Failed;
                if ((Node.Flags & ExpressionFlags.FixedType) == 0)
                    Node.Type = Ch[0].Type;
            }

            //--------------------------------------------------------------------------------------
            else if (Operators.IsRelEquality(Op))
            {
                var IdCh0 = Ch[0] as IdExpressionNode;
                var IdCh1 = Ch[1] as IdExpressionNode;
                if (IdCh0 != null && IdCh1 != null && IdCh0.Identifier == IdCh1.Identifier)
                    State.Messages.Add(MessageId.CmpSameVariable, Node.Code);

                var Res = CastToAppropriateTypes(ref Node);
                if (Res != PluginResult.Succeeded) return Res;

                if (!CanOpApplied(OpNode)) return PluginResult.Failed;
                if ((Node.Flags & ExpressionFlags.FixedType) == 0)
                    Node.Type = Container.GlobalContainer.CommonIds.Boolean;
            }

            //--------------------------------------------------------------------------------------
            else if (Operators.IsShift(Op))
            {
                if (Ch[0].Type.RealId is AutomaticType)
                {
                    State.Messages.Add(MessageId.Untyped, Node.Code);
                    return PluginResult.Failed;
                }

                if (!CanOpApplied(OpNode)) return PluginResult.Failed;
                if ((Node.Flags & ExpressionFlags.FixedType) == 0)
                    Node.Type = Ch[0].Type;
            }

            //--------------------------------------------------------------------------------------
            else if (Operators.IsArithmetical(Op))
            {
                var Ch0Type = Ch[0].Type.RealId as Type;
                var Ch1Type = Ch[1].Type.RealId as Type;

                if ((Ch0Type is StringType) != (Ch1Type is StringType))
                {
                    var StringIndex = Ch0Type is StringType ? 0 : 1;
                    var NotStringIndex = 1 - StringIndex;
                    var NotStringType = NotStringIndex == 0 ? Ch0Type : Ch1Type;

                    if (Identifiers.ContainsMember(NotStringType, "ToString", x => x is Function))
                    {
                        Ch[NotStringIndex] = Expressions.CallToStringSafe(Parent,
                            Ch[NotStringIndex], Node.Code);

                        if (Ch[NotStringIndex] == null) return PluginResult.Failed;
                        return Parent.NewNode(ref Node);
                    }
                }

                if (Op == Operator.Add || Op == Operator.Subract)
                {
                    var PtrIndex = Ch0Type is PointerType ? 0 : (Ch1Type is PointerType ? 1 : -1);
                    var NonPtrIndex = Ch0Type is NonFloatType ? 0 : (Ch1Type is NonFloatType ? 1 : -1);

                    if (PtrIndex != -1 && NonPtrIndex != -1)
                    {
                        if ((Ch[PtrIndex].Type.RealId as PointerType).Child.RealId is VoidType)
                        {
                            State.Messages.Add(MessageId.CantOpApplied2, Node.Code,
                                Ch[0].Type.Name.ToString(), Ch[1].Type.Name.ToString());

                            return PluginResult.Failed;
                        }

                        if ((Node.Flags & ExpressionFlags.FixedType) == 0)
                            Node.Type = Ch[PtrIndex].Type;

                        return PluginResult.Succeeded;
                    }
                }

                if (!CanOpApplied(OpNode)) return PluginResult.Failed;

                var Res = CastToAppropriateTypes(ref Node);
                if (Res != PluginResult.Succeeded) return Res;
            }

            //--------------------------------------------------------------------------------------
            else if (Operators.IsBitwise(Op))
            {
                if (!CanOpApplied(OpNode)) return PluginResult.Failed;

                var Res = CastToAppropriateTypes(ref Node);
                if (Res != PluginResult.Succeeded) return Res;
            }

            //--------------------------------------------------------------------------------------
            else if (Operators.IsRange(Op))
            {
                if (!CanOpApplied(OpNode)) return PluginResult.Failed;

                var Res = CastToAppropriateTypes(ref Node);
                if (Res != PluginResult.Succeeded) return Res;

                if ((Node.Flags & ExpressionFlags.FixedType) == 0)
                    Node.Type = Container.GlobalContainer.CommonIds.Void;
            }

            //--------------------------------------------------------------------------------------
            else if (Op != Operator.NewArray && Op != Operator.Unknown)
            {
                throw new ApplicationException();
            }

            return PluginResult.Succeeded;
        }

        ExpressionNode ExtractMember(ExpressionNode Node, int Index, CodeString Code)
        {
            var Type = Node.Type.RealId as TupleType;
            var Members = Type.StructuredScope.IdentifierList;
            var Member = Parent.NewNode(new IdExpressionNode(Members[Index], Code));
            if (Member == null) return null;

            var Ch = new ExpressionNode[] { Node, Member };
            return Parent.NewNode(new OpExpressionNode(Operator.Member, Ch, Code));
        }

        bool CheckAssignVar(ExpressionNode Dst, CodeString Code)
        {
            var DstIdNode = Dst.RealNode as IdExpressionNode;
            if (DstIdNode != null)
            {
                var Id = DstIdNode.Identifier.RealId;
                if (Id != null && (Id.Flags & IdentifierFlags.ReadOnly) != 0 &&
                   (Flags & TypeMngrPluginFlags.EnableReadonlyWriting) == 0)
                {
                    State.Messages.Add(MessageId.ReadOnly, Dst.Code);
                    return false;
                }
            }
            else if (!Expressions.IsLValue(Dst))
            {
                State.Messages.Add(MessageId.AssignRValue, Code);
                return false;
            }

            return true;
        }

        bool CheckAssignVar(ExpressionNode Dst, Identifier SrcType, CodeString Code)
        {
            if (!CheckAssignVar(Dst, Code))
                return false;

            if (Dst.Type.RealId is AutomaticType)
            {
                if (SrcType.RealId is AutomaticType)
                {
                    State.Messages.Add(MessageId.Untyped, Code);
                    return false;
                }
                else
                {
                    SetAutoNodeType(Dst, SrcType);
                }
            }

            return true;
        }

        private static bool SetAutoNodeType(ExpressionNode Node, Identifier Type, bool AlwaysSet = true)
        {
            if (AlwaysSet)
            {
                Node.Type = Type;
                Node.Flags |= ExpressionFlags.FixedType;
            }

            if (Node is IdExpressionNode)
            {
                Node.Type = Type;

                var IdDst = Node as IdExpressionNode;
                var Var = IdDst.Identifier as Variable;

                if (Var != null && Var.TypeOfSelf.RealId is AutomaticType)
                {
                    Var.Children[0] = Type;
                    Var.Update();
                    return true;
                }
            }

            return false;
        }

        ExpressionNode ResolveAutomaticTypes(ExpressionNode Src, CodeString Code)
        {
            if (Src is OpExpressionNode)
            {
                return ResolveAutomaticTypes_OpNode(Src, Code);
            }

            return Src;
        }

        ExpressionNode ResolveAutomaticTypes_OpNode(ExpressionNode Src, CodeString Code)
        {
            var SrcOp = Expressions.GetOperator(Src);

            //--------------------------------------------------------------------------------------
            if (SrcOp == Operator.Reference_Unsafe || SrcOp == Operator.Reference_IdGetsAssigned ||
                SrcOp == Operator.Reference_IdMustBeAssigned)
            {
                var RefType = Src.Type.RealId as ReferenceType;
                if (RefType.Children[0].RealId is AutomaticType)
                {
                    State.Messages.Add(MessageId.Untyped, Code);
                    return null;
                }
            }

            //--------------------------------------------------------------------------------------
            if (SrcOp == Operator.NewObject)
            {
                if (Src.Type.RealId is AutomaticType)
                {
                    State.Messages.Add(MessageId.Untyped, Code);
                    return null;
                }
            }

            //--------------------------------------------------------------------------------------
            else if (SrcOp == Operator.NewArray)
            {
                var ArrayType = Src.Type.RealId as RefArrayType;
                if (ArrayType != null && ArrayType.TypeOfValues is AutomaticType)
                {
                    var NewType = (Identifier)null;
                    for (var i = 0; i < Src.LinkedNodes.Count; i++)
                    {
                        var LinkedNode = Src.LinkedNodes[i];
                        var LNode = LinkedNode.Node as ArrayInitNode;
                        if (LNode == null) continue;

                        for (var j = 0; j < LNode.Indices.Length; j++)
                        {
                            var ChType = LNode.Children[j].Type;
                            if (NewType == null)
                            {
                                NewType = ChType;
                            }
                            else if (!NewType.IsEquivalent(ChType))
                            {
                                NewType = null;
                                goto CheckEnd;
                            }
                        }
                    }

                CheckEnd:
                    var RNewType = NewType == null ? null : NewType.RealId as Type;
                    if (RNewType != null && (RNewType.TypeFlags & TypeFlags.CanBeArrayType) != 0)
                    {
                        Src.Type = new RefArrayType(Container, NewType, ArrayType.Dimensions);
                        Src = Parent.NewNode(Src);
                    }
                    else
                    {
                        State.Messages.Add(MessageId.ArrayDifferentTypes, Code);
                        return null;
                    }
                }
            }

            //--------------------------------------------------------------------------------------
            else if (SrcOp == Operator.Array)
            {
                var ArrayType = Src.Type.RealId as NonrefArrayType;
                if (ArrayType != null && ArrayType.TypeOfValues is AutomaticType)
                {
                    var NewType = (Identifier)null;
                    for (var i = 0; i < Src.Children.Length; i++)
                    {
                        var ChType = Src.Children[i].Type;
                        if (NewType == null)
                        {
                            NewType = ChType;
                        }
                        else if (!NewType.IsEquivalent(ChType))
                        {
                            NewType = null;
                            break;
                        }
                    }

                    var RNewType = NewType == null ? null : NewType.RealId as Type;
                    if (RNewType != null && (RNewType.TypeFlags & TypeFlags.CanBeArrayType) != 0)
                    {
                        Src.Type = new NonrefArrayType(Container, NewType, ArrayType.Lengths);
                        Src = Parent.NewNode(Src);
                    }
                    else
                    {
                        State.Messages.Add(MessageId.ArrayDifferentTypes, Code);
                        return null;
                    }
                }
            }

            //--------------------------------------------------------------------------------------
            return Src;
        }

        ExpressionNode CheckAssignVar(ExpressionNode Dst, ExpressionNode Src, CodeString Code)
        {
            Dst = Dst.RealNode;
            if (!CheckAssignVar(Dst, Src.Type, Code))
                return null;

            if (Src is IdExpressionNode && Dst is IdExpressionNode)
            {
                var IdDst = Dst as IdExpressionNode;
                var IdSrc = Src as IdExpressionNode;

                if (IdDst.Identifier == IdSrc.Identifier)
                    State.Messages.Add(MessageId.AssignSameVar, Code);
            }

            if (!Dst.Type.IsEquivalent(Src.Type))
            {
                if (Dst.Type == null || Src.Type == null)
                    return null;

                Src = Convert(Src, Dst.Type, Code);
                if (Src == null) return null;
            }

            return Src;
        }

        public Identifier CastToAppropriateTypes(ref Identifier Type0, bool AutoConvert0, ref Identifier Type1, bool AutoConvert1)
        {
            var RealId0 = Type0.RealId;
            var RealId1 = Type1.RealId;

            if (RealId0 is AutomaticType) { Type0 = Type1; return Type0; }
            if (RealId1 is AutomaticType) { Type1 = Type0; return Type0; }

            if (RealId0 is TupleType) { Type1 = Type0; return Type0; }
            if (RealId1 is TupleType) { Type0 = Type1; return Type0; }

            if (RealId0 is NumberType && RealId1 is NumberType)
            {
                if (AutoConvert0 && (!(RealId0 is FloatType) || RealId1 is FloatType))
                {
                    Type0 = Type1;
                    return Type0;
                }
                else if (AutoConvert1 && (!(RealId1 is FloatType) || RealId0 is FloatType))
                {
                    Type1 = Type0;
                    return Type0;
                }
            }

            if (RealId0 is CharType) return Type0;
            if (RealId1 is CharType) return Type1;

            if (RealId0 is PointerType) return Type0;
            if (RealId1 is PointerType) return Type1;

            if (RealId0 is NumberType && RealId1 is NumberType)
            {
                if (RealId0.IsEquivalent(RealId1)) return RealId0;
                Type0 = Type1 = Container.GetNumberRetType(Type0, Type1);
            }

            return Type0;
        }

        bool CheckVariablesTypes(List<Identifier> Identifiers, List<Identifier> Types)
        {
            if (Identifiers.Count != Types.Count)
                throw new ArgumentException();

            for (var i = 0; i < Identifiers.Count; i++)
            {
                if (!Identifiers[i].TypeOfSelf.IsEquivalent(Types[i]))
                    return false;
            }

            return true;
        }

        PluginResult CastToAppropriateTypes(ref ExpressionNode Node)
        {
            var Ch = Node.Children;
            var Type0 = Ch[0].Type;
            var Type1 = Ch[1].Type;

            if (Type0.RealId is TupleType && Type1.RealId is TupleType)
            {
                var Tuple0 = Type0.RealId as TupleType;
                var Tuple1 = Type1.RealId as TupleType;

                var Members0 = Tuple0 != null ? Tuple0.StructuredScope.IdentifierList
                                              : new AutoAllocatedList<Identifier>();

                var Members1 = Tuple1 != null ? Tuple1.StructuredScope.IdentifierList
                                              : new AutoAllocatedList<Identifier>();

                if (Members0.Count == Members1.Count)
                {
                    var Check = false;
                    var RetTypes = new List<Identifier>();
                    var Ch0Types = new List<Identifier>();
                    var Ch1Types = new List<Identifier>();

                    for (var i = 0; i < Members0.Count; i++)
                    {
                        var MType0 = Members0[i].Children[0];
                        var MType1 = Members1[i].Children[0];

                        var MRetType = CastToAppropriateTypes(ref MType0, false, ref MType1, false);
                        if (!MType0.IsEquivalent(Members0[i].TypeOfSelf)) Check = true;
                        if (!MType1.IsEquivalent(Members1[i].TypeOfSelf)) Check = true;

                        Ch0Types.Add(MType0);
                        Ch1Types.Add(MType1);

                        if (MRetType.RealId is AutomaticType)
                        {
                            State.Messages.Add(MessageId.Untyped, Node.Code);
                            return PluginResult.Failed;
                        }

                        RetTypes.Add(MRetType);
                    }

                    if (Check)
                    {
                        var LConvertDone = false;
                        if (Members0.List == null || !CheckVariablesTypes(Members0.List, Ch0Types))
                        {
                            var NewType = Identifiers.CreateTupleFromTypes(Container, Ch0Types);
                            Ch[0] = Convert(Ch[0], NewType, Node.Code);
                            if (Ch[0] == null) return PluginResult.Failed;

                            LConvertDone = true;
                        }

                        if (Members1.List == null || !CheckVariablesTypes(Members1.List, Ch1Types))
                        {
                            var NewType = Identifiers.CreateTupleFromTypes(Container, Ch1Types);
                            Ch[1] = Convert(Ch[1], NewType, Node.Code);
                            if (Ch[1] == null) return PluginResult.Failed;

                            LConvertDone = true;
                        }

                        if (LConvertDone)
                        {
                            Node = Parent.NewNode(Node);
                            return Node == null ? PluginResult.Failed : PluginResult.Ready;
                        }
                    }

                    if (Members0.List != null && CheckVariablesTypes(Members0.List, RetTypes))
                    {
                        if ((Node.Flags & ExpressionFlags.FixedType) == 0)
                            Node.Type = Type0;

                        return PluginResult.Succeeded;
                    }

                    if (Members1.List != null && CheckVariablesTypes(Members1.List, RetTypes))
                    {
                        if ((Node.Flags & ExpressionFlags.FixedType) == 0)
                            Node.Type = Type1;

                        return PluginResult.Succeeded;
                    }

                    if ((Node.Flags & ExpressionFlags.FixedType) == 0)
                        Node.Type = Identifiers.CreateTupleFromTypes(Container, RetTypes);

                    return PluginResult.Succeeded;
                }
            }

            var RetType = CastToAppropriateTypes(ref Type0, (Ch[0].Flags & ExpressionFlags.AutoConvert) != 0,
                ref Type1, (Ch[1].Flags & ExpressionFlags.AutoConvert) != 0);

            if (RetType.RealId is AutomaticType)
            {
                State.Messages.Add(MessageId.Untyped, Node.Code);
                return PluginResult.Failed;
            }

            var ConvertDone = false;
            if (!Type0.IsEquivalent(Ch[0].Type))
            {
                Ch[0] = Convert(Ch[0], Type0, Node.Code);
                if (Ch[0] == null) return PluginResult.Failed;

                ConvertDone = true;
            }

            if (!Type1.IsEquivalent(Ch[1].Type))
            {
                Ch[1] = Convert(Ch[1], Type1, Node.Code);
                if (Ch[1] == null) return PluginResult.Failed;

                ConvertDone = true;
            }

            if (ConvertDone)
            {
                Node = Parent.NewNode(Node);
                return Node == null ? PluginResult.Failed : PluginResult.Ready;
            }

            if ((Node.Flags & ExpressionFlags.FixedType) == 0)
                Node.Type = RetType;

            return PluginResult.Succeeded;
        }

        public bool CanOpApplied(OpExpressionNode Node)
        {
            var Ch = Node.Children;
            var Op = Node.Operator;

            if ((Ch[0].Flags & ExpressionFlags.AutoConvert) == 0 &&
                (Ch.Length < 2 || (Ch[1].Flags & ExpressionFlags.AutoConvert) == 0))
            {
                var SrcType = Ch.Length > 1 ? Ch[1].Type.RealId as Type : null;
                if (!(Ch[0].Type.RealId as Type).CanOpApplied(Op, SrcType))
                {
                    if (Ch.Length > 1 && SrcType != null && !Ch[0].Type.IsEquivalent(SrcType))
                    {
                        State.Messages.Add(MessageId.CantOpApplied2, Node.Code,
                            Ch[0].Type.Name.ToString(), SrcType.Name.ToString());
                    }
                    else
                    {
                        State.Messages.Add(MessageId.CantOpApplied, Node.Code, Ch[0].Type.Name.ToString());
                    }

                    return false;
                }
            }

            return true;
        }

        Identifier GetNonrefType(Identifier Type)
        {
            var RefType = Type.RealId as ReferenceType;
            if (RefType != null) return RefType.Children[0];
            return Type;
        }

        bool IsUnlinkableNode(ExpressionNode Node)
        {
            var Op = Expressions.GetOperator(Node);
            var Ch = Node.Children;

            if (Op == Operator.Index || Op == Operator.Member)
            {
                for (var i = 0; i < Ch.Length; i++)
                    if (Ch[i] is OpExpressionNode) return false;

                if (Op == Operator.Member && Ch[1] is IdExpressionNode)
                {
                    var IdCh1 = Ch[1] as IdExpressionNode;
                    if (IdCh1.Identifier.RealId is Property) return false;
                }

                return true;
            }
            else if (Node is IdExpressionNode)
            {
                var IdNode = Node as IdExpressionNode;
                return !(IdNode.Identifier.RealId is Property);
            }

            return !(Node is OpExpressionNode);
        }

        bool NeedToResolveAutomaticTypes(ExpressionNode Node)
        {
            var Op = Expressions.GetOperator(Node);
            var Ch = Node.Children;

            if (Op == Operator.NewObject) return Ch == null || Ch.Length == 0;
            else if (Op == Operator.Assignment) return Ch[0].Type.RealId is AutomaticType;
            else return Op != Operator.Cast && Op != Operator.Tuple && Op != Operator.Call;
        }

        PluginResult ResolveAutomaticTypes_Tuple(ref ExpressionNode Node, CodeString Code)
        {
            return Expressions.ProcessTuple(Parent, ref Node, (x, Index) =>
                ResolveAutomaticTypes(x, Code));
        }

        public override PluginResult NewNode(ref ExpressionNode Node)
        {
            if (CheckingMode != CheckingMode.Default && Node.CheckingMode == CheckingMode.Default)
                Node.CheckingMode = CheckingMode;

            for (var i = 0; i < Node.LinkedNodes.Count; i++)
            {
                var LNode = Node.LinkedNodes[i];
                LNode.Node = ResolveAutomaticTypes(LNode.Node, Node.Code);
                if (LNode.Node == null) return PluginResult.Failed;

                if ((LNode.Flags & LinkedNodeFlags.PostComputation) != 0)
                {
                    if (LNode.Node.Type.RealId is VoidType)
                        throw new ApplicationException();
                }

                if ((LNode.Flags & LinkedNodeFlags.NotRemovable) == 0 && LNode.LinkingCount == 0)
                {
                    Node.LinkedNodes.RemoveAt(i);
                    i--;
                }
            }

            //-------------------------------------------------------------------------------------
            if (NeedToResolveAutomaticTypes(Node))
            {
                var CallNewNode = false;
                if (Node.Children != null)
                {
                    var Ch = Node.Children;
                    for (var i = 0; i < Ch.Length; i++)
                    {
                        var Chi = Ch[i];
                        var Res = ResolveAutomaticTypes_Tuple(ref Chi, Node.Code);
                        if (Res == PluginResult.Failed) return Res;
                        if (Res != PluginResult.Succeeded) CallNewNode = true;
                    }
                }

                if (CallNewNode)
                {
                    Node = Parent.NewNode(Node);
                    return Node == null ? PluginResult.Failed : PluginResult.Ready;
                }
            }

            //------------------------------------------------------------------------------------
            if (!ConvertChildrenToNonstaticFunction(Node))
                return PluginResult.Failed;

            //--------------------------------------------------------------------------------------
            if (Node is ScopeExpressionNode)
            {
                if ((Node.Flags & ExpressionFlags.FixedType) == 0)
                {
                    var SNode = Node as ScopeExpressionNode;
                    if (SNode.ReturnVar != null) Node.Type = SNode.ReturnVar.Children[0];
                    else Node.Type = Container.GlobalContainer.CommonIds.Void;
                }
            }

            //--------------------------------------------------------------------------------------
            else if (Node is IdExpressionNode)
            {
                var IdNode = Node as IdExpressionNode;
                var Id = IdNode.Identifier;
                Id.SetUsed();

                if ((Node.Flags & ExpressionFlags.FixedType) == 0)
                    IdNode.Type = GetNonrefType(Id.TypeOfSelf);
            }

            //--------------------------------------------------------------------------------------
            else if (Node is OpExpressionNode)
            {
                var Res = NewOpNode(ref Node);
                if (Res != PluginResult.Succeeded)
                    return Res;
            }

            //--------------------------------------------------------------------------------------
            else if (Node is LinkingNode)
            {
                var LinkingNode = Node as LinkingNode;
                var LinkedNode = LinkingNode.LinkedNode;
                var RLinkedNode = LinkedNode.Node;

                if ((LinkedNode.Flags & LinkedNodeFlags.NotRemovable) == 0)
                {
                    if (Expressions.GetOperator(RLinkedNode) != Operator.Assignment)
                    {
                        if (IsUnlinkableNode(RLinkedNode))
                        {
                            Node = RLinkedNode.Copy(Parent, Mode: BeginEndMode.None);
                            return Node == null ? PluginResult.Failed : PluginResult.Ready;
                        }
                    }
                    else if (IsUnlinkableNode(RLinkedNode.Children[0]))
                    {
                        LinkedNode.LinkingCount++;
                        Node = RLinkedNode.Children[0].Copy(Parent, Mode: BeginEndMode.None);
                        if (Node == null) return PluginResult.Failed;

                        Node.Flags &= ~ExpressionFlags.IdMustBeAssigned;
                        return PluginResult.Ready;
                    }
                }

                if ((Node.Flags & ExpressionFlags.FixedType) == 0)
                    Node.Type = RLinkedNode.Type;

                LinkedNode.LinkingCount++;
            }

            //--------------------------------------------------------------------------------------
            else if (Node is DataPointerNode)
            {
                var IdDescNode = Node as DataPointerNode;
                if (IdDescNode.DescPointerType == DataPointerType.Identifier)
                {
                    var Id = IdDescNode.Id;
                    Id.SetUsed();

                    if (!(Id.RealId is Type) && Id.Container.FunctionScope != null)
                    {
                        State.Messages.Add(MessageId.IdDescPtrFromLocal, Node.Code);
                        return PluginResult.Failed;
                    }

                    var RealOrUnder = Id.UnderlyingStructureOrSelf;
                    if (RealOrUnder is Type && RealOrUnder.DeclaredIdType == DeclaredIdType.Unknown)
                    {
                        var Global = Container.GlobalContainer;
                        IdDescNode.Id = Id = Global.GetTypeAlias((Id as Type));
                        if (Id == null) return PluginResult.Failed;
                    }
                }
                else if (IdDescNode.DescPointerType != DataPointerType.IncBin &&
                    IdDescNode.DescPointerType != DataPointerType.Assembly)
                {
                    throw new NotImplementedException();
                }

                if ((Node.Flags & ExpressionFlags.FixedType) == 0)
                    Node.Type = Container.GlobalContainer.CommonIds.VoidPtr;
            }

            //--------------------------------------------------------------------------------------
            else if (Node is LabelExpressionNode)
            {
                if ((Node.Flags & ExpressionFlags.FixedType) == 0)
                    Node.Type = Container.GlobalContainer.CommonIds.VoidPtr;
            }

            //--------------------------------------------------------------------------------------
            else if (Node is ObjectInitNode || Node is ArrayInitNode)
            {
                if ((Node.Flags & ExpressionFlags.FixedType) == 0)
                    Node.Type = Container.GlobalContainer.CommonIds.Void;
            }

            //--------------------------------------------------------------------------------------
            else if (Node is ConstExpressionNode || Node is StrExpressionNode ||
                Node is MacroExpressionNode || Node is MacroArgNode || Node is NamedParameterNode)
            {
                return PluginResult.Succeeded;
            }

            //--------------------------------------------------------------------------------------
            if (Node.Type == null && (Flags & TypeMngrPluginFlags.EnableUntypedNodes) == 0)
                throw new ApplicationException();

            if (!ProcessType(Node)) return PluginResult.Failed;
            if (!Node.CheckChildren(CheckChild)) return PluginResult.Failed;
            return PluginResult.Succeeded;
        }

        public bool CheckChild(ExpressionNode Node)
        {
            if (!CheckPropertyIndices(Node)) return false;
            return true;
        }

        public bool CheckPropertyIndices(ExpressionNode Node)
        {
            var Id = Expressions.GetMemberIdentifier(Node);
            if (Id == null) return true;

            var Property = Id.RealId as Property;
            if (Property != null && Property.Children.Length > 1)
            {
                State.Messages.Add(MessageId.MissingPropertyIndices, Node.Code);
                return false;
            }

            return true;
        }
    }
}