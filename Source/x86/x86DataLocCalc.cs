using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Zinnia.Base;

namespace Zinnia.x86
{
	public enum x86TempGRegPurposeType : byte
	{
		Plugin,
		Plugin_PushedParamUpcast,
		Plugin_FPUZeroTest,
		TwoMemOp,
		Index,
	}

	public struct x86TempGRegPurpose
	{
		public x86TempGRegPurposeType Type;
		public int ChildIndex;

		public x86TempGRegPurpose(x86TempGRegPurposeType Type, int ChildIndex = -1)
		{
			this.Type = Type;
			this.ChildIndex = ChildIndex;
		}

		public bool IsEquivalent(x86TempGRegPurpose Purpose)
		{
			return Type == Purpose.Type && ChildIndex == Purpose.ChildIndex;
		}
	}

	public struct x86TempGRegister
	{
		public x86TempGRegPurpose Purpose;
		public x86GRegLocation Location;

		public x86TempGRegister(x86TempGRegPurpose Purpose, x86GRegLocation Location)
		{
			this.Purpose = Purpose;
			this.Location = Location;
		}
	}

	public struct x86TemporaryData
	{
		public x86StackLocation Memory;
		public x86SSERegLocation[] SSERegs;
		public x86TempGRegister[] GRegs;

		public x86DataLocation GetRegister(x86ExecutorType Executor, x86TempGRegPurposeType Purpose)
		{
			if (Executor == x86ExecutorType.SSE)
				return SSERegs[0];
			else if (Executor == x86ExecutorType.General)
				return GetGRegister(Purpose);
			else return null;
		}

		public x86GRegLocation GetGRegister(x86TempGRegPurpose Purpose)
		{
			if (GRegs == null) return null;
			for (var i = 0; i < GRegs.Length; i++)
			{
				if (GRegs[i].Purpose.IsEquivalent(Purpose))
					return GRegs[i].Location;
			}

			return null;
		}

		public x86GRegLocation GetGRegister(x86TempGRegPurposeType Type, int ChildIndex = -1)
		{
			return GetGRegister(new x86TempGRegPurpose(Type, ChildIndex));
		}

		public bool Verify(x86NeededTempData TempData, x86DataList CantBe = null, bool Strict = true)
		{
			var RealMemSize = Memory == null ? 0 : Memory.Size;
			if (RealMemSize != TempData.Memory) return false;

			var RealSSERegs = SSERegs == null ? 0 : SSERegs.Length;
			if (RealSSERegs != TempData.SSERegisters) return false;

			if (Strict)
			{
				var RealGRegs = GRegs == null ? 0 : GRegs.Length;
				if (RealGRegs != TempData.GRegisters.Count) return false;
			}

			for (var i = 0; i < TempData.GRegisters.Count; i++)
			{
				var NeededGReg = TempData.GRegisters[i];
				var GReg = GetGRegister(NeededGReg.Purpose);
				if (GRegs == null || GReg.Size < NeededGReg.Size) return false;
				if (NeededGReg.OneByteVariant && !GReg.HasPart(0, 1)) return false;
				if (CantBe != null && !GReg.Verify(CantBe.GRegisters)) return false;
			}

			return true;
		}
	}

	public struct x86NeededGRegister
	{
		public x86TempGRegPurpose Purpose;
		public int Size;
		public bool OneByteVariant;

		public x86NeededGRegister(x86TempGRegPurpose Purpose, int Size, bool OneByteVariant = false)
		{
			if (Size <= 0) throw new ArgumentOutOfRangeException("Size");

			this.Purpose = Purpose;
			this.Size = Size;
			this.OneByteVariant = OneByteVariant;

			if (Size == 1)
				this.OneByteVariant = true;
		}

		public x86NeededGRegister Union(x86NeededGRegister With)
		{
			if (!Purpose.IsEquivalent(With.Purpose))
				throw new InvalidOperationException();

			return new x86NeededGRegister(Purpose,
				Math.Max(Size, With.Size),
				OneByteVariant || With.OneByteVariant);
		}

		public x86NeededGRegister Intersect(x86NeededGRegister With)
		{
			if (!Purpose.IsEquivalent(With.Purpose))
				throw new InvalidOperationException();

			return new x86NeededGRegister(Purpose,
				Math.Min(Size, With.Size),
				OneByteVariant && With.OneByteVariant);
		}

		public x86NeededGRegister Copy()
		{
			return new x86NeededGRegister(Purpose, Size, OneByteVariant);
		}
	}

	public struct x86NeededTempData
	{
		public AutoAllocatedList<x86NeededGRegister> GRegisters;
		public int SSERegisters;
		public int Memory;

		public x86NeededTempData(AutoAllocatedList<x86NeededGRegister> GRegisters,
			int SSERegisters, int MemSize)
		{
			this.GRegisters = GRegisters;
			this.SSERegisters = SSERegisters;
			this.Memory = MemSize;
		}

		public x86NeededTempData Copy()
		{
			var GRegs = new AutoAllocatedList<x86NeededGRegister>();
			for (var i = 0; i < this.GRegisters.Count; i++)
				GRegs.Add(this.GRegisters[i].Copy());

			return new x86NeededTempData(GRegs, SSERegisters, Memory);
		}

		public x86TemporaryData Allocate(x86DataAllocator Allocator, x86DataList CantBe = null)
		{
			var Ret = new x86TemporaryData();
			var RegSize = Allocator.Arch.RegSize;

			if (GRegisters.Count > 0)
			{
				Ret.GRegs = new x86TempGRegister[GRegisters.Count];
				for (var i = 0; i < GRegisters.Count; i++)
				{
					var GRegList = CantBe != null ? CantBe.GRegisters : new x86GRegisterList();
					var Reg = Allocator.AllocGRegister(GRegisters[i], GRegList);
					Ret.GRegs[i] = new x86TempGRegister(GRegisters[i].Purpose, Reg);
				}
			}

			if (SSERegisters > 0)
			{
				Ret.SSERegs = new x86SSERegLocation[SSERegisters];
				for (var i = 0; i < SSERegisters; i++)
				{
					var SSERegList = CantBe != null ? CantBe.SSERegisters : new x86RegisterList();
					Ret.SSERegs[i] = Allocator.AllocSSERegister(16, SSERegList);
					if (Ret.SSERegs[i] == null) throw new ApplicationException();
				}
			}

			if (Memory > 0)
			{
				Ret.Memory = Allocator.AllocMemory(Memory, 1);
				if (Ret.Memory == null) throw new ApplicationException();
			}

			return Ret;
		}

		static int GetIndex(AutoAllocatedList<x86NeededGRegister> List, x86TempGRegPurpose Purpose)
		{
			for (var i = 0; i < List.Count; i++)
				if (List[i].Purpose.IsEquivalent(Purpose)) return i;

			return -1;
		}

		public AutoAllocatedList<x86NeededGRegister> Union_GRegs(x86NeededTempData With)
		{
			var Ret = new AutoAllocatedList<x86NeededGRegister>();
			for (var i = 0; i < GRegisters.Count; i++)
			{
				var Index = GetIndex(Ret, GRegisters[i].Purpose);
				if (Index == -1) Ret.Add(GRegisters[i]);
			}

			for (var i = 0; i < With.GRegisters.Count; i++)
			{
				var Index = GetIndex(Ret, With.GRegisters[i].Purpose);
				if (Index == -1) Ret.Add(With.GRegisters[i]);
			}

			return Ret;
		}

		public AutoAllocatedList<x86NeededGRegister> Intersect_GRegs(x86NeededTempData With)
		{
			var Ret = new AutoAllocatedList<x86NeededGRegister>();
			for (var i = 0; i < GRegisters.Count; i++)
			{
				var Index = GetIndex(With.GRegisters, GRegisters[i].Purpose);
				if (Index != -1) Ret.Add(GRegisters[i]);
			}

			return Ret;
		}

		public x86NeededTempData Union(x86NeededTempData With)
		{
			return new x86NeededTempData(
				Union_GRegs(With),
				Math.Max(SSERegisters, With.SSERegisters),
				Math.Max(Memory, With.Memory));
		}

		public x86NeededTempData Intersect(x86NeededTempData With)
		{
			return new x86NeededTempData(
				Intersect_GRegs(With),
				Math.Min(SSERegisters, With.SSERegisters),
				Math.Min(Memory, With.Memory));
		}

		public bool NonZero
		{
			get { return GRegisters.Count > 0 || SSERegisters > 0 || Memory > 0; }
		}

		public void MustHaveGReg(x86Architecture Arch, x86TempGRegPurpose Purpose,
			int Size, bool OneByteVariant = false)
		{
			if (Size <= 0) throw new ArgumentOutOfRangeException("Size");
			if (Size > Arch.RegSize) Size = Arch.RegSize;

			var Index = GetIndex(GRegisters, Purpose);
			if (Index == -1)
			{
				GRegisters.Add(new x86NeededGRegister(Purpose, Size, OneByteVariant || Size == 1));
			}
			else
			{
				var GRegister = GRegisters[Index];
				if (OneByteVariant || Size == 1)
					GRegister.OneByteVariant = true;

				GRegister.Size = Math.Max(GRegister.Size, Size);
				GRegisters[Index] = GRegister;
			}
		}

		public void MustHaveSSEReg()
		{
			if (SSERegisters < 1) SSERegisters = 1;
		}

		public void MustHaveMemory(int Size)
		{
			if (Size <= 0) throw new ArgumentOutOfRangeException("Size");
			Memory = Math.Max(Memory, Size);
		}

