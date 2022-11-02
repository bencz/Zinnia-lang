using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Zinnia.Base;

namespace Zinnia.x86
{
	public struct x86RegisterMask
	{
		public int Offset, Size;

		public x86RegisterMask(int Offset, int Size)
		{
			if (Offset < 0) throw new ArgumentOutOfRangeException("Offset");
			if (Size < 0) throw new ArgumentOutOfRangeException("Offset");

			this.Offset = Offset;
			this.Size = Size;
		}

		public x86RegisterMask(int Size)
		{
			if (Size < 0) throw new ArgumentOutOfRangeException("Offset");

			this.Offset = 0;
			this.Size = Size;
		}

		public x86RegisterMask Intersect(x86RegisterMask With)
		{
			var Start = Math.Max(Offset, With.Offset);
			var End = Math.Min(Offset + Size, With.Offset + With.Size);

			if (End <= Start) return new x86RegisterMask(0);
			return new x86RegisterMask(Start, End - Start);
		}

		public x86RegisterMask Union(x86RegisterMask With)
		{
			var Start = Math.Min(Offset, With.Offset);
			var End = Math.Max(Offset + Size, With.Offset + With.Size);
			return new x86RegisterMask(Start, End - Start);
		}

		public x86RegisterMask Subract(x86RegisterMask Mask)
		{
			if (Mask.Offset > Offset)
			{
				if (Mask.Offset >= Offset + Size) return this;
				return new x86RegisterMask(Offset, Mask.Offset - Offset);
			}

			var Cut = Offset - Mask.Offset;
			return new x86RegisterMask(Offset + Cut, Size - Cut);
		}

		public bool IsFree(x86RegisterMask Mask)
		{
			return Size == 0 || Mask.Size == 0 || Mask.Offset >= Offset + Size ||
				Mask.Offset + Mask.Size <= Offset;
		}
	}

	public class x86DataList
	{
		public x86Architecture Arch;
		public x86GRegisterList GRegisters;
		public x86RegisterList SSERegisters;
		public int StackOffset = 0;

		public x86DataList(x86Architecture Arch, bool Alloc = true)
		{
			this.Arch = Arch;

			if (Alloc)
			{
				GRegisters = new x86GRegisterList(Arch.RegCount);
				SSERegisters = new x86RegisterList(Arch.RegCount);
			}
		}

		public void Set(x86DataList DataList)
		{
			GRegisters.Set(DataList.GRegisters);
			SSERegisters.Set(DataList.SSERegisters);
			StackOffset = DataList.StackOffset;
		}

		public void SetUsed(x86TemporaryData Arrays)
		{
			if (Arrays.Memory != null)
			{
				SetUsed(Arrays.Memory);
			}

			if (Arrays.GRegs != null)
			{
				for (var i = 0; i < Arrays.GRegs.Length; i++)
					SetUsed(Arrays.GRegs[i].Location);
			}

			if (Arrays.SSERegs != null)
			{
				for (var i = 0; i < Arrays.SSERegs.Length; i++)
					SetUsed(Arrays.SSERegs[i]);
			}
		}

		public void SetUnused(x86GRegLocation Pos)
		{
			GRegisters.SetUnused(Pos);
		}

		public void SetUnused(x86SSERegLocation Pos)
		{
			SSERegisters.SetUnused(Pos.Index);
		}

		public void SetUsed(x86GRegLocation Pos)
		{
			GRegisters.SetUsed(Pos);
		}

		public void SetUsed(x86SSERegLocation Pos)
		{
			SSERegisters.SetUsed(Pos.Index);
		}

		public void SetUnused(x86StackLocation Pos)
		{
			StackOffset -= Pos.StackOffset;
		}

		public void SetUsed(x86StackLocation Pos)
		{
			var New = Pos.Offset + Pos.Size;
			if (StackOffset < New) StackOffset = New;
		}

