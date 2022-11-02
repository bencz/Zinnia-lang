using System;
using System.Linq;
using System.Numerics;
using Zinnia.Base;

namespace Zinnia;

public class EvaluatorPlugin : ExpressionPlugin
{
    public bool MustBeConst;

    public EvaluatorPlugin(PluginRoot Parent, bool MustBeConst)
        : base(Parent)
    {
        this.MustBeConst = MustBeConst;
    }

    public override bool Begin()
    {
        if (Parent.GetPlugin<TypeMngrPlugin>() == null)
            throw new ApplicationException();

        return base.Begin();
    }

    public override PluginResult End(ref ExpressionNode Node)
    {
        if (MustBeConst && !(Node is ConstExpressionNode))
        {
            State.Messages.Add(MessageId.MustBeConst, Node.Code);
            return PluginResult.Failed;
        }

        return PluginResult.Succeeded;
    }

    private PluginResult NewCallOpNode(ref ExpressionNode Node)
    {
        var OpNode = Node as OpExpressionNode;
        var Op = OpNode.Operator;
        var Ch = OpNode.Children;

        var Ok = true;
        for (var i = 1; i < Ch.Length; i++)
            if (!(Ch[i] is ConstExpressionNode))
            {
                Ok = false;
                break;
            }

        // ------------------------------------------------------------------------------------
        var IdCh0 = Ch[0] as IdExpressionNode;
        if (IdCh0 != null && IdCh0.Identifier is Function && Ok)
        {
            var Func = IdCh0.Identifier as Function;
            var FuncType = Func.TypeOfSelf.RealId as TypeOfFunction;
            var RetType = FuncType.RetType;
            var Name = Func.AssemblyNameWithoutDecorations;
            var RetValue = (ConstValue)null;

            if (Name != null && Name.StartsWith("_System_Math_"))
            {
                if (Ch.Length == 2)
                {
                    var Param0Node = Ch[1] as ConstExpressionNode;
                    var Param0RealId = Param0Node.Type.RealId;
                    var Param0Value = Param0Node.Value;

                    if (!(Param0RealId is NumberType))
                        return PluginResult.Succeeded;

                    if (Name == "_System_Math_Abs")
                    {
                        RetValue = Param0Value;
                        if (Param0RealId is FloatType)
                        {
                            var FractionValue = Param0Value as DoubleValue;
                            FractionValue.Value = Math.Abs(FractionValue.Value);
                        }
                        else
                        {
                            var IntegerValue = Param0Value as IntegerValue;
                            IntegerValue.Value = BigInteger.Abs(IntegerValue.Value);
                        }
                    }
                    else if (Name == "_System_Math_Sqrt")
                    {
                        if (Param0RealId is FloatType)
                        {
                            RetValue = Param0Value;
                            var FractionValue = Param0Value as DoubleValue;
                            FractionValue.Value = Math.Sqrt(FractionValue.Value);
                        }
                        else
                        {
                            var IntegerValue = Param0Value as IntegerValue;
                            RetValue = new DoubleValue(Math.Sqrt((double)IntegerValue.Value));
                        }
                    }
                    else if (Name == "_System_Math_Sign")
                    {
                        RetValue = Param0Value;
                        if (Param0RealId is FloatType)
                        {
                            var FractionValue = Param0Value as DoubleValue;
                            FractionValue.Value = Math.Sign(FractionValue.Value);
                        }
                        else
                        {
                            var IntegerValue = Param0Value as IntegerValue;
                            if (IntegerValue.Value < 0) IntegerValue.Value = -1;
                            else if (IntegerValue.Value > 0) IntegerValue.Value = 1;
                            else IntegerValue.Value = 0;
                        }
                    }
                    else if (Name == "_System_Math_Log")
                    {
                        RetValue = new DoubleValue(Math.Log(Param0Value.Double));
                    }
                    else if (Name == "_System_Math_Log2")
                    {
                        RetValue = new DoubleValue(Math.Log(Param0Value.Double, 2));
                    }
                    else if (Name == "_System_Math_Log10")
                    {
                        Param0Value = new DoubleValue(Math.Log10(Param0Value.Double));
                    }
                    else if (Name == "_System_Math_Exp")
                    {
                        RetValue = new DoubleValue(Math.Exp(Param0Value.Double));
                    }
                    else if (Name == "_System_Math_Pow2")
                    {
                        Param0Value = new DoubleValue(Math.Pow(2, Param0Value.Double));
                    }
                    else if (Name == "_System_Math_Sin")
                    {
                        RetValue = new DoubleValue(Math.Sin(Param0Value.Double));
                    }
                    else if (Name == "_System_Math_Cos")
                    {
                        RetValue = new DoubleValue(Math.Cos(Param0Value.Double));
                    }
                    else if (Name == "_System_Math_Tan")
                    {
                        RetValue = new DoubleValue(Math.Tan(Param0Value.Double));
                    }
                    else if (Name == "_System_Math_Asin")
                    {
                        RetValue = new DoubleValue(Math.Asin(Param0Value.Double));
                    }
                    else if (Name == "_System_Math_Acos")
                    {
                        RetValue = new DoubleValue(Math.Acos(Param0Value.Double));
                    }
                    else if (Name == "_System_Math_Atan")
                    {
                        RetValue = new DoubleValue(Math.Atan(Param0Value.Double));
                    }
                    else if (Name == "_System_Math_Sinh")
                    {
                        RetValue = new DoubleValue(Math.Sinh(Param0Value.Double));
                    }
                    else if (Name == "_System_Math_Cosh")
                    {
                        RetValue = new DoubleValue(Math.Cosh(Param0Value.Double));
                    }
                    else if (Name == "_System_Math_Tanh")
                    {
                        RetValue = new DoubleValue(Math.Tanh(Param0Value.Double));
                    }
                    else if (Name == "_System_Math_Asinh")
                    {
                        var X = Param0Value.Double;
                        RetValue = new DoubleValue(Math.Log(X + Math.Sqrt(X * X + 1)));
                    }
                    else if (Name == "_System_Math_Acosh")
                    {
                        var X = Param0Value.Double;
                        RetValue = new DoubleValue(Math.Log(X + Math.Sqrt(X * X - 1)));
                    }
                    else if (Name == "_System_Math_Atanh")
                    {
                        var X = Param0Value.Double;
                        RetValue = new DoubleValue(0.5d * Math.Log((1 + X) / (1 - X)));
                    }
                }
                else if (Ch.Length == 3)
                {
                    var Param0Node = Ch[1] as ConstExpressionNode;
                    var Param0RealId = Param0Node.Type.RealId;
                    var Param0Value = Param0Node.Value;

                    var Param1Node = Ch[2] as ConstExpressionNode;
                    var Param1RealId = Param1Node.Type.RealId;
                    var Param1Value = Param1Node.Value;

                    if (!(Param0RealId is NumberType && Param1RealId is NumberType))
                        return PluginResult.Succeeded;

                    if (Name == "_System_Math_Pow")
                    {
                        var X = Param0Value.Double;
                        var Y = Param1Value.Double;
                        RetValue = new DoubleValue(Math.Pow(X, Y));
                    }
                    else if (Name == "_System_Math_Log")
                    {
                        var X = Param0Value.Double;
                        var Y = Param1Value.Double;
                        RetValue = new DoubleValue(Math.Log(X, Y));
                    }
                    else if (Name == "_System_Math_Atan2")
                    {
                        var X = Param0Value.Double;
                        var Y = Param1Value.Double;
                        RetValue = new DoubleValue(Math.Atan2(X, Y));
                    }
                }
            }

            if (RetValue != null)
            {
                Node = RetValue.ToExpression(Parent, RetType, Node.Code);
                return Node == null ? PluginResult.Failed : PluginResult.Ready;
            }
        }

        return PluginResult.Succeeded;
    }

