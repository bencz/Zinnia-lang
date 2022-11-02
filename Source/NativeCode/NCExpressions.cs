using System;
using System.Collections.Generic;
using System.Numerics;
using Zinnia.Base;

namespace Zinnia.NativeCode
{
    public struct NCNeededRunBefore
    {
        public ExpressionNode Node;
        //public Identifier[] Identifiers;

        public NCNeededRunBefore(ExpressionNode Node/*, Identifier[] Identifiers*/)
        {
            this.Node = Node;
            //this.Identifiers = Identifiers;
        }
    }

    public struct NCExpressionRunBefores
    {
        public NCNeededRunBefore[] NeededRunBefores;
        /*
        public Identifier[] GetIdentifiersToShare()
        {
            var List = new List<Identifier>();
            for (var i = 0; i < NeededRunBefores.Length; i++)
            {
                var RunBefore = NeededRunBefores[i];
                for (var j = 0; j < RunBefore.Identifiers.Length; j++)
                {
                    var Id = RunBefore.Identifiers[j];
                    if (!List.Contains(Id)) List.Add(Id);
                }
            }

            return List.ToArray();
        }*/
    }

	public static class NCExpressions
	{
		public static ExpressionNode[] GetArrayDimensions(PluginRoot Plugin,
			ExpressionNode Node, CodeString Code)
		{
			var Ch = Node.Children;
			var Dimensions = new ExpressionNode[Ch.Length - 1];

			for (var i = 0; i < Dimensions.Length; i++)
			{
				Dimensions[i] = GetArrayDimension(Plugin, Ch[0], i, Code);
				if (Dimensions[i] == null) return null;
			}

			return Dimensions;
		}

		public static ExpressionNode[] GetArrayIndices(PluginRoot Plugin,
			ExpressionNode Node, CodeString Code, Identifier IndexType = null)
		{
			var Ch = Node.Children;
			var Indices = new ExpressionNode[Ch.Length - 1];

			for (var i = 0; i < Indices.Length; i++)
			{
				Indices[i] = Ch[i + 1];
				if (IndexType != null && !Indices[i].Type.IsEquivalent(IndexType))
				{
					Indices[i] = Expressions.Convert(Indices[i], IndexType, Plugin);
					if (Indices[i] == null) return null;
				}
			}

			return Indices;
		}

		public static ExpressionNode FlattenIndicesWithoutTempVar(PluginRoot Plugin,
			ExpressionNode Node, CodeString Code, Identifier IndexType = null)
		{
			var Indices = GetArrayIndices(Plugin, Node, Code, IndexType);

			var ArrayType = Node.Children[0].Type.RealId as ArrayType;
			if (ArrayType.Dimensions == 1) return Indices[0];

			var Dimensions = GetArrayDimensions(Plugin, Node, Code);
			return FlattenIndicesWithoutTempVar(Plugin, Indices, Dimensions, Code);
		}

		public static ExpressionNode FlattenIndicesWithoutTempVar(PluginRoot Plugin,
			ExpressionNode[] Indices, ExpressionNode[] Dimensions, CodeString Code)
		{
			if (Dimensions.Length == 1)
				return Indices[0];

			var Ret = (ExpressionNode)null;
			for (var i = Dimensions.Length - 1; i >= 0; i--)
			{
				var Chi = Indices[i];
				for (var j = i + 1; j < Dimensions.Length; j++)
				{
					var MulCh = new ExpressionNode[] { Chi, Dimensions[j] };
					Chi = Plugin.NewNode(new OpExpressionNode(Operator.Multiply, MulCh, Code));
					if (Chi == null) return null;
				}

				if (Ret != null)
				{
					var AddCh = new ExpressionNode[] { Ret, Chi };
					Ret = Plugin.NewNode(new OpExpressionNode(Operator.Add, AddCh, Code));
					if (Ret == null) return null;
				}
				else
				{
					Ret = Chi;
				}
			}

			return Ret;
		}
		
		public static ExpressionNode FlattenIndices(PluginRoot Plugin,
			ExpressionNode Node, CodeString Code, Identifier IndexType = null)
		{
			var Indices = GetArrayIndices(Plugin, Node, Code, IndexType);

			var ArrayType = Node.Children[0].Type.RealId as ArrayType;
			if (ArrayType.Dimensions == 1) return Indices[0];

			var Dimensions = GetArrayDimensions(Plugin, Node, Code);
			return FlattenIndices(Plugin, Indices, Dimensions, Code, IndexType);
		}

