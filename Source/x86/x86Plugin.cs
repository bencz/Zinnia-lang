using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Numerics;

namespace Zinnia.x86
{
	public class x86Plugin : ExpressionPlugin
	{
		public x86Architecture Arch;
		public x86GlobalContainerData GlobalData;
		public x86DataList AllUsedData;
		public x86NodeFlags RootFlags;
		public int ExpressionIndex;
		public x86FuncScopeData FSData;
        public NativeCode.NCPlugin NCPlugin;

		public Identifier GetHelperId(string Name, GetIdOptions Options)
		{
			return GlobalData.GetHelperId(Name, Options);
		}

		public Identifier GetHelperId(string Name)
		{
			return GetHelperId(Name, GetIdOptions.Default);
		}

		public Function GetHelperFunction(string Name)
		{
			var Options = GetIdOptions.Default;
			Options.Func = x => x is Function;

			return GetHelperId(Name, Options) as Function;
		}

		public Variable GetHelperVariable(string Name)
		{
			var Options = GetIdOptions.Default;
			Options.Func = x => x is Variable;

			return GetHelperId(Name, Options) as Variable;
		}

		public ExpressionNode CreateLinkingNode(ExpressionNode Node, ExpressionNode Parent,
			x86DataLocation AssignPos, bool DontCallNewNode = false, x86LinkedNodeFlags Flags = x86LinkedNodeFlags.None)
		{
			if (!(Node.Type.RealId is VoidType))
				Flags |= x86LinkedNodeFlags.AllocateData;

			var Linked = new LinkedExprNode(Node);
			Linked.Data.Set(new x86LinkedNodeData(AssignPos, Flags));
			var Ret = (ExpressionNode)new LinkingNode(Linked, Parent.Code);

			if (!DontCallNewNode)
			{
				if ((Ret = this.Parent.NewNode(Ret)) == null)
					return null;
			}
			else
			{
				Ret.Type = Node.Type;
				Ret.Data.Set(new x86NodeData());
			}

			if (Ret is LinkingNode)
				Parent.LinkedNodes.Add(Linked);

			return Ret;
		}

		void SetTempCantBe(ExpressionNode Node, Action<x86DataList> Action)
		{
			var Data = Node.Data.Get<x86NodeData>();
			if (Data.TempCantBe == null)
				Data.TempCantBe = new x86DataList(Arch);

			Action(Data.TempCantBe);

			if (Node.Children != null)
			{
				for (var i = 0; i < Node.Children.Length; i++)
				{
					var Ch = Node.Children[i];

					x86DataLocCalcHelper.ForeachIndexMemberNode(Ch, x =>
					{
						var xData = x.Data.Get<x86NodeData>();
						if (xData.TempCantBe == null)
							xData.TempCantBe = new x86DataList(Arch);

						Action(xData.TempCantBe);
					});
				}
			}
		}

		public void OnNodeFinishing(ExpressionNode Node, ref int ExecutionNumber)
		{
			var Data = Node.Data.Get<x86NodeData>();
			if ((Data.Flags & x86NodeFlags.AllocateLocation) != 0)
				Data.Properties = x86DataLocCalcHelper.GetDataProperties(Node);
			
			var Op = Expressions.GetOperator(Node);
			if (Node.Children != null && Op != Operator.Call && Op != Operator.NewObject)
			{
				for (var i = 0; i < Node.Children.Length; i++)
				{
					var Ch = Node.Children[i];
					if (Ch is LinkingNode)
					{
						var Linking = Ch as LinkingNode;
						var LNode = Linking.LinkedNode;
						var LData = LNode.Data.Get<x86LinkedNodeData>();
						if (LData.Specified != null)
							SetTempCantBe(Node, x => x.SetUsed(LData.Specified));
					}
					else
					{
						var ChData = Ch.Data.Get<x86NodeData>();
						if (ChData.Output != null && ChData.Output.DataType != x86DataLocationType.None)
							SetTempCantBe(Node, x => x.SetUsed(ChData.Output));
					}
				}
			}

			if (Node is IdExpressionNode)
			{
				var IdNode = Node as IdExpressionNode;
				var Id = IdNode.Identifier.RealId as LocalVariable;
				if (Id != null)
				{
					var IdData = Id.Data.Get<x86IdentifierData>();
					if (Id.Container == Container || (State.Flags & CompilerStateFlags.DebugMode) != 0)
					{
						IdData.Flags |= x86IdentifierFlags.WholeContainerUsed;
					}
					else if ((IdData.Flags & x86IdentifierFlags.WholeContainerUsed) == 0)
					{
						var ContainerIndex = Id.Container.GetIndirectChildIndex(Container);
						if (ContainerIndex == -1) throw new ApplicationException();

						var Ref = new x86IdReference(ContainerIndex, ExpressionIndex, ExecutionNumber);
						if ((IdData.Flags & x86IdentifierFlags.HasReferenceRange) == 0)
						{
							IdData.Flags |= x86IdentifierFlags.HasReferenceRange;
							IdData.ReferenceRange.Set(Ref);
						}
						else
						{
							IdData.ReferenceRange.Add(Ref);
						}
					}
				}
			}
			else if (Node is OpExpressionNode)
			{
				var OpNode = Node as OpExpressionNode;
				var Ch = OpNode.Children;

				if (Op == Operator.Call || Op == Operator.NewObject)
				{
					var FuncType = Ch[0].Type.RealId as TypeOfFunction;
					var RetType = FuncType.RetType;
					var CallConv = FuncType.CallConv;
					var x86CallConv = Arch.GetCallingConvention(CallConv);
					var DS = new x86DataSequence(Arch, x86CallConv.ParameterSequence);

					var Mask = Arch.RegisterMask;
					if (RetType is StructType || RetType is NonrefArrayType)
					{
						var R = DS.GetGRegisterIndex();
						if (R != -1) SetTempCantBe(Node, x => x.GRegisters.SetUsed(R, Mask));
					}

					if (x86Expressions.NeedSelfParameter(Node))
					{
						var R = DS.GetGRegisterIndex();
						if (R != -1) SetTempCantBe(Node, x => x.GRegisters.SetUsed(R, Mask));
					}

					Arch.ProcessRegisterParams(FuncType.GetTypes(), CallConv, (i, Pos) =>
						SetTempCantBe(Node, x => x.SetUsed(Pos)), DS);
				}
			}

			if (Expressions.GetOperator(Node) == Operator.Assignment)
			{
				var Ch = Node.Children;
				if (Ch[0] is IdExpressionNode)
				{
					for (var i = 0; i < Ch[0].LinkedNodes.Count; i++)
					{
						var LNode = Ch[0].LinkedNodes[i].Node;
						OnNodeFinishing(LNode, ref ExecutionNumber);
					}
				}
				else
				{
					OnNodeFinishing(Ch[0], ref ExecutionNumber);
				}

				OnNodeFinishing(Ch[1], ref ExecutionNumber);
				if (Ch[0] is IdExpressionNode)
					OnNodeFinishing(Ch[0], ref ExecutionNumber);
			}
			else
			{
				for (var i = 0; i < Node.LinkedNodes.Count; i++)
				{
					var LNode = Node.LinkedNodes[i].Node;
					OnNodeFinishing(LNode, ref ExecutionNumber);
				}

				if (Node.Children != null)
				{
					for (var i = 0; i < Node.Children.Length; i++)
						OnNodeFinishing(Node.Children[i], ref ExecutionNumber);
				}
			}

			Data.ExecutionNumber = ExecutionNumber;
			ExecutionNumber++;
		}

		public x86Plugin(PluginRoot Parent)
			: base(Parent)
		{
			this.Arch = State.Arch as x86Architecture;

			var Global = Container.GlobalContainer;
			GlobalData = Global.Data.Get<x86GlobalContainerData>();
            NCPlugin = Parent.GetPlugin<NativeCode.NCPlugin>();
		}

		public override bool Begin()
		{
			if (!base.Begin()) return false;

			RootFlags = x86NodeFlags.None;
			AllUsedData = null;
			FSData = Container.FunctionScope.Data.Get<x86FuncScopeData>();
			return true;
		}

		public override PluginResult End(ref ExpressionNode Node)
		{
			if (Expressions.GetOperator(Node) == Operator.Tuple)
			{
				var LData = Node.Data.Get<x86NodeData>();
				LData.Flags &= ~x86NodeFlags.AllocateLocation;
			}

			if (Node.IsMoveAndOp(false))
			{
				Node = CreateMoveNode(Node);
				if (Node == null) return PluginResult.Failed;
			}

			var ExecutionIndex = 0;
			OnNodeFinishing(Node, ref ExecutionIndex);

			var Data = Node.Data.Get<x86NodeData>();
			Data.Container = Container;
			Data.AllNodes = Node.GetNodes();
			Data.Flags |= RootFlags;

			var FS = Container.FunctionScope;
			var FSData = FS.Data.Get<x86FuncScopeData>();
			FSData.Expressions.Add(Node);
			return PluginResult.Succeeded;
		}

		bool IsZero(ExpressionNode Node)
		{
			return IsConstValue(Node, 0);
		}

		bool IsConstValue(ExpressionNode Node, BigInteger Value)
		{
			var ConstNode = Node as ConstExpressionNode;
			if (ConstNode == null) return false;

			var ConstVal = ConstNode.Value;
			if (ConstVal is ZeroValue) return true;
			if (ConstVal is IntegerValue)
				return (ConstVal as IntegerValue).Value == Value;

			return false;
		}

