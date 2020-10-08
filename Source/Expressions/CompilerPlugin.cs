using System;
using System.Collections.Generic;
using System.Linq;
using Zinnia.Recognizers;

namespace Zinnia
{
    public class CompilerPlugin : ExpressionPlugin, IPluginDeclarationHandler
    {
        public AutoAllocatedList<Identifier> DeclaredIds;
        public NodeVariables Vars;
        public bool MustGetValue = true;

        public CompilerPlugin(PluginRoot Parent)
            : base(Parent)
        {
        }

        public override bool Begin()
        {
            if (!base.Begin()) return false;

            Vars = new NodeVariables();
            return true;
        }

        public bool AllPathGetsValue(Identifier Var, ExpressionNode Node)
        {
            for (var i = 0; i < Node.LinkedNodes.Count; i++)
            {
                var Linked = Node.LinkedNodes[i].Node;
                if (AllPathGetsValue(Var, Linked))
                    return true;
            }

            var Ch = Node.Children;
            if (Node is OpExpressionNode)
            {
                var OpNode = Node as OpExpressionNode;
                var Op = OpNode.Operator;

                if (Op == Operator.Assignment)
                {
                    var RetValue = false;
                    Expressions.ProcessTuple(Ch[0], x =>
                    {
                        var Id = Expressions.GetIdentifier(x);
                        if (Id != null && Id.RealId == Var.RealId)
                            RetValue = true;
                    });

                    if (RetValue)
                        return true;
                }
                else if (Op == Operator.Condition)
                {
                    return AllPathGetsValue(Var, Ch[0]) ||
                        (AllPathGetsValue(Var, Ch[1]) && AllPathGetsValue(Var, Ch[2]));
                }
                else if (Op == Operator.And || Op == Operator.Or)
                {
                    return Ch.TrueForAll(x => AllPathGetsValue(Var, x));
                }
                else if (Op == Operator.Call || Op == Operator.NewObject)
                {
                    var FuncType = Ch[0].Type.RealId as TypeOfFunction;
                    for (var i = 1; i < Ch.Length; i++)
                    {
                        var RefType = FuncType.Children[i].TypeOfSelf.RealId as ReferenceType;
                        if (RefType == null || RefType.Mode != ReferenceMode.IdGetsAssigned) continue;

                        var Id = Expressions.GetIdentifier(Ch[i].Children[0]);
                        if (Id != null && Id.RealId == Var.RealId) return true;
                    }
                }
                else if (Op == Operator.Address || Op == Operator.Reference_Unsafe || Op == Operator.Reference_IdGetsAssigned)
                {
                    var Id = Expressions.GetIdentifier(Ch[0]);
                    if (Id != null && Id.RealId == Var.RealId) return true;
                }
                else if (Op == Operator.Cast)
                {
                    var To = Expressions.GetIdentifier(Ch[1]);
                    if (To.RealId is PointerType && Ch[0].Type.RealId is ArrayType)
                    {
                        var Id = Expressions.GetIdentifier(Ch[0]);
                        if (Id != null && Id.RealId == Var.RealId) return true;
                    }
                }

                return !Ch.TrueForAll(x => !AllPathGetsValue(Var, x));
            }

            return false;
        }

        ExpressionNode Extract(ExpressionNode Node, ref bool Extracted)
        {
            Node = ExtractPackedId(Node, ref Extracted);
            if (Node == null) return null;

            return ExtractPropertyGetter(Node, ref Extracted);
        }

        ExpressionNode Extract(ExpressionNode Node)
        {
            var Extracted = false;
            return Extract(Node, ref Extracted);
        }

        public override PluginResult End(ref ExpressionNode Node)
        {
            SetIdNodeUsed(Node);
            Node = Extract(Node);
            if (Node == null) return PluginResult.Failed;

            if (!CheckUsedIdNodes(Node))
                return PluginResult.Failed;

            var RetValue = true;
            for (var i = 0; i < DeclaredIds.Count; i++)
            {
                var e = DeclaredIds[i];
                if (MustGetValue && !AllPathGetsValue(e, Node))
                {
                    State.Messages.Add(MessageId.UnassignedVar, e.Name);
                    RetValue = false;
                    continue;
                }

                e.Container.DeclareIdentifier(e);
            }

            DeclaredIds = null;
            if (!RetValue) return PluginResult.Failed;

            var OldVars = Node.Data.Get<NodeVariables>();
            if (OldVars == null) Node.Data.Set(Vars);
            else OldVars.UnionInPlace(Vars);

            return PluginResult.Succeeded;
        }