		public void SetUnused(x86DataLocation Pos)
		{
			if (Pos is x86GRegLocation)
			{
				SetUnused(Pos as x86GRegLocation);
			}
			else if (Pos is x86StackLocation)
			{
				SetUnused(Pos as x86StackLocation);
			}
			else if (Pos is x86SSERegLocation)
			{
				SetUnused(Pos as x86SSERegLocation);
			}
			else if (Pos is x86MultiLocation)
			{
				var MPos = Pos as x86MultiLocation;
				for (var i = 0; i < MPos.Locations.Length; i++)
					SetUnused(MPos.Locations[i]);
			}
			else
			{
				throw new ApplicationException();
			}
		}

		public void SetUsed(x86DataLocation Pos)
		{
			if (Pos is x86GRegLocation)
			{
				SetUsed(Pos as x86GRegLocation);
			}
			else if (Pos is x86StackLocation)
			{
				SetUsed(Pos as x86StackLocation);
			}
			else if (Pos is x86SSERegLocation)
			{
				SetUsed(Pos as x86SSERegLocation);
			}
			else if (Pos is x86MultiLocation)
			{
				var MPos = Pos as x86MultiLocation;
				for (var i = 0; i < MPos.Locations.Length; i++)
					SetUsed(MPos.Locations[i]);
			}
			else
			{
				throw new ApplicationException();
			}
		}

		public bool IsFree(x86DataLocation Pos)
		{
			if (Pos is x86GRegLocation)
			{
				return GRegisters.IsFree(Pos as x86GRegLocation);
			}
			else if (Pos is x86SSERegLocation)
			{
				return !SSERegisters[(Pos as x86SSERegLocation).Index];
			}
			else if (Pos is x86StackLocation)
			{
				return StackOffset <= (Pos as x86StackLocation).Offset;
			}
			else if (Pos is x86MultiLocation)
			{
				var MPos = Pos as x86MultiLocation;
				for (var i = 0; i < MPos.Locations.Length; i++)
					if (!IsFree(MPos.Locations[i])) return false;

				return true;
			}
			else
			{
				throw new ApplicationException();
			}
		}

		public void SetUnused(x86DataList List)
		{
			GRegisters.SetUnused(List.GRegisters);
			SSERegisters.SetUnused(List.SSERegisters);
			StackOffset -= List.StackOffset;
		}

		public void SetUsed(x86DataList List)
		{
			GRegisters.SetUsed(List.GRegisters);
			SSERegisters.SetUsed(List.SSERegisters);

			if (StackOffset < List.StackOffset)
				StackOffset = List.StackOffset;
		}

		public void Reset()
		{
			StackOffset = 0;
			GRegisters.Reset();
			SSERegisters.Reset();
		}

		public x86DataList Union(x86DataList With)
		{
			var Ret = Copy();
			Ret.SetUsed(With);
			return Ret;
		}

		public x86DataList Copy()
		{
			var Ret = new x86DataList(Arch);
			if (GRegisters.Initialized) Ret.GRegisters = GRegisters.Copy();
			if (SSERegisters.Initialized) Ret.SSERegisters = SSERegisters.Copy();
			Ret.StackOffset = StackOffset;
			return Ret;
		}

		public bool IsZero
		{
			get
			{
				if (StackOffset != 0) return false;
				if (!GRegisters.IsZero) return false;
				if (!SSERegisters.IsZero) return false;
				return true;
			}
		}
	}

	public class x86DataAllocator : x86DataList
	{
		public CompilerState State;
		public IdContainer Container;
		public FunctionScope FuncScope;
		public x86IdContainerData ContainerData;
		public x86FuncScopeData FSData;
		public x86CallingConvention x86CallConv;

		public x86DataAllocator(IdContainer Container, bool AllocRegsLists = true)
			: base(Container.State.Arch as x86Architecture, AllocRegsLists)
		{
			this.Container = Container;
			this.State = Container.State;
			this.FuncScope = Container.FunctionScope;
			this.x86CallConv = Arch.GetCallingConvention(FuncScope.Type.CallConv);
			this.ContainerData = Container.Data.Get<x86IdContainerData>();
			this.FSData = FuncScope.Data.Get<x86FuncScopeData>();
		}