		public void MustHaveSSEOrGReg(x86Architecture Arch, x86TempGRegPurpose Purpose, int Size)
		{
			if (Size <= 0) throw new ArgumentOutOfRangeException("Size");
			if (Size > Arch.RegSize && Size % 4 == 0 && (Arch.Extensions & x86Extensions.SSE2) != 0)
				MustHaveSSEReg(); else MustHaveGReg(Arch, Purpose, Size);
		}

		public void SSERegIfNeeded(x86Architecture Arch, int Size)
		{
			if (Size <= 0) throw new ArgumentOutOfRangeException("Size");
			if (Size > Arch.RegSize && Size % 4 == 0 && (Arch.Extensions & x86Extensions.SSE2) != 0)
				MustHaveSSEReg();
		}
	}

	public struct x86DataProperties
	{
		public int Size;
		public int Align;
		public x86DataLocationType Type;
		public x86DataList CantBe;

		public x86DataProperties(int Size, int Align, x86DataLocationType Type, x86DataList CantBe = null)
		{
			this.Size = Size;
			this.Align = Align;
			this.Type = Type;
			this.CantBe = CantBe;
		}

		public x86DataProperties Intersect(x86DataProperties With)
		{
			var Ret = new x86DataProperties();
			Ret.Size = Math.Max(Size, With.Size);
			Ret.Align = Math.Max(Align, With.Align);
			Ret.Type = x86DataLocCalcHelper.IntersectLocType(Type, With.Type, true);

			if (CantBe != null)
			{
				if (With.CantBe == null) Ret.CantBe = CantBe;
				else Ret.CantBe = CantBe.Union(With.CantBe);
			}
			else
			{
				if (With.CantBe != null)
					Ret.CantBe = With.CantBe;
			}

			return Ret;
		}

		public bool Verify(Identifier Id)
		{
			Id = Id.RealId;

			var Type = Id as Type;
			if (Id is Variable) Type = Id.TypeOfSelf.RealId as Type;
			else if (Type == null) throw new ArgumentException();

			var Arch = Id.Container.State.Arch as x86Architecture;
			return Verify(Type.Size, Type.Align, x86Identifiers.GetPossibleLocations(Type));
		}

		public bool Verify(int LocSize, int LocAlign, x86DataLocationType LocType, bool CantBeRes = false)
		{
			if (Size > LocSize) return false;

			var OneByte = (LocType & x86DataLocationType.OneByte) != 0;
			LocType &= ~x86DataLocationType.OneByte;
			if (LocType != x86DataLocationType.Memory)
				LocType &= ~x86DataLocationType.Memory;

			if ((LocType & Type) != LocType || (!OneByte && (Type & x86DataLocationType.OneByte) != 0))
			{
				if (LocType != x86DataLocationType.Memory) return false;
			}

			if (LocAlign < Align) return false;
			return CantBe != null ? CantBeRes : true;
		}

		public bool Verify(x86DataLocation Location)
		{
			var MemoryLoc = Location as x86MemoryLocation;
			var LocAlign = MemoryLoc != null ? MemoryLoc.Align : int.MaxValue;

			if (CantBe != null && !CantBe.IsFree(Location)) return false;
			return Verify(Location.Size, LocAlign, Location.DataType, true);
		}
	}

	public static class x86DataLocCalcHelper
	{
		public static bool CanUseSSEMove(x86DataLocation Dst, x86DataLocation Src)
		{
			var Arch = Dst.Arch;
			if ((Arch.Extensions & x86Extensions.SSE2) == 0) return false;
			else if (!(Dst is x86MemoryLocation || Dst is x86SSERegLocation)) return false;
			else if (!(Src is x86MemoryLocation || Src is x86SSERegLocation)) return false;
			else if (Dst.Size <= Arch.RegSize || Dst.Size != Src.Size) return false;
			else return true;
		}

		public static x86DataLocationType IntersectLocType(x86DataLocationType A, x86DataLocationType B, bool MemoryIfNull = false)
		{
			var Ret = A & B;
			if ((A & x86DataLocationType.OneByte) != 0 || (B & x86DataLocationType.OneByte) != 0)
				Ret |= x86DataLocationType.OneByte;
			else Ret &= ~x86DataLocationType.OneByte;

			if (Ret == x86DataLocationType.None && MemoryIfNull)
				return x86DataLocationType.Memory;

			return Ret;
		}

		public static void ForeachIndexMemberNode(ExpressionNode Node, Action<ExpressionNode> Action)
		{
			var Data = Node.Data.Get<x86NodeData>();
			if ((Data.Flags & x86NodeFlags.IndexMemberNode) == 0)
				return;

			if (Node.Children != null)
			{
				var Ch = Node.Children;
				for (var i = 0; i < Ch.Length; i++)
					ForeachIndexMemberNode(Ch[i], Action);
			}

			Action(Node);
		}

		public static void ForeachIndexMemberNodeAndChildren(ExpressionNode Node, 
			Action<ExpressionNode> Action, bool SkipRoot = false)
		{
			var Data = Node.Data.Get<x86NodeData>();
			if ((Data.Flags & x86NodeFlags.IndexMemberNode) != 0)
			{
				if (Node.Children != null)
				{
					var Ch = Node.Children;
					for (var i = 0; i < Ch.Length; i++)
						ForeachIndexMemberNodeAndChildren(Ch[i], Action);
				}
			}

			if (!SkipRoot) Action(Node);
		}

		private static void SetLocationsForTuplesAndArrays(ExpressionNode Node, x86NodeData Data)
		{
			var Op = Expressions.GetOperator(Node);
			if ((Op == Operator.Array || Op == Operator.Tuple) && Data.Output != null)
			{
				var Type = Node.Type.RealId as Type;
				for (var i = 0; i < Node.Children.Length; i++)
				{
					var Ch = Node.Children[i];
					var ChData = Ch.Data.Get<x86NodeData>();

					if ((ChData.Flags & x86NodeFlags.AllocateLocation) != 0)
					{
						var Location = x86Identifiers.GetMember(Data.Output, Type, i);
						if (ChData.Properties.Verify(Location))
							x86DataLocCalcHelper.SetDataLocations(Ch, Location);
					}
				}
			}
		}

		public static void SetDataLocations(ExpressionNode Node, x86DataLocation Location)
		{
			ForeachNodesWithSameOutput(Node, x =>
				{
					var xData = x.Data.Get<x86NodeData>();
					var xType = x.Type.RealId as Type;
					var Size = xType.Size;

					if (Location is x86SSERegLocation)
						Size = Math.Max(Size, 16);

					xData.Output = Location.GetPart(0, Size);
					if (xData.Output == null) throw new ApplicationException();

					SetLocationsForTuplesAndArrays(x, xData);
				});
		}

		public static x86DataProperties GetDataProperties(ExpressionNode Node)
		{
			var Ret = x86Expressions.GetDataProperties(Node);
			ForeachChildNodesWithSameOutput(Node, x =>
					Ret = Ret.Intersect(x86Expressions.GetDataProperties(x)));

			if (Ret.Size == 0) throw new ApplicationException();
			return Ret;
		}

		public static void ForeachChildNodesWithSameOutput(ExpressionNode Node, Action<ExpressionNode> Action)
		{
			var Data = Node.Data.Get<x86NodeData>();
			if (Data.SameAllocationAsType == x86SameAllocationAsType.Specified)
			{
				var ChNode = Node.Children[Data.SameAllocationAs];
				var ChData = ChNode.Data.Get<x86NodeData>();

				if ((ChData.Flags & x86NodeFlags.AllocateLocation) != 0)
				{
					Action(ChNode);
					ForeachChildNodesWithSameOutput(ChNode, Action);
				}
			}
			else if (Data.SameAllocationAsType == x86SameAllocationAsType.All)
			{
				for (var i = 0; i < Node.Children.Length; i++)
				{
					var ChNode = Node.Children[i];
					var ChData = ChNode.Data.Get<x86NodeData>();

					if ((ChData.Flags & x86NodeFlags.AllocateLocation) != 0)
					{
						Action(ChNode);
						ForeachChildNodesWithSameOutput(ChNode, Action);
					}
				}
			}
		}

		public static void ForeachNodesWithSameOutput(ExpressionNode Node, Action<ExpressionNode> Action)
		{
			var Data = Node.Data.Get<x86NodeData>();
			if ((Data.Flags & x86NodeFlags.AllocateLocation) == 0)
				throw new InvalidOperationException();
			
			Action(Node);
			ForeachChildNodesWithSameOutput(Node, Action);
		}

		public static bool IsMoveAndOp(this ExpressionNode Self, bool Condition = true)
		{
			var Op = Expressions.GetOperator(Self);
			if (Op == Operator.NewObject)
			{
				if (!(Self.Type.UnderlyingClassOrRealId is ClassType))
					return true;
			}
			else if (Op == Operator.Call)
			{
				var RetType = Self.Type.RealId;
				if (RetType is StructType || RetType is NonrefArrayType)
					return true;
			}
			else if (Condition && Operators.IsBoolRet(Op))
			{
				return true;
			}
			else if (Op == Operator.Tuple || Op == Operator.Array)
			{
				return true;
			}
			else if (Op == Operator.Unknown)
			{
				var Data = Self.Data.Get<x86NodeData>();
				if (Condition && x86Expressions.IsConditionOp(Data.Operator))
					return true;
			}

			return false;
		}

		public static bool DontUseThisVar(this ExpressionNode Self, bool Immediately = false)
		{
			var Data = Self.Data.Get<x86NodeData>();
			if (Data == null) return false;

			if (Data.Output is x86PostCalcedLocation)
			{
				var PostPos = Data.Output as x86PostCalcedLocation;
				if (!PostPos.IsMemory()) return false;

				var AData = PostPos.AssignedTo.Data.Get<x86NodeData>();
				AData.DontUseCount++;

				if (AData.DontUseCount >= 2 || Immediately)
				{
					AData.Flags |= x86NodeFlags.CanUseAssignVar_Calced;
					AData.Flags = AData.Flags & ~x86NodeFlags.CanUseAssignVar;
					return true;
				}
			}

			return false;
		}