		public PluginResult NewOpNode_NonFloat(ref ExpressionNode Node)
		{
			var OpNode = Node as OpExpressionNode;
			var Ch = OpNode.Children;
			var Op = OpNode.Operator;
			var Data = Node.Data.Get<x86NodeData>();
			var Type = Ch[0].Type.RealId as Type;

			//--------------------------------------------------------------------------------------
			if (Operators.IsShift(Op))
			{
				if (Ch[1] is ConstExpressionNode)
				{
					var ConstCh1 = Ch[1] as ConstExpressionNode;
					if (ConstCh1.Value != null)
						(ConstCh1.Value as IntegerValue).Value %= Type.Size * 8;
				}
				else
				{
					var OpCh1 = Ch[1] as OpExpressionNode;
					if (OpCh1 != null && Operators.IsCast(OpCh1.Operator))
					{
						var Ch1Ch0Type = OpCh1.Children[0].Type.RealId as Type;
						if (Ch1Ch0Type.Size < Type.Size) Ch[1] = OpCh1.Children[0];
					}

					if (Data.UsedDataBySelf == null) Data.UsedDataBySelf = new x86DataList(Arch);
					Data.UsedDataBySelf.GRegisters.SetUsed(1, new x86RegisterMask(1));

					var Ch1Data = Ch[1].Data.Get<x86NodeData>();
					Ch1Data.PreferredOutput = new x86GRegLocation(Arch, 1, Arch.RegSize);
				}

				if (Type.Size > Arch.RegSize)
				{
					if (!(Ch[1] is ConstExpressionNode))
					{
						if (Data.PreAllocate == null)
							Data.PreAllocate = new x86DataList(Arch);

						Data.PreAllocate.GRegisters.SetUsed(1, new x86RegisterMask(1));
					}

					Data.Flags |= x86NodeFlags.AllocateLocation;
					Data.Flags |= x86NodeFlags.SaveChResults;
					if (!AdjustRegs(OpNode, false))
						return PluginResult.Failed;
				}
				else
				{
					Data.SameAllocationAs = 0;
					Data.SameAllocationAsType = x86SameAllocationAsType.Specified;
					Data.Flags |= x86NodeFlags.AllocateLocation;
					Data.Flags |= x86NodeFlags.SaveChResults;
					if (!AdjustRegs(OpNode, true))
						return PluginResult.Failed;
				}

				Data.OriginalShiftSize = Type.Size;
			}

			//--------------------------------------------------------------------------------------
			else if (Operators.IsBitwise(Op) || Op == Operator.Add || Op == Operator.Subract)
			{
				if (Op == Operator.Add && NeedSwap(Ch))
				{
					OpNode.Swap();
					Op = OpNode.Operator;
				}

				Data.SameAllocationAs = 0;
				Data.SameAllocationAsType = x86SameAllocationAsType.Specified;
				Data.Flags |= x86NodeFlags.AllocateLocation;
				Data.Flags |= x86NodeFlags.SaveChResults;
				if (!AdjustRegs(OpNode, true))
					return PluginResult.Failed;
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.Multiply || Op == Operator.Divide || Op == Operator.Modolus)
			{
				if (Op == Operator.Multiply && NeedSwap(Ch))
				{
					OpNode.Swap();
					Op = OpNode.Operator;
				}

				var ConstCh1 = Ch[1] as ConstExpressionNode;
				if (ConstCh1 != null && (Op != Operator.Modolus || Type is UnsignedType))
				{
					var V = ConstCh1.Integer;
					if (Helper.Pow2(V) == V)
					{
						Operator NewOp;
						ConstValue Val;
						if (Op == Operator.Modolus)
						{
							NewOp = Operator.And;
							Val = new IntegerValue(V - 1);
						}
						else
						{
							NewOp = Op == Operator.Multiply ? Operator.ShiftLeft : Operator.ShiftRight;
							Val = new IntegerValue(Helper.Pow2Sqrt(V));
						}

						var CNode = Parent.NewNode(new ConstExpressionNode(Ch[1].Type, Val, Node.Code));
						if (CNode == null) return PluginResult.Failed;

						Ch = new ExpressionNode[] {Ch[0], CNode };
						Node = Parent.NewNode(new OpExpressionNode(NewOp, Ch, Node.Code));
						if (Node != null) return PluginResult.Ready;
						return PluginResult.Failed;
					}
				}

				var Size = (Node.Type.RealId as Type).Size;
				if (Size > Arch.RegSize)
				{
					var FuncName = (string)null;
					if (Op == Operator.Multiply) FuncName = "LongMul";
					else if (Op == Operator.Divide) FuncName = Type is UnsignedType ? "ULongDiv" : "LongDiv";
					else if (Op == Operator.Modolus) FuncName = Type is UnsignedType ? "ULongMod" : "LongMod";
					else throw new ApplicationException();

					var Function = GetHelperFunction(FuncName);
					if (Function == null) return PluginResult.Failed;

					if (Op == Operator.Multiply)
					{
						var FuncType = Function.TypeOfSelf.RealId as TypeOfFunction;
						Ch[0].Type = FuncType.Children[1].Children[0];
						Ch[0].Flags |= ExpressionFlags.FixedType;

						Ch[1].Type = FuncType.Children[2].Children[0];
						Ch[1].Flags |= ExpressionFlags.FixedType;
					}

					Node = Expressions.Call(Node.Code, Parent, Function, Ch);
					if (Node == null) return PluginResult.Failed;

					Node.Type = OpNode.Type;
					Node.Flags |= ExpressionFlags.FixedType;
					return PluginResult.Ready;
				}

				/* imul 1:
				*		dst: r16/r32
				*		src: m16/m32/r16/r32/i16/i32
				*		
				* imul 2 / idiv / div:
				*		dst: accumulator
				*		src: m8/m16/m32/r8/r16/r32
				*		
				* remainder (except imul 1):
				*		byte: ah
				*		word: dx
				*		dword: edx
				*/

				Data.Flags |= x86NodeFlags.SaveChResults;
				if (!AdjustRegs(OpNode, true))
					return PluginResult.Failed;

				if (Op != Operator.Multiply || Size == 1)
				{
					if (Ch[1] is ConstExpressionNode)
					{
						var C = Ch[1] as ConstExpressionNode;
						Ch[1] = Container.GlobalContainer.CreateConstNode(C, Parent);
						if (Ch[1] == null || Parent.FinishNode(ref Ch[1]) == PluginResult.Failed)
							return PluginResult.Failed;
					}

					var Ch0Data = Ch[0].Data.Get<x86NodeData>();
					var Ch1Data = Ch[1].Data.Get<x86NodeData>();
					var MaxSize = Size == 1 ? 1 : Arch.RegSize;
					Ch0Data.PreferredOutput = new x86GRegLocation(Arch, 0, MaxSize);

					if (Op != Operator.Multiply)
					{
						if (Ch1Data.TempCantBe == null)
							Ch1Data.TempCantBe = new x86DataList(Arch);

						if (Size != 1) Ch1Data.TempCantBe.GRegisters.SetUsed(2, new x86RegisterMask(Size));
						else Ch1Data.TempCantBe.GRegisters.SetUsed(0, new x86RegisterMask(1, 1));
					}

					Data.UsedDataBySelf = new x86DataList(Arch);
					if (Size == 1)
					{
						Data.UsedDataBySelf.GRegisters.SetUsed(0, new x86RegisterMask(1, 1));
						Data.Output = new x86GRegLocation(Arch, 0, Op == Operator.Modolus ? 1 : 0, 1);
					}
					else
					{
						Data.UsedDataBySelf.GRegisters.SetUsed(0, new x86RegisterMask(Size));
						Data.UsedDataBySelf.GRegisters.SetUsed(2, new x86RegisterMask(Size));
						Data.Output = new x86GRegLocation(Arch, Op == Operator.Modolus ? 2 : 0, Size);
					}
				}
				else
				{
					if (Ch[1] is ConstExpressionNode)
					{
						Ch[0] = CutDownMoveNode(Ch[0]);
					}
					else
					{
						Data.SameAllocationAsType = x86SameAllocationAsType.Specified;
						Data.SameAllocationAs = 0;
					}

					Data.Flags |= x86NodeFlags.AllocateLocation;
					Data.DataCalcPos &= ~x86DataLocationType.Memory;
				}
			}

			//--------------------------------------------------------------------------------------
			else if (Operators.IsRelEquality(Op))
			{
				if (NeedSwap(Ch))
				{
					OpNode.Swap();
					Op = OpNode.Operator;
				}

                var Res = CreateBitTest(ref Node);
                if (Res != PluginResult.Succeeded) return Res;

				Data.Flags |= x86NodeFlags.SaveChResults;
				if (!AdjustRegs(OpNode, false)) return PluginResult.Failed;
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
				/*if (Type.Size > Arch.RegSize && Op == Operator.Neg && (Ch[0] is OpExpressionNode || Ch[1] is CastExpressionNode))
				{
					var Null = Expressions.CreateConstNode(Node.Code, new IntegerValue(0), Ch[0].Type, Root);
					if (Null == null) return PluginRes.Failed;

					var NCh = new ExpressionNode[] { Null, Ch[0] };
					Node = Root.NewNode(new OpExpressionNode(Operator.Subract, NCh, Node.Code));
					return Node == null ? PluginRes.Failed : PluginRes.Ready;
				}
				else
				{*/
				Data.SameAllocationAs = 0;
				Data.SameAllocationAsType = x86SameAllocationAsType.Specified;
				Data.Flags |= x86NodeFlags.AllocateLocation;

				if (!AdjustRegs_SingleOp(Node, Data))
					return PluginResult.Failed;
				//}
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.Cast)
			{
				return NewOpNode_NonFloat_Cast(ref Node);
			}

			//--------------------------------------------------------------------------------------
			else if (Op != Operator.Assignment)
			{
				return NewOpNode_Underlying(ref Node);
			}

			return PluginResult.Succeeded;
		}

        public PluginResult CreateBitTest(ref ExpressionNode Node)
        {
            var Op = Expressions.GetOperator(Node);
            if (Op == Operator.Equality || Op == Operator.Inequality)
            {
                var Ch = Node.Children;
                var OpCh0 = Ch[0] as OpExpressionNode;
                if (OpCh0 != null && OpCh0.Operator == Operator.BitwiseAnd && IsZero(Ch[1]))
                {
                    OpCh0.Children[0] = CutDownMoveNode(OpCh0.Children[0]);

                    var Ch0Data = OpCh0.Data.Get<x86NodeData>();
                    Ch0Data.Flags |= x86NodeFlags.SaveChResults;
                    OpCh0.Operator = Operator.Unknown;
                    OpCh0.Type = Node.Type;
                    OpCh0.Code = Node.Code;

                    if (Op == Operator.Equality) Ch0Data.Operator = x86Operator.BitTestZero;
                    else Ch0Data.Operator = x86Operator.BitTestNonZero;

                    Node = OpCh0;
                    return PluginResult.Ready;
                }
            }

            return PluginResult.Succeeded;
        }

		public ExpressionNode CutDownMoveNode(ExpressionNode Node)
		{
			var OpNode = Node as OpExpressionNode;
			if (OpNode == null || OpNode.Operator != Operator.Cast) return Node;
			if (!Node.Type.IsEquivalent(OpNode.Children[0].Type)) return Node;
			return OpNode.Children[0];
		}

		bool AdjustRegs_SingleOp(ExpressionNode Node, x86NodeData Data)
		{
			var Ch = Node.Children;
			var Ch0Data = Ch[0].Data.Get<x86NodeData>();
			if ((Ch0Data.Flags & x86NodeFlags.AllocateLocation) == 0)
			{
				Ch[0] = CreateMoveNode(Ch[0]);
				if (Ch[0] == null) return false;
			}

			return true;
		}

		public PluginResult NewOpNode_Float(ref ExpressionNode Node)
		{
			if (Arch.FloatingPointMode == x86FloatingPointMode.SSE)
				return NewOpNode_Float_SSE(ref Node);
			else if (Arch.FloatingPointMode == x86FloatingPointMode.FPU) 
				return NewOpNode_Float_FPU(ref Node);

			throw new NotImplementedException();
		}

		public PluginResult NewOpNode_Float_SSE(ref ExpressionNode Node)
		{
			var OpNode = Node as OpExpressionNode;
			var Ch = OpNode.Children;
			var Op = OpNode.Operator;
			var Data = Node.Data.Get<x86NodeData>();

			//--------------------------------------------------------------------------------------
			if (Operators.IsArithmetical(Op))
			{
				if (Op == Operator.Modolus)
					return GetModulusNode(ref Node);

				if (NeedSwap(Ch) && (Op == Operator.Add || Op == Operator.Multiply))
				{
					OpNode.Swap();
					Op = OpNode.Operator;
				}

				Data.SameAllocationAs = 0;
				Data.SameAllocationAsType = x86SameAllocationAsType.Specified;
				Data.DataCalcPos &= ~x86DataLocationType.Memory;
				Data.Flags |= x86NodeFlags.AllocateLocation;
				Data.Flags |= x86NodeFlags.SaveChResults;
				if (!AdjustRegs(OpNode, true))
					return PluginResult.Failed;
			}

			//--------------------------------------------------------------------------------------
			else if (Operators.IsRelEquality(Op))
			{
				if (NeedSwap(Ch))
				{
					OpNode.Swap();
					Op = OpNode.Operator;
				}

				Data.Flags |= x86NodeFlags.SaveChResults;
				if (!AdjustRegs(OpNode, false))
					return PluginResult.Failed;

				var Ch0Data = Ch[0].Data.Get<x86NodeData>();
				Data.DataCalcPos &= ~x86DataLocationType.Memory;
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.Negation)
			{
				var VarName = Expressions.IsDouble(Node) ? "DoubleNegateXOR" : "FloatNegateXOR";
				Data.NegateAbsBitmask = GetHelperVariable(VarName);
				if (Data.NegateAbsBitmask == null) return PluginResult.Failed;

				Data.SameAllocationAs = 0;
				Data.SameAllocationAsType = x86SameAllocationAsType.Specified;
				Data.Flags |= x86NodeFlags.AllocateLocation;
				Data.DataCalcPos &= ~x86DataLocationType.Memory;
				if (!AdjustRegs_SingleOp(Node, Data))
					return PluginResult.Failed;

				/*
				var Value = Node.IsDouble() ? (ConstValue)new DoubleValue(0) : new FloatValue(0);
				var Null = Expressions.CreateConstNode(Node.Code, Value, Ch[0].Type, Root);
				if (Null == null) return PluginRes.Failed;

				var NCh = new ExpressionNode[] { Null, Ch[0] };
				Node = Root.NewNode(new OpExpressionNode(Operator.Subract, NCh, Node.Code));
				return Node == null ? PluginRes.Failed : PluginRes.Ready;*/
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.Cast)
			{
				var RFrom = Ch[0].Type.RealId as Type;
				var To = Expressions.GetIdentifier(Ch[1]);
				var RTo = Node.Type.RealId as Type;
				var ToSize = RTo.Size;

				if (RFrom is FloatType)
				{
					if (RFrom.Size == ToSize)
					{
						Node = Ch[0];
						Node.Type = To;
						Node.Flags |= ExpressionFlags.FixedType;
						return PluginResult.Ready;
					}
					else
					{
						Data.SameAllocationAs = 0;
						Data.SameAllocationAsType = x86SameAllocationAsType.Specified;
						Data.Flags |= x86NodeFlags.AllocateLocation;
						Data.DataCalcPos &= ~x86DataLocationType.Memory;

						if (ToSize == 4 && RFrom.Size == 8)
						{
							var Ch0Data = Ch[0].Data.Get<x86NodeData>();
							var x86Op = Ch0Data.Operator;
							if (x86Op == x86Operator.Sin || x86Op == x86Operator.Cos ||
								x86Op == x86Operator.Sqrt || x86Op == x86Operator.RSqrt)
							{
								var Ch0Ch = Ch[0].Children;
								if (Expressions.GetOperator(Ch0Ch[0]) == Operator.Cast)
									if ((Ch0Ch[0].Type.RealId as Type).Size == 8 &&
										(Ch0Ch[0].Children[0].Type.RealId as Type).Size == 4)
									{
										Ch[0].Type = To;
										Ch[0].Flags |= ExpressionFlags.FixedType;
										Ch0Ch[0] = Ch0Ch[0].Children[0];
										Node = Ch[0];
										return PluginResult.Ready;
									}
							}

						}
					}
				}

				else if (RFrom is NonFloatType)
				{
					var Size = RFrom.Size;
					if ((RFrom is UnsignedType && Size == Arch.RegSize) || Size > Arch.RegSize)
					{
						string FunctionName;
						if (RFrom is UnsignedType)
						{
							if (Size == 4) FunctionName = "UIntToDouble";
							else if (Size == 8) FunctionName = "ULongToDouble";
							else throw new NotImplementedException();
						}
						else
						{
							if (Size == 8) FunctionName = "LongToDouble";
							else throw new NotImplementedException();
						}

						var Function = GetHelperFunction(FunctionName);
						if (Function == null) return PluginResult.Failed;

						Node = Expressions.Call(Node.Code, Parent, Function, Ch[0]);
						if (Node == null) return PluginResult.Failed;

						if (RTo.Size == 8) return PluginResult.Ready;
						else if (RTo.Size != 4) throw new ApplicationException();

						var Single = Container.GlobalContainer.CommonIds.Single;
						var SingleNode = Parent.NewNode(new IdExpressionNode(Single, Node.Code));
						if (SingleNode == null) return PluginResult.Failed;

						Ch = new ExpressionNode[] { Node, SingleNode };
						Node = Parent.NewNode(new OpExpressionNode(Operator.Cast, Ch, Node.Code));
						return Node == null ? PluginResult.Failed : PluginResult.Ready;
					}
					else
					{
						Data.DataCalcPos &= ~x86DataLocationType.Memory;
						Data.Flags |= x86NodeFlags.AllocateLocation;

						if (Size < Arch.RegSize)
						{
							var IntPtr = Container.GlobalContainer.CommonIds.GetIdentifier(typeof(SignedType), Arch.RegSize);
							var IntPtrNode = Parent.NewNode(new IdExpressionNode(IntPtr, Node.Code));
							if (IntPtrNode == null) return PluginResult.Failed;

							var Ch0Ch = new ExpressionNode[] { Ch[0], IntPtrNode };
							Ch[0] = Parent.NewNode(new OpExpressionNode(Operator.Cast, Ch0Ch, Node.Code));
							if (Ch[0] == null) return PluginResult.Failed;
						}
					}
				}

				else
				{
					throw new ApplicationException();
				}
			}

			//--------------------------------------------------------------------------------------
			else if (Op != Operator.Assignment)
			{
				return NewOpNode_Underlying(ref Node);
			}

			return PluginResult.Succeeded;
		}

