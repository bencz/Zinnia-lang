using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Linq;
using Zinnia.Base;

namespace Zinnia
{
    public enum CheckingMode : byte
    {
        Default,
        Unchecked,
        Checked,
    }

    [Flags]
    public enum ExpressionFlags : byte
    {
        None = 0,
        AutoConvert = 1,
        FixedType = 2,

        // IdExpressionNode
        IdMustBeAssigned = 4,

        // OpExpressionNode
        ReverseOperation = 8,
        EnableGetter = 16,
        DisableOpFunc = 32,
        DisableVirtualMember = 64,
    }

    public enum Operator : byte
    {
        Unknown,
        Cast,
        Reinterpret,
        RangeTo,
        RangeUntil,
        ScopeResolution,
        Is,
        As,

        Assignment,
        Increase,
        Decrease,

        Condition,
        Tuple,
        Array,
        NewObject,
        NewArray,
        StackAlloc,

        Reference_Unsafe,
        Reference_IdMustBeAssigned,
        Reference_IdGetsAssigned,

        Call,
        Index,
        Member,

        UnaryPlus,
        Negation,
        Not,
        Address,
        Complement,

        RefEquality,
        RefInequality,

        Less,
        Greater,
        Equality,
        Inequality,
        LessEqual,
        GreaterEqual,

        And,
        Or,

        Add,
        Subract,

        Multiply,
        Divide,
        Modolus,

        ShiftLeft,
        ShiftRight,

        BitwiseAnd,
        BitwiseOr,
        BitwiseXor,
    }

    public struct ZinniaOperatorDefinition
    {
        public string String;
        public int Parameters;
        public Operator Operator;

        public ZinniaOperatorDefinition(string String, int Parameters, Operator Operator)
        {
            this.String = String;
            this.Parameters = Parameters;
            this.Operator = Operator;
        }
    }

    public static class Operators
    {
        public static ZinniaOperatorDefinition[] OpDefinitions;

        static Operators()
        {
            OpDefinitions = new ZinniaOperatorDefinition[]
            {
                new ZinniaOperatorDefinition("+", 1, Operator.UnaryPlus),
                new ZinniaOperatorDefinition("-", 1, Operator.Negation),
                new ZinniaOperatorDefinition("!", 1, Operator.Not),
                new ZinniaOperatorDefinition("~", 1, Operator.Complement),

                new ZinniaOperatorDefinition("+", 2, Operator.Add),
                new ZinniaOperatorDefinition("-", 2, Operator.Subract),
                new ZinniaOperatorDefinition("*", 2, Operator.Multiply),
                new ZinniaOperatorDefinition("/", 2, Operator.Divide),
                new ZinniaOperatorDefinition("%", 2, Operator.Modolus),

                new ZinniaOperatorDefinition("&", 2, Operator.BitwiseAnd),
                new ZinniaOperatorDefinition("|", 2, Operator.BitwiseOr),
                new ZinniaOperatorDefinition("^", 2, Operator.BitwiseXor),
                new ZinniaOperatorDefinition("<<", 2, Operator.ShiftLeft),
                new ZinniaOperatorDefinition(">>", 2, Operator.ShiftRight),

                new ZinniaOperatorDefinition("==", 2, Operator.Equality),
                new ZinniaOperatorDefinition("!=", 2, Operator.Inequality),
                new ZinniaOperatorDefinition("<", 2, Operator.Less),
                new ZinniaOperatorDefinition(">", 2, Operator.Greater),
                new ZinniaOperatorDefinition("<=", 2, Operator.LessEqual),
                new ZinniaOperatorDefinition(">=", 2, Operator.GreaterEqual),

                new ZinniaOperatorDefinition("++", 1, Operator.Increase),
                new ZinniaOperatorDefinition("--", 1, Operator.Decrease),
            };
        }

        public static Operator GetOperator(string OpStr, int Params)
        {
            for (var i = 0; i < OpDefinitions.Length; i++)
            {
                if (OpDefinitions[i].Parameters == Params && OpDefinitions[i].String == OpStr)
                    return OpDefinitions[i].Operator;
            }

            return Operator.Unknown;
        }

        public static Operator GetOperator(CompilerState State, CodeString OpStr, int Params)
        {
            var Op = Operators.GetOperator(OpStr.ToString(), Params);
            if (Op == Operator.Unknown)
                State.Messages.Add(MessageId.UnknownOpFunc, OpStr);

            return Op;
        }

        public static bool IsReference(Operator Op)
        {
            return Op == Operator.Reference_Unsafe || Op == Operator.Reference_IdGetsAssigned ||
                Op == Operator.Reference_IdMustBeAssigned;
        }