		public static bool IsMemory(this ExpressionNode Node, x86Architecture Arch, x86OverlappingMode Mode = x86OverlappingMode.Partial)
		{
			var Data = Node.Data.Get<x86NodeData>();

			var IdNode = Node as IdExpressionNode;
			if (IdNode != null)
			{
				if ((Data.Flags & x86NodeFlags.IdentifierByRef) != 0) return true;
				var IdData = IdNode.Identifier.Data.Get<x86IdentifierData>();
				return IdData == null ? true : IdData.Location.IsMemory(Mode);
			}

			if (Node is ConstExpressionNode)
				return false;

			if (Node is LinkingNode)
			{
				var LNode = Node as LinkingNode;
				var Linked = LNode.LinkedNode;
				var LData = Linked.Data.Get<x86LinkedNodeData>();
				return LData.Location is x86MemoryLocation;
			}

			var OpNode = Node as OpExpressionNode;
			if (OpNode != null)
			{
				var Op = OpNode.Operator;
				if (Op == Operator.Member) return true;
				if (Op == Operator.Index) return true;
				if (Op == Operator.NewObject) return true;

				if (Op == Operator.Assignment)
					return OpNode.Children[0].IsMemory(Arch, Mode);

				if (Op == Operator.Call)
				{
					if (Node.Type is StructType) return true;
				}
			}

			//-----------------------------------------------------
			var Output = Data.Output;
			if (Output == null) return false;

			return Output.IsMemory(Mode);
		}

		public static bool NeedTempReg(x86DataLocation Dst, x86DataLocation Src)
		{
			if (Dst is x86MemoryLocation && Src.IsMemory()) return true;
			if (Src is x86MemoryLocation && Dst.IsMemory()) return true;

			var D = Dst as x86MultiLocation;
			var S = Src as x86MultiLocation;
			if (D == null || S == null) return false;

			var MLen = D.Locations.Length;
			if (S.Locations.Length < MLen)
				MLen = S.Locations.Length;

			for (var i = 0; i < MLen; i++)
			{
				if (D.Locations[i] is x86MemoryLocation && S.Locations[i] is x86MemoryLocation)
					return true;
			}

			return false;
		}

		public static bool NeedTempReg(this ExpressionNode Self, x86Architecture Arch, x86DataLocation Src)
		{
			var SelfData = Self.Data.Get<x86NodeData>();
			var SelfPos = SelfData.ExtractedOutput;

			if (SelfPos is x86MultiLocation && Src is x86MultiLocation)
				return NeedTempReg(SelfPos, Src);

			return Src.IsMemory() && Self.IsMemory(Arch);
		}

		public static bool NeedTempReg(this ExpressionNode Self, x86Architecture Arch, ExpressionNode Node)
		{
			var SelfData = Self.Data.Get<x86NodeData>();
			var NodeData = Node.Data.Get<x86NodeData>();

			var SelfPos = SelfData.ExtractedOutput;
			var NodePos = NodeData.ExtractedOutput;

			if (SelfPos is x86MultiLocation && NodePos is x86MultiLocation)
				return NeedTempReg(SelfPos, NodePos);

			return Self.IsMemory(Arch) && Node.IsMemory(Arch);
		}

		static bool CanUseDataLocation(ExpressionNode Node, x86DataLocation Location)
		{
			var RetValue = true;
			ForeachNodesWithSameOutput(Node, x =>
			{
				var xData = x.Data.Get<x86NodeData>();
				if (!xData.Allocator.IsFree(Location))
					RetValue = false;
			});

			return RetValue;
		}

		static void SetIdentifierCantBe(ExpressionNode Node, x86IdentifierData IdData)
		{
			var Arch = IdData.Identifier.Container.State.Arch as x86Architecture;
			ForeachNodesWithSameOutput(Node, x =>
			{
				var xData = x.Data.Get<x86NodeData>();
				if (!xData.Allocator.IsZero)
				{
					if (IdData.LocationCantBe == null)
						IdData.LocationCantBe = new x86DataList(Arch);

					IdData.LocationCantBe.SetUsed(xData.Allocator);
				}
			});
		}

		static bool _CanUseAssignedLocation(ExpressionNode Node, Predicate<ExpressionNode> Func)
		{
			var Data = Node.Data.Get<x86NodeData>();
			if (Data.SameAllocationAsType == x86SameAllocationAsType.None)
				return true;

			for (var i = 0; i < Node.LinkedNodes.Count; i++)
			{
				var Ch = Node.LinkedNodes[i].Node;
				if (!_CanUseAssignedLocation(Ch, Func))
					return false;
			}

			int From;
			if (Data.SameAllocationAsType == x86SameAllocationAsType.Specified)
				From = Data.SameAllocationAs + 1;
			else if (Data.SameAllocationAsType == x86SameAllocationAsType.All)
				From = 1;
			else 
				throw new ApplicationException();

			for (int i = From; i < Node.Children.Length; i++)
			{
				var Ch = Node.Children[i];
				if (!Ch.CheckNodes(Func) || !_CanUseAssignedLocation(Ch, Func))
					return false;
			}
			return true;
		}
		
		static bool _CanUseAssignVar(ExpressionNode Node, Identifier AssignVar)
		{
			if (!_CanUseAssignedLocation(Node, x => Expressions.GetIdentifier(x) != AssignVar))
				return false;

			if (AssignVar is LocalVariable)
			{
				var IdData = AssignVar.Data.Get<x86IdentifierData>();
				if (IdData.Location != null)
					return CanUseDataLocation(Node, IdData.Location);
				else SetIdentifierCantBe(Node, IdData);
			}

			return true;
		}

		static bool _CanUseClassMemberAssign(ExpressionNode Node, ClassType Type, Identifier Member)
		{
			return _CanUseAssignedLocation(Node, x =>
			{
				if (Expressions.GetOperator(x) == Operator.Member)
				{
					var Ch = Node.Children;
					var Ch0Type = Ch[0].Type.RealId as ClassType;
					if (Ch0Type == null) return true;

					var Ch1Id = Expressions.GetIdentifier(Ch[1]);
					if (Ch1Id != null && Member != null && Ch1Id != Member)
						return true;

					if (Identifiers.IsSubtypeOrEquivalent(Ch0Type, Type) ||
						Identifiers.IsSubtypeOf(Type, Ch0Type))
					{
						return false;
					}
				}

				return true;
			});
		}

		static bool _CanUsePtrIndexAssign(ExpressionNode Node)
		{
			return _CanUseAssignedLocation(Node, x => Expressions.GetIdentifier(x) != null);
		}

		static bool _CanUseNonrefIndexAssign(ExpressionNode Node, Identifier Indexed, ConstValue Index)
		{
			return _CanUseAssignedLocation(Node, x =>
			{
				if (Expressions.GetOperator(x) == Operator.Index)
				{
					var IdCh0 = x.Children[0] as IdExpressionNode;
					var ConstCh1 = x.Children[1] as ConstExpressionNode;

					if (IdCh0.Identifier == Indexed && ConstCh1.Value.IsEqual(Index))
						return false;
				}

				return true;
			});
		}

		public static bool IsTempDataUsed(ExpressionNode Node)
		{
			var Data = Node.Data.Get<x86NodeData>();
			if (Data.NeededTempData.NonZero) return true;

			return !Node.CheckChildren(x => !IsTempDataUsed(x));
		}

		public static bool CanUseAssignedLocation(x86Architecture Arch, ExpressionNode Node, Type DstType,
			ExpressionNode Dst = null)
		{
			var Data = Node.Data.Get<x86NodeData>();
			if ((Data.Flags & x86NodeFlags.CanUseAssignVar_Calced) != 0)
				return (Data.Flags & x86NodeFlags.CanUseAssignVar) != 0;

			if (Dst != null && DstType == null)
				DstType = Dst.Type.RealId as Type;
			else DstType = DstType.RealId as Type;

			var RetValue = true;
			if (!Data.Properties.Verify(DstType))
			{
				RetValue = false;
			}
			else if (Dst != null)
			{
				if (IsTempDataUsed(Dst))
				{
					RetValue = false;
				}
				else if (Dst is IdExpressionNode)
				{
					var IdDst = Dst as IdExpressionNode;
					var Var = IdDst.Identifier.RealId as Variable;
					if (Var != null)
					{
						if (Var is x86ReturnVariable && DstType is FloatType)
						{
							if (Arch.FloatingPointMode == x86FloatingPointMode.FPU)
								RetValue = false;
						}

						if (!_CanUseAssignVar(Node, Var))
							RetValue = false;
					}
					else
					{
						RetValue = false;
					}
				}
				else if (Dst is OpExpressionNode)
				{
					var OpDst = Dst as OpExpressionNode;
					var DstOp = OpDst.Operator;
					var DstCh = OpDst.Children;

					if (DstOp == Operator.Index)
					{
						var Id = Expressions.GetIdentifier(DstCh[0]);
						if (!(Id is x86ReturnVariable) && DstCh.TrueForAll(x => !(x is OpExpressionNode)))
						{
							var ConstDstCh1 = DstCh[1] as ConstExpressionNode;
							if (Id != null && ConstDstCh1 != null && DstCh[0].Type.RealId is NonrefArrayType)
							{
								if (!_CanUseNonrefIndexAssign(Node, Id, ConstDstCh1.Value))
									RetValue = false;
							}
							else
							{
								if (!_CanUsePtrIndexAssign(Node))
									RetValue = false;
							}
						}
						else
						{
							RetValue = false;
						}
					}
					else if (DstOp == Operator.Member)
					{
						var Type = DstCh[0].Type.RealId;
						if (Type is ClassType && !(DstCh[0] is OpExpressionNode))
						{
							var Class = Type as ClassType;
							var Ch1Id = Expressions.GetIdentifier(DstCh[1]);
							if (!_CanUseClassMemberAssign(Node, Class, Ch1Id))
								RetValue = false;
						}
						else
						{
							RetValue = false;
						}
					}
					else
					{
						throw new ApplicationException();
					}
				}
				else
				{
					throw new ApplicationException();
				}
			}

			Data.Flags |= x86NodeFlags.CanUseAssignVar_Calced;
			if (RetValue) Data.Flags |= x86NodeFlags.CanUseAssignVar;
			else Data.Flags = Data.Flags & ~x86NodeFlags.CanUseAssignVar;
			return RetValue;
		}
	}