		bool ZeroTestingNode(OpExpressionNode OpNode)
		{
			var Ch = OpNode.Children;
			var Op = OpNode.Operator;
			var Type = Ch[0].Type.RealId as Type;

			if (Arch.RegSize >= Type.Size && (Op == Operator.Equality || Op == Operator.Inequality))
			{
				if (Ch[0] is ConstExpressionNode && Ch[1] is IdExpressionNode)
				{
					OpNode.Swap();
					Op = OpNode.Operator;
				}

				var IdCh0 = Ch[0] as IdExpressionNode;
				var ConstCh1 = Ch[1] as ConstExpressionNode;
				if (ConstCh1 != null && ConstCh1.CDouble == 0 && IdCh0 != null)
				{
					OpNode.Operator = Op = Operator.Unknown;
					var Data = OpNode.Data.Get<x86NodeData>();
					Data.Operator = x86Operator.FloatZeroTesting;

					var Purpose = new x86TempGRegPurpose(x86TempGRegPurposeType.Plugin_FPUZeroTest);
					Data.NeededTempByPlugin.MustHaveGReg(Arch, Purpose, Arch.RegSize);
					return true;
				}
			}

			return false;
		}

		public PluginResult NewOpNode_Float_FPU(ref ExpressionNode Node)
		{
			var OpNode = Node as OpExpressionNode;
			var Ch = OpNode.Children;
			var Op = OpNode.Operator;
			var Data = Node.Data.Get<x86NodeData>();

			if (ZeroTestingNode(OpNode)) return PluginResult.Succeeded;

			//--------------------------------------------------------------------------------------
			if (Op == Operator.Assignment)
			{
				if (!AdjustRegs_FPU(OpNode, false, false))
					return PluginResult.Failed;
			}

			//--------------------------------------------------------------------------------------
			else if (Operators.IsArithmetical(Op))
			{
				if (Op == Operator.Modolus)
					return GetModulusNode(ref Node);

				if (NeedSwap(Ch))
				{
					OpNode.Swap();
					Op = OpNode.Operator;
				}

				if (!AdjustRegs_FPU(OpNode, true))
					return PluginResult.Failed;
			}

			//--------------------------------------------------------------------------------------
			else if (Operators.IsRelEquality(Op))
			{
				if (NeedSwap(Ch))
				{
					OpNode.Swap();
					Op = OpNode.Operator;
				}

				if (Data.UsedDataBySelf == null) Data.UsedDataBySelf = new x86DataList(Arch);
				Data.UsedDataBySelf.GRegisters.SetUsed(0, new x86RegisterMask(2));
				if (!AdjustRegs_FPU(OpNode, true)) return PluginResult.Failed;
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.Cast)
			{
				var RFrom = Ch[0].Type.RealId as Type;
				var To = Expressions.GetIdentifier(Ch[1]);
				var RTo = Node.Type.RealId as Type;

				if (RFrom is FloatType)
				{
					if (RFrom.Size == RTo.Size)
					{
						Node = Ch[0];
						Node.Type = To;
						Node.Flags |= ExpressionFlags.FixedType;
						return PluginResult.Ready;
					}
					else
					{
						Data.FPUItemsOnStack = 1;
						return PluginResult.Succeeded;
					}
				}
				else if (RFrom is NonFloatType)
				{
					var Size = RFrom.Size;

					if (RFrom is UnsignedType && Size > Arch.RegSize)
					{
						if (Size != 8) throw new ApplicationException();
						var Function = GetHelperFunction("ULongToDouble");
						if (Function == null) return PluginResult.Failed;

						Node = Expressions.Call(Node.Code, Parent, Function, Ch);
						return Node == null ? PluginResult.Failed : PluginResult.Ready;
					}
					else
					{
						//var ChData = Ch.Data.Get<x86NodeData>();
						//ChData.DataCalcPos &= x86DataLocType.Memory;

						var Ok = true;
						if (RFrom is UnsignedType) Ok = false;
						else if (Size == 1) Ok = false;

						if (!Ok)
						{
							Ch[0] = CreateSizeChangerNode(Ch[0], Size * 2);
							var Ch0Data = Ch[0].Data.Get<x86NodeData>();
							Ch0Data.DataCalcPos = x86DataLocationType.Memory;
						}

						Data.FPUItemsOnStack = 1;
					}
				}
				else
				{
					throw new ApplicationException();
				}
			}

			//--------------------------------------------------------------------------------------
			else if (Op != Operator.Negation)
			{
				return NewOpNode_Underlying(ref Node);
			}

			return PluginResult.Succeeded;
		}

		private PluginResult GetModulusNode(ref ExpressionNode Node)
		{
			var OpNode = Node as OpExpressionNode;
			var Ch = OpNode.Children;

			var Options = GetIdOptions.Default;
			var TypeList = new List<Identifier>() { Node.Type, Node.Type };
			Options.OverloadData = new OverloadSelectionData(TypeList);
			Options.Func = x => x is Function;

			var Function = GetHelperId("Modulus", Options) as Function;
			if (Function == null) return PluginResult.Failed;

			Node = Expressions.Call(Node.Code, Parent, Function, Ch);
			return Node == null ? PluginResult.Failed : PluginResult.Ready;
		}

		public PluginResult NewOpNode_Reference(ref ExpressionNode Node)
		{
			var OpNode = Node as OpExpressionNode;
			var Ch = OpNode.Children;
			var Op = OpNode.Operator;
			var Data = Node.Data.Get<x86NodeData>();

			//--------------------------------------------------------------------------------------
			if (Op == Operator.Cast)
			{
				var To = Expressions.GetIdentifier(Ch[1]);
				if (!Ch[0].Type.IsEquivalent(To))
					throw new ApplicationException();
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.Assignment)
			{
				var Ch0Data = Ch[0].Data.Get<x86NodeData>();
				Ch0Data.Flags &= ~x86NodeFlags.FlagsForRefIdentifier;
			}

			//--------------------------------------------------------------------------------------
			else if (Op != Operator.Assignment)
			{
				return NewOpNode_Underlying(ref Node);
			}

			return PluginResult.Succeeded;
		}

		public PluginResult NewOpNode_Pointer(ref ExpressionNode Node)
		{
			var OpNode = Node as OpExpressionNode;
			var Ch = OpNode.Children;
			var Op = OpNode.Operator;
			var Data = Node.Data.Get<x86NodeData>();

			//--------------------------------------------------------------------------------------
			if (Op == Operator.Add || Op == Operator.Subract)
			{
				if (Op == Operator.Add && NeedSwap(Ch))
				{
					OpNode.Swap();
					Op = OpNode.Operator;
				}

				var CallParent = false;
				for (var i = 0; i < 2; i++)
				{
					var Type = Ch[i].Type.RealId as Type;
					if (Type is NonFloatType)
					{
						if (Type.Size != Arch.RegSize)
						{
							Ch[i] = CreateSizeChangerNode(Ch[i], Arch.RegSize);
							if (Ch[i] == null) return PluginResult.Failed;

							CallParent = true;
						}
					}
				}

				if ((Data.Flags & x86NodeFlags.IndicesProcessed) == 0)
				{
					for (var i = 0; i < 2; i++)
					{
						var Type = Ch[i].Type.RealId as Type;
						if (Type is PointerType)
						{
							var PType = Type as PointerType;
							if (PType.Child.Size != 1)
							{
								Ch[1 - i] = MulBy(Ch[1 - i], PType.Child.Size);
								if (Ch[1 - i] == null) return PluginResult.Failed;

								CallParent = true;
							}
						}
					}

					Data.Flags |= x86NodeFlags.IndicesProcessed;
				}

				if (CallParent)
				{
					Node = Parent.NewNode(Node);
					return Node == null ? PluginResult.Failed : PluginResult.Ready;
				}

				Data.SameAllocationAs = 0;
				Data.SameAllocationAsType = x86SameAllocationAsType.Specified;
				Data.Flags |= x86NodeFlags.AllocateLocation;
				Data.Flags |= x86NodeFlags.SaveChResults;
				if (!AdjustRegs(OpNode, true))
					return PluginResult.Failed;
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.Index)
			{
				var pType = Ch[0].Type.RealId as PointerType;
				var Size = pType.Child.Size;

				if ((Ch[1].Type.RealId as Type).Size != Arch.RegSize)
				{
					Ch[1] = CreateSizeChangerNode(Ch[1], Arch.RegSize);
					if (Ch[1] == null) return PluginResult.Failed;
				}

				var AddToCh1 = false;
				var OpCh0 = Ch[0] as OpExpressionNode;
				var Ch0Op = OpCh0 != null ? OpCh0.Operator : Operator.Unknown;
				if (Ch[1] is ConstExpressionNode && (Ch0Op == Operator.Add || Ch0Op == Operator.Subract))
					AddToCh1 = true;

				if (Size != 1 && (Data.Flags & x86NodeFlags.IndicesProcessed) == 0)
				{
					if (Ch[1] is ConstExpressionNode || AddToCh1 || !x86Architecture.CanBeIndexRegScale(Size))
					{
						Ch[1] = MulBy(Ch[1], Size);
						if (Ch[1] == null) return PluginResult.Failed;

						Data.Flags |= x86NodeFlags.IndicesProcessed;

						Node = Parent.NewNode(Node);
						return Node == null ? PluginResult.Failed : PluginResult.Ready;
					}
					else
					{
						Data.Scale = (byte)Size;
					}
				}

				if (AddToCh1)
				{
					var PointerIndex = 0;
					if (OpCh0.Children[1].Type.RealId is PointerType)
						PointerIndex = 1;

					var NewCh = new ExpressionNode[] { Ch[1], OpCh0.Children[1 - PointerIndex] };
					var NewOpNode = Parent.NewNode(new OpExpressionNode(OpCh0.Operator, NewCh, Node.Code));
					if (NewOpNode == null) return PluginResult.Failed;

					Ch[0] = OpCh0.Children[PointerIndex];
					Ch[1] = NewOpNode;

					OpCh0 = Ch[0] as OpExpressionNode;
					if (OpCh0 != null && Operators.IsCast(OpCh0.Operator))
					{
						if (OpCh0.Children[0].Type.IsEquivalent(OpCh0.Type))
							Ch[0] = OpCh0.Children[0];
					}

					Node.Flags |= ExpressionFlags.FixedType;
					Node = Parent.NewNode(Node);
					return Node == null ? PluginResult.Failed : PluginResult.Ready;
				}

				Data.Flags |= x86NodeFlags.IndexMemberNode;
				Data.Flags |= x86NodeFlags.SaveChResults;
			}

			//--------------------------------------------------------------------------------------
			else if (Operators.IsRelEquality(Op))
			{
				if (NeedSwap(Ch))
				{
					OpNode.Swap();
					Op = OpNode.Operator;
				}

				Data.Flags |= x86NodeFlags.SaveChResults;
				if (!AdjustRegs(OpNode, false))
					return PluginResult.Failed;
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.Cast)
			{
				var RFrom = Ch[0].Type.RealId as Type;
				var To = Expressions.GetIdentifier(Ch[1]);
				var RTo = To.RealId as Type;

				if (RFrom is TypeOfFunction || RFrom is PointerType ||
					RFrom is ClassType || RFrom is NonFloatType)
				{
					if (RFrom.Size != RTo.Size)
						throw new ApplicationException();

					Node = Ch[0];
					Node.Type = To;
					Node.Flags |= ExpressionFlags.FixedType;
					return PluginResult.Ready;
				}

				else
				{
					throw new ApplicationException();
				}
			}

			//--------------------------------------------------------------------------------------
			else if (Op != Operator.Assignment)
			{
				return NewOpNode_Underlying(ref Node);
			}

			return PluginResult.Succeeded;
		}