        public static bool IsNewOp(Operator Op)
        {
            return Op == Operator.NewArray || Op == Operator.NewObject;
        }

        public static bool IsRefEquality(Operator Op)
        {
            return Op == Operator.RefEquality || Op == Operator.RefInequality;
        }

        public static bool IsIncDec(Operator Op)
        {
            return Op == Operator.Increase || Op == Operator.Decrease;
        }

        public static bool CanBeOpFunction(Operator Op)
        {
            return IsBoolRetBitArithmShift(Op) || IsIncDec(Op) || Op == Operator.Negation ||
                Op == Operator.UnaryPlus || Op == Operator.Not || Op == Operator.Complement;
        }

        public static bool IsReversible(Operator Op)
        {
            return Op == Operator.Add || Op == Operator.Multiply || IsBitwise(Op) || IsBoolRet(Op);
        }

        public static bool IsCast(Operator Op)
        {
            return Op == Operator.Cast || Op == Operator.Reinterpret;
        }

        public static bool IsCalculable(Operator Op)
        {
            return Operators.IsBoolRetBitArithmShift(Op) || Op == Operator.Negation ||
                Op == Operator.UnaryPlus || Op == Operator.Not || Op == Operator.Complement ||
                Op == Operator.Condition;
        }

        public static bool IsInstruction(Operator Op)
        {
            return IsRelEquality(Op) || IsBitArithmShift(Op) || Op == Operator.Assignment;
        }

        public static bool IsArithmetical(Operator Op)
        {
            return Op == Operator.Add || Op == Operator.Subract || Op == Operator.Multiply ||
                   Op == Operator.Divide || Op == Operator.Modolus;
        }

        public static bool IsRelEquality(Operator Op)
        {
            return Op == Operator.Equality || Op == Operator.Inequality || Op == Operator.Less ||
                   Op == Operator.LessEqual || Op == Operator.Greater || Op == Operator.GreaterEqual;
        }

        public static bool IsShift(Operator Op)
        {
            return Op == Operator.ShiftLeft || Op == Operator.ShiftRight;
        }

        public static bool IsRange(Operator Op)
        {
            return Op == Operator.RangeTo || Op == Operator.RangeUntil;
        }

        public static bool IsBitwise(Operator Op)
        {
            return Op == Operator.BitwiseAnd || Op == Operator.BitwiseOr || Op == Operator.BitwiseXor;
        }

        public static bool IsLogical(Operator Op)
        {
            return Op == Operator.And || Op == Operator.Or || Op == Operator.And;
        }

        public static bool IsBitArithm(Operator Op)
        {
            return IsArithmetical(Op) || IsBitwise(Op);
        }

        public static bool IsBitArithmShift(Operator Op)
        {
            return IsArithmetical(Op) || IsShift(Op) || IsBitwise(Op);
        }

        public static bool IsSameTypeReturn(Operator Op)
        {
            return IsBitArithmShift(Op) || Op == Operator.UnaryPlus ||
                Op == Operator.Negation || Op == Operator.Complement;
        }

        public static bool IsBoolRetBitArithmShift(Operator Op)
        {
            return IsBitArithmShift(Op) || IsBoolRet(Op);
        }

        public static bool IsBoolRet(Operator Op)
        {
            return IsLogical(Op) || IsRelEquality(Op);
        }

        public static Operator Negate(Operator Op)
        {
            switch (Op)
            {
                case Operator.And: return Operator.Or;
                case Operator.Or: return Operator.And;

                case Operator.Equality: return Operator.Inequality;
                case Operator.Inequality: return Operator.Equality;

                case Operator.Less: return Operator.GreaterEqual;
                case Operator.LessEqual: return Operator.Greater;
                case Operator.Greater: return Operator.LessEqual;
                case Operator.GreaterEqual: return Operator.Less;
                default: throw new ApplicationException();
            }
        }
    }

    public class NodeGroup
    {
        public List<object> Children = new List<object>();
        public CodeString Code;

        public NodeGroup(CodeString Code)
        {
            this.Code = Code;
        }

        public NodeGroup(CodeString Code, List<object> Children)
        {
            this.Children = Children;
            this.Code = Code;
        }

        public void GetNodes(List<ExpressionNode> Nodes)
        {
            for (var i = 0; i < Children.Count; i++)
            {
                var Obj = Children[i];
                if (Obj is NodeGroup) (Obj as NodeGroup).GetNodes(Nodes);
                else if (Obj is ExpressionNode) Nodes.Add(Obj as ExpressionNode);
                else throw new ApplicationException();
            }
        }

