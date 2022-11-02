using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Numerics;
using Zinnia.Base;
using Zinnia.NativeCode;

namespace Zinnia.x86
{
	public enum x86GetNodePosMode : byte
	{
		Default,
		PositionOnly,
		ReturnNull,
	}

	public class x86CodeGenerator : CodeGenerator, INCCodeGenerator
	{
		public x86Architecture Arch;

		public x86CodeGenerator(CompilerState State)
			: base(State)
		{
			this.Arch = State.Arch as x86Architecture;
		}

		public x86CodeGenerator(IdContainer Container)
			: this(Container.State)
		{
			this.Container = Container;
		}

		#region InstructionEmitter
		private void EmitInstruction(string Instruction, params object[] Parameters)
		{
			InsContainer.Add(x86InstructionEncoder.EncodeToText(Instruction, Parameters));
		}

		private string DecorateSSEInstruction(string Instruction, Identifier Type)
		{
			var StoredDataType = x86Identifiers.GetStoredDataType(Type);
			return DecorateSSEInstruction(Instruction, StoredDataType);
		}

		private string DecorateSSEInstruction(string Instruction, x86StoredDataType StoredDataType)
		{
			if (!x86Identifiers.IsFloatTypeKind(StoredDataType.TypeKind))
				Instruction = "p" + Instruction;

			return Instruction + x86Identifiers.GetSSETypeString(StoredDataType);
		}

		private string GetSSEMoveInstruction(Identifier Type, int Alignnent = 1)
		{
			var StoredDataType = x86Identifiers.GetStoredDataType(Type);
			return GetSSEMoveInstruction(StoredDataType, Alignnent);
		}

		private string GetSSEMoveInstruction(x86StoredDataType StoredDataType, int Alignnent = 1)
		{
			if (x86Identifiers.IsNonfloatTypeKind(StoredDataType.TypeKind) &&
				x86Identifiers.IsVectorTypeKind(StoredDataType.TypeKind))
			{
				StoredDataType.Precision = 16;
			}

			var Ret = "mov" + x86Identifiers.GetSSETypeString(StoredDataType);
			if (x86Identifiers.IsVectorTypeKind(StoredDataType.TypeKind) || StoredDataType.Precision >= 16)
				Ret += Alignnent >= 16 ? "a" : "u";

			return Ret;
		}

		private string DecorateInstruction(string Instruction, x86ExecutorType Executor, x86StoredDataType StoredDataType)
		{
			if (Executor == x86ExecutorType.SSE)
				return DecorateSSEInstruction(Instruction, StoredDataType);
			else return Instruction;
		}

		private string DecorateInstruction(string Instruction, x86ExecutorType Executor, Identifier Type)
		{
			if (Executor == x86ExecutorType.SSE)
				return DecorateSSEInstruction(Instruction, Type);
			else return Instruction;
		}

		private string GetMoveInstruction(x86ExecutorType Executor, x86StoredDataType StoredDataType, int Alignment = -1)
		{
			if (Executor == x86ExecutorType.SSE)
				return GetSSEMoveInstruction(StoredDataType, Alignment);
			else if (Executor == x86ExecutorType.General)
				return "mov";
			else throw new ArgumentException(null, "Executor");
		}

		private string GetMoveInstruction(x86ExecutorType Executor, Identifier Type, int Alignment = -1)
		{
			var StoredDataType = x86Identifiers.GetStoredDataType(Type);
			return GetMoveInstruction(Executor, Type, Alignment);
		}

		private void EmitMoveInstructionChecked(x86DataLocation Dst, x86DataLocation Src, x86TemporaryData TempData,
			x86ExecutorType Executor, x86StoredDataType StoredDataType, int Alignment = 1)
		{
			if (!Dst.Compare(Src))
				EmitMoveInstruction(Dst, Src, TempData, Executor, StoredDataType, Alignment);
		}

		private void EmitMoveInstruction(x86DataLocation Dst, x86DataLocation Src, x86TemporaryData TempData,
			x86ExecutorType Executor, x86StoredDataType StoredDataType, int Alignment = 1)
		{
			var MoveIns = GetMoveInstruction(Executor, StoredDataType, Alignment);
			EmitInstructionWithoutTwoMemory(MoveIns, Dst, Src, TempData, Executor, StoredDataType, true);
		}

		private void EmitMoveInstruction(x86DataLocation Dst, x86DataLocation Src, x86TemporaryData TempData,
			x86ExecutorType Executor, Identifier Type, int Alignment = 1)
		{
			var StoredDataType = x86Identifiers.GetStoredDataType(Type);
			var MoveIns = GetMoveInstruction(Executor, StoredDataType, Alignment);
			EmitInstructionWithoutTwoMemory(MoveIns, Dst, Src, TempData, Executor, StoredDataType, true);
		}

		private void EmitDecoratedInstruction(string Instruction, x86ExecutorType Executor,
			x86StoredDataType StoredDataType, params object[] Parameters)
		{
			Instruction = DecorateInstruction(Instruction, Executor, StoredDataType);
			EmitInstruction(Instruction, Parameters);
		}

		private void EmitInstructionNonmemoryDestination(string Instruction, x86DataLocation Dst,
			x86DataLocation Src, x86TemporaryData TempData, x86ExecutorType Executor,
			x86StoredDataType StoredDataType, x86StoredDataType DataTypeForInstruction, bool DstOnlyWritten = false)
		{
			Instruction = DecorateInstruction(Instruction, Executor, DataTypeForInstruction);

			EmitInstructionNonmemoryDestination(Instruction, Dst, Src,
				TempData, Executor, StoredDataType, DstOnlyWritten, true);
		}

		private void EmitInstructionNonmemoryDestination(string Instruction, x86DataLocation Dst,
			x86DataLocation Src, x86TemporaryData TempData, x86ExecutorType Executor,
			x86StoredDataType StoredDataType, bool DstOnlyWritten = false, bool DontDecorate = false)
		{
			var DecoratedIns = !DontDecorate ? DecorateInstruction(Instruction, Executor, StoredDataType) : Instruction;
			var MoveIns = GetMoveInstruction(Executor, StoredDataType);

			ProcessIndexMoves(Dst, Src);
			if (Dst is x86MemoryLocation)
			{
				x86DataLocation TempReg;
				if (Executor == x86ExecutorType.SSE)
				{
					TempReg = TempData.SSERegs[0];
				}
				else if (Executor == x86ExecutorType.General)
				{
					TempReg = TempData.GetGRegister(x86TempGRegPurposeType.TwoMemOp);
					TempReg = TempReg.GetPart(0, Dst.Size);
				}
				else
				{
					throw new NotImplementedException();
				}

				if (!DstOnlyWritten && !Dst.Compare(TempReg))
					EmitInstruction(MoveIns, TempReg, Dst);

				EmitInstruction(DecoratedIns, TempReg, Src);

				if (!Dst.Compare(TempReg))
					EmitInstruction(MoveIns, Dst, TempReg);
			}
			else
			{
				EmitInstruction(DecoratedIns, Dst, Src);
			}
		}

		private void EmitInstructionNonmemoryDestination(string Instruction, x86DataLocation Dst,
			x86DataLocation Src, x86TemporaryData TempData, x86ExecutorType Executor,
			Identifier Type, bool DstOnlyWritten = false, bool DontDecorate = false)
		{
			var StoredDataType = x86Identifiers.GetStoredDataType(Type);
			EmitInstructionNonmemoryDestination(Instruction, Dst, Src, TempData,
				Executor, StoredDataType, DstOnlyWritten, DontDecorate);
		}

		private void EmitSSEMoveInstruction(x86TemporaryData TempData, x86DataLocation Dst,
			x86DataLocation Src, x86StoredDataType StoredDataType)
		{
			var Instruction = GetSSEMoveInstruction(StoredDataType);
			if (Dst is x86MemoryLocation && Src is x86MemoryLocation)
			{
				var TempReg = TempData.SSERegs[0];
				EmitInstruction(Instruction, TempReg, Src);
				EmitInstruction(Instruction, Dst, TempReg);
			}
			else
			{
				EmitInstruction(Instruction, Dst, Src);
			}
		}

		private void EmitSSEMoveInstruction(x86TemporaryData TempData, x86DataLocation Dst,
			x86DataLocation Src, Identifier Type)
		{
			var StoredDataType = x86Identifiers.GetStoredDataType(Type);
			EmitSSEMoveInstruction(TempData, Dst, Src, StoredDataType);
		}

		private x86DataLocation CopySourceToTempReg(x86DataLocation Dst, x86DataLocation Src,
			x86TemporaryData TempData, x86ExecutorType Executor, x86StoredDataType StoredDataType)
		{
			if (Dst is x86MemoryLocation && Src is x86MemoryLocation)
			{
				if (Executor == x86ExecutorType.SSE)
				{
					var MovIns = GetSSEMoveInstruction(StoredDataType);
					var TempReg = TempData.SSERegs[0];

					EmitInstruction(MovIns, TempReg, Src);
					return TempReg;
				}
				else if (Executor == x86ExecutorType.General)
				{
					var Reg = TempData.GetGRegister(x86TempGRegPurposeType.TwoMemOp);
					var TempReg = Reg.GetPart(0, Src.Size);

					EmitInstruction("mov", TempReg, Src);
					return TempReg;
				}
				else
				{
					throw new ArgumentException(null, "Executor");
				}
			}

			return Src;
		}

		private void EmitInstructionWithoutTwoMemory(string Instruction, x86DataLocation Dst,
			x86DataLocation Src, x86TemporaryData TempData, x86ExecutorType Executor,
			x86StoredDataType StoredDataType, x86StoredDataType DataTypeForInstruction)
		{
			Instruction = DecorateInstruction(Instruction, Executor, DataTypeForInstruction);

			EmitInstructionWithoutTwoMemory(Instruction, Dst, Src,
				TempData, Executor, StoredDataType, true);
		}

		private void EmitInstructionWithoutTwoMemory(string Instruction, x86DataLocation Dst,
			x86DataLocation Src, x86TemporaryData TempData, x86ExecutorType Executor,
			x86StoredDataType StoredDataType, bool DontDecorate = false)
		{
			Src = CopySourceToTempReg(Dst, Src, TempData, Executor, StoredDataType);

			if (!DontDecorate) Instruction = DecorateInstruction(Instruction, Executor, StoredDataType);
			EmitInstruction(Instruction, Dst, Src);
		}

		private void EmitInstructionWithoutTwoMemory(string Instruction, x86DataLocation Dst,
			x86DataLocation Src, x86TemporaryData TempData, x86ExecutorType Executor,
			Identifier Type, bool DontDecorate = false)
		{
			var StoredDataType = x86Identifiers.GetStoredDataType(Type);
			EmitInstructionWithoutTwoMemory(Instruction, Dst, Src,
				TempData, Executor, StoredDataType, DontDecorate);
		}
		#endregion