		ExpressionNode Negate(ExpressionNode Node)
		{
			if (Node is OpExpressionNode)
			{
				var OpNode = Node as OpExpressionNode;
				var Op = OpNode.Operator;
				var Ch = OpNode.Children;

				if (Operators.IsBoolRet(Op))
				{
					OpNode.Operator = Operators.Negate(OpNode.Operator);
					if (Operators.IsRelEquality(Op)) return Node;

					for (var i = 0; i < Ch.Length; i++)
					{
						Ch[i] = Negate(Ch[i]);
						if (Ch[i] == null) return null;
					}

					return Node;
				}
				else if (Op == Operator.Unknown)
				{
					var Data = Node.Data.Get<x86NodeData>();
					if (x86Expressions.IsConditionOp(Data.Operator))
					{
						Data.Operator = x86Expressions.Negate(Data.Operator);
						return Node;
					}
				}
			}

			var False = Parent.NewNode(Constants.GetBoolValue(Container, false, Node.Code));
			var nCh = new ExpressionNode[] { Node, False };
			return Parent.NewNode(new OpExpressionNode(Operator.Equality, nCh, Node.Code));
		}

		public PluginResult NewOpNode_Bool(ref ExpressionNode Node)
		{
			var OpNode = Node as OpExpressionNode;
			var Ch = OpNode.Children;
			var Op = OpNode.Operator;
			var Data = Node.Data.Get<x86NodeData>();

			//--------------------------------------------------------------------------------------
			if (Operators.IsBoolRet(Op))
			{
				var Log = Operators.IsLogical(Op);
				if (!Log && Ch[0] is ConstExpressionNode && !(Ch[1] is ConstExpressionNode))
				{
					OpNode.Swap();
					Op = OpNode.Operator;
				}

				if (Operators.IsRelEquality(Op))
				{
					for (var i = 0; i < Ch.Length; i++)
						if (x86Expressions.IsCondition(Ch[i]))
						{
							Ch[i] = CreateMoveNode(Ch[i]);
							if (Ch[i] == null) return PluginResult.Failed;
						}
				}

				Data.Flags |= x86NodeFlags.SaveChResults;
				if (!AdjustRegs(OpNode, false))
					return PluginResult.Failed;
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.Not)
			{
				Node = Negate(Ch[0]);
				return Node == null ? PluginResult.Failed : PluginResult.Ready;
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.Cast)
			{
				var RFrom = Ch[0].Type.RealId;
				var To = Expressions.GetIdentifier(Ch[1]);
				var RTo = Node.Type.RealId;

				if (RFrom is BooleanType)
				{
					Node = Ch[0];
					return PluginResult.Ready;
				}
				else
				{
					throw new ApplicationException();
				}
			}

			//--------------------------------------------------------------------------------------
			else if (Op != Operator.Assignment)
			{
				return NewOpNode_Underlying(ref Node);
			}

			return PluginResult.Succeeded;
		}

		private ExpressionNode CutDownCallLinkedNode(ExpressionNode Node)
		{
			if (Node is LinkingNode)
			{
				var LNode = Node as LinkingNode;
				var LData = LNode.LinkedNode.Data.Get<x86LinkedNodeData>();
				if (LData != null && (LData.Flags & x86LinkedNodeFlags.CreatedForCall) != 0)
					return LNode.LinkedNode.Node;
			}

			return Node;
		}

		public PluginResult NewOpNode_RefArray(ref ExpressionNode Node)
		{
			var Data = Node.Data.Get<x86NodeData>();
			var OpNode = Node as OpExpressionNode;
			var Ch = OpNode.Children;
			var Op = OpNode.Operator;

			//--------------------------------------------------------------------------------------
			if (Op != Operator.Assignment)
			{
				return NewOpNode_Underlying(ref Node);
			}

			return PluginResult.Succeeded;
		}

		public PluginResult NewOpNode_String(ref ExpressionNode Node)
		{
			var Data = Node.Data.Get<x86NodeData>();
			var OpNode = Node as OpExpressionNode;
			var Ch = OpNode.Children;
			var Op = OpNode.Operator;

			//--------------------------------------------------------------------------------------
			if (Op != Operator.Assignment)
			{
				return NewOpNode_Underlying(ref Node);
			}

			return PluginResult.Succeeded;
		}

		public PluginResult NewOpNode_Underlying(ref ExpressionNode Node)
		{
			var OpNode = Node as OpExpressionNode;
			var Type = GetOpExpressionType(OpNode) as BuiltinType;
			if (Type != null && Type.UnderlyingStructureOrSelf != null)
				return NewOpNode_Structured(ref Node);

			if (Expressions.GetOperator(Node) == Operator.Assignment)
				return PluginResult.Succeeded;

			throw new ApplicationException();
		}

		public ExpressionNode CreateMemoryOnlyNode(ExpressionNode Node)
		{
			var Data = Node.Data.Get<x86NodeData>();
			var LocTypes = x86Identifiers.GetPossibleLocations(Node.Type.RealId as Type);

			if (LocTypes != x86DataLocationType.Memory || Node is ConstExpressionNode ||
				(Data.Output != null && !(Data.Output is x86MemoryLocation)))
			{
				var Id = Expressions.GetIdentifier(Node);
				if (Id != null)
				{
					var IdData = Id.Data.Get<x86IdentifierData>();
					IdData.Flags |= x86IdentifierFlags.CantBeInReg;
				}
				else
				{
					if ((Data.Flags & x86NodeFlags.AllocateLocation) != 0)
					{
						Data.DataCalcPos = x86DataLocationType.Memory;
					}
					else
					{
						return CreateMoveNode(Node, SpecifyDataCalcPos: true,
							DataCalcPos: x86DataLocationType.Memory);
					}
				}
			}

			return Node;
		}

		bool[] TupleAssignment_Link = new bool[] { false, true };
		public PluginResult NewOpNode_Structured(ref ExpressionNode Node)
		{
			var Data = Node.Data.Get<x86NodeData>();
			var OpNode = Node as OpExpressionNode;
			var Ch = OpNode.Children;
			var Op = OpNode.Operator;

			//--------------------------------------------------------------------------------------
			if (Op == Operator.Negation || Op == Operator.Complement || Operators.IsBitArithmShift(Op))
			{
				if (OpNode.Type.RealId is TupleType)
				{
					var Code = Node.Code;
					Node = Expressions.ExtractTupleOp(Node, Parent, (i, Args) =>
					{
						return Parent.NewNode(new OpExpressionNode(Op, Args, Code));
					});

					return Node == null ? PluginResult.Failed : PluginResult.Ready;
				}
				else
				{
					throw new ApplicationException();
				}
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.Member)
			{
				if (Ch[0].IsMoveAndOp())
				{
					Ch[0] = CreateMoveNode(Ch[0]);
					if (Ch[0] == null) return PluginResult.Failed;
				}

				var Ch0Type = Ch[0].Type.RealId as Type;
				if ((Ch0Type.TypeFlags & TypeFlags.ReferenceValue) == 0)
				{
					Ch[0] = CreateMemoryOnlyNode(Ch[0]);
					if (Ch[0] == null) return PluginResult.Failed;
				}

				Data.Flags |= x86NodeFlags.IndexMemberNode;
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.Assignment)
			{
				var OpCh0 = Ch[0] as OpExpressionNode;
				var Ch0Op = OpCh0 != null ? OpCh0.Operator : Operator.Unknown;

				var OpCh1 = Ch[1] as OpExpressionNode;
				var Ch1Op = OpCh1 != null ? OpCh1.Operator : Operator.Unknown;

				if (Ch0Op == Operator.Tuple)
				{
					TupleAssignment_Link[1] = false;
					if (Ch1Op == Operator.Tuple)
					{
						var HasNull = false;
						for (var i = 0; i < OpCh0.Children.Length; i++)
						{
							var Idi = Expressions.GetIdentifier(OpCh0.Children[i]);
							if (Idi == null) { HasNull = true; continue; }

							for (var j = 0; j < OpCh1.Children.Length; j++)
							{
								var Idj = Expressions.GetIdentifier(OpCh1.Children[j]);
								if (Idj == null) { HasNull = true; continue; }

								if (Idj.RealId == Idi.RealId)
								{
									TupleAssignment_Link[1] = true;
									goto BreakLabel;
								}
							}
						}

						BreakLabel: ;

						if (!HasNull && OpCh0.Children.Length == 2 && OpCh1.Children.Length == 2)
						{
							var OpCh0_Id0 = Expressions.GetIdentifier(OpCh0.Children[0]);
							var OpCh0_Id1 = Expressions.GetIdentifier(OpCh0.Children[1]);
							var OpCh1_Id0 = Expressions.GetIdentifier(OpCh1.Children[0]);
							var OpCh1_Id1 = Expressions.GetIdentifier(OpCh1.Children[1]);

							if (OpCh0_Id0.RealId == OpCh1_Id1.RealId && OpCh0_Id1.RealId == OpCh1_Id0.RealId)
							{
								var Id0RealId = OpCh0_Id0.TypeOfSelf.RealId;
								var Id1RealId = OpCh0_Id1.TypeOfSelf.RealId;

								if (Id0RealId is NonFloatType && Id1RealId is NonFloatType)
								{
									var Ch0Data = OpCh0.Data.Get<x86NodeData>();
									OpCh0.Operator = Operator.Unknown;
									Ch0Data.Operator = x86Operator.Swap;
									Node = OpCh0;
									return PluginResult.Ready;
								}
							}
						}
					}

					var Code = Node.Code;
					Node = Expressions.ExtractTupleOp(Node, Parent, (i, Args) =>
					{
						return Parent.NewNode(new OpExpressionNode(Operator.Assignment, Args, Code));
					}, TupleAssignment_Link);

					return Node == null ? PluginResult.Failed : PluginResult.Ready;
				}
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.NewObject)
			{
				var Type = Node.Type.RealId as Type;
				if (Type is StructType) Data.NeededTempByPlugin.SSERegIfNeeded(Arch, Type.Size);
				if (Ch.Length > 0) return NewCallOpNode(ref Node);
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.Equality || Op == Operator.Inequality)
			{
				if (Ch[0].Type.RealId is TupleType)
				{
					var Code = Node.Code;
					Node = Expressions.ExtractTupleOp(Node, Parent, (i, Args) =>
					{
						return Parent.NewNode(new OpExpressionNode(Op, Args, Code));
					});

					return Node == null ? PluginResult.Failed : PluginResult.Ready;
				}
				else
				{
					if (NeedSwap(Ch))
					{
						OpNode.Swap();
						Op = OpNode.Operator;
					}

					Data.Flags |= x86NodeFlags.SaveChResults;
					if (!AdjustRegs(OpNode, false))
						return PluginResult.Failed;
				}
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.Cast)
			{
				Data.DataCalcPos = x86DataLocationType.Memory;
				Data.Flags |= x86NodeFlags.AllocateLocation;
				Data.SameAllocationAsType = x86SameAllocationAsType.Specified;
				Data.SameAllocationAs = 0;

				var RFrom = Ch[0].Type.UnderlyingStructureOrRealId as Type;
				var To = Expressions.GetIdentifier(Ch[1]);
				var RTo = To.UnderlyingStructureOrRealId as Type;

				if (RFrom.IsEquivalent(RTo))
				{
					Node = Ch[0];
					Node.Type = RTo;
					Node.Flags |= ExpressionFlags.FixedType;
					return PluginResult.Ready;
				}

				if (RTo is TupleType && RFrom is TupleType)
				{
					var DstType = RTo as TupleType;
					var DstMembers = DstType.StructuredScope.IdentifierList;
					var Code = Node.Code;

					Node = Expressions.ExtractTupleOp(Node, Parent, (i, Nodes) =>
					{
						var NewType = DstMembers[i].TypeOfSelf;
						var NewTypeNode = Parent.NewNode(new IdExpressionNode(NewType, Code));
						if (NewTypeNode == null) return null;

						var NewCh = new ExpressionNode[] { Nodes[0], NewTypeNode };
						return Parent.NewNode(new OpExpressionNode(Operator.Cast, NewCh, Code));
					});

					return Node == null ? PluginResult.Failed : PluginResult.Ready;
				}
				else
				{
					throw new ApplicationException();
				}
			}

			//--------------------------------------------------------------------------------------
			else
			{
				throw new ApplicationException();
			}

			return PluginResult.Succeeded;
		}

		public PluginResult NewOpNode_Enum(ref ExpressionNode Node)
		{
			var OpNode = Node as OpExpressionNode;
			var Ch = OpNode.Children;
			var Op = OpNode.Operator;
			var Data = Node.Data.Get<x86NodeData>();
			
			//--------------------------------------------------------------------------------------
			if (Operators.IsBitwise(Op) && Node.Type is FlagType)
			{
				Data.SameAllocationAs = 0;
				Data.SameAllocationAsType = x86SameAllocationAsType.Specified;
				Data.Flags |= x86NodeFlags.AllocateLocation;
				Data.Flags |= x86NodeFlags.SaveChResults;
				if (!AdjustRegs(OpNode, true)) return PluginResult.Failed;
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.Equality || Op == Operator.Inequality)
			{
				if (NeedSwap(Ch))
				{
					OpNode.Swap();
					Op = OpNode.Operator;
				}

                var Res = CreateBitTest(ref Node);
                if (Res != PluginResult.Succeeded) return Res;

				Data.Flags |= x86NodeFlags.SaveChResults;
				if (!AdjustRegs(OpNode, false)) return PluginResult.Failed;
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.Cast)
			{
				var RFrom = Ch[0].Type.RealId;
				var To = Expressions.GetIdentifier(Ch[1]);
				var RTo = To.RealId as EnumType;

				//--------------------------------------------------------------------------------------
				if (RFrom is NonFloatType)
				{
					Node.Type = RTo.TypeOfValues;
					Ch[1] = Parent.NewNode(new IdExpressionNode(RTo.TypeOfValues, Node.Code));
					if (Ch[1] == null || Parent.NewNode(ref Node) == PluginResult.Failed)
						return PluginResult.Failed;

					Node.Type = To;
					Node.Flags |= ExpressionFlags.FixedType;
					return PluginResult.Ready;
				}

				//--------------------------------------------------------------------------------------
				else
				{
					throw new ApplicationException();
				}
			}

			//--------------------------------------------------------------------------------------
			else if (Op != Operator.Assignment)
			{
				return NewOpNode_Underlying(ref Node);
			}

			return PluginResult.Succeeded;
		}