	public class x86DataLocCalcer
	{
		public x86Architecture Arch;
		public x86DataAllocator TempAllocator;
		public ExpressionNode ExprNode;
		public x86NodeData RootData;
		public IdContainer Container;

		public x86DataLocCalcer(x86Architecture Arch)
		{
			this.Arch = Arch;
		}

		public void Set(ExpressionNode ExprNode)
		{
			this.ExprNode = ExprNode;
			RootData = ExprNode.Data.Get<x86NodeData>();
			Container = RootData.Container;
			TempAllocator = new x86DataAllocator(Container);
		}

		public void Calc(ExpressionNode ExprNode)
		{
			Set(ExprNode);
			Calc();
		}

		public void Calc()
		{
			var AllAllocated = new x86DataList(Arch);
			var RootList = new List<x86DataList>(32) { AllAllocated };
			var Flags = RootData.Flags;

			RootData.AllNodes.ForEach(Reset);
			if ((Flags & x86NodeFlags.AllocateTempData) != 0)
				AllocateTempData(RootList, ExprNode, RootData);

			if ((Flags & x86NodeFlags.EnableUsedData) != 0)
				ProcessUsedData(AllAllocated, ExprNode, RootData);

			if ((Flags & x86NodeFlags.NeedAllocations) != 0 && (Flags & x86NodeFlags.UseExistingLocs) != 0)
				DoAllocations(RootList, ExprNode, RootData, UseExistingLocations);

			if ((Flags & x86NodeFlags.LinkedNodesUsed) != 0)
				DoAllocations(RootList, ExprNode, RootData, AllocateLinkedNodes);

			if ((Flags & x86NodeFlags.NeedAllocations) != 0 && (Flags & x86NodeFlags.NonMemoryUsed) != 0)
				DoAllocations(RootList, ExprNode, RootData, AllocateNonMemory);

			if ((Flags & x86NodeFlags.NeedAllocations) != 0)
				DoAllocations(RootList, ExprNode, RootData, AllocateRemaining);

			RootData.AllAllocated = AllAllocated;
			Container.ForEachParent<IdContainer>(x =>
			{
				var xData = x.Data.Get<x86IdContainerData>();
				xData.Allocator.SetUsed(AllAllocated);
			}, Container.FunctionScope.Parent);
		}

		private void UseExistingLocations(List<x86DataList> List, ExpressionNode Node, x86NodeData Data)
		{
			if (Expressions.GetOperator(Node) == Operator.Assignment)
			{
				var Ch = Node.Children;
				var Ch1Data = Ch[1].Data.Get<x86NodeData>();

				if ((Ch1Data.Flags & x86NodeFlags.AllocateLocation) != 0)
				{
					if (x86DataLocCalcHelper.CanUseAssignedLocation(Arch, Ch[1], null, Ch[0]))
					{
						var AVLocation = new x86AssignVarLoc(Arch, Node, Ch[0], Ch[1]);
						x86DataLocCalcHelper.SetDataLocations(Ch[1], AVLocation);
						Ch1Data.DontUseCount = 0;

						var IdCh0 = Ch[0] as IdExpressionNode;
						if (IdCh0 != null && IdCh0.Identifier is LocalVariable)
						{
							var IdData = IdCh0.Identifier.Data.Get<x86IdentifierData>();
							var Ch0Data = IdCh0.Data.Get<x86NodeData>();
							if (IdData.Location != null && (Ch0Data.Flags & x86NodeFlags.IdentifierByRef) == 0)
							{
								List.ForEach(x => x.SetUsed(IdData.Location));
								Ch[1].ForEach(x =>
								{
									var xData = x.Data.Get<x86NodeData>();
									xData.Allocator.SetUsed(IdData.Location);
								});
							}
						}
					}
				}
			}

			if (Data.PreferredOutput != null && (Data.Flags & x86NodeFlags.AllocateLocation) != 0)
			{
				if (Data.Properties.Verify(Data.PreferredOutput))
				{
					PrepareTempAllocator(Node);
					if (TempAllocator.IsFree(Data.PreferredOutput))
					{
						x86DataLocCalcHelper.SetDataLocations(Node, Data.PreferredOutput);
						List.ForEach(x => x.SetUsed(Data.PreferredOutput));
					}
				}
			}
		}

		private void AllocateLinkedNodes(List<x86DataList> List, ExpressionNode Node, x86NodeData Data)
		{
			if (Node is LinkingNode)
			{
				var LinkingNode = Node as LinkingNode;
				var LNode = LinkingNode.LinkedNode;
				var LData = LNode.Data.Get<x86LinkedNodeData>();
				Data.Output = LData.Location;
			}

			if (Node.LinkedNodes.Count > 0)
			{
				var LinkedNodes = Node.LinkedNodes.List;
				TempAllocator.Reset();

				if (Node.Children != null)
				{
					for (var i = 0; i < Node.Children.Length; i++)
						Node.Children[i].ForEach(x =>
						{
							var xData = x.Data.Get<x86NodeData>();
							TempAllocator.SetUsed(xData.Allocator);
						});
				}

				for (var i = LinkedNodes.Count - 1; i >= 0; i--)
				{
					var LNode = LinkedNodes[i];
					var LData = LNode.Data.Get<x86LinkedNodeData>();
					if ((LData.Flags & x86LinkedNodeFlags.AllocateData) == 0) continue;

					var Linked = LNode.Node;
					var LinkedData = Linked.Data.Get<x86NodeData>();
					var Allocator = TempAllocator;

					if ((LinkedData.Flags & x86NodeFlags.AllocateLocation) != 0)
						SetTempAllocatorUsed(Linked);

					if (LData.Specified != null && Allocator.IsFree(LData.Specified))
					{
						LData.Location = LData.Specified;
						Allocator.SetUsed(LData.Location);
					}
					else
					{
						var Out = LinkedData.Output;
						if (Out != null && !(Out is x86PostCalcedLocation) && Allocator.IsFree(Out))
						{
							LData.Location = LinkedData.Output;
							Allocator.SetUsed(LData.Location);
						}
						else
						{
							var Props = x86Expressions.GetDataProperties(Linked.Type);
							if ((LinkedData.Flags & x86NodeFlags.AllocateLocation) != 0)
								Props = Props.Intersect(LinkedData.Properties);

                            Props.Type |= x86DataLocationType.Memory;
							LData.Location = Allocator.Allocate(Props);
						}
					}

					if (LinkedData.Output == null && (LinkedData.Flags & x86NodeFlags.AllocateLocation) != 0)
					{
						if (LinkedData.Properties.Verify(LData.Location))
							x86DataLocCalcHelper.SetDataLocations(Linked, LData.Location);
					}

					Linked.ForEach(x =>
					{
						var xData = x.Data.Get<x86NodeData>();
						TempAllocator.SetUsed(xData.Allocator);
					});

					List.ForEach(x => x.SetUsed(LData.Location));
					for (var j = i + 1; j < LinkedNodes.Count; j++)
						LinkedNodes[j].Node.ForEach(x =>
						{
							var xData = x.Data.Get<x86NodeData>();
							xData.Allocator.SetUsed(LData.Location);
						});

					for (var j = 0; j < Node.Children.Length; j++)
						Node.Children[j].ForEach(x =>
						{
							var xData = x.Data.Get<x86NodeData>();
							xData.Allocator.SetUsed(LData.Location);
						});
				}
			}
		}

		private void SetTempAllocatorUsed(ExpressionNode Node)
		{
			x86DataLocCalcHelper.ForeachNodesWithSameOutput(Node, x =>
			{
				var xData = x.Data.Get<x86NodeData>();
				TempAllocator.SetUsed(xData.Allocator);
			});
		}

		private void PrepareTempAllocator(ExpressionNode Node)
		{
			TempAllocator.Reset();
			SetTempAllocatorUsed(Node);
		}

		private void AllocateNodeOutput(List<x86DataList> List, ExpressionNode Node, x86NodeData Data)
		{
			PrepareTempAllocator(Node);

			var Properties = Data.Properties;
			Properties.Type |= x86DataLocationType.Memory;

			var DataLoc = TempAllocator.Allocate(Properties);
			x86DataLocCalcHelper.SetDataLocations(Node, DataLoc);
			List.ForEach(x => x.SetUsed(DataLoc));
		}

		private void AllocateNonMemory(List<x86DataList> List, ExpressionNode Node, x86NodeData Data)
		{
			if ((Data.Flags & x86NodeFlags.AllocateLocation) != 0 && Data.Output == null)
			{
				if ((Data.Properties.Type & x86DataLocationType.Memory) == 0)
					AllocateNodeOutput(List, Node, Data);
			}
		}

		private void AllocateRemaining(List<x86DataList> List, ExpressionNode Node, x86NodeData Data)
		{
			if ((Data.Flags & x86NodeFlags.AllocateLocation) != 0 && Data.Output == null)
				AllocateNodeOutput(List, Node, Data);
		}