    public override PluginResult NewNode(ref ExpressionNode Node)
    {
        if (Node is ConstExpressionNode)
        {
            var CNode = Node as ConstExpressionNode;
            if (CNode.Value is ZeroValue)
            {
                var T = CNode.Type.RealId as Type;
                if (T is NonFloatType) CNode.Value = new IntegerValue(0);
                else if (T is FloatType && T.Size == 4) CNode.Value = new FloatValue(0);
                else if (T is FloatType && T.Size == 8) CNode.Value = new DoubleValue(0);
                else if (T is BooleanType) CNode.Value = new BooleanValue(false);
                else if (T is CharType) CNode.Value = new CharValue('\0');
                else if (T is NonFloatType) CNode.Value = new IntegerValue(0);
                else if (T is PointerType) CNode.Value = new NullValue();
                else if ((T.TypeFlags & TypeFlags.ReferenceValue) != 0)
                    CNode.Value = new NullValue();
            }
        }

        else if (Node is OpExpressionNode)
        {
            return NewOpNode(ref Node);
        }

        return base.NewNode(ref Node);
    }

    public PluginResult NewOpNode(ref ExpressionNode Node)
    {
        var OpNode = Node as OpExpressionNode;
        var Op = OpNode.Operator;
        var Ch = OpNode.Children;

        // ------------------------------------------------------------------------------------
        if (Op == Operator.Reinterpret)
        {
            if (Ch[0] is ConstExpressionNode)
            {
#warning WARNING
            }

            return PluginResult.Succeeded;
        }

        // ------------------------------------------------------------------------------------

        if (Op == Operator.Tuple || Op == Operator.Array)
        {
            var Type = Node.Type.RealId as NonrefArrayType;
            if (Op != Operator.Array || (Type != null && Type.Lengths != null && !(Type.TypeOfValues is AutomaticType)))
                if (Ch.TrueForAll(x => x is ConstExpressionNode))
                {
                    var Constants = Ch.Select(x => (x as ConstExpressionNode).Value);
                    var SValue = new StructuredValue(Constants.ToList());
                    Node = Parent.NewNode(new ConstExpressionNode(Node.Type, SValue, Node.Code));
                    return Node == null ? PluginResult.Failed : PluginResult.Ready;
                }
        }

        // ------------------------------------------------------------------------------------
        else if (Op == Operator.Index)
        {
            if (Ch.TrueForAll(x => x is ConstExpressionNode))
            {
                var ConstCh0 = Ch[0] as ConstExpressionNode;
                var NonrefArray = Ch[0].Type.RealId as NonrefArrayType;
                var Dimensions = NonrefArray.Lengths;
                var Position = 0;
                var MulBy = 1;

                for (var i = Dimensions.Length - 1; i >= 0; i--)
                {
                    var CChip1 = Ch[i + 1] as ConstExpressionNode;
                    var Index = CChip1.Integer;

                    if (Index < 0 || Index >= Dimensions[i])
                    {
                        State.Messages.Add(MessageId.IndexOutOfRange, CChip1.Code);
                        return PluginResult.Failed;
                    }

                    Position += (int)Index * MulBy;
                    MulBy *= Dimensions[i];
                }

                var RetValue = ConstCh0.Value.GetMember(Position);
                var RetType = NonrefArray.TypeOfValues;

                Node = Parent.NewNode(new ConstExpressionNode(RetType, RetValue, Node.Code));
                return Node == null ? PluginResult.Failed : PluginResult.Ready;
            }
        }

        // ------------------------------------------------------------------------------------
        else if (Op == Operator.Member)
        {
            var ConstCh0 = Ch[0] as ConstExpressionNode;
            var Member = Ch[1] as IdExpressionNode;
            if (ConstCh0 != null && Member != null)
                if (ConstCh0.Value is StructuredValue || ConstCh0.Value is ZeroValue)
                {
                    var SType = ConstCh0.Type.UnderlyingStructureOrRealId as StructuredType;
                    var Members = SType.StructuredScope.IdentifierList;
                    var MemberIndex = Members.IndexOf(Member.Identifier);
                    var RetValue = ConstCh0.Value.GetMember(MemberIndex);
                    var RetType = Member.Identifier.TypeOfSelf;

                    Node = Parent.NewNode(new ConstExpressionNode(RetType, RetValue, Node.Code));
                    return Node == null ? PluginResult.Failed : PluginResult.Ready;
                }
        }

        // ------------------------------------------------------------------------------------
        else if (Op == Operator.Cast)
        {
            var Child = Ch[0] as ConstExpressionNode;
            if (Child == null) return PluginResult.Succeeded;

            var To = Expressions.GetIdentifier(Ch[1]);
            var RTo = To.RealId as Type;
            var RFrom = Child.Type.RealId as Type;

            if (Child.Value is NullValue || RFrom is AutomaticType)
            {
                var OldNode = Node;
                if ((Node = Node.DetachChild(0)) == null)
                    return PluginResult.Failed;

                Node.Type = OldNode.Type;
                Node.Flags |= ExpressionFlags.FixedType;
                return PluginResult.Ready;
            }

            Predicate<Identifier> Func = x =>
            {
                var Rx = x.RealId as Type;
                if (Rx is PointerType || Rx is ReferenceType || Rx is PointerAndLength ||
                    ((Rx.TypeFlags & TypeFlags.ReferenceValue) != 0 && !(Rx is StringType)))
                    return false;

                return true;
            };

            if (!Identifiers.ProcessTuple(RTo, Func))
                return PluginResult.Succeeded;

            if (RTo is TupleType && !(RFrom is TupleType))
            {
                var TupleTo = RTo as TupleType;
                var ToMembers = TupleTo.StructuredScope.IdentifierList;
                var TupleCh = new ExpressionNode[ToMembers.Count];

                for (var i = 0; i < ToMembers.Count; i++)
                {
                    var ToType = ToMembers[i].Children[0];
                    var Value = Child.Value.Convert(ToType);
                    if (Value == null)
                    {
                        var Params = new[] { Child.Value.ToString(), ToType.Name.ToString() };
                        State.Messages.Add(MessageId.CannotConvertConst, Node.Code, Params);
                        return PluginResult.Failed;
                    }

                    TupleCh[i] = Value.ToExpression(Parent, ToType, Node.Code);
                    if (TupleCh[i] == null) return PluginResult.Failed;
                }

                Node = new OpExpressionNode(Operator.Tuple, TupleCh, Node.Code);
                Node.Type = To;
                Node = Parent.NewNode(Node);
                return Node == null ? PluginResult.Failed : PluginResult.Ready;
            }

            var Ret = Child.Value.Convert(RTo);
            if (Ret == null)
            {
                var Params = new[] { Child.Value.ToString(), To.Name.ToString() };
                State.Messages.Add(MessageId.CannotConvertConst, Node.Code, Params);
                return PluginResult.Failed;
            }

            if (Ret is IntegerValue && Node.CheckingMode == CheckingMode.Unchecked)
            {
                var IRet = Ret as IntegerValue;
                IRet.Value = DataStoring.WrapToType(IRet.Value, RTo);
            }

            Node = Ret.ToExpression(Parent, To, Node.Code);
            return Node == null ? PluginResult.Failed : PluginResult.Ready;
        }

        // ------------------------------------------------------------------------------------
        else if (Op == Operator.And || Op == Operator.Or)
        {
            var ConstIndex = Ch[0] is ConstExpressionNode ? 0 :
                Ch[1] is ConstExpressionNode ? 1 : -1;

            if (ConstIndex != -1)
            {
                var CNode = Ch[ConstIndex] as ConstExpressionNode;
                var IsAnd = Op == Operator.And;

                if (CNode.Bool == IsAnd)
                {
                    Node = Node.DetachChild(1 - ConstIndex);
                    return Node == null ? PluginResult.Failed : PluginResult.Ready;
                }

                Node = Parent.NewNode(new ConstExpressionNode(Node.Type, new BooleanValue(!IsAnd), Node.Code));
                return Node == null ? PluginResult.Failed : PluginResult.Ready;
            }
        }

        // ------------------------------------------------------------------------------------
        else if (Op == Operator.Condition)
        {
            var Condition = Ch[0] as ConstExpressionNode;
            if (Condition != null)
            {
                Node = Condition.Bool ? Ch[1] : Ch[2];
                return PluginResult.Ready;
            }
        }

        // ------------------------------------------------------------------------------------
        else if (Op == Operator.Call)
        {
            var Res = NewCallOpNode(ref Node);
            if (Res != PluginResult.Succeeded) return Res;
        }

        // ------------------------------------------------------------------------------------
        else if (Operators.IsCalculable(Op))
        {
            if (Ch[0] is ConstExpressionNode && (Ch.Length == 1 || Ch[1] is ConstExpressionNode))
                return Evaluate(ref Node);

            if (Operators.IsReversible(Op) && Ch[0] is ConstExpressionNode &&
                Identifiers.IsScalarOrVectorNumber(Ch[0].Type) && !(Ch[1] is ConstExpressionNode))
            {
                OpNode.Swap();
                Op = OpNode.Operator;
            }

            if (Op == Operator.Add || Op == Operator.Subract)
            {
                var ConstCh1 = Ch[1] as ConstExpressionNode;
                if (ConstCh1 != null && Identifiers.IsScalarOrVectorNumber(ConstCh1.Type))
                {
                    if (Constants.CompareTupleValues(ConstCh1.Value, x => x.Double == 0))
                    {
                        Node = Node.DetachChild(0);
                        return Node == null ? PluginResult.Failed : PluginResult.Ready;
                    }

                    var OpCh0 = Ch[0] as OpExpressionNode;
                    if (OpCh0 != null)
                    {
                        var Ch0Op = OpCh0.Operator;
                        var Ch0Ch = OpCh0.Children;
                        if (Ch0Op == Operator.Add || Ch0Op == Operator.Subract)
                        {
                            var ConstIndex = -1;
                            if (Ch0Ch[0] is ConstExpressionNode) ConstIndex = 0;
                            else if (Ch0Ch[1] is ConstExpressionNode) ConstIndex = 1;

                            if (ConstIndex != -1)
                            {
                                var ConstCh0Chx = Ch0Ch[ConstIndex] as ConstExpressionNode;
                                var NewOp = Op == Ch0Op ? Operator.Add : Operator.Subract;

                                ExpressionNode[] NewCh; // For strings
                                if (NewOp == Operator.Add) NewCh = new[] { ConstCh0Chx, Ch[1] };
                                else NewCh = new[] { Ch[1], ConstCh0Chx };

                                Ch[0] = OpCh0.Children[1 - ConstIndex];
                                Ch[1] = Parent.NewNode(new OpExpressionNode(NewOp, NewCh, Node.Code));
                                if (Ch[1] == null) return PluginResult.Failed;
                            }
                        }
                    }
                }
            }
            else if (Op == Operator.Multiply || Op == Operator.Divide)
            {
                var ConstCh1 = Ch[1] as ConstExpressionNode;
                if (ConstCh1 != null && Identifiers.IsScalarOrVectorNumber(ConstCh1.Type))
                {
                    if (Constants.CompareTupleValues(ConstCh1.Value, x => x.Double == 1))
                    {
                        Node = Node.DetachChild(0);
                        return Node == null ? PluginResult.Failed : PluginResult.Ready;
                    }

                    if (Constants.CompareTupleValues(ConstCh1.Value, x => x.Double == 0) && Op == Operator.Multiply)
                    {
                        Node = Node.DetachChild(1);
                        return Node == null ? PluginResult.Failed : PluginResult.Ready;
                    }

                    var OpCh0 = Ch[0] as OpExpressionNode;
                    if (OpCh0 != null)
                    {
                        var Ch0Op = OpCh0.Operator;
                        var Ch0Ch = OpCh0.Children;
                        if (Ch0Op == Operator.Multiply || Ch0Op == Operator.Divide)
                        {
                            var ConstIndex = -1;
                            if (Ch0Ch[0] is ConstExpressionNode) ConstIndex = 0;
                            else if (Ch0Ch[1] is ConstExpressionNode) ConstIndex = 1;

                            if (ConstIndex != -1)
                            {
                                var ConstCh0Chx = Ch0Ch[ConstIndex] as ConstExpressionNode;
                                var NewOp = Op == Ch0Op ? Operator.Multiply : Operator.Divide;
                                var NewCh = new[] { Ch[1], ConstCh0Chx };

                                Ch[0] = OpCh0.Children[1 - ConstIndex];
                                Ch[1] = Parent.NewNode(new OpExpressionNode(NewOp, NewCh, Node.Code));
                                if (Ch[1] == null) return PluginResult.Failed;
                            }
                        }
                    }
                }
            }
            else if (Operators.IsRelEquality(Op))
            {
                if (Ch[0] is ConstExpressionNode && !(Ch[1] is ConstExpressionNode))
                {
                    OpNode.Swap();
                    Op = OpNode.Operator;
                }

                var ConstCh1 = Ch[1] as ConstExpressionNode;
                var Type = Ch[0].Type != null ? Ch[0].Type.RealId : null;
                if (Type is UnsignedType && ConstCh1 != null && ConstCh1.Integer == 0)
                {
                    if (Op == Operator.GreaterEqual)
                    {
                        Node = Constants.GetBoolValue(Container, true, Node.Code);
                        return Node == null ? PluginResult.Failed : PluginResult.Ready;
                    }

                    if (Op == Operator.Greater)
                    {
                        OpNode.Operator = Op = Operator.Inequality;
                    }
                    else if (Op == Operator.Less)
                    {
                        Node = Constants.GetBoolValue(Container, false, Node.Code);
                        return Node == null ? PluginResult.Failed : PluginResult.Ready;
                    }
                    else if (Op == Operator.LessEqual)
                    {
                        OpNode.Operator = Op = Operator.Equality;
                    }
                }
            }

            if (Op == Operator.Subract && Ch[1] is ConstExpressionNode)
            {
                var ConstCh1 = Ch[1] as ConstExpressionNode;
                if (ConstCh1 != null && ConstCh1.Type.RealId is NumberType && ConstCh1.CDouble < 0.0)
                {
                    var NewCh = new ExpressionNode[] { ConstCh1 };
                    Ch[1] = Parent.NewNode(new OpExpressionNode(Operator.Negation, NewCh, Node.Code));
                    if (Ch[1] == null) return PluginResult.Failed;

                    OpNode.Operator = Op = Operator.Add;
                }
            }
        }

        return PluginResult.Succeeded;
    }