		public PluginResult NewOpNode_Char(ref ExpressionNode Node)
		{
			var OpNode = Node as OpExpressionNode;
			var Ch = OpNode.Children;
			var Op = OpNode.Operator;
			var Data = Node.Data.Get<x86NodeData>();

			//--------------------------------------------------------------------------------------
			if (Op == Operator.Cast)
			{
				var RFrom = Ch[0].Type.RealId;
				var To = Expressions.GetIdentifier(Ch[1]);
				var RTo = To.RealId;

				if (RFrom is CharType)
				{
					var OldNode = Node;
					Node = Ch[0];
					Node.Type = OldNode.Type;
					Node.Flags |= ExpressionFlags.FixedType;
					return PluginResult.Ready;
				}
				else if (RFrom is NonFloatType)
				{
					var NewType = Container.GlobalContainer.CommonIds.UInt16;
					Ch[1] = Parent.NewNode(new IdExpressionNode(NewType, Node.Code));
					if (Ch[1] == null || Parent.NewNode(ref Node) == PluginResult.Failed)
						return PluginResult.Failed;

					Node.Type = To;
					Node.Flags |= ExpressionFlags.FixedType;
					return PluginResult.Ready;
				}
				else
				{
					throw new ApplicationException();
				}
			}

			//--------------------------------------------------------------------------------------
			else if (Operators.IsBoolRetBitArithmShift(Op))
			{
				var CharType = Container.GlobalContainer.CommonIds.Char;
				for (var i = 0; i < Ch.Length; i++)
				{
					var ChiType = Ch[i].Type.RealId as Type;
					if (ChiType.Size != CharType.Size)
					{
						Ch[i] = CreateSizeChangerNode(Ch[i], CharType.Size);
						if (Ch[i] == null) return PluginResult.Failed;
					}
				}

				return NewOpNode_NonFloat(ref Node);
			}

			//--------------------------------------------------------------------------------------
			else if (Op != Operator.Assignment)
			{
				return NewOpNode_Underlying(ref Node);
			}

			return PluginResult.Succeeded;
		}

		public PluginResult NewOpNode_TypeOfType(ref ExpressionNode Node)
		{
			var OpNode = Node as OpExpressionNode;
			var Ch = OpNode.Children;
			var Op = OpNode.Operator;
			var Data = Node.Data.Get<x86NodeData>();

			throw new ApplicationException();
			//return PluginResult.Succeeded;
		}

		public PluginResult NewOpNode_Function(ref ExpressionNode Node)
		{
			var OpNode = Node as OpExpressionNode;
			var Ch = OpNode.Children;
			var Op = OpNode.Operator;
			var Data = Node.Data.Get<x86NodeData>();

			//--------------------------------------------------------------------------------------
			if (Op == Operator.Cast)
			{
				var To = Expressions.GetIdentifier(Ch[1]);
				var RTo = To.RealId;
				var RFrom = Ch[0].Type.RealId;

				if (RFrom is PointerType)
				{
					Node = Ch[0];
					Node.Type = To;
					Node.Flags |= ExpressionFlags.FixedType;
					return PluginResult.Ready;
				}
				else
				{
					throw new ApplicationException();
				}
			}

			//--------------------------------------------------------------------------------------
			else if (Operators.IsRelEquality(Op))
			{
				if (NeedSwap(Ch))
				{
					OpNode.Swap();
					Op = OpNode.Operator;
				}

				Data.Flags |= x86NodeFlags.SaveChResults;
				if (!AdjustRegs(OpNode, false))
					return PluginResult.Failed;
			}

			//--------------------------------------------------------------------------------------
			else if (Op != Operator.Assignment)
			{
				return NewOpNode_Underlying(ref Node);
			}

			return PluginResult.Succeeded;
		}

		public void CheckIndexType(ExpressionNode Node)
		{
			var Type = Node.Type.RealId as NonFloatType;
			if (Type == null || Type.Size != Arch.RegSize)
				throw new ApplicationException();
		}

		public PluginResult NewOpNode_NonrefArray(ref ExpressionNode Node)
		{
			var OpNode = Node as OpExpressionNode;
			var Ch = OpNode.Children;
			var Op = OpNode.Operator;
			var Data = Node.Data.Get<x86NodeData>();

			//--------------------------------------------------------------------------------------
			if (Op == Operator.Cast)
			{
				var RFrom = Ch[0].Type.RealId;
				var To = Expressions.GetIdentifier(Ch[1]);
				var RTo = To.RealId;

				if (RFrom.IsEquivalent(RTo))
				{
					Data.SameAllocationAs = 0;
					Data.SameAllocationAsType = x86SameAllocationAsType.Specified;
					Data.Flags |= x86NodeFlags.AllocateLocation;
					Data.DataCalcPos = x86DataLocationType.Memory;
				}
				else
				{
					throw new ApplicationException();
				}
			}

			//--------------------------------------------------------------------------------------
			else if (Op != Operator.Assignment)
			{
				return NewOpNode_Underlying(ref Node);
			}

			return PluginResult.Succeeded;
		}

		public PluginResult NewOpNode_Type(ref ExpressionNode Node)
		{
			var OpNode = Node as OpExpressionNode;
			var Type = GetOpExpressionType(OpNode);

			if (Type is NonFloatType) return NewOpNode_NonFloat(ref Node);
			else if (Type is FloatType) return NewOpNode_Float(ref Node);
			else if (Type is EnumType) return NewOpNode_Enum(ref Node);
			else if (Type is BooleanType) return NewOpNode_Bool(ref Node);
			else if (Type is PointerType) return NewOpNode_Pointer(ref Node);
			else if (Type is ReferenceType) return NewOpNode_Reference(ref Node);
			else if (Type is StructuredType) return NewOpNode_Structured(ref Node);
			else if (Type is TypeOfType) return NewOpNode_TypeOfType(ref Node);
			else if (Type is TypeOfFunction) return NewOpNode_Function(ref Node);
			else if (Type is NonrefArrayType) return NewOpNode_NonrefArray(ref Node);
			else if (Type is CharType) return NewOpNode_Char(ref Node);
			else if (Type is StringType) return NewOpNode_String(ref Node);
			else if (Type is RefArrayType) return NewOpNode_RefArray(ref Node);
			else if (Type is ObjectType) return NewOpNode_Structured(ref Node);
			else throw new ApplicationException();
		}

		private static Type GetOpExpressionType(OpExpressionNode Node)
		{
			var Op = Node.Operator;
			var Ch = Node.Children;

			if (Operators.IsNewOp(Op) || Operators.IsCast(Op)) return Node.Type.RealId as Type;
			else if (Ch != null && Ch.Length > 0) return Ch[0].Type.RealId as Type;
			else return null;
		}

		public ExprRecResult NewCallOpNode_SpecialFunctions(ref ExpressionNode Node, x86NodeData Data)
		{
			if (Arch.FloatingPointMode == x86FloatingPointMode.SSE)
				return NewCallOpNode_SpecialFunctions_SSE(ref Node, Data);
			else if (Arch.FloatingPointMode == x86FloatingPointMode.FPU)
				return NewCallOpNode_SpecialFunctions_FPU(ref Node, Data);

			throw new NotImplementedException();
		}

		ExprRecResult NewCallOpNode_SpecialFunctions_SSE(ref ExpressionNode Node, x86NodeData Data)
		{
			var OpNode = Node as OpExpressionNode;
			var Ch = OpNode.Children;
			var Op = OpNode.Operator;

			//--------------------------------------------------------------------------------------
			var IdCh0 = Ch[0] as IdExpressionNode;
			if (IdCh0 != null && IdCh0.Identifier is Function)
			{
				var Func = IdCh0.Identifier as Function;
				var FuncType = Func.TypeOfSelf.RealId as TypeOfFunction;
				var RetType = FuncType.RetType;
				var Name = Func.AssemblyNameWithoutDecorations;
				var x86Op = x86Operator.Unknown;

				if (Name != null && Name.StartsWith("_System_Math_") && HasOnlyTTypes<FloatType>(Ch))
				{
					if (Ch.Length == 2)
					{
						if (Name == "_System_Math_Abs") x86Op = x86Operator.Abs;
						else if (Name == "_System_Math_Sqrt") x86Op = x86Operator.Sqrt;
						else if (Name == "_System_Math_Sin") x86Op = x86Operator.Sin;
						else if (Name == "_System_Math_Cos") x86Op = x86Operator.Cos;
					}
					else if (Ch.Length == 3)
					{
						if (Name == "_System_Math_Max") x86Op = x86Operator.Max;
						else if (Name == "_System_Math_Min") x86Op = x86Operator.Min;
					}

					if (x86Op != x86Operator.Unknown)
					{
						OpNode.Children = Ch = Ch.Slice(1);
						Data.DataCalcPos &= ~x86DataLocationType.Memory;
						OpNode.Operator = Op = Operator.Unknown;
						Data.Operator = x86Op;

						var Ch0Data = Ch[0].Data.Get<x86NodeData>();
						if (x86Op == x86Operator.Sqrt)
						{
							Data.Flags |= x86NodeFlags.AllocateLocation;
						}
						else if (x86Op == x86Operator.Abs)
						{
							var VarName = Expressions.IsDouble(Ch[0]) ? "DoubleAbsAND" : "FloatAbsAND";
							Data.NegateAbsBitmask = GetHelperVariable(VarName);
							if (Data.NegateAbsBitmask == null) return ExprRecResult.Failed;

							Data.Flags |= x86NodeFlags.AllocateLocation;
							Data.SameAllocationAsType = x86SameAllocationAsType.Specified;
							Data.SameAllocationAs = 0;
							if (!AdjustRegs_SingleOp(Node, Data))
								return ExprRecResult.Failed;
						}
						else if (x86Op == x86Operator.Min || x86Op == x86Operator.Max)
						{
							if (NeedSwap(Ch)) OpNode.Swap();

							Data.Flags |= x86NodeFlags.AllocateLocation;
							Data.Flags |= x86NodeFlags.SaveChResults;
							if (!AdjustRegs(OpNode, true))
								return ExprRecResult.Failed;
						}
						else if (x86Op == x86Operator.Sin || x86Op == x86Operator.Cos)
						{
							Data.Flags |= x86NodeFlags.AllocateLocation;
						}

						return ExprRecResult.Succeeded;
					}
				}
			}

			return ExprRecResult.Unknown;
		}