        public List<ExpressionNode> GetNodes()
        {
            var Ret = new List<ExpressionNode>();
            GetNodes(Ret);
            return Ret;
        }

        int _MinDepth = -1;
        public int MinDepth
        {
            get
            {
                if (_MinDepth != -1)
                    return _MinDepth;

                var Min = int.MaxValue;
                for (var i = 0; i < Children.Count; i++)
                {
                    var Obj = Children[i] as NodeGroup;
                    if (Obj != null)
                    {
                        var Res = Obj.MinDepth;
                        if (Res < Min) Min = Res;
                    }
                }

                if (Min == int.MaxValue) _MinDepth = 1;
                else _MinDepth = Min + 1;
                return _MinDepth;
            }
        }

        int _MaxDepth = -1;
        public int MaxDepth
        {
            get
            {
                if (_MaxDepth != -1)
                    return _MaxDepth;

                var Max = 0;
                for (var i = 0; i < Children.Count; i++)
                {
                    var Obj = Children[i] as NodeGroup;
                    if (Obj != null)
                    {
                        var Res = Obj.MaxDepth;
                        if (Res > Max) Max = Res;
                    }
                }

                _MaxDepth = Max + 1;
                return _MaxDepth;
            }
        }
    }

    public delegate PluginResult PluginFunc(ref ExpressionNode Node);

    public class LabelExpressionNode : ExpressionNode
    {
        public string Label;

        public LabelExpressionNode(CodeString Code, string Label, ExpressionFlags Flags = ExpressionFlags.None)
            : base(Code, Flags)
        {
            this.Label = Label;
        }

        protected override ExpressionNode Copy_WithoutLinkedNodes(CompilerState State, CodeString Code, Func<ExpressionNode, ExpressionNode> Func)
        {
            if (!Code.IsValid) Code = this.Code;
            return new LabelExpressionNode(Code, Label, Flags);
        }
    }

    public enum DataPointerType
    {
        Assembly,
        Identifier,
        IncBin,
    };

    public class DataPointerNode : ExpressionNode
    {
        public DataPointerType DescPointerType;
        public Identifier Id;
        public Assembly Assembly;
        public IncludedBinary IncBin;

        public DataPointerNode(CodeString Code, Assembly Assembly, ExpressionFlags Flags = ExpressionFlags.None)
            : base(Code, Flags)
        {
            this.DescPointerType = DataPointerType.Assembly;
            this.Assembly = Assembly;
        }

        public DataPointerNode(CodeString Code, Identifier Id, ExpressionFlags Flags = ExpressionFlags.None)
            : base(Code, Flags)
        {
            this.DescPointerType = DataPointerType.Identifier;
            this.Id = Id;
        }

        public DataPointerNode(CodeString Code, IncludedBinary IncBin, ExpressionFlags Flags = ExpressionFlags.None)
            : base(Code, Flags)
        {
            this.DescPointerType = DataPointerType.IncBin;
            this.IncBin = IncBin;
        }

        public DataPointerNode(CodeString Code, DataPointerType Type, ExpressionFlags Flags = ExpressionFlags.None)
            : base(Code, Flags)
        {
            this.DescPointerType = Type;
        }

        protected override ExpressionNode Copy_WithoutLinkedNodes(CompilerState State, CodeString Code, Func<ExpressionNode, ExpressionNode> Func)
        {
            if (!Code.IsValid) Code = this.Code;

            return new DataPointerNode(Code, Type, Flags)
            {
                Id = this.Id,
                Assembly = this.Assembly,
                IncBin = this.IncBin,
                Type = this.Type,
            };
        }
    }

    public class NamedParameterNode : ExpressionNode
    {
        public CodeString Name;

        public NamedParameterNode(CodeString Code, ExpressionNode Child, CodeString Name, ExpressionFlags Flags = ExpressionFlags.None)
            : base(Code, Flags)
        {
            this.Children = new ExpressionNode[] { Child };
            this.Name = Name;
        }

        protected override ExpressionNode Copy_WithoutLinkedNodes(CompilerState State, CodeString Code, Func<ExpressionNode, ExpressionNode> Func)
        {
            var Ch = Children[0].Copy(State, Code, Func);
            if (!Code.IsValid) Code = this.Code;
            return new NamedParameterNode(Code, Children[0], Name, Flags);
        }
    }

    public class MacroExpressionNode : ExpressionNode
    {
        public Macro Macro;

        public MacroExpressionNode(Macro Macro, CodeString Code, ExpressionFlags Flags = ExpressionFlags.None)
            : base(Code, Flags)
        {
            this.Macro = Macro;
        }