		bool IsGRegisterFree(int Index, x86RegisterMask Mask, bool EnableParamLocs = false)
		{
			if (!GRegisters.IsFree(Index, Mask)) return false;
			if (!FSData.DisabledLocations.GRegisters.IsFree(Index, Mask)) return false;

			if (!EnableParamLocs)
			{
				var P = ContainerData.UsedByParams;
				if (P.GRegisters.Initialized && !P.GRegisters.IsFree(Index, Mask))
					return false;
			}

			return true;
		}

		bool IsSSERegisterFree(int Index, bool EnableParamLocs = false)
		{
			if (!SSERegisters.IsFree(Index)) return false;
			if (!FSData.DisabledLocations.SSERegisters.IsFree(Index)) return false;

			if (!EnableParamLocs)
			{
				var P = ContainerData.UsedByParams;
				if (P.GRegisters.Initialized && !P.SSERegisters.IsFree(Index))
					return false;
			}

			return true;
		}

		public bool RegAvaiable()
		{
			for (var i = 0; i < GRegisters.Size; i++)
			{
				if (IsGRegisterFree(i, Arch.RegisterMask, true))
					return true;
			}

			return false;
		}

		public bool SSERegAvaiable()
		{
			for (var i = 0; i < SSERegisters.Size; i++)
			{
				if (IsSSERegisterFree(i, true))
					return true;
			}

			return false;
		}

		public x86DataLocation Allocate(Type T, x86DataLocationType DataCalcPos = x86DataLocationType.None, x86DataList CantBe = null)
		{
			if (DataCalcPos == x86DataLocationType.None)
				DataCalcPos = x86Identifiers.GetPossibleLocations(T);

			return Allocate(T.Size, T.Align, DataCalcPos, CantBe);
		}

		x86GRegLocation AllocGRegHelper(int Size, bool OneByteVariant,
			x86GRegisterList CantBe = new x86GRegisterList(), bool EnableParamLocs = false)
		{
			if (Size == 1) OneByteVariant = false;

			var Mask = new x86RegisterMask(Size);
			var HighMask = new x86RegisterMask(1, 1);

			var Sequence = x86CallConv.AllocationSequence.GRegisters;
			for (var i = 0; i < Sequence.Length; i++)
			{
				var Reg = Sequence[i];
				if (Arch.IsGRegisterExists(Reg, Mask) && IsGRegisterFree(Reg, Mask, EnableParamLocs))
				{
					if (CantBe.Initialized && !CantBe.IsFree(Reg, Mask)) continue;
					if (OneByteVariant && !Arch.IsGRegisterExists(Reg, 0, 1)) continue;

					GRegisters.SetUsed(Reg, Mask);
					return new x86GRegLocation(Arch, Reg, Mask);
				}

				if (Size == 1 && Arch.IsGRegisterExists(Reg, HighMask) &&
					IsGRegisterFree(Reg, HighMask, EnableParamLocs))
				{
					if (CantBe.Initialized && !CantBe.IsFree(Reg, HighMask)) continue;
					GRegisters.SetUsed(Reg, HighMask);
					return new x86GRegLocation(Arch, Reg, HighMask);
				}
			}

			return null;
		}

		public x86DataLocation Allocate(x86DataProperties DataProperties)
		{
			return Allocate(DataProperties.Size, DataProperties.Align, DataProperties.Type, DataProperties.CantBe);
		}