		public static ExpressionNode FlattenIndices(PluginRoot Plugin, ExpressionNode[] Indices,
			ExpressionNode[] Dimensions, CodeString Code, Identifier TempVarType = null)
		{
			if (Dimensions.Length == 1)
				return Indices[0];

			var Ret = (ExpressionNode)null;
			var MulVar = (Identifier)null;
			var Container = Plugin.Container;
			var State = Plugin.State;

			if (Dimensions.Length >= 2)
			{
				var NCPlugin = Plugin.GetPlugin<NCPlugin>();
				var DeclContainer = NCPlugin.DeclContainer;
				MulVar = DeclContainer.CreateAndDeclareVariable(State.AutoVarName, TempVarType);
				if (MulVar == null) return null;
			}

			for (var i = Dimensions.Length - 1; i >= 0; i--)
			{
				var Chi = Indices[i];
				var LinkedNode = (LinkedExprNode)null;
				if (MulVar != null && i != 1)
				{
					ExpressionNode[] AssignmentCh;
					if (i == Dimensions.Length - 1)
					{
						var Dst = Plugin.NewNode(new IdExpressionNode(MulVar, Code));
						if (Dst == null) return null;

						AssignmentCh = new ExpressionNode[] { Dst, Dimensions[i] };
					}
					else
					{
						var Dst = Plugin.NewNode(new IdExpressionNode(MulVar, Code));
						var MulSrc = Plugin.NewNode(new IdExpressionNode(MulVar, Code));
						if (Dst == null || MulSrc == null) return null;

						var MulCh = new ExpressionNode[] { MulSrc, Dimensions[i] };
						var MulNode = Plugin.NewNode(new OpExpressionNode(Operator.Multiply, MulCh, Code));
						if (MulNode == null) return null;

						AssignmentCh = new ExpressionNode[] { Dst, MulNode };
					}

					var Assignment = Plugin.NewNode(new OpExpressionNode(Operator.Assignment, AssignmentCh, Code));
					if (Assignment == null) return null;

					LinkedNode = new LinkedExprNode(Assignment, LinkedNodeFlags.NotRemovable);
				}

				if (MulVar != null && i != Dimensions.Length - 1)
				{
					var MulNode = Plugin.NewNode(new IdExpressionNode(MulVar, Code));
					if (MulNode == null) return null;

					var MulCh = new ExpressionNode[] { Chi, MulNode };
					Chi = Plugin.NewNode(new OpExpressionNode(Operator.Multiply, MulCh, Code));
					if (Chi == null) return null;
				}

				if (Ret != null)
				{
					var AddCh = new ExpressionNode[] { Ret, Chi };
					var AddNode = new OpExpressionNode(Operator.Add, AddCh, Code);
					if (LinkedNode != null) AddNode.LinkedNodes.Add(LinkedNode);

					Ret = Plugin.NewNode(AddNode);
					if (Ret == null) return null;
				}
				else
				{
					Ret = Chi;
					if (LinkedNode != null)
						Ret.LinkedNodes.Add(LinkedNode);
				}
			}

			return Ret;
		}

		public static ExpressionNode GetArrayDimension(PluginRoot Plugin, 
			ExpressionNode Array, int Index, CodeString Code)
		{
			var Container = Plugin.Container;
			var Type = Array.Type.RealId;

			if (Type is NonrefArrayType)
			{
				var Arr = Type as NonrefArrayType;
				if (Arr.Lengths == null) throw new ArgumentOutOfRangeException("Index");
				return Plugin.NewNode(Constants.GetUIntPtrValue(Container, Arr.Lengths[Index], Code));
			}
			else if (Type is RefArrayType)
			{
				var Arr = Type as RefArrayType;
				var DimensionsId = Identifiers.GetMember(Plugin.State, Arr, "Dimensions", Code);
				if (DimensionsId == null) return null;

				var DimensionsIdNode = Plugin.NewNode(new IdExpressionNode(DimensionsId, Code));
				if (DimensionsIdNode == null) return null;

				var DimensionsCh = new ExpressionNode[] { Array, DimensionsIdNode };
				var Dimensions = Plugin.NewNode(new OpExpressionNode(Operator.Member, DimensionsCh, Code));
				if (Dimensions == null) return null;

				var IndexValue = Plugin.NewNode(Constants.GetIntValue(Container, Index, Code));
				if (IndexValue == null) return null;

				var IndexCh = new ExpressionNode[] { Dimensions, IndexValue };
				return Plugin.NewNode(new OpExpressionNode(Operator.Index, IndexCh, Code));
			}
			else
			{
				throw new NotImplementedException();
			}
		}

		public static bool IsOverflowableOp(Operator Op)
		{
			return Op == Operator.Add || Op == Operator.Subract || Op == Operator.Multiply ||
				Operators.IsIncDec(Op) || Op == Operator.Cast || Op == Operator.Negation;
		}