        private void SetIdNodeUsed(ExpressionNode Ch)
        {
            var IdCh = Ch as IdExpressionNode;
            if (IdCh != null) Vars.UsedBeforeAssignIds.Add(IdCh);
        }

        bool CheckUsedIdNodes(ExpressionNode Node)
        {
            return Expressions.ProcessTuple(Node, x =>
            {
                if ((x.Flags & ExpressionFlags.IdMustBeAssigned) != 0)
                {
                    State.Messages.Add(MessageId.MustHaveInitVal, x.Code);
                    return false;
                }

                return true;
            });
        }

        void MarkIdNodesAssigned(ExpressionNode Node)
        {
            Expressions.ProcessTuple(Node, x =>
            {
                if (x is IdExpressionNode)
                    x.Flags |= ExpressionFlags.IdMustBeAssigned;
            });
        }

        public bool CheckChildrenIdNodes(ExpressionNode Node)
        {
            for (var i = 0; i < Node.LinkedNodes.Count; i++)
            {
                if (!CheckUsedIdNodes(Node.LinkedNodes[i].Node))
                    return false;
            }

            if (Node is OpExpressionNode)
            {
                var OpNode = Node as OpExpressionNode;
                if (OpNode.Operator != Operator.Tuple)
                {
                    var Ch = OpNode.Children;
                    if (OpNode.Operator == Operator.Assignment)
                    {
                        MarkIdNodesAssigned(Ch[0]);
                        if (!CheckUsedIdNodes(Ch[1])) return false;
                    }
                    else if (Ch != null)
                    {
                        for (var i = 0; i < Ch.Length; i++)
                        {
                            if (!CheckUsedIdNodes(Ch[i]))
                                return false;
                        }
                    }
                }
            }

            return true;
        }

        public ExpressionNode ExtractPackedId(ExpressionNode Node, ref bool IdExtracted)
        {
            var IdNode = Node as IdExpressionNode;
            if (IdNode != null)
            {
                var Id = IdNode.Identifier as PackedId;
                if (Id != null)
                {
                    IdExtracted = true;
                    return Id.Extract(Parent);
                }
            }

            return Node;
        }

        SimpleRecResult ExtractPropertyFunction_OnlyId(ref ExpressionNode Node, bool Setter)
        {
            var IdNode = Node as IdExpressionNode;
            if (IdNode != null && IdNode.Identifier.RealId is Property)
            {
                var Property = IdNode.Identifier.RealId as Property;
                var PropScope = Property.PropertyScope;

                Function Func;
                if (Setter) Func = PropScope.SetterIndex == -1 ? null : PropScope.Setter;
                else Func = PropScope.GetterIndex == -1 ? null : PropScope.Getter;

                if (Func == null)
                {
                    if (Setter) State.Messages.Add(MessageId.NoPropertySetter, Node.Code);
                    else State.Messages.Add(MessageId.NoPropertyGetter, Node.Code);
                    return SimpleRecResult.Failed;
                }

                if (!Identifiers.VerifyAccess(Container, Func, Node.Code, false))
                {
                    if (Setter) State.Messages.Add(MessageId.UnaccessableSetter, Node.Code);
                    else State.Messages.Add(MessageId.UnaccessableGetter, Node.Code);
                    return SimpleRecResult.Failed;
                }

                Node = Parent.NewNode(new IdExpressionNode(Func, Node.Code));
                return Node == null ? SimpleRecResult.Failed : SimpleRecResult.Succeeded;
            }

            return SimpleRecResult.Unknown;
        }

        public SimpleRecResult ExtractPropertyFunction(ref ExpressionNode Node, bool IsSetter)
        {
            var Res = ExtractPropertyFunction_OnlyId(ref Node, IsSetter);
            if (Res != SimpleRecResult.Unknown) return Res;

            if (Expressions.GetOperator(Node) == Operator.Member)
            {
                var Ch = Node.Children;
                var NewCh1 = Ch[1];
                Res = ExtractPropertyFunction_OnlyId(ref NewCh1, IsSetter);
                if (Res != SimpleRecResult.Succeeded) return Res;

                var NewCh = new ExpressionNode[] { Ch[0], NewCh1 };
                var NewNode = new OpExpressionNode(Operator.Member, NewCh, Node.Code);
                NewNode.LinkedNodes.AddRange(Node.LinkedNodes);
                Node = Parent.NewNode(NewNode);
                return Node == null ? SimpleRecResult.Failed : SimpleRecResult.Succeeded;
            }

            return SimpleRecResult.Unknown;
        }