		private x86DataList AllocateTempData(List<x86DataList> List, ExpressionNode Node, x86NodeData Data)
		{
			if (Node.Code.IsEqual("Parameters[i].UndeclaredType = Assembly, (Ptr to void*)")) { ; }

			x86DataList RetAllocated = null;
			if (Data.NeededTempData.NonZero)
			{
				Data.TempData = Data.NeededTempData.Allocate(Data.Allocator, Data.TempCantBe);
				if (!Data.TempData.Verify(Data.NeededTempData, Data.TempCantBe))
					throw new ApplicationException();

				if ((Data.Flags & x86NodeFlags.RefIndexMemberNode) == 0)
				{
					if (Node.Children != null)
					{
						for (var i = 0; i < Node.Children.Length; i++)
							SetUsed(Node.Children[i], Data.TempData);
					}
				}
				else
				{
					for (var i = 0; i < Data.NeededTempData.GRegisters.Count; i++)
					{
						var UsedForCh = Data.NeededTempData.GRegisters[i].Purpose.ChildIndex;
						if (UsedForCh == -1) continue;

						for (var j = UsedForCh + 1; j < Node.Children.Length; j++)
							SetUsed(Node.Children[j], Data.TempData.GRegs[i].Location);
					}
				}

				List[0].SetUsed(Data.TempData);
				RetAllocated = new x86DataList(Arch);
				RetAllocated.SetUsed(Data.TempData);
			}
			else
			{
				Data.TempData = new x86TemporaryData();
			}

			if (Node.Children != null)
			{
				for (var i = 0; i < Node.Children.Length; i++)
				{
					var Ch = Node.Children[i];
					var ChData = Ch.Data.Get<x86NodeData>();
					var ChAllocated = AllocateTempData(List, Ch, ChData);

					if (ChAllocated != null)
					{
						Data.Allocator.SetUsed(ChAllocated);
						for (var j = 0; j < Node.Children.Length; j++)
							SetUsed(Node.Children[j], ChAllocated);

						if (RetAllocated == null)
							RetAllocated = new x86DataList(Arch);

						RetAllocated.SetUsed(ChAllocated);
					}
				}
			}

			for (var i = 0; i < Node.LinkedNodes.Count; i++)
			{
				var LNode = Node.LinkedNodes[i];
				var Linked = LNode.Node;
				var LinkedData = Linked.Data.Get<x86NodeData>();
				AllocateTempData(List, Linked, LinkedData);
			}

			return RetAllocated;
		}

		private void Reset(ExpressionNode Node)
		{
			var Data = Node.Data.Get<x86NodeData>();
			Data.Flags &= ~x86NodeFlags.LocationProcessed;

			if (Data.Allocator != null) Data.Allocator.Reset();
			else Data.Allocator = new x86DataAllocator(Container);

			if ((Data.Flags & x86NodeFlags.AllocateLocation) != 0)
				Data.Output = null;
		}

		private void ProcessUsedData(x86DataList AllAllocated, ExpressionNode Node, x86NodeData Data)
		{
			if (Data.UsedData != null)
			{
				AllAllocated.SetUsed(Data.UsedData);
				Data.Allocator.SetUsed(Data.UsedData);
			}

			if (Node.Children != null)
			{
				var Ch = Node.Children;
				for (var i = 0; i < Ch.Length; i++)
				{
					var ChiData = Ch[i].Data.Get<x86NodeData>();
					ProcessUsedData(AllAllocated, Ch[i], ChiData);

					if ((Data.Flags & x86NodeFlags.SaveChResults) != 0)
					{
						if (Data.UsedData != null)
							SetUsed(Ch[i], Data.UsedData);

						if (ChiData.UsedData != null)
						{
							for (var j = i - 1; j >= 0; j--)
								SetUsed(Ch[j], ChiData.UsedData);
						}
					}
				}
			}

			for (var i = 0; i < Node.LinkedNodes.Count; i++)
			{
				var LNode = Node.LinkedNodes[i];
				var Linked = LNode.Node;
				var LinkedData = Linked.Data.Get<x86NodeData>();
				ProcessUsedData(AllAllocated, Linked, LinkedData);

				if ((LNode.Flags & LinkedNodeFlags.PostComputation) != 0)
					Data.AllAllocated.SetUsed(Data.UsedData);
			}
		}

		void ForeachSetUsedNode(ExpressionNode Node, Action<ExpressionNode> Func)
		{
			var Op = Expressions.GetOperator(Node);
			if (Op == Operator.Tuple || Op == Operator.Array)
			{
				Func(Node);

				var Ch = Node.Children;
				for (var i = 0; i < Ch.Length; i++)
					x86DataLocCalcHelper.ForeachIndexMemberNodeAndChildren(Ch[i], Func);
			}
			else
			{
				x86DataLocCalcHelper.ForeachIndexMemberNodeAndChildren(Node, Func);
			}
		}

		void SetUsed(ExpressionNode Node, x86TemporaryData TempData)
		{
			ForeachSetUsedNode(Node, x =>
			{
				var xData = x.Data.Get<x86NodeData>();
				xData.Allocator.SetUsed(TempData);
			});
		}

		void SetUsed(ExpressionNode Node, x86DataList UsedData)
		{
			ForeachSetUsedNode(Node, x =>
			{
				var xData = x.Data.Get<x86NodeData>();
				xData.Allocator.SetUsed(UsedData);
			});
		}

		void SetUsed(ExpressionNode Node, x86DataLocation Output)
		{
			ForeachSetUsedNode(Node, x =>
			{
				var xData = x.Data.Get<x86NodeData>();
				xData.Allocator.SetUsed(Output);
			});
		}

		private void DoAllocations(List<x86DataList> List, ExpressionNode Node, x86NodeData Data,
			Action<List<x86DataList>, ExpressionNode, x86NodeData> Action)
		{
			List.Add(Data.Allocator);
			var OldCount = List.Count;

			for (var i = 0; i < Node.LinkedNodes.Count; i++)
			{
				var LNode = Node.LinkedNodes[i];
				if ((LNode.Flags & LinkedNodeFlags.PostComputation) != 0)
					LNode.Node.ForEach(x => List.Add(x.Data.Get<x86NodeData>().Allocator));
			}

			Action(List, Node, Data);
			List.RemoveRange(OldCount, List.Count - OldCount);

			if (Node.Children != null)
			{
				var Ch = Node.Children;
				for (var i = 0; i < Ch.Length; i++)
				{
					var ChiData = Ch[i].Data.Get<x86NodeData>();

					OldCount = List.Count;
					if ((Data.Flags & x86NodeFlags.SaveChResults) != 0)
					{
						if ((ChiData.Flags & x86NodeFlags.IndexMemberNode) != 0)
						{
							for (var j = 0; j < Ch.Length; j++)
								if (i != j)
								{
									Ch[j].ForEach(x =>
									{
										var xData = x.Data.Get<x86NodeData>();
										List.Add(xData.Allocator);
									});
								}
						}
						else
						{
							for (var j = 0; j < Ch.Length; j++)
								if (i != j)
								{
									x86DataLocCalcHelper.ForeachIndexMemberNode(Ch[j], x =>
									{
										if (x.Children != null)
										{
											for (var xi = 0; xi < x.Children.Length; xi++)
											{
												var xChi = x.Children[xi];
												var xChiData = xChi.Data.Get<x86NodeData>();
												List.Add(xChiData.Allocator);
											}
										}
									});
								}
						}
					}

					DoAllocations(List, Ch[i], ChiData, Action);
					List.RemoveRange(OldCount, List.Count - OldCount);

					if ((Data.Flags & x86NodeFlags.SaveChResults) == 0) continue;
					if ((ChiData.Flags & x86NodeFlags.LocationProcessed) != 0) continue;
					if (ChiData.Output == null || ChiData.Output is x86PostCalcedLocation) continue;

					for (var j = 0; j < Ch.Length; j++)
						if (i != j)
						{
							Ch[j].ForEach(x =>
							{
								var xData = x.Data.Get<x86NodeData>();
								xData.Allocator.SetUsed(ChiData.Output);
							});
						}

					ChiData.Flags |= x86NodeFlags.LocationProcessed;
				}
			}

			for (var i = 0; i < Node.LinkedNodes.Count; i++)
			{
				var LNode = Node.LinkedNodes[i];
				//var LData = LNode.Data.Get<x86LinkedNodeData>();

				var Linked = LNode.Node;
				var LinkedData = Linked.Data.Get<x86NodeData>();
				DoAllocations(List, Linked, LinkedData, Action);
			}

			List.RemoveAt(List.Count - 1);
		}
	}

	public class x86DataLocChecker
	{
		public x86Architecture Arch;
		public x86DataAllocator TempAllocator;
		public ExpressionNode ExprNode;
		public x86NodeData RootData;
		public IdContainer Container;
		
		bool Check_DontUseAssignVar;
		int Check_RecheckIndex;

		public x86DataLocChecker(x86Architecture Arch)
		{
			this.Arch = Arch;
		}

		public void Set(ExpressionNode ExprNode)
		{
			this.ExprNode = ExprNode;
			RootData = ExprNode.Data.Get<x86NodeData>();
			Container = RootData.Container;
			TempAllocator = new x86DataAllocator(Container);
		}

		bool CheckNodes_CallAll(x86NodeData Data, Func<ExpressionNode, bool> Func)
		{
			var RetValue = true;
			for (var i = 0; i < Data.AllNodes.Count; i++)
				if (!Func(Data.AllNodes[i])) RetValue = false;

			return RetValue;
		}