		public x86DataLocation Allocate(int Size, int Align, x86DataLocationType DataCalcPos = x86DataLocationType.GRegMem,
			x86DataList CantBe = null)
		{
			if ((DataCalcPos & x86DataLocationType.SSEReg) != 0)
			{
				if (Size > Arch.SSERegSize)
				{
					if (SSERegAvaiable())
					{
						var Ret = AllocMultiPos(Size, Align, DataCalcPos, CantBe, 16);
						if (Ret != null) return Ret;
					}
				}
				else
				{
					var SSERegList = CantBe != null ? CantBe.SSERegisters : new x86RegisterList();
					var Ret = AllocSSERegister(16, SSERegList);
					if (Ret != null) return Ret;
				}
			}

			if ((DataCalcPos & x86DataLocationType.General) != 0)
			{
				if (Size > Arch.RegSize)
				{
					if (RegAvaiable())
					{
						var Ret = AllocMultiPos(Size, Align, DataCalcPos, CantBe, Arch.RegSize);
						if (Ret != null) return Ret;
					}
				}
				else
				{
					var GRegList = CantBe != null ? CantBe.GRegisters : new x86GRegisterList();
					var Ret = AllocGRegister(Size, (DataCalcPos & x86DataLocationType.OneByte) != 0, GRegList);
					if (Ret != null) return Ret;
				}
			}

			if ((DataCalcPos & x86DataLocationType.Memory) != 0)
			{
				var Ret = AllocMemory(Size, Align);
				if (Ret != null) return Ret;
			}

			return null;
		}

		public x86SSERegLocation AllocSSERegHelper(int Size, x86RegisterList CantBe = new x86RegisterList(), bool EnableParamLocs = false)
		{
			var Sequence = x86CallConv.AllocationSequence.SSERegisters;
			for (var i = 0; i < Sequence.Length; i++)
			{
				var Reg = Sequence[i];
				if (IsSSERegisterFree(Reg) && (!CantBe.Initialized || !CantBe[Reg]))
				{
					SSERegisters[Reg] = true;
					return new x86SSERegLocation(Arch, Reg, Size);
				}
			}

			return null;
		}

		public x86SSERegLocation AllocSSERegister(int Size, x86RegisterList CantBe = new x86RegisterList())
		{
			var Ret = AllocSSERegHelper(Size, CantBe);
			if (Ret != null) return Ret;

			return AllocSSERegHelper(Size, CantBe, true);
		}

		public x86GRegLocation AllocGRegister(x86NeededGRegister Reg, x86GRegisterList CantBe = new x86GRegisterList())
		{
			return AllocGRegister(Reg.Size, Reg.OneByteVariant, CantBe);
		}

		public x86GRegLocation AllocGRegister(int Size, bool OneByteVariant = false,
			x86GRegisterList CantBe = new x86GRegisterList())
		{
			var Ret = AllocGRegHelper(Size, OneByteVariant, CantBe);
			if (Ret == null) Ret = AllocGRegHelper(Size, OneByteVariant, CantBe, true);
			return Ret;
		}

		public x86StackLocation AllocMemory(int Size, int Align)
		{
			var P = DataStoring.AlignWithIncrease(StackOffset, Align);
			StackOffset = P + Size;

			return new x86StackLocation(Arch, FuncScope, P, Size, false);
		}

		private x86DataLocation AllocMultiPos(int Size, int Align, x86DataLocationType DataCalcPos, x86DataList CantBe, int PartSize)
		{
			var Count = Size / PartSize;
			var Positions = new x86DataLocation[Count];

			for (var i = 0; i < Count; i++)
			{
				Positions[i] = Allocate(Arch.RegSize, Align, DataCalcPos, CantBe);
				if (Positions[i] == null) return null;
			}

			return new x86MultiLocation(Arch, Size, Positions);
		}

		public x86DataAllocator CopyAllocator()
		{
			var Ret = new x86DataAllocator(Container, false);
			if (GRegisters.Initialized) Ret.GRegisters = GRegisters.Copy();
			if (SSERegisters.Initialized) Ret.SSERegisters = SSERegisters.Copy();
			Ret.StackOffset = StackOffset;
			return Ret;
		}