        public SimpleRecResult ExtractPropertySetter(ExpressionNode Dst, ExpressionNode Value,
            ref ExpressionNode Call, ref bool Extracted)
        {
            return ExtractPropertySetter(Dst, () => Value, ref Call, ref Extracted);
        }

        public SimpleRecResult ExtractPropertySetter(ExpressionNode Dst, Func<ExpressionNode> Value,
            ref ExpressionNode Call, ref bool Extracted)
        {
            var OldNode = Call;
            ExpressionNode[] CallCh;

            if (Expressions.GetOperator(Dst) == Operator.Index)
            {
                var NewNode = Dst.Children[0];
                var Res = ExtractPropertyFunction(ref NewNode, true);
                if (Res != SimpleRecResult.Succeeded) return Res;

                CallCh = new ExpressionNode[Dst.Children.Length + 1];
                for (var i = 1; i < Dst.Children.Length; i++)
                    CallCh[i] = Dst.Children[i];

                CallCh[0] = NewNode;
                CallCh[Dst.Children.Length] = Value();
            }
            else
            {
                var NewNode = Dst;
                var Res = ExtractPropertyFunction(ref NewNode, true);
                if (Res != SimpleRecResult.Succeeded) return Res;

                CallCh = new ExpressionNode[] { NewNode, Value() };
            }

            Call = Parent.NewNode(new OpExpressionNode(Operator.Call, CallCh, Dst.Code));
            if (Call == null) return SimpleRecResult.Failed;

            Extracted = true;
            Call.Type = Dst.Type;
            Call.Flags |= ExpressionFlags.EnableGetter;
            Call.LinkedNodes.AddRange(OldNode.LinkedNodes);
            return SimpleRecResult.Succeeded;
        }

        ExpressionNode GetGetterForSetter(ExpressionNode Node)
        {
            var IdNode = Node as IdExpressionNode;
            var Id = IdNode.Identifier.RealId as Function;
            if (Id == null) throw new ApplicationException();

            var PScope = Id.Container as PropertyScope;
            return Parent.NewNode(new IdExpressionNode(PScope.Getter, Node.Code));
        }

        public ExpressionNode ExtractPropertyGetter(ExpressionNode Node, ref bool Extracted)
        {
            ExpressionNode[] CallCh;
            AutoAllocatedList<LinkedExprNode> LinkedNodes =
                new AutoAllocatedList<LinkedExprNode>();

            if ((Node.Flags & ExpressionFlags.EnableGetter) != 0)
            {
                var Ch = Node.Children;
                CallCh = new ExpressionNode[Ch.Length - 1];

                if (Expressions.GetOperator(Ch[0]) == Operator.Member)
                {
                    var Ch0Ch = Ch[0].Children;
                    var Linked = new LinkedExprNode(Ch0Ch[0]);
                    LinkedNodes.Add(Linked);

                    Ch0Ch[0] = Parent.NewNode(new LinkingNode(Linked, Node.Code));
                    if (Ch0Ch[0] == null || (Ch[0] = Parent.NewNode(Ch[0])) == null)
                        return null;

                    var CallCh0Ch = new ExpressionNode[]
                    {
                        Parent.NewNode(new LinkingNode(Linked, Node.Code)),
                        CallCh[0] = GetGetterForSetter(Ch0Ch[1]),
                    };

                    if (CallCh0Ch[0] == null || CallCh0Ch[1] == null) return null;
                    CallCh[0] = Parent.NewNode(new OpExpressionNode(Operator.Member, CallCh0Ch, Node.Code));
                    if (CallCh[0] == null) return null;
                }
                else if (Ch[0] is IdExpressionNode)
                {
                    CallCh[0] = GetGetterForSetter(Ch[0]);
                }

                for (var i = 1; i < Ch.Length - 1; i++)
                {
                    var LinkedNode = new LinkedExprNode(Ch[i]);
                    LinkedNodes.Add(LinkedNode);

                    Node.Children[i] = Parent.NewNode(new LinkingNode(LinkedNode, Node.Code));
                    CallCh[i] = Parent.NewNode(new LinkingNode(LinkedNode, Node.Code));
                    if (Node.Children[i] == null || CallCh[i] == null) return null;
                }

                Node = Parent.NewNode(Node);
                if (Node == null) return null;

                Node.Flags &= ~ExpressionFlags.EnableGetter;
                LinkedNodes.Add(new LinkedExprNode(Node, LinkedNodeFlags.NotRemovable));
            }
            else if (Expressions.GetOperator(Node) == Operator.Index)
            {
                var NewNode = Node.Children[0];
                var Res = ExtractPropertyFunction(ref NewNode, false);
                if (Res == SimpleRecResult.Unknown) return Node;
                if (Res == SimpleRecResult.Failed) return null;

                CallCh = Node.Children.Slice(0);
                CallCh[0] = NewNode;
            }
            else
            {
                var NewNode = Node;
                var Res = ExtractPropertyFunction(ref NewNode, false);
                if (Res == SimpleRecResult.Unknown) return Node;
                if (Res == SimpleRecResult.Failed) return null;

                CallCh = new ExpressionNode[] { NewNode };
            }

            var Ret = new OpExpressionNode(Operator.Call, CallCh, Node.Code);
            Ret.LinkedNodes.AddRange(LinkedNodes);
            Extracted = true;
            return Parent.NewNode(Ret);
        }