        protected override ExpressionNode Copy_WithoutLinkedNodes(CompilerState State, CodeString Code, Func<ExpressionNode, ExpressionNode> Func)
        {
            if (!Code.IsValid) Code = this.Code;
            return new MacroExpressionNode(Macro, Code, Flags);
        }
    }

    public class MacroArgNode : ExpressionNode
    {
        public int Index;

        public MacroArgNode(int Index, CodeString Code, ExpressionFlags Flags = ExpressionFlags.None)
            : base(Code, Flags)
        {
            this.Index = Index;
        }

        protected override ExpressionNode Copy_WithoutLinkedNodes(CompilerState State, CodeString Code, Func<ExpressionNode, ExpressionNode> Func)
        {
            if (!Code.IsValid) Code = this.Code;
            return new MacroArgNode(Index, Code, Flags);
        }
    }

    public class StrExpressionNode : ExpressionNode
    {
        public StrExpressionNode(CodeString Code, ExpressionFlags Flags = ExpressionFlags.None)
            : base(Code, Flags)
        {
        }

        protected override ExpressionNode Copy_WithoutLinkedNodes(CompilerState State, CodeString Code, Func<ExpressionNode, ExpressionNode> Func)
        {
            return new StrExpressionNode(this.Code, Flags);
        }
    }

    public class IdExpressionNode : ExpressionNode
    {
        public Identifier Identifier;

        public IdExpressionNode(Identifier Identifier, CodeString Code, ExpressionFlags Flags = ExpressionFlags.None)
            : base(Code, Flags)
        {
            this.Identifier = Identifier;
        }

        protected override ExpressionNode Copy_WithoutLinkedNodes(CompilerState State, CodeString Code, Func<ExpressionNode, ExpressionNode> Func)
        {
            if (!Code.IsValid) Code = this.Code;
            return new IdExpressionNode(Identifier, Code, Flags);
        }
    }

    public class OpExpressionNode : ExpressionNode
    {
        public Operator Operator = Operator.Unknown;

        public OpExpressionNode(Operator Operator, ExpressionNode[] Children, CodeString Code, ExpressionFlags Flags = ExpressionFlags.None)
            : base(Code, Flags)
        {
            this.Operator = Operator;
            this.Children = Children;
        }

        public OpExpressionNode(Operator Operator, CodeString Code, ExpressionFlags Flags = ExpressionFlags.None)
            : base(Code, Flags)
        {
            this.Operator = Operator;
            this.Children = null;
        }

        public OpExpressionNode(CodeString Code, ExpressionFlags Flags = ExpressionFlags.None)
            : base(Code, Flags)
        {
            this.Operator = Operator.Unknown;
            this.Children = null;
        }

        protected override ExpressionNode Copy_WithoutLinkedNodes(CompilerState State, CodeString Code, Func<ExpressionNode, ExpressionNode> Func)
        {
            var Ch = new ExpressionNode[Children.Length];
            for (var i = 0; i < Children.Length; i++)
            {
                Ch[i] = Children[i].Copy(State, Code, Func);
                if (Ch[i] == null) return null;
            }

            if (!Code.IsValid) Code = this.Code;
            return new OpExpressionNode(Operator, Ch, Code, Flags);
        }

        public override bool GetAssignVar(ref Variable AssignVar)
        {
            if (Operator == Operator.Assignment)
            {
                var N = Children[0] as IdExpressionNode;
                if (N != null) AssignVar = N.Identifier as Variable;
                else AssignVar = null;
                return true;
            }

            return false;
        }

        public void Swap()
        {
            var Temp = Children[0];
            Children[0] = Children[1];
            Children[1] = Temp;

            if (Operator == Operator.Subract || Operator == Operator.Divide)
            {
                if ((Flags & ExpressionFlags.ReverseOperation) != 0)
                    Flags &= ~ExpressionFlags.ReverseOperation;
                else Flags |= ExpressionFlags.ReverseOperation;
            }
            else if (Operators.IsRelEquality(Operator))
            {
                if (Operator == Operator.Less) Operator = Operator.Greater;
                else if (Operator == Operator.LessEqual) Operator = Operator.GreaterEqual;
                else if (Operator == Operator.Greater) Operator = Operator.Less;
                else if (Operator == Operator.GreaterEqual) Operator = Operator.LessEqual;
            }
        }
    }

    [Flags]
    public enum LinkedNodeFlags : byte
    {
        None = 0,
        PostComputation = 1,
        NotRemovable = 2,
    }

    public class LinkedExprNode
    {
        public ExpressionNode Node;
        public LinkedNodeFlags Flags;
        public DataList Data = new DataList();
        public int LinkingCount;