		public static ExpressionNode ThrowSystemException(PluginRoot Plugin, string ExceptionType,
			CodeString Code, BeginEndMode BEMode = BeginEndMode.Both)
		{
			var System = Identifiers.GetByFullNameFast<Namespace>(Plugin.State, "System");
			if (System == null) return null;

			return Throw(Plugin, System, new CodeString(ExceptionType), Code, BEMode);
		}

		public static ExpressionNode Throw(PluginRoot Plugin, Identifier ContainerId, string ExceptionType,
			CodeString Code, BeginEndMode BEMode = BeginEndMode.Both)
		{
			return Throw(Plugin, ContainerId, new CodeString(ExceptionType), Code, BEMode);
		}

		public static ExpressionNode Throw(PluginRoot Plugin, Identifier ContainerId, CodeString ExceptionType,
			CodeString Code, BeginEndMode BEMode = BeginEndMode.Both)
		{
			var Options = new GetIdOptions() { EnableMessages = true, Func = x => x.RealId is ClassType };
			var IdExceptionType = Identifiers.GetFromMembers(ContainerId, ExceptionType, Options);
			if (IdExceptionType == null) return null;

			return Throw(Plugin, IdExceptionType, Code, BEMode);
		}

		public static ExpressionNode Throw(PluginRoot Plugin, Identifier ExceptionType,
			CodeString Code, BeginEndMode BEMode = BeginEndMode.Both)
		{
			var GlobalContainer = Plugin.Container.GlobalContainer;
			if ((BEMode & BeginEndMode.Begin) != 0 && !Plugin.Begin())
				return null;

			var IdNode = Plugin.NewNode(new IdExpressionNode(ExceptionType, Code));
			if (IdNode == null) return null;

			var Ch = new ExpressionNode[] { IdNode };
			var Node = Plugin.NewNode(new OpExpressionNode(Operator.NewObject, Ch, Code));
			if (Node == null) return null;

			return Throw(Plugin, Node, Code, (BEMode & BeginEndMode.End) != 0);
		}

		public static ExpressionNode Throw(PluginRoot Plugin, ExpressionNode Node,
			CodeString Code, bool End = true)
		{
			var Global = Plugin.Container.GlobalContainer;
			var Func = Identifiers.GetByFullNameFast<Function>(Global, "Internals.ThrowException");
			if (Func == null) return null;

			Node = Expressions.Call(Code, Plugin, Func, Node);
			return Node == null || !End ? Node : Plugin.End(Node);
		}

		public static ExpressionNode Negate(PluginRoot Plugin, ExpressionNode Node, CodeString Code, bool End = false)
		{
			var Ch = new ExpressionNode[] { Node };
			Node = Plugin.NewNode(new OpExpressionNode(Operator.Not, Ch, Code));
			if (Node == null) return null;

			if (End  && Plugin.End(ref Node) == PluginResult.Failed)
				return null;

			return Node;
		}

		public static ExpressionNode IsInRange(PluginRoot Plugin, ExpressionNode Node,
			NonFloatType Type, CodeString Code)
		{
			if (Node.InterrupterPlugin != -1 && Node.Type == null)
			{
				Node = Plugin.FinishNode(Node);
				if (Node == null) return null;
			}

			var NodeType = Node.Type.RealId as NonFloatType;
			var Min = BigInteger.Max(Type.MinValue, NodeType.MinValue);
			var Max = BigInteger.Min(Type.MaxValue, NodeType.MaxValue);
			return IsInRange(Plugin, Node, Min, Max, Code);
		}

		public static ExpressionNode IsInRange(PluginRoot Plugin, ExpressionNode Node,
			BigInteger Min, BigInteger Max, CodeString Code)
		{
			if (!(Node.Type.RealId is NonFloatType))
				throw new ArgumentException("Node");

			var MinValue = new IntegerValue(Min);
			var MaxValue = new IntegerValue(Max);
			return IsInRange(Plugin, Node, MinValue, MaxValue, Code);
		}

		public static ExpressionNode IsInRange(PluginRoot Plugin, ExpressionNode Node,
			ConstValue Min, ConstValue Max, CodeString Code)
		{
			var MinNode = Plugin.NewNode(new ConstExpressionNode(Node.Type, Min, Code));
			var MaxNode = Plugin.NewNode(new ConstExpressionNode(Node.Type, Max, Code));
			if (MinNode == null || MaxNode == null) return null;

			return IsInRange(Plugin, Node, MinNode, MaxNode, Code);
		}