        private bool IsClassMemberNode(ExpressionNode Node)
        {
            if (Expressions.GetOperator(Node) != Operator.Member) return false;
            return Node.Children[0].Type.RealId is ClassType;
        }

        private bool ProcessLinkedAssignmentMember(CodeString Code, ref LinkedExprNode LinkedNode,
            out ExpressionNode Dst, out ExpressionNode Src)
        {
            Dst = null;
            Src = null;

            var MemberId = Expressions.GetMemberIdentifier(LinkedNode.Node);
            if (MemberId.RealId is Property)
            {
                if (LinkedNode.Node is IdExpressionNode)
                {
                    Dst = Parent.NewNode(new IdExpressionNode(MemberId, Code));
                    Src = Parent.NewNode(new IdExpressionNode(MemberId, Code));
                    return Dst != null && Src != null;
                }
                else
                {
                    var LinkedOpNode = LinkedNode.Node as OpExpressionNode;
                    if (LinkedOpNode.Operator != Operator.Member)
                        throw new ApplicationException();

                    var NewLinkedNode = new LinkedExprNode(LinkedOpNode.Children[0]);
                    var DstCh = new ExpressionNode[]
                    {
                        Parent.NewNode(new LinkingNode(NewLinkedNode, Code)),
                        Parent.NewNode(new IdExpressionNode(MemberId, Code)),
                    };

                    if (DstCh[0] == null || DstCh[1] == null)
                        return false;

                    var SrcCh = new ExpressionNode[]
                    {
                        Parent.NewNode(new LinkingNode(NewLinkedNode, Code)),
                        Parent.NewNode(new IdExpressionNode(MemberId, Code)),
                    };

                    if (SrcCh[0] == null || SrcCh[1] == null)
                        return false;

                    Dst = Parent.NewNode(new OpExpressionNode(Operator.Member, DstCh, Code));
                    Src = Parent.NewNode(new OpExpressionNode(Operator.Member, SrcCh, Code));
                    LinkedNode = NewLinkedNode;
                    return Dst != null && Src != null;
                }
            }
            else if (IsClassMemberNode(LinkedNode.Node))
            {
                LinkedNode.Node = LinkedNode.Node.Children[0];

                Dst = Parent.NewNode(new LinkingNode(LinkedNode, Code));
                Src = Parent.NewNode(new LinkingNode(LinkedNode, Code));
                if (Dst == null || Src == null) return false;

                var MemberIdNode1 = Parent.NewNode(new IdExpressionNode(MemberId, Code));
                var MemberIdNode2 = Parent.NewNode(new IdExpressionNode(MemberId, Code));
                if (MemberIdNode1 == null || MemberIdNode2 == null) return false;

                var DstCh = new ExpressionNode[] { Dst, MemberIdNode1 };
                var SrcCh = new ExpressionNode[] { Src, MemberIdNode2 };

                Dst = Parent.NewNode(new OpExpressionNode(Operator.Member, DstCh, Code));
                Src = Parent.NewNode(new OpExpressionNode(Operator.Member, SrcCh, Code));
                return Dst != null && Src != null;
            }
            else
            {
                LinkedNode.Node = Expressions.GetAddress(Parent, LinkedNode.Node, Code);
                if (LinkedNode.Node == null) return false;

                Dst = Parent.NewNode(new LinkingNode(LinkedNode, Code));
                Src = Parent.NewNode(new LinkingNode(LinkedNode, Code));
                if (Dst == null || Src == null) return false;

                Dst = Expressions.Indirection(Parent, Dst, Code);
                Src = Expressions.Indirection(Parent, Src, Code);
                return Dst != null && Src != null;
            }
        }