		x86UnknownMemory _UnknownMemory;
		x86UnknownMemory GetUnknownMemory(int Size)
		{
			if (_UnknownMemory == null)
				_UnknownMemory = new x86UnknownMemory(Arch, 0, 0);

			_UnknownMemory.Size = Size;
			return _UnknownMemory;
		}

		x86UnknownMemory GetUnknownMemory(Identifier Type)
		{
			var TType = Type.RealId as Type;
			return GetUnknownMemory(TType.Size);
		}

		public bool Check(ExpressionNode ExprNode)
		{
			Set(ExprNode);
			return Check();
		}

		public void CheckReset(ExpressionNode Node)
		{
			var Data = Node.Data.Get<x86NodeData>();
			Data.NeededTempData = Data.NeededTempByPlugin.Copy();
		}

		public bool Check()
		{
			Check_RecheckIndex = 0;
			RootData.NumberOfFails++;

			do
			{
				Check_DontUseAssignVar = false;
				RootData.AllNodes.ForEach(CheckReset);
				RootData.AllNodes.ForEach(CheckMain);

				if (Check_RecheckIndex == 0)
				{
					if (Check_DontUseAssignVar)
					{
						RootData.Flags |= x86NodeFlags.AllocateTempData;
						return false;
					}
					else
					{
						Check_RecheckIndex++;
					}
				}
				else
				{
					//ForEachNodes(RootData, CheckCalcCantBe);
					var Ret = CheckNodes_CallAll(RootData, CheckVerify);
					if (!Ret) RootData.Flags |= x86NodeFlags.AllocateTempData;
					return Ret;
				}

				Check_RecheckIndex++;
			}
			while (true);
		}

		bool CheckVerify(ExpressionNode Node)
		{
			var Data = Node.Data.Get<x86NodeData>();
			var Result = Data.TempData.Verify(Data.NeededTempData, Data.TempCantBe, RootData.NumberOfFails < 20);

			if (!Result && RootData.NumberOfFails >= 32)// { ; }
				throw new ApplicationException();

			return Result;
		}

		x86DataLocation GetLocationForNode(ExpressionNode Node)
		{
			var Ret = x86Expressions.GetLocation(Arch, Node);
			if (Ret == null) Ret = GetUnknownMemory(Node.Type);
			return Ret;
		}

		void CheckMain(ExpressionNode Node)
		{
			var Data = Node.Data.Get<x86NodeData>();
			var NeededTempData = new x86NeededTempData();

			//--------------------------------------------------------------------------------------
			for (var i = 0; i < Node.LinkedNodes.Count; i++)
			{
				var LNode = Node.LinkedNodes[i];
				var LData = LNode.Data.Get<x86LinkedNodeData>();

				if ((LData.Flags & x86LinkedNodeFlags.AllocateData) != 0)
					CheckMoveExpression(LData.Location, Data, LNode.Node);
			}

			//--------------------------------------------------------------------------------------
			if (Node is OpExpressionNode)
			{
				CheckOpNode(Node, Data, ref NeededTempData);
			}

			//--------------------------------------------------------------------------------------
			else if (Node is IdExpressionNode)
			{
				var IdNode = Node as IdExpressionNode;
				var IdData = IdNode.Identifier.Data.Get<x86IdentifierData>();
				if ((Data.Flags & x86NodeFlags.IdentifierByRef) != 0 && IdData.Location.IsMemory())
				{
					var Purpose = new x86TempGRegPurpose(x86TempGRegPurposeType.Index);
					NeededTempData.GRegisters.Add(new x86NeededGRegister(Purpose, Arch.RegSize));
				}
			}

			Data.NeededTempData = Data.NeededTempData.Union(NeededTempData);
		}

		private bool MoveStructure(x86DataLocation Dst, x86DataLocation Src,
			ref x86NeededTempData NeededTempData, Identifier Type)
		{
			if (Src is x86ConstLocation)
			{
				var ConstSrc = Src as x86ConstLocation;
				if (ConstSrc.Value is ZeroValue)
					return ZeroData(Dst, ref NeededTempData, x86ExecutorType.All, Type);
			}

			var RetValue = true;
			var Length = x86Identifiers.GetMemberCount(Type);
			for (var j = 0; j < Length; j++)
				if (x86Identifiers.IsMovableMember(Type, j))
				{
					var Dsti = x86Identifiers.GetMember(Dst, Type, j);
					var Srci = x86Identifiers.GetMember(Src, Type, j);
					var MemberType = x86Identifiers.GetMemberType(Type, j);

					if (MemberType.RealId is StructType || MemberType.RealId is NonrefArrayType)
					{
						if (!MoveStructure(Dsti, Srci, ref NeededTempData, MemberType))
							RetValue = false;
					}
					else
					{
						if (!MoveData(Dsti, Srci, ref NeededTempData, x86ExecutorType.All, MemberType))
							RetValue = false;
					}
				}

			return RetValue;
		}