		#region DataOperations
		private void ConvertScalarGeneral(x86DataLocation Dst, x86DataLocation Src,
			x86TemporaryData TempData, bool Signed)
		{
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
						ZeroData(DstPart, TempData, x86ExecutorType.General, new x86StoredDataType());
					}
					else if (!x86Architecture.IsGRegister(DstPart, 2))
					{
						var Edx = x86Architecture.GetGRegisterName(2, DstPart.Size);
						EmitInstruction("mov", DstPart, Edx);
					}
				}
				else
				{
					var SrcPart = SplittedSrc[i];
					if (Signed && i == SplittedSrc.Length - 1 && SplittedDst.Length > SplittedSrc.Length)
					{
						if (SrcPart.Size < Arch.RegSize) EmitInstruction(MovIns, Eax, SrcPart);
						else if (!Eax.Compare(SrcPart)) EmitInstruction("mov", Eax, SrcPart);
						EmitInstruction("cdq");

						SrcPart = Eax;
					}

					var ConstSrc = SrcPart as x86ConstLocation;
					if (ConstSrc != null && ConstSrc.Unsigned == 0 && !(DstPart is x86MemoryLocation))
					{
						EmitInstruction("xor", DstPart, DstPart);
						continue;
					}
					
					if (DstPart.Size > SrcPart.Size)
					{
						if (DstPart.GetPart(0, SrcPart.Size).Compare(SrcPart))
						{
							var SizeMask = DataStoring.GetSizeMask(SrcPart.Size);
							EmitInstruction("and", DstPart, SizeMask);
						}
						else
						{
							EmitInstructionNonmemoryDestination(MovIns, DstPart, SrcPart,
								TempData, x86ExecutorType.General, new x86StoredDataType(), true);
						}
					}
					else
					{
						SrcPart = SrcPart.GetPart(0, DstPart.Size);
						if (!DstPart.Compare(SrcPart))
						{
							EmitInstructionWithoutTwoMemory("mov", DstPart, SrcPart,
								TempData, x86ExecutorType.General, new x86StoredDataType());
						}
					}
				}
			}
		}

		private void ConvertData(x86DataLocation Dst, x86DataLocation Src, x86TemporaryData TempData,
			x86ExecutorType Executor, x86StoredDataType To, x86StoredDataType From)
		{
			if (!From.CheckLocation(Src)) throw new ArgumentException(null, "Dst");
			if (!To.CheckLocation(Dst)) throw new ArgumentException(null, "Src");

			if (From.IsEquivalent(To))
			{
				MoveData(Dst, Src, TempData, Executor, From);
				return;
			}

			var BothNonfloat = x86Identifiers.IsNonfloatTypeKind(To.TypeKind) &&
				x86Identifiers.IsNonfloatTypeKind(From.TypeKind);

			if (BothNonfloat && From.Precision > To.Precision && 
				!x86Identifiers.IsVectorTypeKind(To.TypeKind) &&
				!x86Identifiers.IsVectorTypeKind(From.TypeKind))
			{
				var SrcPart = Src.GetPart(0, Dst.Size);
				MoveData(Dst, SrcPart, TempData, Executor, To);
				return;
			}

			if ((Executor & x86ExecutorType.General) != 0 && BothNonfloat)
			{
				var Signed = x86Identifiers.IsSignedTypeKind(From.TypeKind);
				x86DataLocations.SplitBySize(Dst, Src, To.Precision, From.Precision, To, From,
					(DstPart, SrcPart, ToPart, FromPart) =>
						ConvertScalarGeneral(DstPart, SrcPart, TempData, Signed)
				);
			}
			else
			{
				throw new NotImplementedException();
			}
		}

		public void MoveData(AutoAllocatedList<x86MoveStruct> Moves, bool Inverse = false)
		{
			if (Moves.List != null)
				MoveData(Moves.List, Inverse);
		}

		public void MoveData(List<x86MoveStruct> Moves, bool Inverse = false)
		{
			for (var i = 0; i < Moves.Count; i++)
				MoveData(Inverse ? Moves[i].Inverse : Moves[i]);
		}

		public void MoveData(x86MoveStruct Struct)
		{
			MoveData(Struct.Dst, Struct.Src, Struct.TempData, Struct.Executor, Struct.StoredDataType);
		}

		public void MoveData(x86DataLocation Dst, x86DataLocation Src, x86TemporaryData TempData,
			x86ExecutorType Executor, Identifier Type)
		{
			if (Src is x86ConstLocation && x86Expressions.NeedReturnPointer(Type))
			{
				MoveStructure(Dst, Src, TempData, Type);
			}
			else
			{
				var StoredDataType = x86Identifiers.GetStoredDataType(Type);
				MoveData(Dst, Src, TempData, Executor, StoredDataType);
			}
		}

		public static bool UseSSEMove(x86Architecture Arch, x86DataLocation Src, 
			x86ExecutorType Executor, x86StoredDataType StoredDataType)
		{
			var CanUseSSE = (Executor & x86ExecutorType.SSE) != 0 && 
				Src.Size >= 4 && !(Src is x86ConstLocation);

			var OptimalToUseSSE = x86Identifiers.IsFloatTypeKind(StoredDataType.TypeKind) ||
				Src.Size > Arch.RegSize || (Executor & x86ExecutorType.General) == 0;
				
			return CanUseSSE && OptimalToUseSSE;
		}

		public void MoveData(x86DataLocation Dst, x86DataLocation Src, x86TemporaryData TempData,
			x86ExecutorType Executor, x86StoredDataType StoredDataType)
		{
			if (!StoredDataType.CheckLocation(Dst)) throw new ArgumentException(null, "Dst");
			if (!StoredDataType.CheckLocation(Src)) throw new ArgumentException(null, "Src");
			
			var ConstSrc = Src as x86ConstLocation;
			if (ConstSrc != null && ConstSrc.Unsigned == 0)
			{
				ZeroData(Dst, TempData, Executor, StoredDataType);
			}
			else if (Dst is x86MultiLocation || Src is x86MultiLocation)
			{
				x86DataLocations.SplitByMultiLocation(Dst, Src, StoredDataType,
					(DstPart, SrcPart, StoredDataTypePart) =>
						MoveData(DstPart, SrcPart, TempData, Executor, StoredDataTypePart)
				);
			}
			else if (Dst is x86GRegLocation || Src is x86GRegLocation)
			{
				if ((Executor & x86ExecutorType.General) == 0)
					throw new ArgumentException(null, "Location");

				if (Dst.Size != Src.Size)
					throw new ArgumentException("Dst.Size != Src.Size");

				if (!Src.Compare(Dst))
					EmitInstruction("mov", Dst, Src);
			}
			else if (Dst is x86SSERegLocation || Src is x86SSERegLocation)
			{
				if ((Executor & x86ExecutorType.SSE) == 0)
					throw new ArgumentException(null, "Location");

				if (x86Identifiers.IsVectorTypeKind(StoredDataType.TypeKind) && Dst.Size != Src.Size)
					throw new ArgumentException("Dst.Size != Src.Size");

				var Nonreg = Dst is x86SSERegLocation ? Src : Dst;
				x86Identifiers.GetDefaultStoredDataType(ref StoredDataType, Nonreg);
				EmitMoveInstructionChecked(Dst, Src, TempData, x86ExecutorType.SSE, StoredDataType);
			}
			else
			{
				if (Dst.Size != Src.Size)
					throw new ArgumentException("Dst.Size != Src.Size");

				if (UseSSEMove(Arch, Src, Executor, StoredDataType))
				{
					x86DataLocations.SplitByPow2Size(ref Dst, ref Src, 4, 16, StoredDataType,
						(DstPart, SrcPart, StoredDataTypePart) =>
						{
							EmitMoveInstructionChecked(DstPart, SrcPart, TempData, 
								x86ExecutorType.SSE, StoredDataTypePart.SelfOrDefault(DstPart));
						}
					);
				}

				if (Dst != null && (Executor & x86ExecutorType.General) != 0 &&
					!x86Identifiers.IsFloatTypeKind(StoredDataType.TypeKind))
				{
					var Precision = StoredDataType.Precision;
					if (Precision == 0 || Precision > Arch.RegSize)
						Precision = Arch.RegSize;

					x86DataLocations.SplitByPow2Size(ref Dst, ref Src, 1, Precision, StoredDataType,
						(DstPart, SrcPart, StoredDataTypePart) =>
						{
							EmitMoveInstructionChecked(DstPart, SrcPart, TempData,
								x86ExecutorType.General, StoredDataTypePart.SelfOrDefault(DstPart));
						}
					);
				}

				if (Dst != null) throw new ArgumentException(null, "Executor");
			}
		}

		public void MoveToEveryPart(x86DataLocation Dst, x86DataLocation Src, x86TemporaryData TempData,
			x86ExecutorType Executor, x86StoredDataType StoredDataType)
		{
			if (Dst.Size == Src.Size)
				MoveData(Dst, Src, TempData, Executor, StoredDataType);

			x86DataLocations.SplitBySize(Dst, Src.Size, StoredDataType,
				(Part, StoredDataTypePart) =>
					MoveData(Part, Src, TempData, Executor, StoredDataTypePart)
			);
		}

		public void ZeroData(x86DataLocation Location, x86TemporaryData TempData,
			x86ExecutorType Executor, Identifier Type)
		{
			var StoredDataType = x86Identifiers.GetStoredDataType(Type);
			ZeroData(Location, TempData, Executor, StoredDataType);
		}

		public void ZeroData(x86DataLocation Location, x86TemporaryData TempData,
			x86ExecutorType Executor, x86StoredDataType StoredDataType)
		{
			if (!StoredDataType.CheckLocation(Location))
				throw new ArgumentException(null, "Location");

			if (Location is x86MultiLocation)
			{
				x86DataLocations.SplitByMultiLocation(Location, StoredDataType,
					(LocPart, StoredDataTypePart) =>
						ZeroData(LocPart, TempData, Executor, StoredDataTypePart)
				);
			}
			else if (Location is x86GRegLocation)
			{
				if ((Executor & x86ExecutorType.General) == 0)
					throw new ArgumentException(null, "Location");

				EmitInstruction("xor", Location, Location);
			}
			else if (Location is x86SSERegLocation)
			{
				if ((Executor & x86ExecutorType.SSE) == 0)
					throw new ArgumentException(null, "Location");

				if (x86Identifiers.IsFloatTypeKind(StoredDataType.TypeKind))
				{
					x86Identifiers.GetDefaultStoredDataType(ref StoredDataType, Location);
					EmitDecoratedInstruction("xor", Executor, 
						StoredDataType.VectorType(), Location, Location);
				}
				else
				{
					EmitInstruction("pxor", Location, Location);
				}
			}
			else if (Location is x86MemoryLocation)
			{
				if (UseSSEMove(Arch, Location, Executor, StoredDataType))
				{
					var TempReg = TempData.SSERegs[0];
					ZeroData(TempReg, new x86TemporaryData(), Executor, StoredDataType);

					x86DataLocations.SplitByPow2Size(ref Location, 4, 16, StoredDataType,
						(LocationPart, StoredDataTypePart) =>
						{
							EmitMoveInstructionChecked(LocationPart, TempReg, TempData,
								x86ExecutorType.SSE, StoredDataTypePart.SelfOrDefault(LocationPart));
						}
					);
				}

				if (Location != null && (Executor & x86ExecutorType.General) != 0 &&
					!x86Identifiers.IsFloatTypeKind(StoredDataType.TypeKind))
				{
					var Precision = StoredDataType.Precision;
					if (Precision == 0 || Precision > Arch.RegSize)
						Precision = Arch.RegSize;

					var Zero = x86DataLocations.GetZeroData(Arch, Container, Precision);
					x86DataLocations.SplitByPow2Size(ref Location, 1, Precision, StoredDataType,
						(LocationPart, StoredDataTypePart) =>
						{
							var ZeroPart = x86DataLocations.GetPartOrSelf(Zero, 0, LocationPart.Size);
							EmitMoveInstructionChecked(LocationPart, ZeroPart, TempData,
								x86ExecutorType.General, StoredDataTypePart.SelfOrDefault(LocationPart));
						}
					);
				}

				if (Location != null) throw new ArgumentException(null, "Executor");
			}
			else
			{
				throw new ArgumentException(null, "Location");
			}
		}

		private void MoveStructure(x86DataLocation Dst, x86DataLocation Src, 
			x86TemporaryData TempData, Identifier Type)
		{
			if (Src is x86ConstLocation)
			{
				var ConstSrc = Src as x86ConstLocation;
				if (ConstSrc.Value is ZeroValue)
				{
					ZeroData(Dst, TempData, x86ExecutorType.All, Type);
					return;
				}
			}

			var Length = x86Identifiers.GetMemberCount(Type);
			for (var j = 0; j < Length; j++)
				if (x86Identifiers.IsMovableMember(Type, j))
				{
					var Dsti = x86Identifiers.GetMember(Dst, Type, j);
					var Srci = x86Identifiers.GetMember(Src, Type, j);
					var MemberType = x86Identifiers.GetMemberType(Type, j);

					if (MemberType.RealId is StructType || MemberType.RealId is NonrefArrayType)
						MoveStructure(Dsti, Srci, TempData, MemberType);
					else MoveData(Dsti, Srci, TempData, x86ExecutorType.All, MemberType);
				}
		}

		#endregion

		#region DataDeclarations
		public override void DeclareFile(string FileName)
		{
			EmitInstruction("file", "\"" + FileName + "\"");
		}

		public override void DeclareUnknownBytes(int Count)
		{
			if (Count == 1 || Count == 2 || Count == 4 || Count == 8 || Count == 10)
				EmitInstruction(x86Architecture.GetDataTypeString(Count), "?");
			else EmitInstruction("db", Count + " dup ?");
		}

		public override void DeclareZeroBytes(int Count)
		{
			if (Count == 1 || Count == 2 || Count == 4 || Count == 8 || Count == 10)
				EmitInstruction(x86Architecture.GetDataTypeString(Count), 0);
			else EmitInstruction("db", Count + " dup 0");
		}

		public override void Align(int Align)
		{
			if (Align <= 0)
				throw new ArgumentOutOfRangeException("Align");

			if (Align != 1)
				EmitInstruction("align", Align);
		}

		public override void Store(string String, bool WideString = true, bool ZeroTerminated = true)
		{
			if (String.Length > 0)
			{
				var Ins = WideString ? "dw" : "db";
				var Parameters = new List<object>();

				for (var i = 0; i < String.Length; i++)
					Parameters.Add((int)String[i]);

				if (ZeroTerminated) Parameters.Add(0);
				EmitInstruction(Ins, Parameters.ToArray());
			}
		}

		public override void Declare(Identifier Type, ConstValue Data)
        {
            var RType = Type.RealId as Type;
            if (RType == null) throw new ArgumentException("Type");

			if (Data == null)
			{
				DeclareUnknownBytes(RType.Size);
			}
			else if (Data is ZeroValue)
			{
				DeclareZeroBytes(RType.Size);
			}
			else if (Data is StructuredValue)
			{
				var SValue = Data as StructuredValue;
				var Offset = 0;

				if (RType is StructuredType)
				{
					var SType = RType as StructuredType;
					var Members = SType.StructuredScope.IdentifierList;

					for (var i = 0; i < SValue.Members.Count; i++)
					{
						var MemberType = Members[i].TypeOfSelf;
						Declare(MemberType, SValue.Members[i]);
						Offset += (MemberType.RealId as Type).Size;
					}
				}
				else if (RType is NonrefArrayType)
				{
					var NonrefArray = RType as NonrefArrayType;
					var TypeOfValues = NonrefArray.TypeOfValues;

					for (var i = 0; i < SValue.Members.Count; i++)
					{
						Declare(TypeOfValues, SValue.Members[i]);
						Offset += (TypeOfValues.RealId as Type).Size;
					}
				}
				else
				{
					throw new NotImplementedException();
				}

				if (Offset != RType.Size)
					DeclareUnknownBytes(RType.Size - Offset);
			}
			else if (Data is StringValue)
			{
				var String = (Data as StringValue).Value;
				if (RType is StringType)
				{
					var l = State.AutoLabel;
					DeclareLabelPtr(l);
					Align(RType.Align);
					InsContainer.Label(l);
					DeclareValueString(String);
				}
				else
				{
					var TStr = x86Architecture.GetDataTypeString(RType.Size);
					EmitInstruction(TStr, String);
				}
			}
			else
			{
				var TStr = x86Architecture.GetDataTypeString(RType.Size);
				if (RType is SignedType) EmitInstruction(TStr, Data.GetSigned(0, RType.Size));
				else EmitInstruction(TStr, Data.GetUnsigned(0, RType.Size));
			}
		}

		public void DeclareValueString(string String)
		{
			DeclareNull();
			DeclareNull();
			EmitInstruction("dd", 0, 0, String.Length);
			Store(String, true, false);
		}

		public override void DeclareLabelPtr(string Label)
		{
			var TypeStr = x86Architecture.GetDataTypeString(Arch.RegSize);
			EmitInstruction(TypeStr, Label);
		}

		public override void DeclareLabelPtr(int Label)
		{
			var TypeStr = x86Architecture.GetDataTypeString(Arch.RegSize);
			EmitInstruction(TypeStr, "_" + Label);
		}
		#endregion

		#region FunctionBeginEnd
		void GetSSERegisterSaves(FunctionScope Scope, x86DataAllocator Allocator)
		{
			var Data = Scope.Data.Get<x86FuncScopeData>();
			if (Data.SpaceForSaving == null) return;

			var SpaceForSaving = Data.SpaceForSaving;
			var Conv = Scope.Type.CallConv;
			var x86CallConv = Arch.GetCallingConvention(Conv);
			var SSERegSize = Arch.SSERegSize;
			var Offset = 0;

			for (var i = 0; i < Allocator.SSERegisters.Size; i++)
				if (Allocator.SSERegisters[i] && x86CallConv.SavedSSERegs.Contains(i))
				{
					var SaveTo = SpaceForSaving.GetPart(Offset, SSERegSize);
					Offset += SSERegSize;

					var Reg = new x86SSERegLocation(Arch, i, SSERegSize);
					Data.FuncLeaveLoadRegs.Add(new x86MoveStruct(SaveTo, Reg,
						new x86TemporaryData(), x86ExecutorType.SSE, new x86StoredDataType()));
				}

			Data.SpaceForSaving = SpaceForSaving.GetPart(Offset, SpaceForSaving.Size - Offset);
		}

		int PushPopGRegs(x86DataAllocator Allocator, x86CallingConvention x86CallConv, bool Push)
		{
			var UsedRegs = Allocator.GRegisters;
			var Ret = 0;

			if (Push)
			{
				for (var i = 0; i < UsedRegs.Size; i++)
					if (UsedRegs[i].Size > 0 && PushPopGReg(x86CallConv, Push, i)) Ret++;
			}
			else
			{
				for (var i = UsedRegs.Size - 1; i >= 0; i--)
					if (UsedRegs[i].Size > 0 && PushPopGReg(x86CallConv, Push, i)) Ret++;
			}

			return Ret;
		}

		bool PushPopGReg(x86CallingConvention x86CallConv, bool Push, int Index)
		{
			if (x86CallConv.SavedGRegs.Contains(Index))
			{
				var Reg = x86Architecture.GetGRegisterName(Index, Arch.RegSize);
				EmitInstruction(Push ? "push" : "pop", Reg);
				return true;
			}

			return false;
		}

		public void LeaveFunction(FunctionScope Scope, x86DataAllocator Allocator)
		{
			var Data = Scope.Data.Get<x86FuncScopeData>();
			var Type = Scope.Type;
			MoveData(Data.FuncLeaveLoadRegs, true);

			if ((Data.Flags & x86FuncScopeFlags.SaveParameterPointer) != 0)
				EmitInstruction("mov", Data.StackPointer, Data.ParameterPointer);
			else if ((Data.Flags & x86FuncScopeFlags.SaveFramePointer) != 0)
				EmitInstruction("mov", Data.StackPointer, Data.FramePointer);

			Data.Flags &= ~x86FuncScopeFlags.StackLocationsValid;
			if ((Data.Flags & x86FuncScopeFlags.SaveFramePointer) != 0)
			{
				EmitInstruction("pop", Data.FramePointer);
				Data.PushedRegisters -= Arch.RegSize;
			}

			if ((Data.Flags & x86FuncScopeFlags.SaveParameterPointer) != 0)
			{
				EmitInstruction("pop", Data.ParameterPointer);
				Data.PushedRegisters -= Arch.RegSize;
			}

			if ((Data.Flags & x86FuncScopeFlags.SaveFramePointer) == 0 &&
				(Data.Flags & x86FuncScopeFlags.SaveParameterPointer) == 0)
			{
				if (Data.SubractedFromESP > 0)
					EmitInstruction("add", "esp", Data.SubractedFromESP);
			}

			var x86CallConv = Arch.GetCallingConvention(Type.CallConv);
			Data.PushedRegisters += PushPopGRegs(Allocator, x86CallConv, false) * Arch.RegSize;

			if (!x86CallConv.StackCleanupByCaller)
			{
				var StackOffset = Data.UsedToPassParams.StackOffset;
				if (StackOffset == 0) EmitInstruction("ret");
				else EmitInstruction("ret", StackOffset);
			}
			else
			{
				EmitInstruction("ret");
			}
		}

		public void BeginFunction(FunctionScope Scope, x86DataAllocator Allocator)
		{
			var Arch = Scope.State.Arch as x86Architecture;
			var Data = Scope.Data.Get<x86FuncScopeData>();
			var Type = Scope.Type;

			var x86CallConv = Arch.GetCallingConvention(Type.CallConv);
			Data.FuncLeaveInsCount = PushPopGRegs(Allocator, x86CallConv, true);
			Data.PushedRegisters = Data.FuncLeaveInsCount * Arch.RegSize;

			if ((Data.Flags & x86FuncScopeFlags.SaveParameterPointer) != 0)
			{
				EmitInstruction("push", Data.ParameterPointer);
				Data.PushedRegisters += Arch.RegSize;
				Data.FuncLeaveInsCount++;
			}

			if ((Data.Flags & x86FuncScopeFlags.SaveFramePointer) != 0)
			{
				EmitInstruction("push", Data.FramePointer);
				Data.PushedRegisters += Arch.RegSize;
				Data.FuncLeaveInsCount++;
			}

			if ((Data.Flags & x86FuncScopeFlags.SaveParameterPointer) != 0)
			{
				EmitInstruction("mov", Data.ParameterPointer, Data.StackPointer);
				Data.FuncLeaveInsCount++;
			}

			if (Data.StackAlignment > x86CallConv.StackAlignment)
			{
				var Mask = DataStoring.GetAlignmentMask(Data.StackPointer.Size, Data.StackAlignment);
				EmitInstruction("and", Data.StackPointer, Mask);
			}

			if ((Data.Flags & x86FuncScopeFlags.SaveFramePointer) != 0)
			{
				EmitInstruction("mov", Data.FramePointer, Data.StackPointer);
				if ((Data.Flags & x86FuncScopeFlags.SaveParameterPointer) == 0)
					Data.FuncLeaveInsCount++;
			}

			if (Allocator.StackOffset > 0)
			{
				if ((Data.Flags & x86FuncScopeFlags.FunctionCalled) != 0 ||
					(Data.Flags & x86FuncScopeFlags.SaveFramePointer) != 0)
				{
					Data.SubractedFromESP = DataStoring.AlignWithIncrease(
						Allocator.StackOffset, Data.StackAlignment);

					EmitInstruction("sub", Data.StackPointer, Data.SubractedFromESP);
					Data.FuncLeaveInsCount++;
				}
				else
				{
					Data.UnallocatedSpace = DataStoring.AlignWithIncrease(
						Allocator.StackOffset, Arch.RegSize);
				}
			}

			Data.Flags |= x86FuncScopeFlags.StackLocationsValid;
			Data.FuncLeaveLoadRegs.Clear();
			GetSSERegisterSaves(Scope, Allocator);
			MoveData(Data.FuncLeaveLoadRegs);
			Data.FuncLeaveInsCount += Data.FuncLeaveLoadRegs.Count;
		}
		#endregion

		public void Jump(ExpressionNode Node)
		{
			var Loc = GetNodeLoc(Node);
			ProcessIndexMoves(Loc);
			EmitInstruction("jmp", Loc);
		}

		static int GetSSECondition(Operator Op)
		{
			if (Op == Operator.Equality) return 0;
			else if (Op == Operator.Inequality) return 4;
			else if (Op == Operator.Less) return 1;
			else if (Op == Operator.LessEqual) return 2;
			else if (Op == Operator.Greater) return 6;
			else if (Op == Operator.GreaterEqual) return 5;
			else throw new ApplicationException();
		}

		public override void EmitCondition(ExpressionNode Node, CondBranch Then, CondBranch Else, int NextLabel = -1)
		{
			if (ConditionalMove(Node, Then, Else)) return;
			if (JumpCondition(Node, Then, Else)) return;
			StdCondition(Node, Then, Else, NextLabel);
		}

		private bool JumpCondition(ExpressionNode Node, CondBranch Then, CondBranch Else)
		{
			var JumpThen = Then as JumpCodeBranch;
			var JumpElse = Else as JumpCodeBranch;
			if (JumpThen == null && JumpElse == null) return false;

			var ThenLbl = JumpThen == null ? State.AutoLabel : JumpThen.Label;
			var ElseLbl = JumpElse == null ? State.AutoLabel : JumpElse.Label;

			var ThenAfterElse = JumpThen == null && JumpElse != null;
			EmitCondition(Node, ThenLbl, ElseLbl, ThenAfterElse);

			if (!ThenAfterElse)
			{
				GetBranchCode(Then);
				if (JumpElse == null)
				{
					InsContainer.Label(ElseLbl);
					GetBranchCode(Else);
				}
			}
			else
			{
				GetBranchCode(Else);
				if (JumpThen == null)
				{
					InsContainer.Label(ThenLbl);
					GetBranchCode(Then);
				}
			}

			return true;
		}

		private void StdCondition(ExpressionNode Node, CondBranch Then, CondBranch Else, int NextLabel)
		{
			var ThenLabel = State.AutoLabel;
			var ElseLabel = State.AutoLabel;

			var NextLblCreated = false;
			if (NextLabel == -1)
			{
				NextLabel = State.AutoLabel;
				NextLblCreated = true;
			}

			EmitCondition(Node, ThenLabel, ElseLabel, false);
			GetLabelsCode(Node, ThenLabel, ElseLabel, NextLabel, Then, Else);
			if (NextLblCreated) InsContainer.Label(NextLabel);
		}

		private bool ConditionalMove(ExpressionNode Node, CondBranch Then, CondBranch Else)
		{
			var MThen = Then as x86MoveCondBranch;
			var MElse = Else as x86MoveCondBranch;
			if (MThen == null || (Else != null && MElse == null))
				return false;
			
			var Data = Node.Data.Get<x86NodeData>();
			var Op = Operator.Unknown;
			var Ch = (ExpressionNode[])null;

			var OpNode = Node as OpExpressionNode;
			if (OpNode != null)
			{
				Op = OpNode.Operator;
				Ch = OpNode.Children;
			}
			else
			{
				var False = new ConstExpressionNode(Node.Type, new BooleanValue(false), new CodeString());
				Ch = new ExpressionNode[] { Node, False };
				Op = Operator.Equality;
			}

			if (Operators.IsRelEquality(Op))
			{
				if (MThen.Struct.Src.Size != MThen.Struct.Dst.Size) return false;
				if (MElse != null && MElse.Struct.Src.Size != MElse.Struct.Dst.Size) return false;
				if ((MThen.Struct.Executor & x86ExecutorType.General) == 0) return false;
				if (MElse != null && (MElse.Struct.Executor & x86ExecutorType.General) == 0) return false;

				if (MElse == null || MThen.Struct.Dst.Compare(MElse.Struct.Dst))
				{
					if (MThen.Struct.Src.Compare(MThen.Struct.Dst))
					{
						if (MElse == null)
						{
							ComputeLinkedNodes(Node);
							for (var i = 0; i < Ch.Length; i++)
								EmitExpression(Ch[i]);
						}
						else
						{
							if (!(MElse.Struct.Dst is x86GRegLocation) || MElse.Struct.Dst.Size == 1) return false;
							if (!(MElse.Struct.Src is x86GRegLocation || MElse.Struct.Src is x86MemoryLocation))
								return false;

							ComputeLinkedNodes(Node);

							var Ins = "cmov" + EmitSinglePartCmp(OpNode, Data, Operators.Negate(Op));
							EmitInstructionNonmemoryDestination(Ins, MElse.Struct.Dst, MElse.Struct.Src,
								MElse.Struct.TempData, x86ExecutorType.General, MElse.Struct.StoredDataType);
						}
					}
					else
					{
						if (!(MThen.Struct.Dst is x86GRegLocation) || MThen.Struct.Dst.Size == 1) return false;
						if (!(MThen.Struct.Src is x86GRegLocation || MThen.Struct.Src is x86MemoryLocation))
							return false;

						ComputeLinkedNodes(Node);
						var Str = EmitSinglePartCmp(OpNode, Data);
						if (Else != null)
						{
							ProcessIndexMoves(MElse.Struct.Dst, MElse.Struct.Src);
							MoveData(MElse.Struct);
						}

						ProcessIndexMoves(MThen.Struct.Dst, MThen.Struct.Src);
						EmitInstructionNonmemoryDestination("cmov" + Str, MThen.Struct.Dst, MThen.Struct.Src,
							MThen.Struct.TempData, x86ExecutorType.General, MThen.Struct.StoredDataType);
					}

					return true;
				}
			}

			return false;
		}

		void GetBranchCode(CondBranch Branch)
		{
			if (Branch is CodeCondBranch)
			{
				var CodeB = Branch as CodeCondBranch;
				CodeB.GetCode(this);
			}
			else if (Branch is JumpCodeBranch)
			{
				var JumpB = Branch as JumpCodeBranch;
				InsContainer.Jump(JumpB.Label);
			}
			else if (Branch is x86MoveCondBranch)
			{
				var MoveBrach = Branch as x86MoveCondBranch;
				var S = MoveBrach.Struct;
				ProcessIndexMoves(S.Dst, S.Src);
				MoveData(S);
			}
		}

		void GetLabelsCode(ExpressionNode Condition, int ThenLabel, int ElseLabel,
			int NextLabel, CondBranch Then, CondBranch Else)
		{
			if (Then != null)
			{
				InsContainer.Label(ThenLabel);
				GetBranchCode(Then);
			}

			if (Else != null)
			{
				var ElseInsContainer = ExecuteOnTempInsContainer(() => GetBranchCode(Else));

				if (ElseInsContainer.Instructions.Count > 0)
				{
					InsContainer.Jump(NextLabel);
					InsContainer.Label(ElseLabel);
					InsContainer.Add(ElseInsContainer);
					return;
				}
			}

			InsContainer.Label(ElseLabel);
		}

		private string EmitCmpCodeHelper(x86DataLocation Dst, x86DataLocation Src,
			x86NodeData Data, Operator Op, x86Operator x86Op, bool Signed)
		{
			if (Op != Operator.Unknown)
			{
				if (Op == Operator.Equality || Op == Operator.Inequality)
				{
					var ConstSrc = Src as x86ConstLocation;
					if (ConstSrc != null && ConstSrc.Unsigned == 0 && Dst is x86GRegLocation)
					{
						EmitInstruction("test", Dst, Dst);
						return Op == Operator.Equality ? "z" : "nz";
					}
				}

				EmitInstructionWithoutTwoMemory("cmp", Dst, Src, Data.TempData, 
					x86ExecutorType.General, new x86StoredDataType());

				return x86Architecture.OpInstruction(Op, Signed);
			}
			else if (x86Op == x86Operator.BitTestNonZero || x86Op == x86Operator.BitTestZero)
			{
				EmitInstruction("test", Dst, Src);
				return x86Op == x86Operator.BitTestZero ? "z" : "nz";
			}
			else
			{
				throw new ApplicationException();
			}
		}

		private string EmitSinglePartCmp(OpExpressionNode OpNode, x86NodeData Data, 
			Operator Op = Operator.Unknown, x86Operator x86Op = x86Operator.Unknown)
		{
			var Ch = OpNode.Children;
			if (Op == Operator.Unknown) Op = OpNode.Operator;
			if (x86Op == x86Operator.Unknown) x86Op = Data.Operator;

			var Ch0Type = Ch == null || Ch.Length == 0 ? null : Ch[0].Type.RealId as Type;
			if (Ch0Type is FloatType)
			{
				if (Arch.FloatingPointMode == x86FloatingPointMode.FPU)
				{
					if (x86Op == x86Operator.IsNan || x86Op == x86Operator.IsNotNan)
					{
						GetNodePos_FPU(Ch[0], LoadIds: true);
						EmitInstruction("fcomp", "st0");
						EmitInstruction("fnstsw", "ax");
						EmitInstruction("sahf");
						return x86Op == x86Operator.IsNan ? "p" : "np";
					}
					else if (x86Op == x86Operator.IsInfinite || x86Op == x86Operator.IsFinite)
					{
						var InfVar = Data.InfinityVariable;
						var InfVarData = InfVar.Data.Get<x86IdentifierData>();

						GetNodePos_FPU(Ch[0], LoadIds: true);
						EmitInstruction("fabs");
						EmitInstruction("fcomp", InfVarData.Location);
						EmitInstruction("fnstsw", "ax");
						EmitInstruction("sahf");
						return x86Op == x86Operator.IsInfinite ? "e" : "ne";
					}
					else if (x86Op == x86Operator.FloatZeroTesting)
					{
						var Pos = GetNodeLoc(Ch[0]);
						var TmpReg = Data.TempData.GetGRegister(x86TempGRegPurposeType.Plugin_FPUZeroTest);
						ProcessIndexMoves(Pos);

						EmitInstruction("mov", TmpReg, Pos);
						EmitInstruction("add", TmpReg, TmpReg);
						return Op == Operator.Equality ? "z" : "nz";
					}
					else if (x86Op == x86Operator.Unknown)
					{
						GetNodePos_FPU(Ch[0], LoadIds: true);
						var Src = GetNodePos_FPU(Ch[1]);
						ProcessIndexMoves(Src);

						FPUOp("comp", Src, Ch[1].Type.RealId is FloatType, false, Data);
						EmitInstruction("fnstsw", "ax");
						EmitInstruction("sahf");
						return x86Architecture.OpInstruction(Op, false);
					}
					else
					{
						throw new ApplicationException();
					}
				}
				else if (Arch.FloatingPointMode == x86FloatingPointMode.SSE)
				{
					var Dst = GetNodeLoc(Ch[0]);
					var Src = GetNodeLoc(Ch[1]);
					ProcessIndexMoves(Dst, Src);
					
					var S = Ch0Type.Size;
					if (!(Dst is x86SSERegLocation))
					{
						if (S == 4) EmitInstruction("movss", Data.TempData.SSERegs[0], Dst);
						else if (S == 8) EmitInstruction("movsd", Data.TempData.SSERegs[0], Dst);
						else throw new ApplicationException();

						Dst = Data.TempData.SSERegs[0];
					}

					if (S == 4) EmitInstruction("ucomiss", Dst, Src);
					else if (S == 8) EmitInstruction("ucomisd", Dst, Src);
					else throw new ApplicationException();

					return x86Architecture.OpInstruction(Op, false);
				}
				else
				{
					throw new NotImplementedException();
				}
			}
			else if (Op == Operator.Unknown && x86Expressions.IsFlagOp(x86Op))
			{
				return x86Architecture.OpInstruction(x86Op);
			}
			else
			{
				var Dst = GetNodeLoc(Ch[0]);
				var Src = GetNodeLoc(Ch[1]);
				ProcessIndexMoves(Dst, Src);

				return EmitCmpCodeHelper(Dst, Src, Data, Op, x86Op, Ch0Type is SignedType);
			}
		}

		public override void EmitCondition(ExpressionNode Node, int Then, int Else, bool ElseAfterCondition)
		{
			var Data = Node.Data.Get<x86NodeData>();
			var x86Op = Data.Operator;

			var OpNode = Node as OpExpressionNode;
			if (OpNode == null || !(Operators.IsBoolRet(OpNode.Operator) || OpNode.Operator == Operator.Unknown))
			{
				if (Node is ConstExpressionNode)
				{
					ComputeLinkedNodes(Node);
					var ConstNode = Node as ConstExpressionNode;
					if (!ConstNode.Bool && !ElseAfterCondition) InsContainer.Jump(Else);
					if (ConstNode.Bool && ElseAfterCondition) InsContainer.Jump(Then);
					return;
				}
				else
				{
					var False = new ConstExpressionNode(Node.Type, new BooleanValue(false), new CodeString());
					var NCh = new ExpressionNode[] { Node, False };
					Node = OpNode = new OpExpressionNode(Operator.Inequality, NCh, new CodeString());
				}
			}

			ComputeLinkedNodes(Node);
			var Op = OpNode.Operator;
			var Ch = OpNode.Children;
			if (!Operators.IsLogical(Op))
			{
				int Lbl;
				var CmpOp = Op;
				var x86CmpOp = x86Op;

				if (!ElseAfterCondition) x86Expressions.Negate(ref CmpOp, ref x86CmpOp);
				Lbl = !ElseAfterCondition ? Else : Then;

				var Type = Ch == null || Ch.Length == 0 ? null : Ch[0].Type.RealId as Type;
				if (Type != null && Type.Size > Arch.RegSize && Type is NonFloatType)
				{
					EmitMultiPartCmp(OpNode, Data, Lbl, CmpOp, x86CmpOp);
				}
				else
				{
					var InsStr = EmitSinglePartCmp(OpNode, Data, CmpOp, x86CmpOp);
					InsContainer.Add(new x86ConditionalJump(Lbl, InsStr));
				}
			}
			else
			{
				var NewLabel = State.AutoLabel;
				if (Op == Operator.And) EmitCondition(Ch[0], NewLabel, Else, false);
				else if (Op == Operator.Or) EmitCondition(Ch[0], Then, NewLabel, true);
				else throw new NotImplementedException();

				InsContainer.Label(NewLabel);
				EmitCondition(Ch[1], Then, Else, ElseAfterCondition);
			}
		}

		private void EmitMultiPartCmp(OpExpressionNode OpNode, x86NodeData Data, int Then,
			Operator Op = Operator.Unknown, x86Operator x86Op = x86Operator.Unknown)
		{
			var Ch = OpNode.Children;
			var Signed = Ch[0].Type.RealId is SignedType;
			
			if (Op == Operator.Unknown) Op = OpNode.Operator;
			if (x86Op == x86Operator.Unknown) x86Op = Data.Operator;

			var Dst = GetNodeLoc(Ch[0]);
			var Src = GetNodeLoc(Ch[1]);
			ProcessIndexMoves(Dst, Src);

			var SplDst = x86DataLocations.Split(Dst, Arch.RegSize);
			var SplSrc = x86DataLocations.Split(Src, Arch.RegSize);
			if (SplDst.Length != SplSrc.Length) throw new ApplicationException();

			var Else = State.AutoLabel;
			if (x86Op == x86Operator.BitTestNonZero || x86Op == x86Operator.BitTestZero)
			{
				var LastPart = -1;
				for (var i = 0; i < SplDst.Length; i++)
				{
					var ConstSrci = SplSrc[i] as x86ConstLocation;
					if (ConstSrci == null || ConstSrci.Unsigned != 0)
						LastPart = i;
				}

				if (LastPart != -1)
				{
					for (var i = 0; i < SplDst.Length; i++)
					{
						var ConstSrci = SplSrc[i] as x86ConstLocation;
						if (ConstSrci == null || ConstSrci.Unsigned != 0)
						{
							if (x86Op == x86Operator.BitTestZero)
							{
								var PartOp = x86Op;
								var PartLabel = Then;
								if (i != LastPart)
								{
									PartOp = x86Operator.BitTestNonZero;
									PartLabel = Else;
								}

								var InsStr = EmitCmpCodeHelper(SplDst[i], SplSrc[i], Data, Op, PartOp, Signed);
								InsContainer.Add(new x86ConditionalJump(PartLabel, InsStr));
							}
							else
							{
								var InsStr = EmitCmpCodeHelper(SplDst[i], SplSrc[i], Data, Op, x86Op, Signed);
								InsContainer.Add(new x86ConditionalJump(Then, InsStr));
							}
						}
					}
				}
				else
				{
					InsContainer.Jump(Then);
				}
			}
			else
			{
				for (var i = SplDst.Length - 1; i >= 0; i--)
				{
					if (Op == Operator.Unknown)
					{
						throw new NotImplementedException();
					}

					else if (Op == Operator.Equality)
					{
						var NewOp = Operators.Negate(Op);
						var InsStr = EmitCmpCodeHelper(SplDst[i], SplSrc[i], Data, NewOp, x86Op, Signed);
						InsContainer.Add(new x86ConditionalJump(Else, InsStr));
					}

					else if (Op == Operator.Inequality)
					{
						var InsStr = EmitCmpCodeHelper(SplDst[i], SplSrc[i], Data, Op, x86Op, Signed);
						InsContainer.Add(new x86ConditionalJump(Then, InsStr));
					}

					else
					{
						var LSigned = Signed && i == SplDst.Length - 1;
						var InsStr = EmitCmpCodeHelper(SplDst[i], SplSrc[i], Data, Op, x86Op, LSigned);
						var Greater = x86Architecture.OpInstruction(Operator.Greater, LSigned);
						var Less = x86Architecture.OpInstruction(Operator.Less, LSigned);

						if (i > 0)
						{
							if (Op == Operator.Less || Op == Operator.LessEqual)
							{
								InsContainer.Add(new x86ConditionalJump(Else, Greater));
								InsContainer.Add(new x86ConditionalJump(Then, Less));
							}
							else if (Op == Operator.Greater || Op == Operator.GreaterEqual)
							{
								InsContainer.Add(new x86ConditionalJump(Then, Greater));
								InsContainer.Add(new x86ConditionalJump(Else, Less));
							}
							else
							{
								throw new ApplicationException();
							}
						}
						else
						{
							InsContainer.Add(new x86ConditionalJump(Then, InsStr));
						}
					}
				}
			}

			InsContainer.Label(Else);
		}

		public x86DataLocation GetNodeLoc(ExpressionNode Node, x86GetNodePosMode Mode = x86GetNodePosMode.Default)
		{
			var P = x86Expressions.GetLocation(Arch, Node);
			if (Mode != x86GetNodePosMode.Default && x86Expressions.NeedsInstructions(Node))
			{
				if (Mode == x86GetNodePosMode.PositionOnly) return P;
				else if (Mode == x86GetNodePosMode.ReturnNull) return null;
				else throw new ApplicationException();
			}

			EmitExpression(Node);
			return P;
		}

		private CondBranch GetPushCondBrach(ExpressionNode Node)
		{
			return new CodeCondBranch(CG =>
			{
				var x86CG = CG as x86CodeGenerator;
				var Pos = GetNodeLoc(Node);

				x86CG.ProcessIndexMoves(Pos);
				x86CG.EmitInstruction("push", Pos);
			});
		}

		private void EmitCallExpression(OpExpressionNode OpNode, x86DataLocation ValueTypeRet = null,
			x86DataLocation Self = null, bool ByRefSelf = false)
		{
			var Data = OpNode.Data.Get<x86NodeData>();
			var RegSize = Arch.RegSize;

			var FS = Container.FunctionScope as FunctionScope;
			var FSData = FS.Data.Get<x86FuncScopeData>();
			var OldPushedBytes = FSData.CallParameters;
			var Ch = OpNode.Children;
			var FuncType = Ch[0].Type.RealId as TypeOfFunction;
			var CallConv = FuncType.CallConv;
			var x86CallConv = Arch.GetCallingConvention(CallConv);
			var RetType = FuncType.RetType;
			var RetSize = RetType.Size;

			var Accumulator = new x86GRegLocation(Arch, 0, RegSize);
			var DS = new x86DataSequence(Arch, x86CallConv.ParameterSequence);

			//-------------------------------------------------------------------------------
			if (Self == null && Ch[0] is OpExpressionNode)
			{
				var OpCh0 = Ch[0] as OpExpressionNode;
				var Ch0Op = OpCh0.Operator;
				var Ch0Ch = OpCh0.Children;

				if (Ch0Op == Operator.Member && Ch0Ch[1] is IdExpressionNode)
				{
					var IdCh0Ch1 = Ch0Ch[1] as IdExpressionNode;
					if ((IdCh0Ch1.Identifier.Flags & IdentifierFlags.Static) == 0 && IdCh0Ch1.Identifier is Function)
					{
						Self = GetNodeLoc(Ch0Ch[0], x86GetNodePosMode.PositionOnly);
						if (Self == null) throw new ApplicationException();
						
						var Ch0Ch0Type = Ch0Ch[0].Type.RealId as Type;
						if ((Ch0Ch0Type.TypeFlags & TypeFlags.ReferenceValue) == 0)
							ByRefSelf = true;
					}
				}
			}

			var RetAddressParam = (x86DataLocation)null;
			var SelfParam = (x86DataLocation)null;
			var InRegParams = new Dictionary<ExpressionNode, x86DataLocation>();

			if (x86Expressions.NeedReturnPointer(RetType))
				RetAddressParam = DS.GetPosition(FS, RegSize, RegSize);

			if (x86Expressions.NeedSelfParameter(OpNode))
				SelfParam = DS.GetPosition(FS, RegSize, RegSize);

			var ProcessedCh = new bool[Ch.Length];
			var ParamTypes = FuncType.GetTypes();
			Arch.ProcessRegisterParams(ParamTypes, CallConv, (i, Pos) =>
			{
				InRegParams.Add(Ch[i + 1], Pos);
				ProcessedCh[i + 1] = true;
			}, DS);

			//-------------------------------------------------------------------------------
			if (Data.ParameterBytes > 0)
			{
				EmitInstruction("sub", FSData.StackPointer, Data.ParameterBytes);
				FSData.CallParameters += Data.ParameterBytes;
			}

			if (SelfParam != null && SelfParam is x86MemoryLocation)
			{
				ProcessIndexMoves(SelfParam, Self);
				MoveData(SelfParam, Self, Data.TempData, 
					x86ExecutorType.General, new x86StoredDataType());
			}

			if (RetAddressParam != null && SelfParam is x86MemoryLocation)
			{
				ProcessIndexMoves(RetAddressParam, ValueTypeRet);
				GetAddrOf(RetAddressParam, ValueTypeRet, Data);
			}

			for (var i = 1; i < Ch.Length; i++)
			{
				if (ProcessedCh[i]) continue;

				var Type = Ch[i].Type.RealId as Type;
				var Align = Math.Max(Type.Align, x86CallConv.ParameterAlignment);
				if (Align > FSData.StackAlignment) throw new ApplicationException();

				DS.StackOffset = DataStoring.AlignWithIncrease(DS.StackOffset, Align);
				var Dst = new x86IndexLocation(Arch, DS.StackOffset, Type.Size, FSData.StackPointer);
				EmitMoveExpression(Data, Dst, Ch[i]);

				DS.StackOffset += Type.Size;
			}

			//-------------------------------------------------------------------------------
			var CanUseSelfForFuncLoc = false;
			if (Expressions.GetOperator(Ch[0]) == Operator.Member)
			{
				var Ch0Ch = Ch[0].Children;
				var IdCh0Ch1 = Ch0Ch[1] as IdExpressionNode;
				if (IdCh0Ch1 == null) throw new ApplicationException();

				if (SelfParam != null && x86Expressions.IsVirtualMember(Ch[0]))
				{
					CanUseSelfForFuncLoc = true;
					EmitExpression(Ch[0].Children[0]);
				}
			}

			var FuncLoc = !CanUseSelfForFuncLoc ? GetNodeLoc(Ch[0]) : null;

			//-------------------------------------------------------------------------------
			if (SelfParam != null && !(SelfParam is x86MemoryLocation))
			{
				ProcessIndexMoves(SelfParam, Self);
				if (ByRefSelf)
				{
					GetAddrOf(SelfParam, Self, Data);
				}
				else
				{
					MoveData(SelfParam, Self, Data.TempData,
						x86ExecutorType.General, new x86StoredDataType());
				}
			}

			if (RetAddressParam != null && !(SelfParam is x86MemoryLocation))
			{
				ProcessIndexMoves(RetAddressParam, ValueTypeRet);
				GetAddrOf(RetAddressParam, ValueTypeRet, Data);
			}

			foreach (var e in InRegParams)
			{
				if (e.Key is LinkingNode)
					EmitMoveExpression(Data, e.Value, e.Key);
			}

			foreach (var e in InRegParams)
			{
				if (!(e.Key is LinkingNode))
					EmitMoveExpression(Data, e.Value, e.Key);
			}

			//-------------------------------------------------------------------------------
			if (FuncLoc == null)
			{
				var Ch0Data = Ch[0].Data.Get<x86NodeData>();
				var TmpReg = Ch0Data.TempData.GetGRegister(x86TempGRegPurposeType.Index, 0);

				if ((Arch.Extensions & x86Extensions.LongMode) != 0)
					EmitInstruction("mov", TmpReg, "qword[" + SelfParam + "]");
				else EmitInstruction("mov", TmpReg, "dword[" + SelfParam + "]");

				var MemberFunc = Expressions.GetIdentifier(Ch[0].Children[1]) as MemberFunction;
				FuncLoc = new x86IndexLocation(Arch, RegSize * MemberFunc.VirtualIndex, RegSize, TmpReg);
			}
			
			ProcessIndexMoves(FuncLoc);
			EmitInstruction("call", FuncLoc);

			if (FuncType.CallConv == CallingConvention.CDecl)
			{
				if (Data.ParameterBytes > 0)
					EmitInstruction("add", FSData.StackPointer, Data.ParameterBytes);
			}

			FSData.CallParameters = OldPushedBytes;
		}

		void PushData(x86DataLocation Pos)
		{
			ProcessIndexMoves(Pos);
			var FuncScope = Container.FunctionScope as FunctionScope;
			var FuncData = FuncScope.Data.Get<x86FuncScopeData>();
			var Parts = x86DataLocations.Split(Pos, Arch.RegSize);

			for (var i = Parts.Length - 1; i >= 0; i--)
			{
				EmitInstruction("push", Parts[i]);
				FuncData.CallParameters += Arch.RegSize;
			}
		}

		void GetAddrOf(x86DataLocation Dst, x86DataLocation Pos, x86NodeData Data, bool Push = false)
		{
			var MemPos = Pos as x86MemoryLocation;
			if (MemPos == null) throw new ApplicationException();

			var FuncScope = Container.FunctionScope as FunctionScope;
			var FuncData = FuncScope.Data.Get<x86FuncScopeData>();

			var Address = MemPos.GetAddress();
			if (Address != null && Push)
			{
				EmitInstruction("push", Address);
				FuncData.CallParameters += Arch.RegSize;
				return;
			}

			if (Address == null)
			{
				var OldSize = Pos.Size;
				Pos.Size = 0;

				if (Dst is x86MemoryLocation)
				{
					var TempReg = Data.TempData.GetGRegister(x86TempGRegPurposeType.TwoMemOp);
					EmitInstruction("lea", TempReg, Pos);
					EmitInstruction("mov", Dst,  TempReg);
				}
				else
				{
					EmitInstruction("lea", Dst, Pos );
				}

				Pos.Size = OldSize;
				if (Push)
				{
					EmitInstruction("push", Dst);
					FuncData.CallParameters += Arch.RegSize;
				}
			}
			else
			{
				if (!Push)
				{
					if (!Address.Compare(Dst))
						EmitInstruction("mov", Dst, Address);
				}
				else
				{
					EmitInstruction("push", Address);
					FuncData.CallParameters += Arch.RegSize;
				}
			}
		}

		void ComputeLinkedNodes(ExpressionNode Node)
		{
			var Data = Node.Data.Get<x86NodeData>();
			for (var i = 0; i < Node.LinkedNodes.Count; i++)
			{
				var LNode = Node.LinkedNodes[i];
				var LData = LNode.Data.Get<x86LinkedNodeData>();

				if ((LData.Flags & x86LinkedNodeFlags.AllocateData) != 0)
					EmitMoveExpression(Data, LData.Location, LNode.Node);
				else EmitExpression(LNode.Node);
			}
		}

		public override void EmitExpression(ExpressionNode Node)
		{
			ComputeLinkedNodes(Node);

			var OpNode = Node as OpExpressionNode;
			if (OpNode == null) return;
			/*
			EmitInstruction("; Operator = " + Expressions.GetOperator(Node) + 
				" Code = " + Node.Code.String.GetSingleLineString());
			*/
			var Op = OpNode.Operator;
			var Ch = OpNode.Children;
			var Type = OpNode.Type.RealId as Type;
			var Data = OpNode.Data.Get<x86NodeData>();

			if (Op == Operator.StackAlloc)
			{
				var FS = Container.FunctionScope;
				var FSData = FS.Data.Get<x86FuncScopeData>();

				var Bytes = GetNodeLoc(Ch[0]);
				if (Bytes is x86ConstLocation)
				{
					var ConstLoc = Bytes as x86ConstLocation;
					var ConstVal = ConstLoc.Value as IntegerValue;
					var NewVal = DataStoring.AlignWithIncrease(ConstVal.Value, FSData.StackAlignment);
					EmitInstruction("sub", FSData.StackPointer, NewVal);
				}
				else
				{
					ProcessIndexMoves(Bytes);
					EmitInstruction("sub", FSData.StackPointer, Bytes);

					var Mask = DataStoring.GetAlignmentMask(FSData.StackPointer.Size, FSData.StackAlignment);
					EmitInstruction("and", FSData.StackPointer, Mask);
				}

				var Dst = Data.ExtractedOutput;
				ProcessIndexMoves(Dst);
				EmitInstruction("mov", Dst, FSData.StackPointer);
			}
			else if (Op == Operator.Index)
			{
				EmitExpression(Ch[0]);
				EmitExpression(Ch[1]);
			}
			else if (Op == Operator.Member)
			{
				EmitExpression(Ch[0]);
			}
			else if (Op == Operator.Condition)
			{
				if (OpNode.Type.RealId is FloatType)
				{
					if (Arch.FloatingPointMode == x86FloatingPointMode.FPU)
					{
						EmitCondition(Ch[0], GetFPULoadBranch(Ch[1]), GetFPULoadBranch(Ch[2]));
					}
					else if (Arch.FloatingPointMode == x86FloatingPointMode.SSE)
					{
						var T = Arch.GetCondBranchWithMove(Data.ExtractedOutput, Data, Ch[1]);
						var E = Arch.GetCondBranchWithMove(Data.ExtractedOutput, Data, Ch[2]);
						EmitCondition(Ch[0], T, E);
					}
					else
					{
						throw new NotImplementedException();
					}
				}
				else
				{
					var T = Arch.GetCondBranchWithMove(Data.ExtractedOutput, Data, Ch[1]);
					var E = Arch.GetCondBranchWithMove(Data.ExtractedOutput, Data, Ch[2]);
					EmitCondition(Ch[0], T, E);
				}
			}
			else if (Op == Operator.Call)
			{
				EmitCallExpression(OpNode);
			}
			else if (Op == Operator.Address)
			{
				var Src = GetNodeLoc(Ch[0]);
				var Dst = Data.ExtractedOutput;

				ProcessIndexMoves(Src, Dst);
				GetAddrOf(Dst, Src, Data);
			}
			else if (Op == Operator.NewObject)
			{
				var LType = Node.Type.UnderlyingClassOrRealId;
				if (!(LType is ClassType || LType is RefArrayType))
					throw new ApplicationException();

				var Self = x86Expressions.GetNullLocation(Arch, Container);
				EmitCallExpression(OpNode, null, Self); 
			}
			else
			{
				if (Type is FloatType) GetOpBaseCode_Float(OpNode);
				else GetOpBaseCode_NonFloat(OpNode);
			}
		}

		void MoveAndOp_BootRet(x86DataLocation Dst, OpExpressionNode OpNode, x86NodeData Data, bool x86Op = false)
		{
			var Op = OpNode.Operator;
			var Ch = OpNode.Children;

			if (x86Op || Operators.IsRelEquality(Op))
			{
				var Type = Ch[0].Type.RealId as Type;
				if (Type is NonFloatType && Type.Size > Arch.RegSize)
				{
					EmitCondition(OpNode, Dst);
				}
				else
				{
					ComputeLinkedNodes(OpNode);
					var Str = EmitSinglePartCmp(OpNode, Data);

					ProcessIndexMoves(Dst);
					if (Dst.Size != 1)
					{
						var Dst2 = Dst.GetPart(0, 1);
						EmitInstruction("set" + Str, Dst2);
						EmitInstruction("and", Dst, "0xFF");
					}
					else
					{
						EmitInstruction("set" + Str, Dst);
					}
				}
			}
			else
			{
				EmitCondition(OpNode, Dst);
			}
		}

		public void EmitMoveExpression(x86NodeData Data, x86DataLocation Dst, ExpressionNode Node)
		{
			if (!MoveAndOp(Dst, Node))
			{
				if (Node.Type.RealId is FloatType && Arch.FloatingPointMode == x86FloatingPointMode.FPU)
				{
					GetNodePos_FPU(Node, LoadIds: true);
					ProcessIndexMoves(Dst);
					FPUStore(Dst, Data.TempData, false);
				}
				else
				{
					var Src = GetNodeLoc(Node);
					if (!Dst.Compare(Src))
					{
						ProcessIndexMoves(Dst, Src);
						MoveData(Dst, Src, Data.TempData, x86ExecutorType.All, Node.Type);
					}
				}
			}
		}

		bool MoveAndOp(x86DataLocation Dst, ExpressionNode Node)
		{
			var Data = Node.Data.Get<x86NodeData>();
			var OpNode = Node as OpExpressionNode;
			if (OpNode == null) return false;

			var Ch = OpNode.Children;
			var Op = OpNode.Operator;

			//--------------------------------------------------------------------------------------------
			if (Op == Operator.Unknown)
			{
				if (x86Expressions.IsConditionOp(Data.Operator))
				{
					MoveAndOp_BootRet(Dst, OpNode, Data, true);
					return true;
				}
			}

			//--------------------------------------------------------------------------------------------
			else if (Op == Operator.Tuple || Op == Operator.Array)
			{
				ProcessTupleArrayOp(OpNode.Children, Data, Dst, 
					(i) => x86Identifiers.GetMember(Dst, Node.Type, i));

				return true;
			}

			//--------------------------------------------------------------------------------------------
			else if (Operators.IsBoolRet(Op))
			{
				MoveAndOp_BootRet(Dst, OpNode, Data);
				return true;
			}

			//--------------------------------------------------------------------------------------------
			else if (Op == Operator.NewObject)
			{
				var Type = Node.Type.UnderlyingStructureOrRealId as Type;
				if (!(Type is ClassType || Type is RefArrayType))
				{
					ComputeLinkedNodes(Node);
					if (Type is StructType && !(Type is TupleType))
					{
						EmitCallExpression(OpNode, null, Dst, true);
					}
					else
					{
						ProcessIndexMoves(Dst);

						var StoredDataType = x86Identifiers.GetStoredDataType(Node.Type);
						ZeroData(Dst, Data.TempData, x86ExecutorType.All, StoredDataType);
					}

					return true;
				}
			}

			//--------------------------------------------------------------------------------------------
			else if (Op == Operator.Call)
			{
				var Type = Ch[0].Type as TypeOfFunction;
				if (Type.RetType.RealId is StructType)
				{
					ComputeLinkedNodes(Node);
					EmitCallExpression(OpNode, Dst);
					return true;
				}
			}

			//--------------------------------------------------------------------------------------------
			else if (Op == Operator.Add || Op == Operator.Multiply || Operators.IsBitwise(Op))
			{
				var ChDst = GetNodeLoc(Ch[0], x86GetNodePosMode.PositionOnly);
				var ChSrc = GetNodeLoc(Ch[1], x86GetNodePosMode.PositionOnly);

				if (ChSrc.Compare(Dst) && (!(Dst is x86MemoryLocation) || (Data.DataCalcPos & x86DataLocationType.Memory) != 0))
				{
					ComputeLinkedNodes(Node);
					EmitExpression(Ch[0]);
					EmitExpression(Ch[1]);

					if (Node.Type.RealId is FloatType)
					{
						if (Arch.FloatingPointMode == x86FloatingPointMode.SSE)
							GetOpBaseCode_SSE(OpNode, Dst, ChDst);
						else throw new ApplicationException();
					}
					else
					{
						GetOpBaseCode_NonFloat(OpNode, Dst, ChDst);
					}

					return true;
				}
			}

			return false;
		}

		public void ProcessIndexMoves(x86DataLocation DataLoc)
		{
			var IndexPos = DataLoc as x86IndexLocation;
			if (IndexPos != null) MoveData(IndexPos.Moves);
		}

		public void ProcessIndexMoves(x86DataLocation DataLoc1, x86DataLocation DataLoc2)
		{
			ProcessIndexMoves(DataLoc1);
			ProcessIndexMoves(DataLoc2);
		}

		private void FPULoad(x86DataLocation Src, x86TemporaryData TempData, bool Int)
		{
			if (!(Src is x86MemoryLocation))
			{
				if (Src == null)
					throw new ArgumentNullException("Src");

				var NSrc = TempData.Memory.GetPart(0, Src.Size);
				MoveData(NSrc, Src, TempData, x86ExecutorType.General, new x86StoredDataType());
				Src = NSrc;
			}

			if (Int) EmitInstruction("fild", Src);
			else EmitInstruction("fld", Src);
		}

		private void FPUStore(x86DataLocation Dst, x86TemporaryData TempData, bool Int)
		{
			var NDst = Dst;
			if (!(Dst is x86MemoryLocation))
				NDst = TempData.Memory.GetPart(0, Dst.Size);

			if (Int)
			{
				if ((Arch.Extensions & x86Extensions.SSE3) == 0)
					throw new ApplicationException();

				EmitInstruction("fisttp", NDst);
			}
			else
			{
				EmitInstruction("fstp", NDst);
			}

			if (NDst != Dst) 
				MoveData(Dst, NDst, TempData, x86ExecutorType.General, new x86StoredDataType());
		}

		private void ProcessTupleArrayOp(ExpressionNode[] Ch, x86NodeData Data,
			x86DataLocation Dst, Func<int, x86DataLocation> Func)
		{
			if (Dst != null)
			{
				for (var i = 0; i < Ch.Length; i++)
					EmitMoveExpression(Data, Func(i), Ch[i]);
			}
			else
			{
				for (var i = 0; i < Ch.Length; i++)
					EmitExpression(Ch[i]);
			}
		}

		private void ProcessTupleArrayOp(OpExpressionNode Node, Func<int, x86DataLocation, x86DataLocation> Func)
		{
			var Data = Node.Data.Get<x86NodeData>();
			var Output = Data.ExtractedOutput;
			ProcessTupleArrayOp(Node.Children, Data, Output, i => Func(i, Output));
		}

		CondBranch GetFPULoadBranch(ExpressionNode Node)
		{
			return new CodeCondBranch(CG =>
			{
				var x86CG = CG as x86CodeGenerator;
				x86CG.GetNodePos_FPU(Node, LoadIds: true);
			});
		}

		public void EmitCondition(ExpressionNode Node, x86DataLocation Dst)
		{
			var Type = Node.Type as BooleanType;
			var FuncScope = Container.FunctionScope as FunctionScope;
			var Data = Node.Data.Get<x86NodeData>();

			var BoolType = Container.GlobalContainer.CommonIds.Boolean;
			var True = new x86ConstLocation(Arch, new BooleanValue(true), BoolType, 0, Dst.Size);
			var False = new x86ConstLocation(Arch, new BooleanValue(false), BoolType, 0, Dst.Size);

			var StoredDataType = x86Identifiers.GetStoredDataType(Node.Type);
			var Then = new x86MoveCondBranch(Dst, True, Data.TempData,
				x86ExecutorType.General, StoredDataType);

			var Else = new x86MoveCondBranch(Dst, False, Data.TempData,
				x86ExecutorType.General, StoredDataType);

			EmitCondition(Node, Then, Else);
		}

		x86DataLocation GetNodePos_FPU(ExpressionNode Node, x86GetNodePosMode Mode = x86GetNodePosMode.Default, 
			bool LoadIds = false)
		{
			var Data = Node.Data.Get<x86NodeData>();
			if (Node is ConstExpressionNode)
			{
				var ConstNode = Node as ConstExpressionNode;
				if (Mode == x86GetNodePosMode.Default)
					LoadFloatConst(ConstNode);

				return null;
			}
			else if (Node is IdExpressionNode)
			{
				var IdNode = Node as IdExpressionNode;
				var IdData = IdNode.Identifier.Data.Get<x86IdentifierData>();
				var Pos = IdData.Location;

				if (LoadIds)
				{
					if (Mode == x86GetNodePosMode.ReturnNull) return null;
					else if (Mode == x86GetNodePosMode.PositionOnly) return Pos;

					ProcessIndexMoves(Pos);
					FPULoad(Pos, Data.TempData, Node.Type.RealId is NonFloatType);
				}

				return Pos;
			}
			else
			{
				if (Mode == x86GetNodePosMode.ReturnNull) return null;
				var Pos = GetNodeLoc(Node, Mode);

				if (Pos == null && x86Expressions.NeedLoadFloat(Node, true))
					throw new ApplicationException();

				if (LoadIds && Pos != null)
				{
					if (Mode == x86GetNodePosMode.ReturnNull) return null;
					else if (Mode == x86GetNodePosMode.PositionOnly) return Pos;

					ProcessIndexMoves(Pos);
					FPULoad(Pos, Data.TempData, Node.Type.RealId is NonFloatType);
				}

				return Pos;
			}
		}

		private void LoadFloatConst(ConstExpressionNode ConstCh)
		{
			var V = ConstCh.CDouble;
			if (V == 0.0) EmitInstruction("fldz");
			else if (V == 1.0) EmitInstruction("fld1");
			else if (V == Math.PI) EmitInstruction("fldpi");
			else throw new ApplicationException();
		}

		private void GetOpBaseCode_Float(OpExpressionNode OpNode)
		{
			if (Arch.FloatingPointMode == x86FloatingPointMode.SSE)
				GetOpBaseCode_SSE(OpNode);
			else if (Arch.FloatingPointMode == x86FloatingPointMode.FPU)
				GetOpBaseCode_FPU(OpNode);
			else
				throw new NotImplementedException();
		}

		private void GetOpBaseCode_SSE(OpExpressionNode OpNode)
		{
			var Op = OpNode.Operator;
			var Ch = OpNode.Children;

			var Data = OpNode.Data.Get<x86NodeData>();
			var Double = Expressions.IsDouble(OpNode);

			if (Op == Operator.Assignment)
			{
				var Dst = GetNodeLoc(Ch[0]);
				EmitMoveExpression(Data, Dst, Ch[1]);
				return;
			}

			if (Operators.IsCast(Op))
			{
				var DstType = x86Identifiers.GetStoredDataType(OpNode.Type);
				var SrcType = x86Identifiers.GetStoredDataType(Ch[0].Type);

				if (Op == Operator.Reinterpret)
				{
					var Src = GetNodeLoc(Ch[0]);
					var Dst = Data.ExtractedOutput;
					var Ch0Executor = Arch.GetDefaultExecutor(SrcType.TypeKind);

					ProcessIndexMoves(Dst, Src);
					MoveData(Dst, Src, Data.TempData, Ch0Executor, SrcType);
				}
				else if (Op == Operator.Cast)
				{
					if (SrcType.IsEquivalent(DstType))
					{
						var Dst = Data.ExtractedOutput;
						EmitMoveExpression(Data, Dst, Ch[0]);
					}

					else if (x86Identifiers.IsFloatTypeKind(SrcType.TypeKind))
					{
						var Src = GetNodeLoc(Ch[0]);
						var Dst = Data.ExtractedOutput;

						if (SrcType.TypeKind != DstType.TypeKind)
							throw new NotSupportedException();

						if (SrcType.Precision == DstType.Precision)
						{
							ProcessIndexMoves(Dst, Src);
							EmitSSEMoveInstruction(Data.TempData, Dst, Src, DstType);
						}
						else
						{
							var CvtIns = DecorateSSEInstruction("cvt", SrcType) + "2";
							EmitInstructionNonmemoryDestination(CvtIns, Dst, Src, 
								Data.TempData, x86ExecutorType.SSE, DstType, true);
						}
					}

					else if (x86Identifiers.IsNonfloatTypeKind(SrcType.TypeKind))
					{
						var Src = GetNodeLoc(Ch[0]);
						var Dst = Data.ExtractedOutput;

						if (SrcType.Precision != Arch.RegSize)
							throw new NotSupportedException();

						var CvtIns = "cvt";
						if (x86Identifiers.IsVectorTypeKind(SrcType.TypeKind))
							CvtIns += "pi2"; else CvtIns += "si2";

						EmitInstructionNonmemoryDestination(CvtIns, Dst, Src, 
							Data.TempData, x86ExecutorType.SSE, DstType, true);
					}

					else
					{
						throw new NotImplementedException();
					}
				}
			}
			else
			{
				var Dst = GetNodeLoc(Ch[0]);
				var Src = Ch.Length > 1 ? GetNodeLoc(Ch[1]) : null;
				GetOpBaseCode_SSE(OpNode, Dst, Src);
			}
		}

		private void GetOpBaseCode_SSE(OpExpressionNode OpNode, x86DataLocation Dst, x86DataLocation Src)
		{
			var Ch = OpNode.Children;
			var Op = OpNode.Operator;
			
			var Data = OpNode.Data.Get<x86NodeData>();
			var Type = OpNode.Type.RealId as Type;
			var StoredDataType = x86Identifiers.GetStoredDataType(OpNode.Type);

			if (Operators.IsBitArithm(Op))
			{
				string Ins;
				if (Op == Operator.Add) Ins = "add";
				else if (Op == Operator.Subract) Ins = "sub";
				else if (Op == Operator.Multiply) Ins = "mul";
				else if (Op == Operator.Divide) Ins = "div";
				else if (Op == Operator.BitwiseAnd) Ins = "and";
				else if (Op == Operator.BitwiseOr) Ins = "or";
				else if (Op == Operator.BitwiseXor) Ins = "xor";
				else throw new NotImplementedException();

				var DataTypeForInstruction = StoredDataType;
				if (Operators.IsBitwise(Op))
					x86Identifiers.GetVectorStoredDataType(ref DataTypeForInstruction);

				EmitInstructionNonmemoryDestination(Ins, Dst, Src, Data.TempData, 
					x86ExecutorType.SSE, StoredDataType, DataTypeForInstruction);
			}
			else if (Op == Operator.Negation)
			{
				Src = x86Expressions.GetLocation(Arch, Data.NegateAbsBitmask);
				EmitInstructionNonmemoryDestination("xor", Dst, Src, Data.TempData,
					x86ExecutorType.SSE, StoredDataType, StoredDataType.VectorType());
			}
			else if (Op == Operator.Unknown)
			{
				var x86Op = Data.Operator;
				if (x86Expressions.IsMinMaxOp(x86Op))
				{
					string Ins;
					if (x86Op == x86Operator.Min) Ins = "min";
					else if (x86Op == x86Operator.Max) Ins = "max";
					else throw new NotImplementedException();

					EmitInstructionNonmemoryDestination(Ins, Dst, Src, 
						Data.TempData, x86ExecutorType.SSE, StoredDataType);
				}
				else if (x86Op == x86Operator.Abs)
				{
					Src = x86Expressions.GetLocation(Arch, Data.NegateAbsBitmask);
					EmitInstructionNonmemoryDestination("and", Dst, Src, Data.TempData,
						x86ExecutorType.SSE, StoredDataType, StoredDataType.VectorType());
				}
				else if (x86Op == x86Operator.Sqrt)
				{
					EmitInstructionNonmemoryDestination("sqrt", Data.ExtractedOutput, Dst,
						Data.TempData, x86ExecutorType.SSE, StoredDataType);
				}
				else if (x86Op == x86Operator.Sin || x86Op == x86Operator.Cos)
				{
					var MovIns = DecorateSSEInstruction("mov", StoredDataType);
					Src = Dst;
					Dst = Data.ExtractedOutput;

					if (!(Src is x86MemoryLocation))
					{
						var Memory = Data.TempData.Memory.GetPart(0, Type.Size);
						ProcessIndexMoves(Memory, Src);
						EmitInstruction(MovIns, Memory, Src);
						Src = Memory;
					}

					EmitInstruction("fld", Src);
					if (x86Op == x86Operator.Sin) EmitInstruction("fsin");
					else if (x86Op == x86Operator.Cos) EmitInstruction("fcos");
					else throw new NotImplementedException();

					if (!(Dst is x86MemoryLocation))
					{
						var Memory = Data.TempData.Memory.GetPart(0, Type.Size);
						ProcessIndexMoves(Dst, Memory);
						EmitInstruction("fstp", Memory);
						EmitInstruction(MovIns, Dst, Memory);
					}
					else
					{
						ProcessIndexMoves(Dst);
						EmitInstruction("fstp", Dst);
					}
				}
				else
				{
					throw new NotImplementedException();
				}
			}
			else
			{
				throw new NotImplementedException();
			}
		}

		private void GetOpBaseCode_FPU(OpExpressionNode OpNode)
		{
			var Ch = OpNode.Children;
			var Op = OpNode.Operator;
			var Data = OpNode.Data.Get<x86NodeData>();

			if (Op == Operator.Assignment)
			{
				var RetVar = false;
				if (Ch[0] is IdExpressionNode)
				{
					var IdCh0 = Ch[0] as IdExpressionNode;
					RetVar = IdCh0.Identifier is x86ReturnVariable;
				}

				var ConstNode = Ch[1] as ConstExpressionNode;
				if (!RetVar)
				{
					if (ConstNode != null)
					{
						var Ch1Type = Ch[1].Type.RealId as Type;
						var Src = new x86ConstLocation(Arch, ConstNode, 0, Ch1Type.Size);
						var Dst = GetNodeLoc(Ch[0]);

						ProcessIndexMoves(Dst);
						MoveData(Dst, Src, Data.TempData,
							x86ExecutorType.General, new x86StoredDataType());
					}
					else
					{
						GetNodePos_FPU(Ch[1], LoadIds: true);
						var Dst = GetNodeLoc(Ch[0]);

						ProcessIndexMoves(Dst);
						FPUStore(Dst, Data.TempData, false);
					}
				}
				else
				{
					GetNodePos_FPU(Ch[1], LoadIds: true);
				}
			}
			else if (Operators.IsCast(Op))
			{
				var FuncScope = Container.FunctionScope as FunctionScope;
				var IsCh0Int = Ch[0].Type.RealId is NonFloatType;

				var Src = GetNodeLoc(Ch[0]);
				var Dst = Data.ExtractedOutput;

				if (Src != null) ProcessIndexMoves(Src);
				if (Dst != null) ProcessIndexMoves(Dst);

				if (!x86Expressions.NeedLoadFloat(Ch[0], true))
				{
					if (IsCh0Int) FPULoad(Src, Data.TempData, Op != Operator.Reinterpret);
					if (Dst != null) FPUStore(Dst, Data.TempData, false);
				}
				else
				{
					if (Ch[0] is ConstExpressionNode)
					{
						if (Dst == null) LoadFloatConst(Ch[0] as ConstExpressionNode);
						else EmitInstruction("mov", Dst, Src);
					}
					else if (Dst == null || !Dst.Compare(Src))
					{
						FPULoad(Src, Data.TempData, IsCh0Int && Op != Operator.Reinterpret);
						if (Dst != null) FPUStore(Dst, Data.TempData, false);
					}
				}
			}
			else if (Op == Operator.Negation)
			{
				GetNodePos_FPU(Ch[0], LoadIds: true);
				EmitInstruction("fchs");
			}
			else if (Op == Operator.Unknown)
			{
				var x86Op = Data.Operator;
				if (x86Op == x86Operator.Abs)
				{
					GetNodePos_FPU(Ch[0], LoadIds: true);
					EmitInstruction("fabs");
				}
				else if (x86Op == x86Operator.Sqrt)
				{
					GetNodePos_FPU(Ch[0], LoadIds: true);
					EmitInstruction("fsqrt");
				}
				else if (x86Op == x86Operator.Sin)
				{
					GetNodePos_FPU(Ch[0], LoadIds: true);
					EmitInstruction("fsin");
				}
				else if (x86Op == x86Operator.Cos)
				{
					GetNodePos_FPU(Ch[0], LoadIds: true);
					EmitInstruction("fcos");
				}
				else if (x86Op == x86Operator.Tan)
				{
					GetNodePos_FPU(Ch[0], LoadIds: true);
					EmitInstruction("fptan");
					EmitInstruction("ffree", "st0");
					EmitInstruction("fincstp");
				}
				else if (x86Op == x86Operator.Atan)
				{
					GetNodePos_FPU(Ch[0], LoadIds: true);
					EmitInstruction("fld1");
					EmitInstruction("fpatan");
				}
				else if (x86Op == x86Operator.Atan2)
				{
					GetNodePos_FPU(Ch[0], LoadIds: true);
					GetNodePos_FPU(Ch[1], LoadIds: true);
					EmitInstruction("fpatan");
				}
				else if (x86Expressions.IsRoundOp(x86Op))
				{
					var ForRoundingPos = x86Expressions.GetLocation(Arch, Data.ControlWordForRounding);
					var DefaultPos = x86Expressions.GetLocation(Arch, Data.DefaultFPUControlWord);

					EmitInstruction("fldcw", ForRoundingPos);
					EmitInstruction("frndint");
					EmitInstruction("fldcw", DefaultPos);
				}
				else
				{
					throw new ApplicationException();
				}
			}
			else
			{
				var Dst = GetNodePos_FPU(Ch[0]);
				var Src = GetNodePos_FPU(Ch[1]);
				if (Dst != null) ProcessIndexMoves(Dst);
				if (Src != null) ProcessIndexMoves(Src);

				var OpStr = "";
				if (Op == Operator.Add) OpStr = "add";
				else if (Op == Operator.Subract) OpStr = "sub";
				else if (Op == Operator.Multiply) OpStr = "mul";
				else if (Op == Operator.Divide) OpStr = "div";
				else throw new NotImplementedException();

				if (Dst != null)
				{
					if (Src != null) throw new ApplicationException();
					var Reverse = (OpNode.Flags & ExpressionFlags.ReverseOperation) == 0 && 
								  (Op == Operator.Subract || Op == Operator.Divide);

					var Float = Ch[0].Type.RealId is FloatType;
					FPUOp(OpStr, Dst, Float, Reverse, Data);
				}
				else
				{
					var Float = Ch[1].Type.RealId is FloatType;
					FPUOp(OpStr, Src, Float, (OpNode.Flags & ExpressionFlags.ReverseOperation) != 0, Data);
				}
			}
		}

		private void FPUOp(string Op, x86DataLocation Src, bool Float, bool Reverse, x86NodeData Data)
		{
			if (Src == null)
			{
				if (!Reverse) EmitInstruction("f" + Op + "p");
				else EmitInstruction("f" + Op + "rp");
				return;
			}

			if (!(Src is x86MemoryLocation))
			{
				var NSrc = Data.TempData.Memory.GetPart(0, Src.Size);
				EmitInstruction("mov", NSrc, Src);
				Src = NSrc;
			}

			if (Float)
			{
				if (!Reverse) EmitInstruction("f" + Op, Src);
				else EmitInstruction("f" + Op + "r", Src);
			}
			else
			{
				if (!Reverse) EmitInstruction("fi" + Op, Src);
				else EmitInstruction("fi" + Op + "r", Src);
			}
		}

		private void GetOpBaseCode_NonFloat(OpExpressionNode Node, x86DataLocation Dst, x86DataLocation Src)
		{
			var Op = Node.Operator;
			var Ch = Node.Children;
			var Data = Node.Data.Get<x86NodeData>();
			var Signed = Ch[0].Type.RealId is SignedType;

			ProcessIndexMoves(Dst);
			if (Src != null)
				ProcessIndexMoves(Src);

			var SplDst = x86DataLocations.Split(Dst, Arch.RegSize);
			var SplSrc = Src != null ? x86DataLocations.Split(Src, Arch.RegSize) : null;

			if (Src != null && SplDst.Length != SplSrc.Length && !Operators.IsShift(Op))
				throw new ApplicationException();

			//--------------------------------------------------------------------------------------
			if (Op == Operator.Negation)
			{
				for (var i = 0; i < SplDst.Length; i++)
				{
					if (i > 0) EmitInstruction("adc", SplDst[i], "0");
					EmitInstruction("neg", SplDst[i]);
				}
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.Complement)
			{
				EmitInstructionForEachPart("not", SplDst, SplSrc, Data.TempData);
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.Add || Op == Operator.Subract)
			{
				var ConstSplSrc0 = SplSrc[0] as x86ConstLocation;
				if (SplDst.Length == 1 && ConstSplSrc0 != null && ConstSplSrc0.Signed == 1)
				{
					if (Op == Operator.Add) EmitInstruction("inc", SplDst[0]);
					else if (Op == Operator.Subract) EmitInstruction("dec", SplDst[0]);
					else throw new NotImplementedException();
				}
				else
				{
					var First = true;
					for (var i = 0; i < SplDst.Length; i++)
					{
						string Ins;
						if (Op == Operator.Add) Ins = First ? "add" : "adc";
						else if (Op == Operator.Subract) Ins = First ? "sub" : "sbb";
						else throw new NotImplementedException();

						var ConstSplSrci = SplSrc[i] as x86ConstLocation;
						if (!First || ConstSplSrci == null || ConstSplSrci.Signed != 0)
						{
							EmitInstructionWithoutTwoMemory(Ins, SplDst[i], SplSrc[i], 
								Data.TempData, x86ExecutorType.General, new x86StoredDataType());

							First = false;
						}
					}
				}
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.Multiply)
			{
				if (SplSrc.Length == 1)
				{
					var EAXEDXUsed = false;
					if (Data.UsedDataBySelf != null)
					{
						var UsedData = Data.UsedDataBySelf;
						if (Dst.Size == 1) EAXEDXUsed = UsedData.GRegisters[0].Size >= 2 && UsedData.GRegisters[0].Offset == 0;
						else EAXEDXUsed = UsedData.GRegisters[0].Size >= Dst.Size && UsedData.GRegisters[2].Size >= Dst.Size;
					}

					var RDst = GetNodeLoc(Node, x86GetNodePosMode.PositionOnly);
					if (EAXEDXUsed && x86Architecture.IsGRegister(Dst, 0) && !(Src is x86ConstLocation))
					{
						if (!RDst.Compare(Dst)) throw new ApplicationException();
						EmitInstruction("imul", Src);
					}
					else
					{
						if (Dst.Size == 1) throw new ApplicationException();
						Action<x86DataLocation, bool, Action<x86DataLocation>> Func = (Pos, Move, F) =>
						{
							var OldPos = Pos;
							if (Pos is x86MemoryLocation)
							{
								var TempReg = Data.TempData.GetGRegister(x86TempGRegPurposeType.TwoMemOp);
								if (Move) EmitInstruction("mov", TempReg, Pos);
								Pos = TempReg;
							}

							F(Pos);
							if (Pos != OldPos) EmitInstruction("mov", OldPos, Pos);
						};

						if (RDst.Compare(Dst)) Func(Dst, true, Pos => EmitInstruction("imul", Pos, Src));
						else Func(RDst, false, Pos => EmitInstruction("imul", Pos, Dst, Src));
					}
				}
				else
				{
					throw new NotImplementedException();
				}
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.Divide || Op == Operator.Modolus)
			{
				if (SplDst.Length == 1)
				{
					var Dst0 = SplDst[0];
					var Src0 = SplSrc[0];

					var Size = Dst0.Size;
					if (Size == 1) Dst0.Size = 2;

					if (!x86Architecture.IsGRegister(Dst0, 0))
					{
						var Accumulator = x86Architecture.GetGRegisterName(0, Size);
						EmitInstruction("mov", Accumulator, Dst0);
					}

					if (!Signed)
					{
						if (Size == 1) EmitInstruction("xor", "ah", "ah");
						else EmitInstruction("xor", "edx", "edx");

						EmitInstruction("div", Src0);
					}
					else
					{
						if (Size == 1) EmitInstruction("cbw");
						else if (Size == 2) EmitInstruction("cwd");
						else if (Size == 4) EmitInstruction("cdq");
						else if (Size == 8) EmitInstruction("cqo");
						else throw new ApplicationException();

						EmitInstruction("idiv", Src0);
					}
				}
				else
				{
					throw new NotImplementedException();
				}
			}

			//--------------------------------------------------------------------------------------
			else if (Operators.IsBitwise(Op))
			{
				var RDst = GetNodeLoc(Node, x86GetNodePosMode.PositionOnly);
				if (!RDst.Compare(Dst)) SplDst = x86DataLocations.Split(RDst, Arch.RegSize);

				string Ins;
				if (Op == Operator.BitwiseAnd) Ins = "and";
				else if (Op == Operator.BitwiseOr) Ins = "or";
				else if (Op == Operator.BitwiseXor) Ins = "xor";
				else throw new NotImplementedException();

				for (var i = 0; i < SplDst.Length; i++)
				{
					if (SplSrc.Length <= i)
					{
						ZeroData(SplDst[i], Data.TempData, x86ExecutorType.General, new x86StoredDataType());
						continue;
					}

					var ConstSplSrci = SplSrc[i] as x86ConstLocation;
					if (ConstSplSrci != null && ConstSplSrci.Signed == 0)
					{
						if (Op == Operator.BitwiseAnd)
							ZeroData(SplDst[i], Data.TempData, x86ExecutorType.General, new x86StoredDataType());
						else if (Op != Operator.BitwiseOr && Op != Operator.BitwiseXor)
							throw new NotImplementedException();
					}
					else if (ConstSplSrci != null && ConstSplSrci.Signed == -1)
					{
						if (Op == Operator.BitwiseOr) EmitInstruction("mov", SplDst[i], -1);
						else if (Op == Operator.BitwiseXor) EmitInstruction("not", SplDst[i]);
						else if (Op != Operator.BitwiseAnd) throw new NotImplementedException();
					}
					else
					{
						EmitInstructionWithoutTwoMemory(Ins, SplDst[i], SplSrc[i], 
							Data.TempData, x86ExecutorType.General, new x86StoredDataType());
					}
				}
			}

			//--------------------------------------------------------------------------------------
			else if (Operators.IsShift(Op))
			{
				var RSrc = SplSrc[0];
				if (!(RSrc is x86ConstLocation))
				{
					var CL = new x86GRegLocation(Arch, 1, 1);
					var RSrcPart = RSrc.GetPart(0, 1);
					if (!CL.Compare(RSrcPart))
						EmitInstruction("mov", CL, RSrcPart);

					RSrc = CL;
				}

				if (SplDst.Length == 1)
				{
					if (Op == Operator.ShiftLeft)
					{
						EmitInstructionWithoutTwoMemory("shl", SplDst[0], RSrc,
							Data.TempData, x86ExecutorType.General, new x86StoredDataType());
					}
					else if (Op == Operator.ShiftRight)
					{
						if (!Signed)
						{
							EmitInstructionWithoutTwoMemory("shr", SplDst[0], RSrc, 
								Data.TempData, x86ExecutorType.General, new x86StoredDataType());
						}
						else
						{
							EmitInstructionWithoutTwoMemory("sar", SplDst[0], RSrc,
								Data.TempData, x86ExecutorType.General, new x86StoredDataType());
						}
					}
					else
					{
						throw new NotImplementedException();
					}
				}
				else
				{
					var RegBitCount = Arch.RegSize * 8;
					var RDst = GetNodeLoc(Node, x86GetNodePosMode.PositionOnly);

					if (Src is x86ConstLocation)
					{
						var ConstSrc = Src as x86ConstLocation;
						if (ConstSrc.Integer > Dst.Size * 8)
							throw new ApplicationException();

						var Value = (int)ConstSrc.Integer;
						var PartOffset = Value / RegBitCount;
						var NewValue = Value % RegBitCount;
						var IntType = Container.GlobalContainer.CommonIds.Byte;
						var NewSrc = new x86ConstLocation(Arch, new IntegerValue(NewValue), IntType, 0, 1);
						ShiftData_Helper(Op, Data, Signed, RDst, Dst, PartOffset, NewSrc);
					}
					else
					{
						var NextLabel = State.AutoLabel;
						var Count = Data.OriginalShiftSize / Arch.RegSize;
						var Mask = Data.OriginalShiftSize * 8 - 1;

						for (var i = 1; i < Count; i++)
						{
							var Lbl = State.AutoLabel;
							EmitInstruction("test", "cl", i * RegBitCount);
							InsContainer.Add(new x86ConditionalJump(Lbl, "nz"));
							ShiftData_Helper(Op, Data, Signed, RDst, Dst, i - 1, RSrc);
							InsContainer.Jump(NextLabel);
							InsContainer.Label(Lbl);
						}

						ShiftData_Helper(Op, Data, Signed, RDst, Dst, Count - 1, RSrc);
						InsContainer.Label(NextLabel);
					}
				}
			}

			//--------------------------------------------------------------------------------------
			else if (Op == Operator.Unknown)
			{
				if (Data.Operator == x86Operator.Swap)
				{
					var OldDst = Dst;
					if (Dst is x86MemoryLocation)
					{
						Dst = Data.TempData.GetGRegister(x86TempGRegPurposeType.TwoMemOp);
						EmitInstruction("mov", Dst, OldDst);
					}

					EmitInstruction("xchg", Dst, Src);
					if (OldDst != Dst)
						EmitInstruction("mov", OldDst, Dst);
				}

				//----------------------------------------------------------------------------------
				else
				{
					throw new NotImplementedException();
				}
			}

			//--------------------------------------------------------------------------------------
			else
			{
				throw new NotImplementedException();
			}
		}

		private void ShiftData_Helper(Operator Op, x86NodeData Data, bool Signed, x86DataLocation Dst,
			x86DataLocation Src, int PartOffset, x86DataLocation Shift)
		{
			var SplSrc = x86DataLocations.Split(Src, Arch.RegSize);
			var SplDst = Dst.Compare(Src) ? SplSrc : x86DataLocations.Split(Dst, Arch.RegSize);
			var SkipNextMove = false;

			if (Op == Operator.ShiftRight)
			{
				var NulledPart = (x86DataLocation)null;
				var ShiftIns = Signed ? "sar" : "shr";

				for (var i = 0; i < SplDst.Length; i++)
				{
					var From = i + PartOffset;
					if (From < SplSrc.Length)
					{
						if (!SkipNextMove)
						{
							EmitMoveInstructionChecked(SplDst[i], SplSrc[From], Data.TempData,
								x86ExecutorType.General, new x86StoredDataType());
						}
						else
						{
							SkipNextMove = false;
						}

						if (From < SplSrc.Length - 1)
						{
							var Part = SplSrc[From + 1];
							if (Part is x86MemoryLocation)
							{
								if (PartOffset == 0 && !(SplDst[i + 1] is x86MemoryLocation))
								{
									EmitInstruction("mov", SplDst[i + 1], SplSrc[i + 1]);
									Part = SplDst[i + 1];
									SkipNextMove = true;
								}
								else
								{
									var TempReg = Data.TempData.GetGRegister(x86TempGRegPurposeType.TwoMemOp);
									EmitInstruction("mov", TempReg, Part);
									Part = TempReg;
								}
							}

							EmitInstruction("shrd", SplDst[i], Part, Shift);
						}
						else
						{
							EmitInstruction(ShiftIns, SplDst[i], Shift);
						}
					}
					else
					{
						if (Signed)
						{
							if (NulledPart != null && !(SplDst[i] is x86MemoryLocation && NulledPart is x86MemoryLocation))
							{
								EmitInstruction("mov", SplDst[i], NulledPart);
							}
							else
							{
								EmitInstructionWithoutTwoMemory("mov", SplDst[i], SplSrc[SplSrc.Length - 1],
									Data.TempData, x86ExecutorType.General, new x86StoredDataType());

								EmitInstruction("sar", SplDst[i], 31);
							}

							NulledPart = SplDst[i];
						}
						else
						{
							ZeroData(SplDst[i], Data.TempData,
								x86ExecutorType.General, new x86StoredDataType());
						}
					}
				}
			}
			else if (Op == Operator.ShiftLeft)
			{
				for (var i = SplDst.Length - 1; i >= 0; i--)
				{
					var From = i - PartOffset;
					if (From >= 0)
					{
						if (!SkipNextMove)
						{
							MoveData(SplDst[i], SplSrc[From], Data.TempData,
								x86ExecutorType.General, new x86StoredDataType());
						}
						else
						{
							SkipNextMove = false;
						}

						if (From > 0)
						{
							var Part = SplSrc[From - 1];
							if (Part is x86MemoryLocation)
							{
								if (PartOffset == 0 && !(SplDst[i - 1] is x86MemoryLocation))
								{
									EmitInstruction("mov", SplDst[i - 1], SplSrc[i - 1]);
									Part = SplDst[i - 1];
									SkipNextMove = true;
								}
								else
								{
									var TempReg = Data.TempData.GetGRegister(x86TempGRegPurposeType.TwoMemOp);
									EmitInstruction("mov" + TempReg, Part);
									Part = TempReg;
								}
							}

							EmitInstruction("shld", SplDst[i], Part,  Shift);
						}
						else
						{
							EmitInstruction("shl", SplDst[i], Shift);
						}
					}
					else
					{
						ZeroData(SplDst[i], Data.TempData,
							x86ExecutorType.General, new x86StoredDataType());
					}
				}
			}
			else
			{
				throw new NotImplementedException();
			}
		}

		private void EmitInstructionForEachPart(string Instruction, x86DataLocation[] SplDst, 
			x86DataLocation[] SplSrc, x86TemporaryData TempData)
		{
			if (SplSrc != null)
			{
				for (var i = 0; i < SplDst.Length; i++)
				{
					EmitInstructionWithoutTwoMemory(Instruction, SplDst[i], SplSrc[i], 
						TempData, x86ExecutorType.General, new x86StoredDataType());
				}
			}
			else
			{
				for (var i = 0; i < SplDst.Length; i++)
					EmitInstruction(Instruction, SplDst[i]);
			}
		}

		private void GetOpBaseCode_NonFloat(OpExpressionNode OpNode)
		{
			var Ch = OpNode.Children;
			var Op = OpNode.Operator;
			var Data = OpNode.Data.Get<x86NodeData>();

			if (Op == Operator.Assignment)
			{
				var Dst = GetNodeLoc(Ch[0]);
				EmitMoveExpression(Data, Dst, Ch[1]);
			}
			else if (Operators.IsCast(Op))
			{
				var Ch0Data = Ch[0].Data.Get<x86NodeData>();
				var SrcType = x86Identifiers.GetStoredDataType(Ch[0].Type);
				var DstType = x86Identifiers.GetStoredDataType(OpNode.Type);

				var FuncScope = Container.FunctionScope as FunctionScope;
				var Dst = Data.ExtractedOutput;

				if (SrcType.IsEquivalent(DstType))
				{
					EmitMoveExpression(Data, Dst, Ch[0]);
				}

				else if (SrcType.TypeKind == x86TypeKind.Float)
				{
					if (Arch.FloatingPointMode == x86FloatingPointMode.SSE)
					{
						var Double = Expressions.IsDouble(Ch[0]);
						var Src = GetNodeLoc(Ch[0]);

						if (Op == Operator.Reinterpret)
						{
							ProcessIndexMoves(Dst, Src);
							MoveData(Dst, Src, Data.TempData, x86ExecutorType.All, DstType);
						}
						else
						{
							var OldDst = Dst;
							if (Dst is x86MemoryLocation)
								Dst = Data.TempData.GetGRegister(x86TempGRegPurposeType.TwoMemOp);

							var CvtIns = Double ? "cvttsd2si" : "cvttss2si";
							ProcessIndexMoves(Dst, Src);
							EmitInstruction(CvtIns, Dst, Src);

							if (OldDst != Dst)
								EmitInstruction("mov", OldDst, Dst);
						}
					}
					else if (Arch.FloatingPointMode == x86FloatingPointMode.FPU)
					{
						GetNodePos_FPU(Ch[0], LoadIds: true);
						if (Dst != null)
						{
							ProcessIndexMoves(Dst);
							FPUStore(Dst, Data.TempData, Op != Operator.Reinterpret);
						}
					}
					else
					{
						throw new NotImplementedException();
					}
				}

				else if (SrcType.TypeKind == x86TypeKind.Signed || SrcType.TypeKind == x86TypeKind.Unsigned)
				{
					var Src = GetNodeLoc(Ch[0]);
					ProcessIndexMoves(Dst, Src);
					ConvertData(Dst, Src, Data.TempData, x86ExecutorType.General, DstType, SrcType);
				}
			}

			else
			{
				var Dst = GetNodeLoc(Ch[0]);
				var Src = Ch.Length > 1 ? GetNodeLoc(Ch[1]) : null;
				GetOpBaseCode_NonFloat(OpNode, Dst, Src);
			}
		}
	}
}