		ExprRecResult NewCallOpNode_SpecialFunctions_FPU(ref ExpressionNode Node, x86NodeData Data)
		{
			var OpNode = Node as OpExpressionNode;
			var Ch = OpNode.Children;
			var Op = OpNode.Operator;

			//--------------------------------------------------------------------------------------
			var IdCh0 = Ch[0] as IdExpressionNode;
			if (IdCh0 != null && IdCh0.Identifier is Function)
			{
				var Func = IdCh0.Identifier as Function;
				var FuncType = Func.TypeOfSelf.RealId as TypeOfFunction;
				var RetType = FuncType.RetType;
				var Name = Func.AssemblyNameWithoutDecorations;
				var x86Op = x86Operator.Unknown;

				if (Name != null && Name.StartsWith("_System_Math_") && HasOnlyTTypes<FloatType>(Ch))
				{
					if (Ch.Length == 2)
					{
						if (Name == "_System_Math_IsNaN") x86Op = x86Operator.IsNan;
						else if (Name == "_System_Math_IsInfinite") x86Op = x86Operator.IsInfinite;

						else if (Name == "_System_Math_Abs") x86Op = x86Operator.Abs;
						else if (Name == "_System_Math_Sqrt") x86Op = x86Operator.Sqrt;
						else if (Name == "_System_Math_Sin") x86Op = x86Operator.Sin;
						else if (Name == "_System_Math_Cos") x86Op = x86Operator.Cos;
						else if (Name == "_System_Math_Tan") x86Op = x86Operator.Tan;
						else if (Name == "_System_Math_Atan") x86Op = x86Operator.Atan;

						/*
						else if (Name == "_System_Math_Round") x86Op = x86Operator.Round;
						else if (Name == "_System_Math_Floor") x86Op = x86Operator.Floor;
						else if (Name == "_System_Math_Ceiling") x86Op = x86Operator.Ceiling;
						else if (Name == "_System_Math_Truncate") x86Op = x86Operator.Truncate;*/
					}
					else if (Ch.Length == 3)
					{
						if (Name == "_System_Math_Atan2") Data.Operator = x86Operator.Atan2;
					}

					if (x86Op != x86Operator.Unknown)
					{
						OpNode.Children = Ch = Ch.Slice(1);
						OpNode.Operator = Op = Operator.Unknown;
						Data.Operator = x86Op;

						var Ch0Data = Ch[0].Data.Get<x86NodeData>();
						Data.FPUItemsOnStack = Ch0Data.FPUItemsOnStack;

						if (x86Op == x86Operator.Atan2)
						{
							if (!AdjustRegs_FPU(OpNode, true))
								return ExprRecResult.Failed;
						}
						else if (x86Op == x86Operator.Atan || x86Op == x86Operator.Tan)
						{
							Data.UsedFPUStack = 1;
							if (Ch0Data.FPUItemsOnStack >= 8)
							{
								if ((Ch[0] = CreateMoveNode(Ch[0])) == null)
									return ExprRecResult.Failed;

								Ch[0].Type = Container.GlobalContainer.CommonIds.Double;
								Ch[0].Flags |= ExpressionFlags.FixedType;
							}
						}
						else if (x86Op == x86Operator.IsInfinite || x86Op == x86Operator.IsFinite)
						{
							var Infinity = new FloatValue(1.0F / 0.0F);
							var InfType = Container.GlobalContainer.CommonIds.Single;
							Data.InfinityVariable = Container.GlobalContainer.CreateExprConst(Infinity, InfType);
						}
						else if (x86Expressions.IsRoundOp(x86Op))
						{
							var ControlWordForRounding_Name = (string)null;
							if (x86Op == x86Operator.Round)
								ControlWordForRounding_Name = "RoundToNearestFPUControlWord";
							else if (x86Op == x86Operator.Floor)
								ControlWordForRounding_Name = "RoundDownFPUControlWord";
							else if (x86Op == x86Operator.Ceiling)
								ControlWordForRounding_Name = "RoundUpControlWord";
							else if (x86Op == x86Operator.Floor)
								ControlWordForRounding_Name = "TruncateFPUControlWord";
							else throw new ApplicationException();

							var DefaultFPUControlWord = GetHelperVariable("DefaultFPUControlWord");
							var ControlWordForRounding = GetHelperVariable(ControlWordForRounding_Name);
							if (DefaultFPUControlWord == null || ControlWordForRounding == null)
								return ExprRecResult.Failed;

							Data.DefaultFPUControlWord = DefaultFPUControlWord;
							Data.ControlWordForRounding = ControlWordForRounding;
						}

						return ExprRecResult.Succeeded;
					}
				}
			}

			return ExprRecResult.Unknown;
		}

		private ExpressionNode GetAbsoluteValue(ref ExpressionNode Node)
		{
			var LinkedNode = new LinkedExprNode(Node);

			var Zero = Parent.NewNode(Constants.GetIntValue(Container, 0, Node.Code));
			var CmpWith = Parent.NewNode(new LinkingNode(LinkedNode, Node.Code));
			if (Zero == null || CmpWith == null) return null;

			var CondCh = new ExpressionNode[] { Node, Zero };
			var Cond = Parent.NewNode(new OpExpressionNode(Operator.Less, CondCh, Node.Code));
			if (Cond == null) return null;

			var Then = Parent.NewNode(new LinkingNode(LinkedNode, Node.Code));
			var Else = Parent.NewNode(new LinkingNode(LinkedNode, Node.Code));
			if (Then == null || Else == null) return null;

			var ElseCh = new ExpressionNode[] { Else };
			Else = Parent.NewNode(new OpExpressionNode(Operator.Negation, ElseCh, Node.Code));
			if (Else == null) return null;

			var NewCh = new ExpressionNode[] { Cond, Then, Else };
			var Ret = new OpExpressionNode(Operator.Condition, NewCh, Node.Code);
			Ret.LinkedNodes = new List<LinkedExprNode>() { LinkedNode };

			return Node = Parent.NewNode(Ret);
		}

		private static bool TrueForAllParam(ExpressionNode[] Ch, Predicate<ExpressionNode> Func)
		{
			var Ret = true;
			for (var i = 1; i < Ch.Length; i++)
				if (!Func(Ch[i])) Ret = false;

			return Ret;
		}

		private static bool HasOnlyTTypes<T>(ExpressionNode[] Ch) where T : Type
		{
			return TrueForAllParam(Ch, x => x.Type.RealId is T);
		}

		public PluginResult NewCallOpNode(ref ExpressionNode Node)
		{
			var Data = Node.Data.Get<x86NodeData>();
			var Res = NewCallOpNode_SpecialFunctions(ref Node, Data);
			if (Res != ExprRecResult.Unknown)
			{
				if (Res == ExprRecResult.Succeeded) return PluginResult.Succeeded;
				else if (Res == ExprRecResult.Ready) return PluginResult.Ready;
				else if (Res == ExprRecResult.Failed) return PluginResult.Failed;
				else throw new ApplicationException();
			}

			var FS = Container.FunctionScope;
			var FSData = FS.Data.Get<x86FuncScopeData>();
            FSData.Flags |= x86FuncScopeFlags.FunctionCalled;

			var OpNode = Node as OpExpressionNode;
			var Ch = OpNode.Children;

			var CalledFuncType = Ch[0].Type.RealId as TypeOfFunction;
			var CalledCallConv = CalledFuncType.CallConv;
			var Calledx86CallConv = Arch.GetCallingConvention(CalledCallConv);

			var CallerFuncType = Container.FunctionScope.Type.RealId as TypeOfFunction;
			var CallerCallConv = CallerFuncType.CallConv;
			var Callerx86CallConv = Arch.GetCallingConvention(CallerCallConv);

			Data.UsedDataBySelf = new x86DataList(Arch);
			Data.UsedFPUStack = byte.MaxValue;

			Data.UsedDataBySelf.GRegisters.SetUsed(Calledx86CallConv.SavedGRegs.Inverse(), Arch.RegisterMask);
			Data.UsedDataBySelf.SSERegisters.SetUsed(Calledx86CallConv.SavedSSERegs.Inverse());

			//--------------------------------------------------------------------------------------
			var RetType = CalledFuncType.RetType as Type;
			if (RetType is EnumType) RetType = (RetType as EnumType).TypeOfValues;

			var S = RetType.Size;
			var NeedMoveRetType = false;
			if (x86Expressions.NeedReturnPointer(RetType))
			{
				NeedMoveRetType = true;
			}
			else if (RetType is FloatType)
			{
				if (Arch.FloatingPointMode == x86FloatingPointMode.SSE)
					Data.Output = new x86SSERegLocation(Arch, 0);
				else if (Arch.FloatingPointMode == x86FloatingPointMode.FPU)
					Data.FPUItemsOnStack = 1;
				else throw new NotImplementedException();
			}
			else if (RetType is PointerType || RetType is BooleanType || RetType is CharType ||
				RetType is TypeOfFunction || RetType is ReferenceType || RetType is ClassType ||
				RetType is StringType || RetType is ObjectType)
			{
				Data.Output = new x86GRegLocation(Arch, 0, S);
			}
			else if (RetType is NonFloatType)
			{
				x86DataLocation Pos = null;
				if (S <= Arch.RegSize) Pos = new x86GRegLocation(Arch, 0, S);
				else if (S == Arch.RegSize * 2) Pos = new x86MultiLocation(Arch, 0, 2);
				else throw new ApplicationException();
				Data.Output = Pos;
			}
			else if (!(RetType is VoidType))
			{
				throw new ApplicationException();
			}

			//--------------------------------------------------------------------------------------
			var PushedCh = Ch.ToList();
			var DS = new x86DataSequence(Arch, Calledx86CallConv.ParameterSequence);
			Data.ParameterBytes = 0;

			if (NeedMoveRetType)
			{
				var Reg = DS.GetGRegisterIndex();
				if (Reg != -1)
				{
					if (Data.PreAllocate == null) Data.PreAllocate = new x86DataList(Arch);
					Data.PreAllocate.GRegisters.SetUsed(Reg, Arch.RegisterMask);
				}
				else
				{
					Data.ParameterBytes += Arch.RegSize;
				}
			}

			if (x86Expressions.NeedSelfParameter(Node))
			{
				var Reg = DS.GetGRegisterIndex();
				if (Reg != -1)
				{
					if (Data.PreAllocate == null) Data.PreAllocate = new x86DataList(Arch);
					Data.PreAllocate.GRegisters.SetUsed(Reg, Arch.RegisterMask);
				}
				else
				{
					Data.ParameterBytes += Arch.RegSize;
				}
			}

			var Failed = false;
			var Types = CalledFuncType.GetTypes();

			var N = Node;
			Arch.ProcessRegisterParams(Types, CalledCallConv, (i, Pos) =>
			{
				var I1 = i + 1;
				var Chi = Ch[I1];
				PushedCh.Remove(Chi);

				if (Chi is OpExpressionNode)
				{/*
					var ChiData = Chi.Data.Get<x86NodeData>();
					if (ChiData.Output != null && ChiData.Output.DataType != x86DataLocType.None)
					{
						Chi = CreateMoveNode(Chi);
						if (Chi == null) { Failed = true; return; }
					}
					*/
					var Flags = x86LinkedNodeFlags.OnlyUseInParent | x86LinkedNodeFlags.CreatedForCall;
					Ch[I1] = Chi = CreateLinkingNode(Chi, N, Pos, true, Flags);
					if (Chi == null) Failed = true;
				}
			}, DS);

			if (Failed) return PluginResult.Failed;

			//--------------------------------------------------------------------------------------
			for (var i = 1; i < PushedCh.Count; i++)
			{
				var Type = PushedCh[i].Type.RealId as Type;
				var Align = Math.Max(Type.Align, Calledx86CallConv.ParameterAlignment);

				Data.ParameterBytes = DataStoring.AlignWithIncrease(Data.ParameterBytes, Align);
				Data.ParameterBytes += Type.Size;

				FSData.StackAlignment = Math.Max(FSData.StackAlignment, Align);
			}

			return PluginResult.Succeeded;
		}

		LocalVariable FindFirstlyMovedId(ExpressionNode Node)
		{
			var Data = Node.Data.Get<x86NodeData>();
			if ((Data.Flags & x86NodeFlags.AllocateLocation) == 0)
				return null;

			var Ret = (LocalVariable)null;
			x86DataLocCalcHelper.ForeachNodesWithSameOutput(Node, x =>
			{
				if (Ret != null) return;

				var Opx = Expressions.GetOperator(x);
				if (Opx != Operator.Cast) return;

				var IdxCh0 = x.Children[0] as IdExpressionNode;
				if (IdxCh0 != null) Ret = IdxCh0.Identifier.RealId as LocalVariable;
			});

			return Ret;
		}