        public LinkedExprNode(ExpressionNode Node, LinkedNodeFlags Flags = LinkedNodeFlags.None)
        {
            this.Node = Node;
            this.Flags = Flags;
        }
    }

    public class LinkingNode : ExpressionNode
    {
        public LinkedExprNode LinkedNode;

        public LinkingNode(LinkedExprNode Node, CodeString Code, ExpressionFlags Flags = ExpressionFlags.None)
            : base(Code, Flags)
        {
            this.LinkedNode = Node;
        }

        protected override ExpressionNode Copy_WithoutLinkedNodes(CompilerState State, CodeString Code, Func<ExpressionNode, ExpressionNode> Func)
        {
            if (!Code.IsValid) Code = this.Code;
            return new LinkingNode(LinkedNode, Code, Flags);
        }

        public override ExpressionNode RealNode
        {
            get { return LinkedNode.Node; }
        }
    }

    public class ScopeExpressionNode : ExpressionNode
    {
        public IdContainer Container;
        public LocalVariable ReturnVar;

        public ScopeExpressionNode(IdContainer Container, CodeString Code)
            : base(Code)
        {
            this.Container = Container;
        }

        protected override ExpressionNode Copy_WithoutLinkedNodes(CompilerState State, CodeString Code, Func<ExpressionNode, ExpressionNode> Func)
        {
            throw new ApplicationException();
        }
    }

    public struct IdentifierReference
    {
        public Identifier Identifier;
        public CodeString Code;

        public IdentifierReference(Identifier Identifier, CodeString Code)
        {
            this.Identifier = Identifier;
            this.Code = Code;
        }

        public IdentifierReference(CodeString Code)
        {
            this.Identifier = null;
            this.Code = Code;
        }
    }

    public abstract class InitializationNode : ExpressionNode
    {
        public InitializationNode(CodeString Code, ExpressionFlags Flags = ExpressionFlags.None)
            : base(Code, Flags)
        {
        }
    }

    public class ObjectInitNode : InitializationNode
    {
        public IdentifierReference[] Members;

        public ObjectInitNode(IdentifierReference[] Members, ExpressionNode[] Nodes,
            CodeString Code, ExpressionFlags Flags = ExpressionFlags.None)
            : base(Code, Flags)
        {
            this.Members = Members;
            this.Children = Nodes;
        }

        protected override ExpressionNode Copy_WithoutLinkedNodes(CompilerState State, CodeString Code, Func<ExpressionNode, ExpressionNode> Func)
        {
            var Nodes = new ExpressionNode[Children.Length];
            for (var i = 0; i < Children.Length; i++)
            {
                Nodes[i] = Children[i].Copy(State, Code, Func);
                if (Nodes[i] == null) return null;
            }

            return new ObjectInitNode(Members, Children, Code, Flags);
        }
    }

    public struct ArrayIndices
    {
        public int[] Indices;

        public ArrayIndices(params int[] Indices)
        {
            this.Indices = Indices;
        }
    }

    public class ArrayInitNode : InitializationNode
    {
        public ArrayIndices[] Indices;

        public ArrayInitNode(ExpressionNode[] Nodes, ArrayIndices[] Indices,
            CodeString Code, ExpressionFlags Flags = ExpressionFlags.None)
            : base(Code, Flags)
        {
            this.Children = Nodes;
            this.Indices = Indices;
        }

        protected override ExpressionNode Copy_WithoutLinkedNodes(CompilerState State, CodeString Code, Func<ExpressionNode, ExpressionNode> Func)
        {
            var Nodes = new ExpressionNode[Children.Length];
            for (var i = 0; i < Children.Length; i++)
            {
                Nodes[i] = Children[i].Copy(State, Code, Func);
                if (Nodes[i] == null) return null;
            }

            return new ArrayInitNode(Children, Indices, Code, Flags);
        }
    }

    public abstract class ExpressionNode
    {
        public Identifier Type;
        public CodeString Code;
        public DataList Data = new DataList();
        public AutoAllocatedList<LinkedExprNode> LinkedNodes;
        public ExpressionNode[] Children;
        public int InterrupterPlugin = -1;
        public ExpressionFlags Flags;
        public CheckingMode CheckingMode;

        public int GetLinkingCount(LinkedExprNode Node)
        {
            var Ret = 0;
            if (this is LinkingNode)
            {
                var LinkingNode = this as LinkingNode;
                if (LinkingNode.LinkedNode == Node) Ret++;
            }

            for (var i = 0; i < LinkedNodes.Count; i++)
            {
                var Linked = LinkedNodes[i].Node;
                Ret += Linked.GetLinkingCount(Node);
            }

            if (Children != null)
            {
                for (var i = 0; i < Children.Length; i++)
                    Ret += Children[i].GetLinkingCount(Node);
            }

            return Ret;
        }