		private void CheckOpNode(ExpressionNode Node, x86NodeData Data, ref x86NeededTempData NeededTempData)
		{
			var OpNode = Node as OpExpressionNode;
			var Op = OpNode.Operator;
			var Ch = OpNode.Children;

			//--------------------------------------------------------------------------------------
			if (Op == Operator.Negation)
			{
				if (Arch.FloatingPointMode == x86FloatingPointMode.SSE)
				{
					if (Ch[0].IsMemory(Arch))
					{
						NeededTempData.MustHaveSSEReg();
						DontUseAssignVariables(Ch[0], true);
					}
				}
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.Member)
			{
				var IdCh1 = Ch[1] as IdExpressionNode;
				if (IdCh1.Identifier is MemberVariable)
				{
					var Ch0Type = Ch[0].Type.RealId as Type;
					if ((Ch0Type.TypeFlags & TypeFlags.ReferenceValue) != 0 && Ch[0].IsMemory(Arch))
					{
						var Purpose = new x86TempGRegPurpose(x86TempGRegPurposeType.Index, 0);
						NeededTempData.GRegisters.Add(new x86NeededGRegister(Purpose, Arch.RegSize));
					}
				}
				else if (IdCh1.Identifier is MemberFunction)
				{
					if (x86Expressions.IsVirtualMember(Node))
					{
						var Purpose = new x86TempGRegPurpose(x86TempGRegPurposeType.Index, 0);
						NeededTempData.GRegisters.Add(new x86NeededGRegister(Purpose, Arch.RegSize));
					}
				}
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.Index)
			{
				if (!(Ch[0].Type.RealId is NonrefArrayType) && Ch[0].IsMemory(Arch))
				{
					var Purpose = new x86TempGRegPurpose(x86TempGRegPurposeType.Index, 0);
					NeededTempData.GRegisters.Add(new x86NeededGRegister(Purpose, Arch.RegSize));
				}

				if (Ch[1].IsMemory(Arch))
				{
					var Purpose = new x86TempGRegPurpose(x86TempGRegPurposeType.Index, 1);
					NeededTempData.GRegisters.Add(new x86NeededGRegister(Purpose, Arch.RegSize));
				}
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.Call || Op == Operator.NewObject)
			{
				for (var i = 1; i < Ch.Length; i++)
				{
					var Dst = GetUnknownMemory(Ch[i].Type);
					CheckMoveExpression(Dst, Data, Ch[i]);
				}
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.Reinterpret)
			{
				var SrcNode = Ch[0];
				var SrcData = SrcNode.Data.Get<x86NodeData>();
				var To = Expressions.GetIdentifier(Ch[1]);
				var RTo = To.RealId as Type;
				var RFrom = SrcNode.Type.RealId as Type;
				var Size = RTo.Size;

				var Dst = GetLocationForNode(Node);
				var Src = GetLocationForNode(SrcNode);

				if (RTo is FloatType || RFrom is FloatType)
				{
					if (Arch.FloatingPointMode == x86FloatingPointMode.FPU)
					{
						if (!(RTo is FloatType))
						{
							if (!Node.IsMemory(Arch, x86OverlappingMode.Whole))
								NeededTempData.MustHaveMemory(Size);
						}
						else
						{
							if (x86Expressions.NeedLoadFloat(SrcNode, true) &&
								!SrcNode.IsMemory(Arch, x86OverlappingMode.Whole))
							{
								NeededTempData.MustHaveMemory(RFrom.Size);
							}
						}
					}
					else if (Arch.FloatingPointMode == x86FloatingPointMode.SSE)
					{
						if (!MoveData(Dst, Src, ref NeededTempData, x86ExecutorType.All, To))
							DontUseAssignVariables(Node, SrcNode, true);
					}
					else
					{
						throw new NotImplementedException();
					}
				}
				else if ((Data.Flags & x86NodeFlags.AllocateLocation) != 0)
				{
					if (!MoveData(Dst, Src, ref NeededTempData, x86ExecutorType.All, To))
						DontUseAssignVariables(Node, SrcNode, true);
				}
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.Cast)
			{
				var SrcNode = Ch[0];
				var SrcData = SrcNode.Data.Get<x86NodeData>();
				var To = Expressions.GetIdentifier(Ch[1]);
				var From = SrcNode.Type;
				var RTo = To.RealId as Type;
				var RFrom = From.RealId as Type;
				
				var Dst = GetLocationForNode(Node);
				var Src = GetLocationForNode(SrcNode);

				if (SrcNode.IsMoveAndOp())
				{
					CheckMoveAndOp(Dst, SrcNode);
				}
				else if (RTo is NonFloatType && RFrom is FloatType)
				{
					if (Arch.FloatingPointMode == x86FloatingPointMode.FPU)
					{
						if (!OpNode.IsMemory(Arch, x86OverlappingMode.Whole))
							NeededTempData.MustHaveMemory(RTo.Size);
					}
					else if (Arch.FloatingPointMode == x86FloatingPointMode.SSE)
					{
						if (Node.IsMemory(Arch))
						{
							var Purpose = new x86TempGRegPurpose(x86TempGRegPurposeType.TwoMemOp);
							NeededTempData.MustHaveGReg(Arch, Purpose, Arch.RegSize);
							DontUseAssignVariables(Node, true);
						}
					}
					else
					{
						throw new NotImplementedException();
					}
				}
				else if (RTo is FloatType && RFrom is NonFloatType)
				{
					if (Arch.FloatingPointMode == x86FloatingPointMode.FPU)
					{
						if (!SrcNode.IsMemory(Arch, x86OverlappingMode.Whole))
							NeededTempData.MustHaveMemory(RFrom.Size);
					}
					else if (Arch.FloatingPointMode == x86FloatingPointMode.SSE)
					{
						if (Node.IsMemory(Arch))
						{
							NeededTempData.MustHaveSSEReg();
							DontUseAssignVariables(Node, true);
						}
					}
					else
					{
						throw new NotImplementedException();
					}
				}
				else if (RTo is FloatType && RFrom is FloatType)
				{
					if (RTo.Size == RFrom.Size)
					{
						if (Arch.FloatingPointMode == x86FloatingPointMode.SSE)
						{
							if (Node.IsMemory(Arch) && SrcNode.IsMemory(Arch))
							{
								NeededTempData.MustHaveSSEReg();
								DontUseAssignVariables(Node, SrcNode, true);
							}
						}
					}
					else
					{
						if (Arch.FloatingPointMode == x86FloatingPointMode.SSE)
						{
							if (Node.IsMemory(Arch))
							{
								NeededTempData.MustHaveSSEReg();
								DontUseAssignVariables(Node, SrcNode, true);
							}
						}
					}
				}
				else if ((Data.Flags & x86NodeFlags.AllocateLocation) != 0)
				{
					if (!ConvertData(Dst, Src, ref NeededTempData, x86ExecutorType.All, To, From))
						DontUseAssignVariables(Node, SrcNode, true);
				}
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.Condition)
			{
				for (var i = 1; i < 3; i++)
					if (!(Ch[i].Type.RealId is FloatType) || Arch.FloatingPointMode == x86FloatingPointMode.SSE)
					{
						var Dst = GetLocationForNode(Node);
						var Src = GetLocationForNode(Ch[i]);

						if (!MoveData(Dst, Src, ref NeededTempData, x86ExecutorType.All, Ch[i].Type))
							DontUseAssignVariables(Node, Ch[i]);
					}
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.Address)
			{
				if (Node.IsMemory(Arch))
				{
					DontUseAssignVariables(Node, true);
					var Purpose = new x86TempGRegPurpose(x86TempGRegPurposeType.TwoMemOp);
					NeededTempData.MustHaveGReg(Arch, Purpose, Arch.RegSize);
				}
			}

			//--------------------------------------------------------------------------------------
			else if (Operators.IsShift(Op))
			{
				var Ch0Type = Ch[0].Type.RealId as Type;
				if (Ch0Type.Size > Arch.RegSize && Ch[0].IsMemory(Arch) && Node.IsMemory(Arch))
				{
					var Purpose = new x86TempGRegPurpose(x86TempGRegPurposeType.TwoMemOp);
					NeededTempData.MustHaveGReg(Arch, Purpose, Arch.RegSize);
				}

				if ((Ch[1].Type.RealId as Type).Size > 1)
				{
					var SrcPos = x86Expressions.GetLocation(Arch, Ch[1]);
					if (SrcPos != null && !SrcPos.HasPart(0, 1))
					{
						var Purpose = new x86TempGRegPurpose(x86TempGRegPurposeType.TwoMemOp);
						NeededTempData.MustHaveGReg(Arch, Purpose, 1);
					}
				}
			}

			//--------------------------------------------------------------------------------------
			else if (Operators.IsRelEquality(Op) || Operators.IsBitArithm(Op))
			{
				var Type = Ch[0].Type.RealId as Type;
				if (!(Type is FloatType))
				{
					if (Op == Operator.Multiply)
					{
						if (Node.IsMemory(Arch))
						{
							var Purpose = new x86TempGRegPurpose(x86TempGRegPurposeType.TwoMemOp);
							NeededTempData.MustHaveGReg(Arch, Purpose, Type.Size);
							DontUseAssignVariables(Node, true);
						}
					}
					else
					{
						if (Ch[0].NeedTempReg(Arch, Ch[1]))
						{
							var Purpose = new x86TempGRegPurpose(x86TempGRegPurposeType.TwoMemOp);
							NeededTempData.MustHaveGReg(Arch, Purpose, Type.Size);
							DontUseAssignVariables(Ch[0], Ch[1]);
						}
					}
				}
				else
				{
					if (Arch.FloatingPointMode == x86FloatingPointMode.FPU)
					{
						if (!(Ch[1].Type.RealId is FloatType) && !Ch[1].IsMemory(Arch))
							NeededTempData.MustHaveMemory((Ch[1].Type.RealId as Type).Size);
					}
					else if (Arch.FloatingPointMode == x86FloatingPointMode.SSE)
					{
						if (Ch[0].IsMemory(Arch))
						{
							NeededTempData.MustHaveSSEReg();
							DontUseAssignVariables(Ch[0], true);
						}
					}
					else
					{
						throw new NotImplementedException();
					}
				}
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.Assignment)
			{
				var Dst = GetLocationForNode(Ch[0]);
				CheckMoveExpression(Dst, Data, Ch[1]);
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.Unknown)
			{
				CheckUnknownOp(Node, Data, ref NeededTempData);
			}
		}

		private bool CheckMoveExpression(x86DataLocation Dst, x86NodeData Data, ExpressionNode Node)
		{
			if (Node.IsMoveAndOp()) return CheckMoveAndOp(Dst, Node);

			if (!(Node.Type.RealId is FloatType) || Arch.FloatingPointMode != x86FloatingPointMode.FPU)
			{
				var Src = GetLocationForNode(Node);
				return MoveData(Dst, Src, ref Data.NeededTempData, x86ExecutorType.All, Node.Type);
			}

			return true;
		}

		private bool CheckMoveAndOp(x86DataLocation Dst, ExpressionNode Node)
		{
			var Data = Node.Data.Get<x86NodeData>();
			var Op = Expressions.GetOperator(Node);
			var Ch = Node.Children;

			if (Op == Operator.Tuple || Op == Operator.Array)
			{
				for (var i = 0; i < Ch.Length; i++)
				{
					var DstPart = x86Identifiers.GetMember(Dst, Node.Type, i);
					var SrcPart = GetLocationForNode(Ch[i]);
					if (!MoveData(DstPart, SrcPart, ref Data.NeededTempData, x86ExecutorType.All, Ch[i].Type))
						return false;
				}
			}

			return true;
		}

		private void CheckUnknownOp(ExpressionNode Node, x86NodeData Data, ref x86NeededTempData NeededTempData)
		{
			var OpNode = Node as OpExpressionNode;
			var Ch = OpNode.Children;
			var Op = Data.Operator;

			//--------------------------------------------------------------------------------------
			if (x86Expressions.IsBitTestOp(Op))
			{
				var Type = Ch[0].Type.RealId as Type;
				if (Ch[0].NeedTempReg(Arch, Ch[1]))
				{
					var Purpose = new x86TempGRegPurpose(x86TempGRegPurposeType.TwoMemOp);
					NeededTempData.MustHaveGReg(Arch, Purpose, Type.Size);
					DontUseAssignVariables(Ch[0], Ch[1]);
				}
			}

			//--------------------------------------------------------------------------------------
			else if (Op == x86Operator.Abs || Op == x86Operator.Sqrt || x86Expressions.IsTwoOperandSSEOp(Op))
			{
				if (Arch.FloatingPointMode == x86FloatingPointMode.SSE)
				{
					if (Ch[0].IsMemory(Arch))
					{
						NeededTempData.MustHaveSSEReg();
						DontUseAssignVariables(Ch[0], true);
					}
				}
			}

			//--------------------------------------------------------------------------------------
			else if (Op == x86Operator.Sin || Op == x86Operator.Cos)
			{
				if (Arch.FloatingPointMode == x86FloatingPointMode.SSE)
				{
					var Ch0Type = Ch[0].Type.RealId as Type;
					if (!Ch[0].IsMemory(Arch, x86OverlappingMode.Whole) || !Node.IsMemory(Arch, x86OverlappingMode.Whole))
						NeededTempData.MustHaveMemory(Ch0Type.Size);
				}

			}

			//------------------------------------------------------------------------------------
			else if (Op == x86Operator.Swap)
			{
				var Ch0Type = Ch[0].Type.RealId as Type;
				if (Ch[0].IsMemory(Arch) && Ch[1].IsMemory(Arch))
				{
					var Purpose = new x86TempGRegPurpose(x86TempGRegPurposeType.TwoMemOp);
					NeededTempData.MustHaveGReg(Arch, Purpose, Ch0Type.Size);
				}
			}
		}

		private void DontUseAssignVariables(ExpressionNode Node, bool Immediately = false)
		{
			if (Check_RecheckIndex == 0 && Node.DontUseThisVar(Immediately))
				Check_DontUseAssignVar = true;
		}

		private void DontUseAssignVariables(ExpressionNode Node1, ExpressionNode Node2, bool Immediately = false)
		{
			if (Check_RecheckIndex == 0 && (Node1.DontUseThisVar(Immediately) || Node2.DontUseThisVar(Immediately)))
				Check_DontUseAssignVar = true;
		}