		public x86DataLocation GetAllocated(x86DataLocationType Type, int Size, int Align = -1)
		{
			if ((Type & x86DataLocationType.General) != 0)
			{
				var Sequence = x86CallConv.AllocationSequence.GRegisters;
				var Mask = new x86RegisterMask(Size);
				var HighMask = new x86RegisterMask(1, Size);

				if ((Type & x86DataLocationType.OneByte) == 0)
				{
					for (var i = 0; i < Sequence.Length; i++)
					{
						var Reg = Sequence[i];
						if (Arch.IsGRegisterExists(Reg, Mask) && !Arch.IsGRegisterExists(Reg, 0, 1) && !GRegisters.IsFree(Reg, Mask))
							return new x86GRegLocation(Arch, Reg, Mask);
					}

					for (var i = 0; i < Sequence.Length; i++)
					{
						var Reg = Sequence[i];
						if (Arch.IsGRegisterExists(Reg, Mask) && !GRegisters.IsFree(Reg, Mask))
							return new x86GRegLocation(Arch, Reg, Mask);
					}
				}
				else
				{
					for (var i = 0; i < Sequence.Length; i++)
					{
						var Reg = Sequence[i];
						if (Arch.IsGRegisterExists(Reg, Mask) && Arch.IsGRegisterExists(Reg, 0, 1) && !GRegisters.IsFree(Reg, Mask))
							return new x86GRegLocation(Arch, Reg, Mask);
					}
				}
			}

			if ((Type & x86DataLocationType.SSEReg) != 0)
			{
				var Sequence = x86CallConv.AllocationSequence.SSERegisters;
				for (var i = 0; i < Sequence.Length; i++)
				{
					var Reg = Sequence[i];
					if (Reg < Arch.RegCount && !SSERegisters.IsFree(Reg))
						return new x86SSERegLocation(Arch, Reg, Size);
				}
			}

			if ((Type & x86DataLocationType.Memory) != 0)
			{
				StackOffset = DataStoring.AlignWithDecrease(StackOffset - Size, Align);
				return new x86StackLocation(Arch, FuncScope, StackOffset, Size, false);
			}

			return null;
		}

		public x86DataLocation Deallocate(x86DataLocationType Type, int Size, int Align = -1)
		{
			var Ret = GetAllocated(Type, Size, Align);
			SetUnused(Ret);
			return Ret;
		}
	}

	public struct x86RegisterList
	{
		public bool[] UsedRegs;

		public x86RegisterList(int Size)
		{
			UsedRegs = new bool[Size];
		}

		public bool Initialized
		{
			get { return UsedRegs != null; }
		}

		public int Size
		{
			get { return UsedRegs.Length; }
		}

		public bool this[int Pos]
		{
			get { return UsedRegs[Pos]; }
			set { UsedRegs[Pos] = value; }
		}

		public x86GRegisterList ToGRegList(x86RegisterMask Mask)
		{
			var Ret = new x86GRegisterList(Size);
			for (var i = 0; i < Size; i++)
				Ret[i] = Mask;

			return Ret;
		}

		public x86RegisterList Inverse()
		{
			var Ret = new x86RegisterList(Size);
			for (var i = 0; i < Size; i++)
				if (!UsedRegs[i]) Ret[i] = true;

			return Ret;
		}

		public void Reset()
		{
			if (Initialized)
			{
				for (var i = 0; i < Size; i++)
					UsedRegs[i] = false;
			}
		}

		public void SetUsed(int Index)
		{
			UsedRegs[Index] = true;
		}

		public void SetUsed(x86RegisterList Lst)
		{
			for (var i = 0; i < Lst.Size; i++)
				if (Lst[i]) UsedRegs[i] = true;
		}

		public void SetUnused(int Index)
		{
			UsedRegs[Index] = false;
		}

		public void SetUnused(x86RegisterList List)
		{
			for (var i = 0; i < List.Size; i++)
				if (List[i]) UsedRegs[i] = false;
		}

		public x86RegisterList Intersect(x86RegisterList Other)
		{
			if (!Initialized) return Other;
			if (!Other.Initialized) return this;

			var MinSize = Math.Min(Size, Other.Size);
			var Ret = new x86RegisterList(MinSize);

			for (var i = 0; i < MinSize; i++)
				if (this[i] && Other[i]) Ret[i] = true;

			return Ret;
		}