        public ExpressionNode DetachChild(int Index)
        {
            var Ret = Children[Index];
            var LinkedNodes = new AutoAllocatedList<LinkedExprNode>();

            for (var i = 0; i < this.LinkedNodes.Count; i++)
            {
                var LNode = this.LinkedNodes[i];
                if ((LNode.Flags & LinkedNodeFlags.PostComputation) != 0)
                    throw new NotImplementedException();

                if ((LNode.Flags & LinkedNodeFlags.NotRemovable) != 0)
                {
                    LinkedNodes.Add(LNode);
                }
                else
                {
                    var LinkingCount = Children[Index].GetLinkingCount(LNode);
                    if (LinkingCount > 0)
                    {
                        LNode.LinkingCount = LinkingCount;
                        LinkedNodes.Add(LNode);
                    }
                }
            }

            for (var i = 0; i < Index; i++)
                Children[i].GetLinkedNodes(ref LinkedNodes, true);

            if (Children[Index].LinkedNodes.List != null)
                LinkedNodes.AddRange(Children[Index].LinkedNodes.List);

            for (var i = Index + 1; i < Children.Length; i++)
                Children[i].GetLinkedNodes(ref LinkedNodes, true);

            Ret.LinkedNodes = LinkedNodes;
            return Ret;
        }

        public List<LinkedExprNode> GetLinkedNodes(bool OnlyNotRemovable = false)
        {
            var Ret = new List<LinkedExprNode>();
            GetLinkedNodes(Ret, OnlyNotRemovable);
            return Ret;
        }

        public void GetLinkedNodes(List<LinkedExprNode> Out, bool OnlyNotRemovable = false)
        {
            if (LinkedNodes.List != null)
                Out.AddRange(LinkedNodes.List);

            for (var i = 0; i < LinkedNodes.Count; i++)
            {
                var LNode = LinkedNodes[i];
                if (!OnlyNotRemovable || (LNode.Flags & LinkedNodeFlags.NotRemovable) != 0)
                    Out.Add(LNode);
            }

            if (Children != null)
            {
                for (var i = 0; i < Children.Length; i++)
                    Children[i].GetLinkedNodes(Out, OnlyNotRemovable);
            }
        }

        public void GetLinkedNodes(ref AutoAllocatedList<LinkedExprNode> Out, bool OnlyNotRemovable = false)
        {
            if (LinkedNodes.List != null)
                Out.AddRange(LinkedNodes.List);

            for (var i = 0; i < LinkedNodes.Count; i++)
            {
                var LNode = LinkedNodes[i];
                if (!OnlyNotRemovable || (LNode.Flags & LinkedNodeFlags.NotRemovable) != 0)
                    Out.Add(LNode);
            }

            if (Children != null)
            {
                for (var i = 0; i < Children.Length; i++)
                    Children[i].GetLinkedNodes(ref Out, OnlyNotRemovable);
            }
        }

        public static PluginResult ReplaceNodes(ref ExpressionNode Node, PluginRoot Plugin,
            PluginFunc Func, bool NoReplaceOnPluginCall = false)
        {
            var CallPluginOnce = false;
            for (var i = 0; i < Node.LinkedNodes.Count; i++)
            {
                var LNode = Node.LinkedNodes[i];
                var OldNode = LNode.Node;
                var Res = ReplaceNodes(ref LNode.Node, Plugin, Func, NoReplaceOnPluginCall);

                if (Res == PluginResult.Failed) return Res;
                if (Res != PluginResult.Succeeded || LNode.Node != OldNode)
                    CallPluginOnce = true;
            }

            if (Node.Children != null)
            {
                for (var i = 0; i < Node.Children.Length; i++)
                {
                    var OldNode = Node.Children[i];
                    var Res = ReplaceNodes(ref Node.Children[i], Plugin, Func, NoReplaceOnPluginCall);
                    if (Res == PluginResult.Failed) return Res;

                    if (Res != PluginResult.Succeeded || Node.Children[i] != OldNode)
                        CallPluginOnce = true;
                }
            }

            if (!CallPluginOnce || (CallPluginOnce && !NoReplaceOnPluginCall))
            {
                var Res = Func(ref Node);
                if (Res != PluginResult.Succeeded)
                    return Res;
            }

            if (CallPluginOnce)
            {
                var OldType = Node.Type;
                var Res = Plugin.NewNode(ref Node);
                if (Res == PluginResult.Failed) return Res;

                if (OldType != null && Node.Type != null && Node.Type.RealId != OldType.RealId)
                {
                    if (Node.Type.UnderlyingStructureOrRealId is StructuredType)
                        throw new ApplicationException("Cannot change structured type of a node");
                }

                return Res;
            }

            return PluginResult.Succeeded;
        }