		private bool ConvertData(x86DataLocation Dst, x86DataLocation Src,
			ref x86NeededTempData NeededTempData, x86ExecutorType Executor,
			Identifier To, Identifier From)
		{
			var ToStoredDataType = x86Identifiers.GetStoredDataType(To);
			var FromStoredDataType = x86Identifiers.GetStoredDataType(From);

			return ConvertData(Dst, Src, ref NeededTempData,
				x86ExecutorType.All, ToStoredDataType, FromStoredDataType);
		}

		private bool ConvertData(x86DataLocation Dst, x86DataLocation Src, 
			ref x86NeededTempData NeededTempData, x86ExecutorType Executor, 
			x86StoredDataType To, x86StoredDataType From)
		{
			if (!From.CheckLocation(Src)) throw new ArgumentException(null, "Dst");
			if (!To.CheckLocation(Dst)) throw new ArgumentException(null, "Src");

			if (From.IsEquivalent(To))
				return MoveData(Dst, Src, ref NeededTempData, Executor, From);

			var BothNonfloat = x86Identifiers.IsNonfloatTypeKind(To.TypeKind) &&
				x86Identifiers.IsNonfloatTypeKind(From.TypeKind);

			if (BothNonfloat && From.Precision > To.Precision &&
				!x86Identifiers.IsVectorTypeKind(To.TypeKind) &&
				!x86Identifiers.IsVectorTypeKind(From.TypeKind))
			{
				var SrcPart = Src.GetPart(0, Dst.Size);
				return MoveData(Dst, SrcPart, ref NeededTempData, Executor, To);
			}

			var RetValue = true;
			if ((Executor & x86ExecutorType.General) != 0 && BothNonfloat)
			{
				var NTDCopy = NeededTempData;
				var Signed = x86Identifiers.IsSignedTypeKind(From.TypeKind);

				x86DataLocations.SplitBySize(Dst, Src, To.Precision, From.Precision, To, From,
					(DstPart, SrcPart, ToPart, FromPart) =>
					{
						if (!ConvertScalarGeneral(DstPart, SrcPart, ref NTDCopy, Signed))
							RetValue = false;
					}
				);

				NeededTempData = NTDCopy;
			}
			else
			{
				throw new NotImplementedException();
			}

			return RetValue;
		}

		private bool ConvertScalarGeneral(x86DataLocation Dst, x86DataLocation Src,
			ref x86NeededTempData NeededTempData, bool Signed)
		{
			var RetValue = true;
			var MovIns = Signed ? "movsx" : "movzx";
			var Eax = new x86GRegLocation(Arch, 0, Arch.RegSize);
			var SplittedDst = x86DataLocations.Split(Dst, Arch.RegSize);
			var SplittedSrc = x86DataLocations.Split(Src, Arch.RegSize);

			for (var i = 0; i < SplittedDst.Length; i++)
			{
				var DstPart = SplittedDst[i];
				if (i >= SplittedSrc.Length)
				{
					if (!Signed)
					{
						if (!ZeroData(DstPart, ref NeededTempData,
							x86ExecutorType.General, new x86StoredDataType()))
						{
							RetValue = false;
						}
					}
				}
				else
				{
					var SrcPart = SplittedSrc[i];
					if (DstPart.Size > SrcPart.Size)
					{
						if (!DstPart.GetPart(0, SrcPart.Size).Compare(SrcPart))
						{
							if (!RequestTempIfMemoryDestination(DstPart, SrcPart,
								ref NeededTempData, x86ExecutorType.General))
							{
								RetValue = false;
							}
						}
					}
					else
					{
						SrcPart = SrcPart.GetPart(0, DstPart.Size);
						if (!DstPart.Compare(SrcPart))
						{
							if (!RequestTempIfTwoMemory(DstPart, SrcPart,
								ref NeededTempData, x86ExecutorType.General))
							{
								RetValue = false;
							}
						}
					}
				}
			}

			return RetValue;
		}

		private bool ZeroData(x86DataLocation Location, ref x86NeededTempData NeededTempData,
			x86ExecutorType Executor, Identifier Type)
		{
			var StoredDataType = x86Identifiers.GetStoredDataType(Type);
			return ZeroData(Location, ref NeededTempData, Executor, StoredDataType);
		}

		private bool ZeroData(x86DataLocation Location, ref x86NeededTempData NeededTempData,
			x86ExecutorType Executor, x86StoredDataType StoredDataType)
		{
			if (!StoredDataType.CheckLocation(Location))
				throw new ArgumentException(null, "Location");

			var RetValue = true;
			var NTDCopy = NeededTempData;
			if (Location is x86MultiLocation)
			{
				x86DataLocations.SplitByMultiLocation(Location, StoredDataType,
					(LocPart, StoredDataTypePart) =>
					{
						if (!ZeroData(LocPart, ref NTDCopy, Executor, StoredDataTypePart))
							RetValue = false;
					}
				);
			}
			else if (Location is x86MemoryLocation)
			{
				if (x86CodeGenerator.UseSSEMove(Arch, Location, Executor, StoredDataType))
				{
					NTDCopy.MustHaveSSEReg();
					Location = x86DataLocations.CutDownFromEnd(Location, Location.Size % 4);
					RetValue = false;
				}

				if (Location != null && (Executor & x86ExecutorType.General) != 0 &&
					!x86Identifiers.IsFloatTypeKind(StoredDataType.TypeKind))
				{
					Location = null;
				}

				if (Location != null) throw new ArgumentException(null, "Executor");
			}

			NeededTempData = NTDCopy;
			return RetValue;
		}

		private bool MoveData(x86DataLocation Dst, x86DataLocation Src,
			ref x86NeededTempData NeededTempData, x86ExecutorType Executor, Identifier Type)
		{
			if (Src is x86ConstLocation && x86Expressions.NeedReturnPointer(Type))
			{
				return MoveStructure(Dst, Src, ref NeededTempData, Type);
			}
			else
			{
				var StoredDataType = x86Identifiers.GetStoredDataType(Type);
				return MoveData(Dst, Src, ref NeededTempData, Executor, StoredDataType);
			}
		}

		private bool MoveData(x86DataLocation Dst, x86DataLocation Src, ref x86NeededTempData NeededTempData,
			x86ExecutorType Executor, x86StoredDataType StoredDataType)
		{
			if (!StoredDataType.CheckLocation(Dst)) throw new ArgumentException(null, "Dst");
			if (!StoredDataType.CheckLocation(Src)) throw new ArgumentException(null, "Src");

			var RetValue = true;
			var NTDCopy = NeededTempData;

			var ConstSrc = Src as x86ConstLocation;
			if (ConstSrc != null && ConstSrc.Unsigned == 0)
			{
				ZeroData(Dst, ref NTDCopy, Executor, StoredDataType);
			}
			else if (Dst is x86MultiLocation || Src is x86MultiLocation)
			{
				x86DataLocations.SplitByMultiLocation(Dst, Src, StoredDataType,
					(DstPart, SrcPart, StoredDataTypePart) =>
					{
						if (!MoveData(DstPart, SrcPart, ref NTDCopy, Executor, StoredDataTypePart))
							RetValue = false;
					}
				);
			}
			else if (Dst is x86MemoryLocation && Src is x86MemoryLocation && !Dst.Compare(Src))
			{
				if (Dst.Size != Src.Size)
					throw new ArgumentException("Dst.Size != Src.Size");

				if (x86CodeGenerator.UseSSEMove(Arch, Src, Executor, StoredDataType))
				{
					NTDCopy.MustHaveSSEReg();
					Dst = x86DataLocations.CutDownFromEnd(Dst, Dst.Size % 4);
					Src = x86DataLocations.CutDownFromEnd(Src, Src.Size % 4);
					RetValue = false;
				}

				if (Dst != null && (Executor & x86ExecutorType.General) != 0 &&
					!x86Identifiers.IsFloatTypeKind(StoredDataType.TypeKind))
				{
					var Precision = StoredDataType.Precision;
					if (Precision == 0 || Precision > Arch.RegSize)
						Precision = Arch.RegSize;

					var Purpose = new x86TempGRegPurpose(x86TempGRegPurposeType.TwoMemOp);
					NTDCopy.MustHaveGReg(Arch, Purpose, Precision, Dst.Size % 1 != 0);
					RetValue = false;
					Dst = null;
					Src = null;
				}

				if (Dst != null) throw new ArgumentException(null, "Executor");
			}

			NeededTempData = NTDCopy;
			return RetValue;
		}

		private bool RequestTempIfMemoryDestination(x86DataLocation Dst, x86DataLocation Src,
			ref x86NeededTempData NeededTempData, x86ExecutorType Executor)
		{
			if (Dst is x86MemoryLocation)
			{
				if (Executor == x86ExecutorType.SSE)
				{
					NeededTempData.MustHaveSSEReg();
					return false;
				}
				else if (Executor == x86ExecutorType.General)
				{
					var Purpose = new x86TempGRegPurpose(x86TempGRegPurposeType.TwoMemOp);
					NeededTempData.MustHaveGReg(Arch, Purpose, Dst.Size);
					return false;
				}
				else
				{
					throw new NotImplementedException();
				}
			}

			return true;
		}

		private bool RequestTempIfTwoMemory(x86DataLocation Dst, x86DataLocation Src, 
			ref x86NeededTempData NeededTempData, x86ExecutorType Executor)
		{
			if (Dst is x86MemoryLocation && Src is x86MemoryLocation)
			{
				if (Executor == x86ExecutorType.SSE)
				{
					NeededTempData.MustHaveSSEReg();
					return false;
				}
				else if (Executor == x86ExecutorType.General)
				{
					var Purpose = new x86TempGRegPurpose(x86TempGRegPurposeType.TwoMemOp);
					NeededTempData.MustHaveGReg(Arch, Purpose, Src.Size);
					return false;
				}
				else
				{
					throw new ArgumentException(null, "Executor");
				}
			}

			return true;
		}
	}

}