		public PluginResult NewOpNode(ref ExpressionNode Node)
		{
			var OpNode = Node as OpExpressionNode;
			var Ch = OpNode.Children;
			var Op = OpNode.Operator;
			var Data = Node.Data.Get<x86NodeData>();

			//--------------------------------------------------------------------------------------
			if (Op == Operator.Unknown)
			{
				if (Data.Operator == x86Operator.Unknown)
					throw new ApplicationException();

				return PluginResult.Succeeded;
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.StackAlloc)
			{
				var FS = Container.FunctionScope;
				var FSData = FS.Data.Get<x86FuncScopeData>();

				Ch[0] = DataStoring.AlignWithIncrease(Parent, Ch[0], FSData.StackAlignment, Node.Code);
				if (Ch[0] == null) return PluginResult.Failed;

                FSData.Flags |= x86FuncScopeFlags.SaveFramePointer;
				Data.Flags |= x86NodeFlags.AllocateLocation;
				return PluginResult.Succeeded;
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.Index)
			{
				Data.Flags |= x86NodeFlags.IndexMemberNode;

				var Ch0Type = Ch[0].Type.RealId;
				if (Ch0Type is PointerType || Ch0Type is RefArrayType)
					Data.Flags |= x86NodeFlags.RefIndexMemberNode;
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.Member)
			{
				Data.Flags |= x86NodeFlags.IndexMemberNode;

				var Ch0Type = Ch[0].Type.RealId as Type;
				if ((Ch0Type.TypeFlags & TypeFlags.ReferenceValue) != 0)
					Data.Flags |= x86NodeFlags.RefIndexMemberNode;
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.Reinterpret)
			{
				var From = Ch[0].Type.RealId;
				var To = OpNode.Type.RealId;

				if (From is FloatType || To is FloatType)
				{
					if (Arch.FloatingPointMode == x86FloatingPointMode.FPU)
					{
						if (From is FloatType)
						{
							if (Ch[0] is OpExpressionNode)
							{
								Data.SameAllocationAs = 0;
								Data.SameAllocationAsType = x86SameAllocationAsType.Specified;
								Data.Flags |= x86NodeFlags.AllocateLocation;
								return PluginResult.Succeeded;
							}
						}
						else if (To is FloatType)
						{
							return PluginResult.Succeeded;
						}
					}
					else if (Arch.FloatingPointMode == x86FloatingPointMode.SSE)
					{
						Data.SameAllocationAs = 0;
						Data.SameAllocationAsType = x86SameAllocationAsType.Specified;
						Data.Flags |= x86NodeFlags.AllocateLocation;
						Data.DataCalcPos = x86DataLocationType.Memory;
						return PluginResult.Succeeded;
					}
					else
					{
						throw new NotImplementedException();
					}
				}

				Node = Ch[0];
				Node.Type = OpNode.Type;
				Node.Flags |= ExpressionFlags.FixedType;
				return PluginResult.Ready;
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.NewObject)
			{
				return NewOpNode_Type(ref Node);
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.Condition)
			{
				if (Node.Type.RealId is FloatType)
				{
					if (Arch.FloatingPointMode == x86FloatingPointMode.FPU)
					{
						Data.FPUItemsOnStack = 0;

						for (var i = 1; i < 3; i++)
							if (x86Expressions.NeedLoadFloat(Ch[i], true))
							{
								if ((Ch[i] = CreateFPULoadNode(Ch[i])) == null)
									return PluginResult.Failed;

								var ChiData = Ch[i].Data.Get<x86NodeData>();
								if (Data.FPUItemsOnStack < ChiData.FPUItemsOnStack)
									Data.FPUItemsOnStack = ChiData.FPUItemsOnStack;
							}
					}
					else if (Arch.FloatingPointMode == x86FloatingPointMode.SSE)
					{
						Data.SameAllocationAsType = x86SameAllocationAsType.All;
						Data.Flags |= x86NodeFlags.AllocateLocation;
					}
					else
					{
						throw new NotImplementedException();
					}
				}
				else
				{
					Data.SameAllocationAsType = x86SameAllocationAsType.All;
					Data.Flags |= x86NodeFlags.AllocateLocation;
				}

				return PluginResult.Succeeded;
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.Call)
			{
				return NewCallOpNode(ref Node);
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.Assignment)
			{
				var IdCh0 = Ch[0] as IdExpressionNode;
				if (IdCh0 != null && IdCh0.Identifier.RealId is LocalVariable)
				{
					var Id = IdCh0.Identifier.RealId as LocalVariable;/*
					if (FSData.RetAddressInParams && !FSData.DisableAlwaysReturned && Id is x86ReturnVariable)
					{
						var SrcId = Expressions.GetIdentifier(Ch[1]);
						if (SrcId != null) SrcId = SrcId.RealId as LocalVariable;

						if (FSData.AlwaysReturned != null && FSData.AlwaysReturned != SrcId)
						{
							FSData.DisableAlwaysReturned = true;
							FSData.AlwaysReturned = null;
						}
						else
						{
							FSData.AlwaysReturned = (LocalVariable)SrcId;
						}
					}
					*/
					var Preferred = FindFirstlyMovedId(Ch[1]);
					if (Preferred != null && Id.Container == Preferred.Container)
					{
						if (Id.TypeOfSelf.IsEquivalent(Preferred.TypeOfSelf))
						{
							var IdData = Id.Data.Get<x86IdentifierData>();
							if (IdData.PreferredIdForLocation == null)
								IdData.PreferredIdForLocation = Preferred;
						}
					}
				}

				Data.Flags |= x86NodeFlags.SaveChResults;
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.Address)
			{
				var IdCh0 = Ch[0] as IdExpressionNode;
				if (IdCh0 != null)
				{
					var Ch0Data = Ch[0].Data.Get<x86NodeData>();
					if ((Ch0Data.Flags & x86NodeFlags.IdentifierByRef) != 0)
					{
						var OldType = Node.Type;
						Node = Ch[0];
						Node.Type = OldType;
						Node.Flags |= ExpressionFlags.FixedType;
						Ch0Data.Flags &= ~x86NodeFlags.FlagsForRefIdentifier;
						return PluginResult.Ready;
					}

					var Id = IdCh0.Identifier.RealId;
					if (Id is LocalVariable)
					{
						var IdData = Id.Data.Get<x86IdentifierData>();
						IdData.Flags |= x86IdentifierFlags.CantBeInReg;
					}
					else if (Id is GlobalVariable)
					{
						var OldNode = Node;
						Node = Parent.NewNode(new LabelExpressionNode(Node.Code, Id.AssemblyName));
						if (Node == null) return PluginResult.Failed;

						Node.Type = OldNode.Type;
						Node.Flags |= ExpressionFlags.FixedType;
						return PluginResult.Ready;
					}
				}

				Data.Flags |= x86NodeFlags.AllocateLocation;
				return PluginResult.Succeeded;
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.Tuple || Op == Operator.Array)
			{
				return PluginResult.Succeeded;
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.Add || Op == Operator.Subract)
			{
				if (Ch[0].Type.RealId is PointerType || Ch[1].Type.RealId is PointerType)
					return NewOpNode_Pointer(ref Node);
			}

			return NewOpNode_Type(ref Node);
		}

		public PluginResult NewOpNode_NonFloat_Cast(ref ExpressionNode Node)
		{
			var OpNode = Node as OpExpressionNode;
			var Ch = OpNode.Children;
			var Data = Node.Data.Get<x86NodeData>();

			Data.Flags |= x86NodeFlags.AllocateLocation;
			Data.SameAllocationAsType = x86SameAllocationAsType.Specified;
			Data.SameAllocationAs = 0;

			var RFrom = Ch[0].Type.RealId as Type;
			var To = Expressions.GetIdentifier(Ch[1]);
			var RTo = To.RealId as Type;

			//--------------------------------------------------------------------------------------
			if (RFrom is FloatType)
			{
				if (Arch.FloatingPointMode == x86FloatingPointMode.FPU)
				{
					if (RTo.Size > Arch.RegSize && RTo is UnsignedType)
					{
						if (RTo.Size != 8) throw new NotImplementedException();
						var Function = GetHelperFunction("DoubleToULong");
						if (Function == null) return PluginResult.Failed;

						Node = Expressions.Call(Node.Code, Parent, Function, Ch[0]);
						return Node == null ? PluginResult.Failed : PluginResult.Ready;
					}

					if ((Arch.Extensions & x86Extensions.SSE3) == 0)
					{
						var Function = GetHelperFunction("DoubleToLong");
						if (Function == null) return PluginResult.Failed;

						Node = Expressions.Call(Node.Code, Parent, Function, Ch[0]);
						if (Node == null) return PluginResult.Failed;

						Node = CreateSizeChangerNode(Node, RTo.Size);
						return Node == null ? PluginResult.Failed : PluginResult.Ready;
					}
					else if (RTo.Size == 1 || RTo is UnsignedType)
					{
						var Global = Container.GlobalContainer;
						var NewCh1Type = Global.CommonIds.GetIdentifier(RTo.GetType(), RTo.Size * 2);
						if (NewCh1Type == null) return PluginResult.Failed;

						Ch[1] = Parent.NewNode(new IdExpressionNode(NewCh1Type, Node.Code));
						if (Ch[1] == null || Parent.NewNode(ref Node) == PluginResult.Failed)
							return PluginResult.Failed;

						Node = CreateSizeChangerNode(Node, RTo.Size);
						return Node == null ? PluginResult.Failed : PluginResult.Ready;
					}
				}
				else if (Arch.FloatingPointMode == x86FloatingPointMode.SSE)
				{
					var Size = RTo.Size;
					if ((RTo is UnsignedType && Size == Arch.RegSize) || Size > Arch.RegSize)
					{
						string FunctionName;
						if (RTo is UnsignedType)
						{
							if (Size == 4) FunctionName = "DoubleToUInt";
							else if (Size == 8) FunctionName = "DoubleToULong";
							else throw new NotImplementedException();
						}
						else
						{
							if (Size == 8) FunctionName = "DoubleToLong";
							else throw new NotImplementedException();
						}

						var Function = GetHelperFunction(FunctionName);
						if (Function == null) return PluginResult.Failed;

						Node = Expressions.Call(Node.Code, Parent, Function, Ch[0]);
						if (Node == null) return PluginResult.Failed;

						Node.Type = OpNode.Type;
						Node.Flags |= ExpressionFlags.FixedType;
						return PluginResult.Ready;
					}
					else
					{
						Data.Flags |= x86NodeFlags.AllocateLocation;
						Data.DataCalcPos &= ~x86DataLocationType.Memory;
						Data.SameAllocationAsType = x86SameAllocationAsType.None;

						if (Size < Arch.RegSize)
						{
							var IntType = Container.GlobalContainer.CommonIds.GetIdentifier(typeof(SignedType), Arch.RegSize);
							Ch[1] = Parent.NewNode(new IdExpressionNode(IntType, Node.Code));
							if (Ch[1] == null || Parent.NewNode(ref Node) == PluginResult.Failed) 
								return PluginResult.Failed;

							var ToTypeNode = Parent.NewNode(new IdExpressionNode(RTo, Node.Code));
							if (ToTypeNode == null) return PluginResult.Failed;

							var NewCh = new ExpressionNode[] { Node, ToTypeNode };
							Node = Parent.NewNode(new OpExpressionNode(Operator.Cast, NewCh, Node.Code));
							return Node == null ? PluginResult.Failed : PluginResult.Ready;
						}
					}
				}
				else
				{
					throw new NotImplementedException();
				}
			}

			//--------------------------------------------------------------------------------------
			else if (RFrom is NonFloatType || RFrom is CharType)
			{
				if (RFrom.Size == RTo.Size)
				{
					Node = Ch[0];
					Node.Type = OpNode.Type;
					Node.Flags |= ExpressionFlags.FixedType;
					return PluginResult.Ready;
				}
				else if (RFrom.Size < RTo.Size)
				{
					var OpCh0 = Ch[0] as OpExpressionNode;
					if (OpCh0 != null && Operators.IsBitwise(OpCh0.Operator))
					{
						Node = Ch[0];
						Node.Type = OpNode.Type;
						Node.Flags |= ExpressionFlags.FixedType;
						return PluginResult.Ready;
					}

					if (RFrom is SignedType && RTo.Size > Arch.RegSize)
					{
						Data.Output = new x86MultiLocation(Arch, 0, 2);
						Data.UsedDataBySelf = new x86DataList(Arch);
						Data.UsedDataBySelf.GRegisters.SetUsed(0, new x86RegisterMask(Arch.RegSize));
						Data.UsedDataBySelf.GRegisters.SetUsed(2, new x86RegisterMask(Arch.RegSize));
						Data.SameAllocationAsType = x86SameAllocationAsType.None;
					}
					else
					{
						Data.SameAllocationAs = 0;
						Data.SameAllocationAsType = x86SameAllocationAsType.Specified;
						Data.DataCalcPos &= ~x86DataLocationType.Memory;
					}
				}
				else
				{
					var OpCh0 = Ch[0] as OpExpressionNode;
					if (OpCh0 != null && Operators.IsShift(OpCh0.Operator) && RTo.Size % Arch.RegSize == 0)
					{
						Node = Ch[0];
						Node.Type = OpNode.Type;
						Node.Flags |= ExpressionFlags.FixedType;
						return PluginResult.Ready;
					}
				}
			}

			//--------------------------------------------------------------------------------------
			else if (RFrom is EnumType)
			{
				var EnumFrom = RFrom as EnumType;
				Ch[0].Type = EnumFrom.TypeOfValues;
				Ch[0].Flags |= ExpressionFlags.FixedType;
				return NewOpNode_NonFloat_Cast(ref Node);
			}

			//--------------------------------------------------------------------------------------
			else if (RFrom is PointerType)
			{
				Node = Ch[0];
				Node.Type = OpNode.Type;
				Node.Flags |= ExpressionFlags.FixedType;
				return PluginResult.Ready;
			}

			//--------------------------------------------------------------------------------------
			else
			{
				throw new ApplicationException();
			}

			return PluginResult.Succeeded;
		}

		public override PluginResult NewNode(ref ExpressionNode Node)
		{
			CutDownCallLinkedNodeFromCh(Node);
			InitLinkedNodes(Node);

			var Data = Node.Data.Get<x86NodeData>();
			Data.DataCalcPos = x86Identifiers.GetPossibleLocations(Node.Type);

			// ------------------------------------------------------------------------------------
			if (Node is IdExpressionNode)
			{
				var IdNode = Node as IdExpressionNode;
				var Id = IdNode.Identifier.RealId;

				if (Id is LocalVariable)
				{
					var IdData = Id.Data.GetOrCreate<x86IdentifierData>(Id);
					if (IdData.ReferenceCount != int.MaxValue)
					{
						var LoopCount = 0;
						Container.ForEachParent<Command>(x =>
						{
							if (Commands.IsLoopCommand(x.Type))
								LoopCount++;
						}, Container.FunctionScope);

						if (LoopCount >= 16)
						{
							IdData.ReferenceCount = int.MaxValue;
						}
						else
						{
							var AddVal = 1 << (LoopCount * 2);
							var NewVal = (long)IdData.ReferenceCount + AddVal;
							IdData.ReferenceCount = (int)NewVal;
							if (IdData.ReferenceCount != NewVal)
								IdData.ReferenceCount = int.MaxValue;
						}
					}
				}

				if (Id.TypeOfSelf.RealId is ReferenceType)
					Data.Flags |= x86NodeFlags.FlagsForRefIdentifier;
			}

			else if (Node is OpExpressionNode)
			{
				var Result = NewOpNode(ref Node);
				if (Result != PluginResult.Succeeded)
					return Result;
			}

			else if (Node is ConstExpressionNode)
			{
				var CNode = Node as ConstExpressionNode;
				if (Node.Type.RealId is StringType)
				{
					if (CNode.Value is StringValue)
					{
						var StringValue = CNode.Value as StringValue;
						var String = StringValue.Value;
						int Label;

						lock (GlobalData.ConstStrings)
						{
							var Res = GlobalData.ConstStrings.Find(x => x.String == String);
							if (Res.String != null)
							{
								Label = Res.Label;
							}
							else
							{
								Label = State.AutoLabel;
								GlobalData.ConstStrings.Add(new x86ConstString(String, Label));
							}
						}

						Data.Output = new x86NamedLabelPosition(Arch, "_" + Label);
					}
				}
			}

			// ------------------------------------------------------------------------------------
			if (!PostProcNode(Node)) 
				return PluginResult.Failed;

			var LNodes = Node.LinkedNodes;
			if (Data.Output != null && !LNodes.TrueForAll(x => (x.Flags & LinkedNodeFlags.PostComputation) == 0))
			{
				var NewNode = CreateMoveNode(Node);
				for (var i = 0; i < LNodes.Count; i++)
				{
					var LNode = LNodes[i];
					if ((LNode.Flags & LinkedNodeFlags.PostComputation) != 0)
					{
						Node.LinkedNodes.Remove(LNode);
						NewNode.LinkedNodes.Add(LNode);
					}
				}

				Node = NewNode;
				return PluginResult.Ready;
			}

			return PluginResult.Succeeded;
		}