        static PluginResult CallNewNode_NoBeginEnd(ref ExpressionNode Node, PluginRoot Plugin)
        {
            return ReplaceNodes(ref Node, Plugin, (ref ExpressionNode x) =>
            {
                var OldType = x.Type;
                var Res = Plugin.NewNodeDontCallAll(ref x);
                if (Res == PluginResult.Failed) return Res;

                if (OldType != null && x.Type != null && x.Type.RealId != OldType.RealId)
                {
                    if (x.Type.UnderlyingStructureOrRealId is StructuredType)
                        throw new ApplicationException("Cannot change structured type of a node");
                }

                return Res;
            }, true);
        }

        public bool ReplaceChildren(ExpressionNode From, ExpressionNode To, bool ReplaceLinkedNodes = true)
        {
            if (ReplaceLinkedNodes)
            {
                for (var i = 0; i < LinkedNodes.Count; i++)
                {
                    var e = LinkedNodes[i];
                    if (e.Node == From) e.Node = To;
                }
            }

            if (Children != null)
            {
                for (var i = 0; i < Children.Length; i++)
                    if (Children[i] == From) Children[i] = To;
            }

            return true;
        }

        public bool ReplaceChildren(Func<ExpressionNode, ExpressionNode> Func, bool ReplaceLinkedNodes = true)
        {
            if (ReplaceLinkedNodes)
            {
                for (var i = 0; i < LinkedNodes.Count; i++)
                {
                    var e = LinkedNodes[i];
                    if ((e.Node = Func(e.Node)) == null)
                        return false;
                }
            }

            if (Children != null)
            {
                for (var i = 0; i < Children.Length; i++)
                {
                    if ((Children[i] = Func(Children[i])) == null)
                        return false;
                }
            }

            return true;
        }

        public void GetNodes(List<ExpressionNode> Out)
        {
            ForEach(Ch => Out.Add(Ch));
        }

        public List<ExpressionNode> GetNodes()
        {
            var Out = new List<ExpressionNode>(16);
            GetNodes(Out);
            return Out;
        }

        public void ForEachChildren(Action<ExpressionNode> Func)
        {
            for (var i = 0; i < LinkedNodes.Count; i++)
                Func(LinkedNodes[i].Node);

            if (Children != null)
            {
                for (var i = 0; i < Children.Length; i++)
                    Func(Children[i]);
            }
        }

        public bool CheckChildren(Predicate<ExpressionNode> Func)
        {
            for (var i = 0; i < LinkedNodes.Count; i++)
                if (!Func(LinkedNodes[i].Node)) return false;

            if (Children != null)
            {
                for (var i = 0; i < Children.Length; i++)
                    if (!Func(Children[i])) return false;
            }

            return true;
        }

        public void ForEach(Action<ExpressionNode> Func)
        {
            Func(this);

            for (var i = 0; i < LinkedNodes.Count; i++)
                LinkedNodes[i].Node.ForEach(Func);

            if (Children != null)
            {
                for (var i = 0; i < Children.Length; i++)
                    Children[i].ForEach(Func);
            }
        }

        public bool CheckNodes(Predicate<ExpressionNode> Func, bool CheckRoot = true)
        {
            if (CheckRoot && !Func(this))
                return false;

            for (var i = 0; i < LinkedNodes.Count; i++)
                if (!LinkedNodes[i].Node.CheckNodes(Func)) return false;

            if (Children != null)
            {
                for (var i = 0; i < Children.Length; i++)
                    if (!Children[i].CheckNodes(Func)) return false;
            }

            return true;
        }

        public virtual ExpressionNode RealNode
        {
            get { return this; }
        }

        public IEnumerable<ExpressionNode> EnumChildren
        {
            get
            {
                for (var i = 0; i < LinkedNodes.Count; i++)
                    yield return LinkedNodes[i].Node;

                if (Children != null)
                {
                    for (var i = 0; i < Children.Length; i++)
                    {
                        if (!(Children[i] is LinkingNode))
                            yield return Children[i];
                    }
                }
            }
        }

        public virtual ConditionResult ConditionResult
        {
            get { return ConditionResult.Unknown; }
        }