		public x86RegisterList Union(x86RegisterList Other)
		{
			if (!Initialized) return Other;
			if (!Other.Initialized) return this;

			var MaxSize = Math.Max(Size, Other.Size);
			var Ret = new x86RegisterList(MaxSize);

			for (var i = 0; i < MaxSize; i++)
			{
				if (i < Size && this[i]) Ret[i] = true;
				else if (i < Other.Size && Other[i]) Ret[i] = true;
			}

			return Ret;
		}

		public x86RegisterList Copy()
		{
			if (Initialized)
			{
				var Ret = new x86RegisterList(Size);
				for (var i = 0; i < Size; i++)
					Ret[i] = this[i];

				return Ret;
			}
			
			return new x86RegisterList();
		}

		public bool Contains(int Index)
		{
			return this[Index];
		}

		public void Set(x86RegisterList List)
		{
			if (List.Size != Size)
				throw new ApplicationException();

			for (var i = 0; i < Size; i++)
				UsedRegs[i] = List[i];
		}

		public bool IsFree(int Index)
		{
			return !UsedRegs[Index];
		}

		public bool IsFree(x86SSERegLocation SSEReg)
		{
			return !UsedRegs[SSEReg.Index];
		}

		public bool IsZero
		{
			get
			{
				if (UsedRegs != null)
				{
					for (var i = 0; i < UsedRegs.Length; i++)
						if (UsedRegs[i]) return false;
				}

				return true;
			}
		}
	}

	public struct x86GRegisterList
	{
		public x86RegisterMask[] UsedRegs;

		public x86GRegisterList(int Size)
		{
			UsedRegs = new x86RegisterMask[Size];
		}

		public bool Initialized
		{
			get { return UsedRegs != null; }
		}

		public int Size
		{
			get { return UsedRegs.Length; }
		}

		public x86RegisterMask this[int Pos]
		{
			get { return UsedRegs[Pos]; }
			set { UsedRegs[Pos] = value; }
		}

		public void Reset()
		{
			if (Initialized)
			{
				for (var i = 0; i < Size; i++)
					UsedRegs[i] = new x86RegisterMask();
			}
		}

		public void SetUsed(int Index, x86RegisterMask Mask)
		{
			UsedRegs[Index] = UsedRegs[Index].Union(Mask);
		}

		public void SetUnused(int Index, x86RegisterMask Mask)
		{
			UsedRegs[Index] = UsedRegs[Index].Subract(Mask);
		}

		public void SetUsed(x86RegisterList List, x86RegisterMask Mask)
		{
			for (var i = 0; i < List.Size; i++)
				if (List[i]) SetUsed(i, Mask);
		}

		public void SetUsed(x86GRegisterList List)
		{
			for (var i = 0; i < List.Size; i++)
				SetUsed(i, List[i]);
		}

		public void SetUnused(x86GRegisterList List)
		{
			for (var i = 0; i < List.Size; i++)
				SetUnused(i, List[i]);
		}

		public bool IsFree(int Index, x86RegisterMask Mask)
		{
			return UsedRegs[Index].IsFree(Mask);
		}

		public bool IsFree(x86GRegLocation Pos)
		{
			return UsedRegs[Pos.Index].IsFree(Pos.Mask);
		}

		public void SetUsed(x86GRegLocation Pos)
		{
			SetUsed(Pos.Index, Pos.Mask);
		}

		public void SetUnused(x86GRegLocation Pos)
		{
			SetUnused(Pos.Index, Pos.Mask);
		}

		public bool IsFree(x86DataLocation Pos)
		{
			if (Pos is x86GRegLocation)
			{
				var RegPos = Pos as x86GRegLocation;
				return IsFree(RegPos);
			}
			else if (Pos is x86MultiLocation)
			{
				var MultiPos = Pos as x86MultiLocation;
				foreach (var e in MultiPos.Locations)
				{
					var RegPos = e as x86GRegLocation;
					if (RegPos == null) throw new ApplicationException();
					return IsFree(RegPos);
				}

				return true;
			}
			else
			{
				throw new ApplicationException();
			}
		}
		