		private static void InitLinkedNodes(ExpressionNode Node)
		{
			for (var i = 0; i < Node.LinkedNodes.Count; i++)
			{
				var LNode = Node.LinkedNodes[i];
				var LData = new x86LinkedNodeData();
				if ((LData.Flags & x86LinkedNodeFlags.CreatedForCall) != 0)
					throw new ApplicationException();

				if (!(LNode.Node.Type.RealId is VoidType) && LNode.LinkingCount > 0)
					LData.Flags |= x86LinkedNodeFlags.AllocateData;

				LNode.Data.Set(LData);
			}
		}

		private void CutDownCallLinkedNodeFromCh(ExpressionNode Node)
		{
			var NodeCopy = Node;
			Node.ReplaceChildren(x =>
			{
				var Ret = CutDownCallLinkedNode(x);
				if (Ret != x)
				{
					var Lx = x as LinkingNode;
					NodeCopy.LinkedNodes.Remove(Lx.LinkedNode);
				}

				return Ret;
			});
		}

		public bool PostProcNode(ExpressionNode Node)
		{
			var Data = Node.Data.Get<x86NodeData>();
			Data.NeededTempData = Data.NeededTempByPlugin;

			if ((Data.Flags & x86NodeFlags.AllocateLocation) != 0)
			{
				var Type = Node.Type.RealId as Type;
				var FS = Container.FunctionScope;
				var FSData = FS.Data.Get<x86FuncScopeData>();
				FSData.StackAlignment = Math.Max(FSData.StackAlignment, Type.Align);
			}

			if (Data.PreferredOutput != null)
				RootFlags |= x86NodeFlags.UseExistingLocs;

			if ((RootFlags & x86NodeFlags.UseExistingLocs) == 0)
			{
				if (Expressions.GetOperator(Node) == Operator.Assignment)
					RootFlags |= x86NodeFlags.UseExistingLocs;
			}

			CalcFPUUsedRegs(Data, Node);
			if (!CalcLinkedNodes(Data, Node)) return false;
			if (!CalcNodeUsedData(Data, Node)) return false;
			return true;
		}

		public bool CalcLinkedNodes(x86NodeData Data, ExpressionNode Node)
		{
			for (var i = 0; i < Node.LinkedNodes.Count; i++)
			{
				var LNode = Node.LinkedNodes[i];
				var LData = LNode.Data.Get<x86LinkedNodeData>();

				if ((LData.Flags & x86LinkedNodeFlags.AllocateData) != 0)
				{
					RootFlags |= x86NodeFlags.UseExistingLocs;
					RootFlags |= x86NodeFlags.LinkedNodesUsed;
				}
			}

			return true;
		}

		void CalcFPUUsedRegs(x86NodeData Data, ExpressionNode Node)
		{
			if ((Data.Flags & x86NodeFlags.AllocateLocation) != 0)
			{
				RootFlags |= x86NodeFlags.NeedAllocations;
				if ((Data.DataCalcPos & x86DataLocationType.Memory) == 0)
					RootFlags |= x86NodeFlags.NonMemoryUsed;
			}

			if (Data.UsedFPUStack < Data.FPUItemsOnStack)
				Data.UsedFPUStack = Data.FPUItemsOnStack;

			foreach (var Ch in Node.EnumChildren)
			{
				var ChData = Ch.Data.Get<x86NodeData>();
				if (ChData.UsedFPUStack > Data.UsedFPUStack)
					Data.UsedFPUStack = ChData.UsedFPUStack;
			}
		}

		bool CalcNodeUsedData(x86NodeData Data, ExpressionNode Node)
		{
			if (Data.UsedDataBySelf != null)
				RootFlags |= x86NodeFlags.EnableUsedData;

			// ----------------------------------------------------------------
			var UsedData = (x86DataList)null;
			if (Data.UsedDataBySelf != null)
			{
				if (UsedData == null)
					UsedData = new x86DataList(Arch);

				UsedData.SetUsed(Data.UsedDataBySelf);
			}

			if (Node.Children != null)
			{
				for (var i = 0; i < Node.Children.Length; i++)
				{
					var Ch = Node.Children[i];
					var ChData = Ch.Data.Get<x86NodeData>();
					if (ChData.UsedData != null)
					{
						if (UsedData == null)
							UsedData = new x86DataList(Arch);

						UsedData.SetUsed(ChData.UsedData);
					}
				}
			}

			// ----------------------------------------------------------------
			for (var i = Node.LinkedNodes.Count - 1; i >= 0; i--)
			{
				var e = Node.LinkedNodes[i];
				var LData = e.Node.Data.Get<x86NodeData>();

				if (LData.UsedData != null)
				{
					if (UsedData != null) UsedData.SetUsed(LData.UsedData);
					else UsedData = LData.UsedData.Copy();
				}
			}

			// ----------------------------------------------------------------
			if (UsedData != null)
			{
				if (Data.UsedData == null) Data.UsedData = UsedData;
				else Data.UsedData.SetUsed(UsedData);
			}

			return true;
		}

		private ExpressionNode CreateSizeChangerNode(ExpressionNode Ch, int NewSize)
		{
			var Type = Container.GlobalContainer.CommonIds.GetIdentifier(Ch.Type.GetType(), NewSize);
			var TypeNode = Parent.NewNode(new IdExpressionNode(Type, Ch.Code));
			if (TypeNode == null) return null;

			var CastCh = new ExpressionNode[] { Ch, TypeNode };
			return Parent.NewNode(new OpExpressionNode(Operator.Cast, CastCh, Ch.Code));
		}

		private ExpressionNode CreateMoveNode(ExpressionNode Node, bool AllocateData = true, bool ModCastNode = true,
			bool SpecifyDataCalcPos = false, x86DataLocationType DataCalcPos = x86DataLocationType.GRegMem)
		{
			var Type = Node.Type.RealId as Type;
			var OpNode = Node as OpExpressionNode;
			if (ModCastNode && OpNode != null && Operators.IsCast(OpNode.Operator))
			{
				var Data = Node.Data.Get<x86NodeData>();
				if (AllocateData) Data.Flags |= x86NodeFlags.AllocateLocation;

				if (SpecifyDataCalcPos) Data.DataCalcPos = DataCalcPos;
				else Data.DataCalcPos = x86Identifiers.GetPossibleLocations(Type);
			}
			else
			{
				if (!SpecifyDataCalcPos)
					DataCalcPos = x86Identifiers.GetPossibleLocations(Type);

				var TypeNode = Parent.NewNode(new IdExpressionNode(Type, Node.Code));
				if (TypeNode == null || Parent.FinishNode(ref TypeNode) == PluginResult.Failed)
					return null;

				var CastCh = new ExpressionNode[] { Node, TypeNode };
				Node = new OpExpressionNode(Operator.Cast, CastCh, Node.Code);
				Node.Type = Type;

				var Data = new x86NodeData();
				Node.Data.Set(Data);
				Data.DataCalcPos = DataCalcPos;

				if (AllocateData) Data.Flags |= x86NodeFlags.AllocateLocation;
				if (!PostProcNode(Node)) return null;
			}

			return Node;
		}

		ExpressionNode CreateFPULoadNode(ExpressionNode Node)
		{
			Node = CreateMoveNode(Node, false);
			if (Node == null) return null;

			var Data = Node.Data.Get<x86NodeData>();
			Data.FPUItemsOnStack = 1;
			return Node;
		}

		bool AdjustRegs_FPU(OpExpressionNode Node, bool Mod, bool RemoveIntCasts = true)
		{
			var Ch = Node.Children;
			if (x86Expressions.NeedLoadFloat(Ch[0], Mod))
			{
				if ((Ch[0] = CreateFPULoadNode(Ch[0])) == null)
					return false;
			}

			if (x86Expressions.NeedLoadFloat(Ch[1], false))
			{
				if ((Ch[1] = CreateFPULoadNode(Ch[1])) == null)
					return false;
			}

			var Data = Node.Data.Get<x86NodeData>();
			var Ch0Data = Ch[0].Data.Get<x86NodeData>();
			var Ch1Data = Ch[1].Data.Get<x86NodeData>();

			// -----------------------------------------------------------------------
			if (Ch1Data.UsedFPUStack >= 8 && Mod && !x86Expressions.NeedLoadFloat(Ch[0], true))
			{
				Ch[0] = FPUOpIntSrc(Ch[0]);
				if ((Ch[0] = CreateMoveNode(Ch[0], SpecifyDataCalcPos: true, DataCalcPos: x86DataLocationType.Memory)) == null)
					return false;

				//Ch[0].Type = Container.GetType(typeof(FloatType), 8);
				//Ch[0].Flags |= ExpressionFlags.FixedType;
				Ch0Data = Ch[0].Data.Get<x86NodeData>();
			}
			else if (RemoveIntCasts)
			{
				if ((Ch[1] = FPUOpIntSrc(Ch[1])) == null)
					return false;
			}

			Data.FPUItemsOnStack = (byte)(Ch0Data.FPUItemsOnStack + Ch1Data.FPUItemsOnStack);
			return true;
		}

		bool AdjustRegs(OpExpressionNode Node, bool Mod)
		{
			var Ch = Node.Children;
			var DstIndex = (Node.Flags & ExpressionFlags.ReverseOperation) != 0 ? 1 : 0;

			var Dst = Ch[DstIndex];
			var Src = Ch[1 - DstIndex];

			var DstData = Dst.Data.Get<x86NodeData>();
			var SrcData = Src.Data.Get<x86NodeData>();

			var NeedMove = (DstData.Flags & x86NodeFlags.AllocateLocation) == 0 && Mod;
			if (!NeedMove && x86Expressions.IsImmediateValue(Dst)) NeedMove = true;
			if (!NeedMove && x86Expressions.SamePosition(Arch, Dst, Src, x86OverlappingMode.Partial)) NeedMove = true;

			if (!NeedMove && (Dst.Type.RealId as Type).Size <= Arch.RegSize)
			{
				var DstIndexNode = (DstData.Flags & x86NodeFlags.IndexMemberNode) != 0;
				if (DstIndexNode && (SrcData.Flags & x86NodeFlags.IndexMemberNode) != 0)
					NeedMove = true;
			}

			if (NeedMove)
			{
				Dst = CreateMoveNode(Ch[DstIndex]);
				if (Dst == null) return false;

				Ch[DstIndex] = Dst;
			}

			return true;
		}
		
		private static bool NeedSwap(ExpressionNode[] Ch)
		{
			if (Ch[0].Type.RealId is FloatType)
			{
				return x86Expressions.NeedLoadFloat(Ch[0], true) &&
					!x86Expressions.NeedLoadFloat(Ch[1], true);
			}

			var Ch0Data = Ch[0].Data.Get<x86NodeData>();
			var Ch1Data = Ch[1].Data.Get<x86NodeData>();

			var Ch0Allocated = (Ch0Data.Flags & x86NodeFlags.AllocateLocation) != 0;
			var Ch1Allocated = (Ch1Data.Flags & x86NodeFlags.AllocateLocation) != 0;

			if (!Ch0Allocated && !Ch1Allocated)
			{
				if (x86Expressions.IsImmediateValue(Ch[0]) && !x86Expressions.IsImmediateValue(Ch[1]))
					return true;
			}

			return !Ch0Allocated && Ch1Allocated;
		}

		private ExpressionNode FPUOpIntSrc(ExpressionNode Node)
		{
			var OpNode = Node as OpExpressionNode;
			if (OpNode == null || !Operators.IsCast(OpNode.Operator)) 
				return Node;

			var Ch = OpNode.Children[0];
			var ChType = Ch.Type.RealId as Type;
			if (!(ChType.RealId is NonFloatType)) 
				return Node;

			if (ChType.Size != 2 && ChType.Size != 4) return Node;
			return OpNode.Children[0];
		}

		private ExpressionNode MulBy(ExpressionNode N, int Num)
		{
			var Val = new IntegerValue(Num);
			var NumNode = Parent.NewNode(new ConstExpressionNode(N.Type, Val, N.Code));
			if (NumNode == null) return null;

			var NCh = new ExpressionNode[] { N, NumNode };
			return Parent.NewNode(new OpExpressionNode(Operator.Multiply, NCh, N.Code));
		}
	}
}