        private PluginResult ResolveLinkingAssigment(ref ExpressionNode Node)
        {
            if (Expressions.GetOperator(Node) != Operator.Assignment)
                return PluginResult.Succeeded;

            var Ch = Node.Children;
            var LinkingCh0 = Ch[0] as LinkingNode;
            if (LinkingCh0 == null)
                return PluginResult.Succeeded;

            var Ch1Ch = Ch[1].Children;
            var LCh1Ch0 = Ch1Ch[0] as LinkingNode;
            var LinkedNode = LinkingCh0.LinkedNode;

            if (LCh1Ch0 == null || LCh1Ch0.LinkedNode != LinkedNode ||
                !Node.LinkedNodes.Contains(LinkedNode) || LinkedNode.LinkingCount != 2)
            {
                throw new ApplicationException();
            }

            Node.LinkedNodes.Remove(LinkedNode);
            if (Expressions.GetOperator(LinkedNode.Node) == Operator.Tuple)
            {
                var OpLinked = LinkedNode.Node as OpExpressionNode;
                var LinkedCh = OpLinked.Children;

                var DstCh = new ExpressionNode[LinkedCh.Length];
                var SrcCh = new ExpressionNode[LinkedCh.Length];

                for (var i = 0; i < LinkedCh.Length; i++)
                {
                    ExpressionNode Dst, Src;
                    var Linked = new LinkedExprNode(LinkedCh[i]);
                    if (!ProcessLinkedAssignmentMember(Node.Code, ref Linked, out Dst, out Src))
                        return PluginResult.Failed;

                    DstCh[i] = Dst;
                    SrcCh[i] = Src;
                    Node.LinkedNodes.Add(Linked);
                }

                Ch[0] = Parent.NewNode(new OpExpressionNode(Operator.Tuple, DstCh, Node.Code));
                Ch1Ch[0] = Parent.NewNode(new OpExpressionNode(Operator.Tuple, SrcCh, Node.Code));
                if (Ch[0] == null || Ch1Ch[0] == null) return PluginResult.Failed;
            }
            else
            {
                ExpressionNode Dst, Src;
                if (!ProcessLinkedAssignmentMember(Node.Code, ref LinkedNode, out Dst, out Src))
                    return PluginResult.Failed;

                Ch[0] = Dst;
                Ch1Ch[0] = Src;
                Node.LinkedNodes.Add(LinkedNode);
            }

            Ch[1] = Parent.NewNode(Ch[1]);
            if (Ch[1] == null) return PluginResult.Failed;

            Node = Parent.NewNode(Node);
            return Node == null ? PluginResult.Failed : PluginResult.Ready;
        }