		public void SetUnused(x86DataLocation Pos)
		{
			if (Pos is x86GRegLocation)
			{
				SetUnused(Pos as x86GRegLocation);
			}
			else if (Pos is x86MultiLocation)
			{
				var MultiPos = Pos as x86MultiLocation;
				for (var i = 0; i < MultiPos.Locations.Length; i++)
				{
					var RegPos = MultiPos.Locations[i] as x86GRegLocation;
					if (RegPos == null) throw new ApplicationException();
					SetUnused(RegPos);
				}
			}
			else
			{
				throw new ApplicationException();
			}
		}

		public void SetUsed(x86DataLocation Pos)
		{
			if (Pos is x86GRegLocation)
			{
				SetUsed(Pos as x86GRegLocation);
			}
			else if (Pos is x86MultiLocation)
			{
				var MultiPos = Pos as x86MultiLocation;
				for (var i = 0; i < MultiPos.Locations.Length; i++)
				{
					var RegPos = MultiPos.Locations[i] as x86GRegLocation;
					if (RegPos == null) throw new ApplicationException();
					SetUsed(RegPos);
				}
			}
			else
			{
				throw new ApplicationException();
			}
		}

		public x86GRegisterList Intersect(x86GRegisterList Other)
		{
			if (!Initialized) return Other;
			if (!Other.Initialized) return this;

			var RetSize = Math.Min(Size, Other.Size);
			var Ret = new x86GRegisterList(RetSize);

			for (var i = 0; i < RetSize; i++)
				Ret[i] = this[i].Intersect(Other[i]);

			return Ret;
		}

		public x86GRegisterList Union(x86GRegisterList Other)
		{
			if (!Initialized) return Other;
			if (!Other.Initialized) return this;

			var RetSize = Math.Max(Size, Other.Size);
			var Ret = new x86GRegisterList(RetSize);

			for (var i = 0; i < RetSize; i++)
			{
				if (i < Size && i < Other.Size)
					Ret[i] = this[i].Union(Other[i]);
				else if (i < Size) Ret[i] = this[i];
				else if (i < Other.Size) Ret[i] = Other[i];
			}

			return Ret;
		}

		public x86GRegisterList Copy()
		{
			if (Initialized)
			{
				var Ret = new x86GRegisterList(Size);
				for (var i = 0; i < Size; i++)
					Ret[i] = this[i];

				return Ret;
			}

			return new x86GRegisterList();
		}

		public void Set(x86GRegisterList List)
		{
			if (List.Size != Size)
				throw new ApplicationException();

			for (var i = 0; i < Size; i++)
				UsedRegs[i] = List[i];
		}

		public bool IsZero
		{
			get
			{
				if (UsedRegs != null)
				{
					for (var i = 0; i < UsedRegs.Length; i++)
						if (UsedRegs[i].Size != 0) return false;
				}

				return true;
			}
		}
	}

	public struct x86SequenceOptions
	{
		public int[] GRegisters;
		public int[] SSERegisters;
		public bool AllowPartRegisters;
		public int Align;
	}

	public class x86DataSequence
	{
		public x86Architecture Arch;
		public x86DataList StoredDataList;
		public x86SequenceOptions Options;

		public bool NextMaybeHighByte;
		public int GRegisterIndex;
		public int SSERegisterIndex;
		public int StackOffset;

		public x86DataSequence(x86Architecture Arch, x86SequenceOptions Options)
		{
			this.Arch = Arch;
			this.Options = Options;
		}

		public int UsedCount
		{
			get { return NextMaybeHighByte ? GRegisterIndex + 1 : GRegisterIndex; }
		}