		public static ExpressionNode IsInRange(PluginRoot Plugin, ExpressionNode Node,
			ExpressionNode Min, ExpressionNode Max, CodeString Code)
		{
			var Nodes = new ExpressionNode[] { Min, Node, Max };
			var Operators = new Operator[] { Operator.LessEqual, Operator.LessEqual };
			return Expressions.ChainedRelation(Plugin, Nodes, Operators, Code);
		}

		public static NonFloatType GetEquivalentNumberType(Identifier Id)
		{
			var Global = Id.Container.GlobalContainer;
			var Type = Id.RealId as Type;

			if (Type is EnumType)
			{
				var EType = Type as EnumType;
				Type = EType.TypeOfValues;
			}

			if (Type is NonFloatType) return Type as NonFloatType;
			if (Type is CharType) return Global.CommonIds.UInt16;
			if (Type is PointerType) return Global.CommonIds.GetIdentifier<UnsignedType>(Type.Size);
			return null;
		}

		public static NCExpressionRunBefores[] GetCommandRunBefores(Command Command)
		{
            var Ret = new NCExpressionRunBefores[Command.Expressions.Count];
            for (var i = 0; i < Command.Expressions.Count; i++)
                Ret[i] = GetExpressionRunBefores(Command, Command.Expressions[i]);

            return Ret;
		}

        public static void GetUsedCommandIdentifiers(Command Command, ExpressionNode Node, List<Identifier> Out)
        {
			if (Command.Children.Count == 0 && Command.IdentifierList.Count == 0)
				return;

            Node.ForEach(x =>
            {
                if (x is IdExpressionNode)
                {
                    var Idx = x as IdExpressionNode;
                    var IdContainer = Idx.Identifier.Container;
                    if (IdContainer == Command || IdContainer.IsSubContainerOf(Command))
                        Out.Add(Idx.Identifier);
                }
            });
        }

        public static Identifier[] GetUsedCommandIdentifiers(Command Command, ExpressionNode Node)
        {
            var List = new List<Identifier>();
            GetUsedCommandIdentifiers(Command, Node, List);
            return List.ToArray();
        }

        public static NCNeededRunBefore GetNeededRunBefore(Command Command, ExpressionNode Node)
        {
            //var Identifiers = GetUsedCommandIdentifiers(Command, Node);
            return new NCNeededRunBefore(Node/*, Identifiers*/);
        }

        public static void GetExpressionRunBefores(Command Command, ExpressionNode Node, List<NCNeededRunBefore> Out)
        {
            for (var i = 0; i < Node.LinkedNodes.Count; i++)
                GetExpressionRunBefores(Command, Node.LinkedNodes[i].Node, Out);

            if (Node.Children != null)
            {
                for (var i = 0; i < Node.Children.Length; i++)
                    GetExpressionRunBefores(Command, Node.Children[i], Out);
            }

            if (Node is OpExpressionNode)
            {
                var OpNode = Node as OpExpressionNode;
                var Op = OpNode.Operator;
                var Ch = OpNode.Children;

                if (Op == Operator.Cast)
                {
                    if (Node.CheckingMode == CheckingMode.Checked)
                    {
                        var NFrom = GetEquivalentNumberType(Ch[0].Type);
                        var NTo = GetEquivalentNumberType(Node.Type);
                        if (NFrom != null && NTo != null)
                        {
                            if (NFrom.Size > NTo.Size || (NFrom is SignedType && NTo is UnsignedType))
                                Out.Add(GetNeededRunBefore(Command, Ch[0]));
                        }
                    }
                }
                else if (IsOverflowableOp(Op))
                {
                    if (Node.CheckingMode == CheckingMode.Checked)
                    {
                        if (GetEquivalentNumberType(Node.Type) != null)
                            Out.Add(GetNeededRunBefore(Command, Node));
                    }
                }
                else if (Operators.IsNewOp(Op))
                {
                    if (!Node.LinkedNodes.TrueForAll(x => !(x.Node is InitializationNode)))
                    {/*
                        var IdList = new List<Identifier>();
                        for (var i = 0; i < Node.LinkedNodes.Count; i++)
                        {
                            var LNode = Node.LinkedNodes[i].Node;
                            if (LNode is InitializationNode)
                                LNode.ForEachChildren(x => GetUsedCommandIdentifiers(Command, x, IdList));
                        }
                        */
                        Out.Add(new NCNeededRunBefore(Node/*, IdList.ToArray()*/));
                    }
                }
            }
        }

        public static NCExpressionRunBefores GetExpressionRunBefores(Command Command, ExpressionNode Node)
		{
            var Ret = new NCExpressionRunBefores();
            var List = new List<NCNeededRunBefore>();
            GetExpressionRunBefores(Command, Node, List);
            Ret.NeededRunBefores = List.ToArray();
            return Ret;
		}
	}
}