        public override PluginResult NewNode(ref ExpressionNode Node)
        {
            if (!CheckChildrenIdNodes(Node))
                return PluginResult.Failed;

            var TempRes = ResolveLinkingAssigment(ref Node);
            if (TempRes != PluginResult.Succeeded) return TempRes;

            var Extracted = false;
            for (var i = 0; i < Node.LinkedNodes.Count; i++)
            {
                var LNode = Node.LinkedNodes[i];
                SetIdNodeUsed(LNode.Node);

                LNode.Node = Extract(LNode.Node, ref Extracted);
                if (LNode.Node == null) return PluginResult.Failed;
            }

            var DontExtractProperties = false;
            var Ch = Node.Children;

            if (Node is OpExpressionNode)
            {
                var OpNode = Node as OpExpressionNode;
                var Op = OpNode.Operator;

                if (Operators.IsIncDec(Op))
                {
                    var Linked = new LinkedExprNode(Ch[0]);
                    var AddSubCh = new ExpressionNode[2]
                    {
                        Parent.NewNode(new LinkingNode(Linked, Node.Code)),
                        Parent.NewNode(Constants.GetIntValue(Container, 1, Node.Code, true)),
                    };

                    if (AddSubCh[0] == null || AddSubCh[1] == null)
                        return PluginResult.Failed;

                    Operator AddSubOp;
                    if (Op == Operator.Increase) AddSubOp = Operator.Add;
                    else if (Op == Operator.Decrease) AddSubOp = Operator.Subract;
                    else throw new ApplicationException();

                    var AssignmentCh = new ExpressionNode[2]
                    {
                        Parent.NewNode(new LinkingNode(Linked, Node.Code)),
                        Parent.NewNode(new OpExpressionNode(AddSubOp, AddSubCh, Node.Code))
                    };

                    if (AssignmentCh[0] == null || AssignmentCh[1] == null)
                        return PluginResult.Failed;

                    Node = new OpExpressionNode(Operator.Assignment, AssignmentCh, Node.Code);
                    Node.LinkedNodes.Add(Linked);

                    if ((Node = Parent.NewNode(Node)) == null)
                        return PluginResult.Failed;

                    return PluginResult.Ready;
                }
                else if (Op == Operator.Assignment)
                {
                    PluginResult Res;
                    if (Ch[0] is IdExpressionNode && Ch[1] is IdExpressionNode)
                    {
                        var IdCh0 = Ch[0] as IdExpressionNode;
                        var IdCh1 = Ch[1] as IdExpressionNode;

                        if (IdCh0.Identifier.RealId is Variable && IdCh1.Identifier.RealId is Variable)
                        {
                            var Ch0NotUsed = Ch[1].CheckNodes(x =>
                            {
                                var xId = Expressions.GetIdentifier(x);
                                return xId == null ? true : xId.RealId != IdCh0.Identifier.RealId;
                            });

                            if (Ch0NotUsed && DeclaredIds.Contains(IdCh1.Identifier.RealId))
                            {
                                var Ch1 = (ExpressionNode)IdCh1;
                                Res = ExpressionNode.ReplaceNodes(ref Ch1, Parent, (ref ExpressionNode x) =>
                                {
                                    var Idx = x as IdExpressionNode;
                                    if (Idx != null && Idx.Identifier.RealId == IdCh1.Identifier.RealId)
                                    {
                                        Vars.Remove(Idx);

                                        x = new IdExpressionNode(IdCh0.Identifier, Idx.Code);
                                        x.LinkedNodes.AddRange(Idx.LinkedNodes);
                                        return Parent.NewNode(ref x);
                                    }

                                    return PluginResult.Succeeded;
                                });

                                Node = Ch1;
                                DeclaredIds.Remove(IdCh1.Identifier.RealId);
                                return Res == PluginResult.Failed ? Res : PluginResult.Ready;
                            }
                        }
                    }

                    var Ch0 = Ch[0];
                    var Linked = (LinkedExprNode)null;

                    Res = Expressions.ProcessTuple(Parent, ref Ch0, (x, Index) =>
                    {
                        if (x is IdExpressionNode)
                        {
                            var IdNode = x as IdExpressionNode;
                            Vars.AssignedIds.Add(IdNode);
                        }

                        Func<ExpressionNode> Value = () =>
                        {
                            LinkedExprNode LLinked;

                            var Ch1 = Ch[1];
                            var Ret = Expressions.GetTupleMember(Parent, ref Ch1, Index, out LLinked);
                            Ch[1] = Ch1;

                            if (LLinked != null)
                                Linked = LLinked;

                            return Ret;
                        };

                        var LRes = ExtractPropertySetter(x, Value, ref x, ref Extracted);
                        if (LRes == SimpleRecResult.Failed) return null;

                        return x;
                    });

                    if (Res != PluginResult.Succeeded)
                    {
                        Ch0.LinkedNodes.AddRange(Node.LinkedNodes);
                        if (Linked != null) Ch0.LinkedNodes.Add(Linked);
                        if (Res == PluginResult.Ready) Node = Ch0;
                        return Res;
                    }

                    SetIdNodeUsed(Ch[1]);
                }
                else if (Op == Operator.Member)
                {
                    var IdCh1 = Ch[1] as IdExpressionNode;
                    if (IdCh1 != null && IdCh1.Identifier.RealId is Property)
                    {
                        Ch[0] = ExtractPropertyGetter(Ch[0], ref Extracted);
                        if (Ch[0] == null) return PluginResult.Failed;

                        DontExtractProperties = true;
                    }

                    SetIdNodeUsed(Ch);
                }
                else if (Op == Operator.Index)
                {
                    var Id = Expressions.GetMemberIdentifier(Ch[0]);
                    if (Id != null && Id.RealId is Property)
                    {
                        for (var i = 1; i < Ch.Length; i++)
                        {
                            Ch[i] = ExtractPropertyGetter(Ch[i], ref Extracted);
                            if (Ch[i] == null) return PluginResult.Failed;
                        }

                        DontExtractProperties = true;
                    }

                    SetIdNodeUsed(Ch);
                }
                else if (Op == Operator.Address || Operators.IsReference(Op))
                {
                    var IdCh0 = Ch[0] as IdExpressionNode;
                    if (IdCh0 != null)
                    {
                        if (Op == Operator.Reference_IdMustBeAssigned)
                        {
                            if (IdCh0 != null) Vars.UsedBeforeAssignIds.Add(IdCh0);
                        }
                        else if (Op == Operator.Reference_IdGetsAssigned)
                        {
                            if (IdCh0 != null) Vars.AssignedIds.Add(IdCh0);

                            if (!Expressions.IsLValue(Ch[0]))
                            {
                                State.Messages.Add(MessageId.AddressOfRValue, Node.Code);
                                return PluginResult.Failed;
                            }
                        }
                        else
                        {
                            if (IdCh0 != null)
                                Vars.AddressUsed.Add(IdCh0);
                        }
                    }
                }
                else if (Op == Operator.Cast)
                {
                    var From = Ch[0].Type;
                    var To = Expressions.GetIdentifier(Ch[1]);

                    if (To.RealId is PointerType && From.RealId is ArrayType && Ch[0] is IdExpressionNode)
                    {
                        var IdCh0 = Ch[0] as IdExpressionNode;
                        Vars.AddressUsed.Add(IdCh0);
                    }
                    else
                    {
                        SetIdNodeUsed(Ch);
                    }
                }
                else if (Op == Operator.Tuple)
                {
                    DontExtractProperties = true;
                }
                else
                {
                    SetIdNodeUsed(Ch);
                }
            }
            else
            {
                SetIdNodeUsed(Ch);
            }

            if (Ch != null)
            {
                for (var i = 0; i < Ch.Length; i++)
                {
                    Ch[i] = ExtractPackedId(Ch[i], ref Extracted);
                    if (Ch[i] == null) return PluginResult.Failed;

                    if (!DontExtractProperties)
                    {
                        Ch[i] = ExtractPropertyGetter(Ch[i], ref Extracted);
                        if (Ch[i] == null) return PluginResult.Failed;
                    }

                    if (Expressions.GetOperator(Ch[i]) == Operator.Tuple)
                    {
                        var LocalExtracted = false;
                        var ChiCh = Ch[i].Children;
                        for (var j = 0; j < ChiCh.Length; j++)
                        {
                            ChiCh[j] = ExtractPropertyGetter(ChiCh[j], ref LocalExtracted);
                            if (ChiCh[j] == null) return PluginResult.Failed;
                        }

                        if (LocalExtracted)
                        {
                            Ch[i] = Parent.NewNode(Ch[i]);
                            if (Ch[i] == null) return PluginResult.Failed;
                            Extracted = true;
                        }
                    }
                }
            }

            if (Extracted)
            {
                Node = Parent.NewNode(Node);
                return PluginResult.Ready;
            }

            return PluginResult.Succeeded;
        }

        private void SetIdNodeUsed(ExpressionNode[] Ch)
        {
            if (Ch != null)
            {
                for (var i = 0; i < Ch.Length; i++)
                    SetIdNodeUsed(Ch[i]);
            }
        }

        public bool OnIdentifierDeclared(Identifier Id)
        {
            var Loc = Id as LocalVariable;
            if (Loc != null) Loc.PreAssigned = true;

            DeclaredIds.Add(Id);
            return true;
        }
    }
}