		public bool CanAllocGReg(int Size = -1)
		{
			var RegCount = 1;
			if (Size != -1)
				RegCount = (Size - 1) / Arch.RegSize + 1;

			if (Size > 1 && NextMaybeHighByte)
				return GRegisterIndex + RegCount + 1 <= Options.GRegisters.Length;
			else return GRegisterIndex + RegCount <= Options.GRegisters.Length;
		}

		public bool CanAllocSSEReg()
		{
			return SSERegisterIndex < Options.SSERegisters.Length;
		}

		public x86SSERegLocation GetSSERegister(int Size = 16)
		{
			if (SSERegisterIndex >= Options.SSERegisters.Length)
				return null;

			var Ret = new x86SSERegLocation(Arch, Options.SSERegisters[SSERegisterIndex], Size);
			if (StoredDataList != null) StoredDataList.SSERegisters[SSERegisterIndex] = true;

			SSERegisterIndex++;
			return Ret;
		}

		public int GetGRegisterIndex()
		{
			if (GRegisterIndex >= Options.GRegisters.Length) 
				return -1;

			var ReturnVal = Options.GRegisters[GRegisterIndex];
			GRegisterIndex++;
			return ReturnVal;
		}

		public int GetSSERegisterIndex()
		{
			if (SSERegisterIndex >= Options.SSERegisters.Length)
				return -1;

			var ReturnVal = Options.SSERegisters[SSERegisterIndex];
			SSERegisterIndex++;
			return ReturnVal;
		}

		public x86GRegLocation GetGRegister(int Size)
		{
			var Ret = (x86GRegLocation)null;

			if (NextMaybeHighByte)
			{
				if (Size == 1)
				{
					var Reg = Options.GRegisters[GRegisterIndex];
					Ret = new x86GRegLocation(Arch, Reg, 1, 1);
				}

				NextMaybeHighByte = false;
				GRegisterIndex++;
			}

			if (Ret == null)
			{
				if (GRegisterIndex >= Options.GRegisters.Length)
					return null;

				var Reg = Options.GRegisters[GRegisterIndex];
				if (Options.AllowPartRegisters && Size == 1 && Arch.IsGRegisterExists(GRegisterIndex, 1, 1))
				{
					Ret = new x86GRegLocation(Arch, Reg, Size);
					NextMaybeHighByte = true;
				}
				else
				{
					Ret = new x86GRegLocation(Arch, Reg, Size);
					GRegisterIndex++;
				}
			}

			if (StoredDataList != null)
				StoredDataList.GRegisters.SetUsed(Ret);

			return Ret;
		}

		public void AlignStack(int Align)
		{
			StackOffset = DataStoring.AlignWithIncrease(StackOffset, Align);
		}

		public x86StackLocation GetStackPosition(FunctionScope Scope, int Size, int Align = 1)
		{
			AlignStack(Math.Max(Options.Align, Align));
			var Ret = new x86StackLocation(Arch, Scope, StackOffset, Size, true);

			StackOffset += Size;
			AlignStack(Options.Align);

			if (StoredDataList != null && StoredDataList.StackOffset < StackOffset)
				StoredDataList.StackOffset = StackOffset;

			return Ret;
		}

		public x86DataLocation GetPosition(FunctionScope Scope, int Size, int Align = 1,
			x86DataLocationType PositionTypes = x86DataLocationType.GRegMem)
		{
			if ((PositionTypes & x86DataLocationType.General) != 0)
			{
				if (CanAllocGReg(Size))	return GetGRegister(Size);
			}

			if ((PositionTypes & x86DataLocationType.SSEReg) != 0)
			{
				if (CanAllocSSEReg() && Size <= Arch.SSERegSize)
					return GetSSERegister(Arch.SSERegSize);
			}

			if ((PositionTypes & x86DataLocationType.Memory) != 0)
				return GetStackPosition(Scope, Size, Align);

			return null;
		}

		public x86DataLocation GetPosition(FunctionScope Scope, Type Type)
		{
			var DataLocType = x86Identifiers.GetPossibleLocations(Type);
			return GetPosition(Scope, Type.Size, Type.Align, DataLocType);
		}
	}

}