        public ExpressionNode CallNewNode(PluginRoot Plugin, BeginEndMode Mode = BeginEndMode.Both)
        {
            if ((Mode & BeginEndMode.Begin) != 0 && !Plugin.Begin())
                return null;

            var RetValue = this;
            var Res = CallNewNode_NoBeginEnd(ref RetValue, Plugin);
            if (Res == PluginResult.Failed) return null;

            if ((Mode & BeginEndMode.End) != 0)
                RetValue = Plugin.End(RetValue);

            return RetValue;
        }

        public ExpressionNode Copy(PluginRoot Plugin, PluginFunc Func = null,
            BeginEndMode Mode = BeginEndMode.Both, CodeString Code = new CodeString(), bool LeftType = false)
        {
            if ((Mode & BeginEndMode.Begin) != 0 && !Plugin.Begin())
                return null;

            if (!Code.IsValid)
                Code = this.Code;

            var Ret = Copy(Plugin.State, Code, x =>
            {
                if (Func != null)
                {
                    var Res = Func(ref x);
                    if (Res == PluginResult.Failed) return null;
                    if (Res == PluginResult.Interrupt || Res == PluginResult.Ready)
                        return x;
                }

                return Plugin.NewNode(x);
            });

            if ((Mode & BeginEndMode.End) != 0 && Ret != null)
                Ret = Plugin.End(Ret);

            if (Ret != null && LeftType)
                Ret.Type = Type;

            return Ret;
        }

        public ExpressionNode Copy(CompilerState State, CodeString Code, Func<ExpressionNode, ExpressionNode> Func)
        {
            var Ret = Copy_WithoutLinkedNodes(State, Code, Func);
            if (Ret == null) return null;

            for (var i = 0; i < LinkedNodes.Count; i++)
            {
                var Node = LinkedNodes[i].Node.Copy(State, Code, Func);
                if (Node == null) return null;

                Ret.LinkedNodes.Add(new LinkedExprNode(Node, LinkedNodes[i].Flags));
            }

            return Func(Ret);
        }

        protected abstract ExpressionNode Copy_WithoutLinkedNodes(CompilerState State,
            CodeString Code, Func<ExpressionNode, ExpressionNode> Func);

        public ExpressionNode(CodeString Code, ExpressionFlags Flags = ExpressionFlags.None)
        {
            this.Code = Code;
            this.Flags = Flags;
        }

        public bool IdUsed(Identifier Id)
        {
            return CheckNodes(Ch =>
            {
                var IdNode = this as IdExpressionNode;
                return IdNode != null && IdNode.Identifier == Id;
            });
        }

        public bool IdUsed(Type Type)
        {
            return CheckNodes(Ch =>
            {
                var IdNode = this as IdExpressionNode;
                return IdNode != null && IdNode.Identifier.TypeOfSelf.IsEquivalent(Type);
            });
        }

        public bool IdUsed()
        {
            return CheckNodes(Ch => Ch is IdExpressionNode);
        }

        public virtual bool GetAssignVar(ref Variable AssignVar)
        {
            return false;
        }
    }

    public class NodeVariables
    {
        public AutoAllocatedList<IdExpressionNode> AssignedIds;
        public AutoAllocatedList<IdExpressionNode> UsedBeforeAssignIds;
        public AutoAllocatedList<IdExpressionNode> AddressUsed;

        public void Remove(IdExpressionNode IdNode)
        {
            AssignedIds.Remove(IdNode);
            UsedBeforeAssignIds.Remove(IdNode);
            AddressUsed.Remove(IdNode);
        }

        public void Remove(Identifier Id)
        {
            AssignedIds.RemoveAll(x => x.Identifier.IsEquivalent(Id));
            UsedBeforeAssignIds.RemoveAll(x => x.Identifier.IsEquivalent(Id));
            AddressUsed.RemoveAll(x => x.Identifier.IsEquivalent(Id));
        }

        public void UnionInPlace(NodeVariables Vars)
        {
            for (var i = 0; i < Vars.AssignedIds.Count; i++)
            {
                if (!AssignedIds.Contains(Vars.AssignedIds[i]))
                    AssignedIds.Add(Vars.AssignedIds[i]);
            }

            for (var i = 0; i < Vars.UsedBeforeAssignIds.Count; i++)
            {
                if (!UsedBeforeAssignIds.Contains(Vars.UsedBeforeAssignIds[i]))
                    UsedBeforeAssignIds.Add(Vars.UsedBeforeAssignIds[i]);
            }

            for (var i = 0; i < Vars.AddressUsed.Count; i++)
            {
                if (!AddressUsed.Contains(Vars.AddressUsed[i]))
                    AddressUsed.Add(Vars.AddressUsed[i]);
            }
        }
    }
}