    private PluginResult Evaluate(ref ExpressionNode Node)
    {
        var OpNode = Node as OpExpressionNode;
        var Op = OpNode.Operator;
        var Ch = OpNode.Children;

        var Dst = Ch[0] as ConstExpressionNode;
        var Src = Ch.Length > 1 ? Ch[1] as ConstExpressionNode : null;

        var AllLinkedNodes = new AutoAllocatedList<LinkedExprNode>();
        Node.GetLinkedNodes(ref AllLinkedNodes, true);

        if (Dst.Value is NullValue || (Src != null && Src.Value is NullValue))
        {
            if (Dst.Type.UnderlyingClassOrRealId is ClassType)
                if (Op == Operator.Equality || Op == Operator.Inequality)
                {
                    var Value = Dst.Value is NullValue == Src.Value is NullValue;
                    var Ret = Constants.GetBoolValue(Container, Value, Node.Code);
                    Ret.LinkedNodes.AddRange(AllLinkedNodes);

                    Node = Parent.NewNode(Ret);
                    return Node == null ? PluginResult.Failed : PluginResult.Ready;
                }

            if (Dst.Type.RealId is StringType)
                if (Op == Operator.Add)
                {
                    var DstVal = Dst.Value is NullValue ? "" : Dst.String;
                    var SrcVal = Src.Value is NullValue ? "" : Src.String;
                    var Value = DstVal + SrcVal;

                    var Ret = Constants.GetStringValue(Container, Value, Node.Code);
                    Ret.LinkedNodes.AddRange(AllLinkedNodes);

                    Node = Parent.NewNode(Ret);
                    return Node == null ? PluginResult.Failed : PluginResult.Ready;
                }

            throw new ApplicationException();
        }

        {
            var DstType = Dst.Type;
            var SrcType = Src != null ? Src.Type : null;
            var SrcVal = Src != null ? Src.Value : null;

            var Ret = Dst.Value.DoOperation(SrcVal, Op, Node.Type);
            if (Ret is IntegerValue && Node.CheckingMode == CheckingMode.Unchecked)
            {
                var IRet = Ret as IntegerValue;
                IRet.Value = DataStoring.WrapToType(IRet.Value, Node.Type);
            }

            Node = Ret.ToExpression(State, Node.Type, Node.Code);
            if (Node == null) return PluginResult.Failed;

            Node.LinkedNodes.AddRange(AllLinkedNodes);
            Node = Parent.NewNode(Node);
            return Node == null ? PluginResult.Failed : PluginResult.Ready;
        }
    }
}