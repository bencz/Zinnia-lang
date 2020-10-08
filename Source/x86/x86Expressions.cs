using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Numerics;

namespace Zinnia.x86
{
	[Flags]
	public enum x86ExecutorType : byte
	{
		None = 0,
		General = 1,
		FPU = 2,
		SSE = 4,
		All = General | FPU | SSE,
	}

	public enum x86TypeKind : byte
	{
		Unknown,
		Signed,
		Unsigned,
		Float,
		SignedVector,
		UnsignedVector,
		FloatVector,
	}

	[Flags]
	public enum x86DataLocationType : byte
	{
		None = 0,
		Memory = 1,
		General = 2,
		OneByte = 4,
		SSEReg = 8,

		OneByteGeneral = General | OneByte,
		GRegMem = General | Memory,
		SSEMem = SSEReg | Memory,
		All = General | SSEReg | Memory,
	}

	public enum x86Operator : byte
	{
		Unknown,
		FloatZeroTesting,
		BitTestZero,
		BitTestNonZero,
		Swap,

		IsNan,
		IsNotNan,
		IsInfinite,
		IsFinite,

		IsCarryFlagSet,
		IsCarryFlagZero,
		IsParityFlagSet,
		IsParityFlagZero,
		IsZeroFlagSet,
		IsZeroFlagZero,
		IsSignFlagSet,
		IsSignFlagZero,
		IsOverflowFlagSet,
		IsOverflowFlagZero,

		Abs,
		Sqrt,
		Sin,
		Cos,
		Tan,
		Atan,
		Atan2,

		RSqrt,
		Rcp,
		RRcp,
		AndNot,
		Min,
		Max,

		Round,
		Floor,
		Ceiling,
		Truncate,
	}

	[Flags]
	public enum x86NodeFlags : ushort
	{
		None = 0,
		IdentifierByRef = 1,
		AllocateLocation = 2,
		SaveChResults = 4,
		IndexMemberNode = 8,
		RefIndexMemberNode = 16,

		LocationProcessed = 32,
		IndicesProcessed = 64,

		// Assign OpNode
		CanUseAssignVar_Calced = 256,
		CanUseAssignVar = 512,

		// RootNode
		AllocateTempData = 1024,
		EnableUsedData = 2048,
		LinkedNodesUsed = 4096,
		NonMemoryUsed = 8192,
		NeedAllocations = 16384,
		UseExistingLocs = 32768,

		//
		FlagsForRefIdentifier = IdentifierByRef |
			IndexMemberNode | RefIndexMemberNode,

		LeftByReset = IndicesProcessed,
	}

	public enum x86SameAllocationAsType : byte
	{
		None,
		Specified,
		All,
	}

	public struct x86StoredDataType
	{
		public x86TypeKind TypeKind;
		public int Precision;

		public x86StoredDataType(x86TypeKind TypeKind, int Precision)
		{
			this.TypeKind = TypeKind;
			this.Precision = Precision;
		}

		public bool IsEquivalent(x86StoredDataType Other)
		{
			return TypeKind == Other.TypeKind && Precision == Other.Precision;
		}

		public bool CheckLocation(int Size)
		{
			if (TypeKind != x86TypeKind.Unknown)
			{
				if (x86Identifiers.IsVectorTypeKind(TypeKind))
				{
					if (Size % 16 != 0) return false;
					return Size % Precision == 0;
				}
				else
				{
					return Size == Precision;
				}
			}

			return true;
		}

		public bool CheckLocation(x86DataLocation Location)
		{
			if (Location is x86SSERegLocation) return true;
			return CheckLocation(Location.Size);
		}

		public x86StoredDataType GetPart(int Offset, int Size)
		{
			if (TypeKind == x86TypeKind.Unknown || Size < Precision || Offset % Precision != 0)
				return new x86StoredDataType();

			return this;
		}

		public x86StoredDataType SelfOrDefault(int Size)
		{
			var Ret = this;
			x86Identifiers.GetDefaultStoredDataType(ref Ret, Size);
			return Ret;
		}

		public x86StoredDataType SelfOrDefault(x86DataLocation Location)
		{
			return SelfOrDefault(Location.Size);
		}

		public x86StoredDataType VectorType()
		{
			var VecTypeKind = x86Identifiers.GetVectorTypeKind(TypeKind);
			return new x86StoredDataType(VecTypeKind, Precision);
		}

		public x86StoredDataType ScalarType()
		{
			var ScalarTypeKind = x86Identifiers.GetScalarTypeKind(TypeKind);
			return new x86StoredDataType(ScalarTypeKind, Precision);
		}
	}

	public class x86NodeData
	{
		public byte FPUItemsOnStack;
		public byte UsedFPUStack;
		public byte Scale = 1;
		public int Displacement;
		public x86SameAllocationAsType SameAllocationAsType;
		public byte SameAllocationAs;
		public x86NodeFlags Flags;
		public x86DataLocationType DataCalcPos;
		public x86DataLocation PreferredOutput;
		public x86DataList UsedDataBySelf;
		public x86DataList UsedData;
		public x86DataList TempCantBe;
		public x86DataList PreAllocate;
		public x86Operator Operator;
		public x86DataProperties Properties;
		public x86NeededTempData NeededTempByPlugin;
		public x86NeededTempData NeededTempData;
		public x86TemporaryData TempData;
		public x86DataLocation Output;
		public x86DataAllocator Allocator;
		public int ExecutionNumber;

		// Call Op
		public int ParameterBytes;

		// Shift Op
		public int OriginalShiftSize;

		// Float globals
		public Variable NegateAbsBitmask;
		public Variable InfinityVariable;
		public Variable DefaultFPUControlWord;
		public Variable ControlWordForRounding;

		// Assign OpNode
		public byte DontUseCount;

		// Root Data
		public IdContainer Container;
		public x86DataList AllUsedData;
		public List<ExpressionNode> AllNodes;
		public x86DataList AllAllocated;
		public byte NumberOfFails;

		public void Reset()
		{
			FPUItemsOnStack = 0;
			UsedFPUStack = 0;
			SameAllocationAsType = x86SameAllocationAsType.None;
			Flags &= x86NodeFlags.LeftByReset;
			DataCalcPos = x86DataLocationType.None;
			PreferredOutput = null;
			UsedData = null;
			UsedDataBySelf = null;
			TempCantBe = null;
			PreAllocate = null;
			Properties = new x86DataProperties();
			NeededTempByPlugin = new x86NeededTempData();
			Output = null;

			ParameterBytes = 0;
			OriginalShiftSize = 0;
		}

		public x86DataLocation ExtractedOutput
		{
			get
			{
				var PostCalcedPos = Output as x86PostCalcedLocation;
				return PostCalcedPos == null ? Output : PostCalcedPos.Location;
			}
		}
	}

	[Flags]
	public enum x86LinkedNodeFlags
	{
		None = 0,
		OnlyUseInParent = 1,
		AllocateData = 2,
		LocationProcessed = 4,
		CreatedForCall = 8,
	}

	public class x86LinkedNodeData
	{
		public x86DataLocation Specified;
		public x86DataLocation Location;
		public x86LinkedNodeFlags Flags;

		public x86LinkedNodeData(x86DataLocation Specified = null, x86LinkedNodeFlags Flags = x86LinkedNodeFlags.None)
		{
			this.Specified = Specified;
			this.Location = null;
			this.Flags = Flags;
		}
	}

	public static class x86Identifiers
	{
		public static x86StoredDataType GetDefaultStoredDataType(int Size)
		{
			return new x86StoredDataType(x86TypeKind.Unsigned, Size);
		}

		public static void GetDefaultStoredDataType(ref x86StoredDataType StoredDataTypePart, int Size)
		{
			if (StoredDataTypePart.TypeKind == x86TypeKind.Unknown)
				StoredDataTypePart = GetDefaultStoredDataType(Size);
		}

		public static void GetDefaultStoredDataType(ref x86StoredDataType StoredDataTypePart, x86DataLocation Location)
		{
			GetDefaultStoredDataType(ref StoredDataTypePart, Location.Size);
		}

		public static bool IsScalarNonfloatTypeKind(x86TypeKind TypeKind)
		{
			return IsNonfloatTypeKind(TypeKind) && !IsVectorTypeKind(TypeKind);
		}

		public static void GetScalarStoredDataType(ref x86StoredDataType StoredDataType)
		{
			StoredDataType.TypeKind = GetScalarTypeKind(StoredDataType.TypeKind);
		}

		public static void GetVectorStoredDataType(ref x86StoredDataType StoredDataType)
		{
			StoredDataType.TypeKind = GetVectorTypeKind(StoredDataType.TypeKind);
		}

		public static x86StoredDataType GetScalarStoredDataType(x86StoredDataType StoredDataType)
		{
			StoredDataType.TypeKind = GetScalarTypeKind(StoredDataType.TypeKind);
			return StoredDataType;
		}

		public static x86StoredDataType GetVectorStoredDataType(x86StoredDataType StoredDataType)
		{
			StoredDataType.TypeKind = GetVectorTypeKind(StoredDataType.TypeKind);
			return StoredDataType;
		}

		public static x86TypeKind GetVectorTypeKind(x86TypeKind TypeKind)
		{
			if (TypeKind == x86TypeKind.Float) return x86TypeKind.FloatVector;
			if (TypeKind == x86TypeKind.Signed) return x86TypeKind.SignedVector;
			if (TypeKind == x86TypeKind.Unsigned) return x86TypeKind.UnsignedVector;
			return TypeKind;
		}

		public static x86TypeKind GetScalarTypeKind(x86TypeKind TypeKind)
		{
			if (TypeKind == x86TypeKind.FloatVector) return x86TypeKind.Float;
			if (TypeKind == x86TypeKind.SignedVector) return x86TypeKind.Signed;
			if (TypeKind == x86TypeKind.UnsignedVector) return x86TypeKind.Unsigned;
			return TypeKind;
		}

		public static bool IsVectorTypeKind(x86TypeKind TypeKind)
		{
			if (TypeKind == x86TypeKind.FloatVector) return true;
			if (TypeKind == x86TypeKind.SignedVector) return true;
			if (TypeKind == x86TypeKind.UnsignedVector) return true;
			return false;
		}

		public static bool IsNonfloatTypeKind(x86TypeKind TypeKind)
		{
			return IsSignedTypeKind(TypeKind) || IsUnsignedTypeKind(TypeKind);
		}

		public static bool IsSignedTypeKind(x86TypeKind TypeKind)
		{
			if (TypeKind == x86TypeKind.Signed) return true;
			if (TypeKind == x86TypeKind.SignedVector) return true;
			return false;
		}

		public static bool IsUnsignedTypeKind(x86TypeKind TypeKind)
		{
			if (TypeKind == x86TypeKind.Unsigned) return true;
			if (TypeKind == x86TypeKind.UnsignedVector) return true;
			return false;
		}

		public static bool IsFloatTypeKind(x86TypeKind TypeKind)
		{
			if (TypeKind == x86TypeKind.Float) return true;
			if (TypeKind == x86TypeKind.FloatVector) return true;
			return false;
		}

		public static string GetSSETypeString(Identifier Identifier)
		{
			var StoredDataType = GetStoredDataType(Identifier);
			return GetSSETypeString(StoredDataType);
		}

		public static string GetSSETypeString(x86StoredDataType StoredDataType)
		{
			var Kind = StoredDataType.TypeKind;
			var Precision = StoredDataType.Precision;

			if (Precision == -1 || Kind == x86TypeKind.Unknown)
				throw new ArgumentException(null, "Identifier");

			if (Kind == x86TypeKind.Float)
			{
				if (Precision == 4) return "ss";
				else if (Precision == 8) return "sd";
				else throw new ApplicationException();
			}
			else if (Kind == x86TypeKind.FloatVector)
			{
				if (Precision == 4) return "ps";
				else if (Precision == 8) return "pd";
				else throw new ApplicationException();
			}
			else if (IsNonfloatTypeKind(Kind))
			{
				if (Precision == 1) return "b";
				else if (Precision == 2) return "w";
				else if (Precision == 4) return "d";
				else if (Precision == 8) return "q";
				else if (Precision == 16) return "dq";
				else throw new ApplicationException();
			}
			else
			{
				throw new ApplicationException();
			}
		}

		public static int GetPrecision(Identifier Identifier)
		{
			if (Identifier.RealId is TupleType)
			{
				var Tuple = Identifier.RealId as TupleType;
				var Members = Tuple.StructuredScope.IdentifierList;
				var Size = -1;

				for (var i = 0; i < Members.Count; i++)
				{
					var MemberType = Members[i].TypeOfSelf;
					var RMemberType = MemberType.RealId as Type;

					if (Size == -1) Size = RMemberType.Size;
					else if (Size != RMemberType.Size) return -1;
				}

				return Size;
			}

			return (Identifier.RealId as Type).Size;
		}

		public static x86TypeKind GetScalarTypeKind(Identifier Identifier)
		{
			if (!(Identifier.RealId is StructType))
			{
				if (Identifier.RealId is EnumType)
				{
					var Enum = Identifier.RealId as EnumType;
					Identifier = Enum.Children[0];
				}

				if (Identifier.RealId is FloatType) return x86TypeKind.Float;
				else if (Identifier.RealId is SignedType) return x86TypeKind.Signed;
				else return x86TypeKind.Unsigned;
			}

			return x86TypeKind.Unknown;
		}

		public static x86StoredDataType GetStoredDataType(Identifier Identifier)
		{
			var Precision = GetPrecision(Identifier);
			var Kind = GetTypeKind(Identifier, true);

			if (Precision == -1 || Kind == x86TypeKind.Unknown)
				return new x86StoredDataType();
			else return new x86StoredDataType(Kind, Precision);
		}

		public static x86TypeKind GetTypeKind(Identifier Identifier, bool DontCheckPrecision = false)
		{
			var State = Identifier.Container.State;
			var Arch = State.Arch as x86Architecture;

			if (Identifier.RealId is TupleType && false)
			{
				if (!DontCheckPrecision && GetPrecision(Identifier) == -1)
					return x86TypeKind.Unknown;

				var Tuple = Identifier.RealId as TupleType;
				var Members = Tuple.StructuredScope.IdentifierList;
				var Kind = x86TypeKind.Unknown;

				for (var i = 0; i < Members.Count; i++)
				{
					var MemberType = Members[i].TypeOfSelf;
					if (Kind == x86TypeKind.Unknown)
					{
						Kind = GetScalarTypeKind(MemberType);
						if (Kind == x86TypeKind.Unknown) return x86TypeKind.Unknown;
					}
					else
					{
						if (Kind != GetScalarTypeKind(MemberType))
							return x86TypeKind.Unknown;
					}
				}

				return GetVectorTypeKind(Kind);
			}

			return GetScalarTypeKind(Identifier);
		}

		public static x86DataLocationType GetPossibleLocations(Identifier Type)
		{
			//return x86DataLocType.Memory;
			var State = Type.Container.State;
			var Arch = State.Arch as x86Architecture;

			var RType = Type.RealId as Type;
			if (RType is EnumType) RType = (RType as EnumType).TypeOfValues;

			if (RType is FloatType)
			{
				if (Arch.FloatingPointMode == x86FloatingPointMode.SSE) return x86DataLocationType.SSEMem;
				else if (Arch.FloatingPointMode == x86FloatingPointMode.FPU) return x86DataLocationType.Memory;
				else throw new NotImplementedException();
			}

			if (RType is StructType || RType is NonrefArrayType) return x86DataLocationType.Memory;
			else if (RType.Size == 1) return x86DataLocationType.OneByteGeneral | x86DataLocationType.Memory;
			else return x86DataLocationType.GRegMem;
		}

		public static x86DataLocationType GetPossibleLocations(Identifier Id, x86IdentifierData Data)
		{
			if ((Data.Flags & x86IdentifierFlags.CantBeInReg) != 0) return x86DataLocationType.Memory;
			else return GetPossibleLocations(Id.TypeOfSelf.RealId as Type);
		}

		public static int GetMemberCount(Identifier Type)
		{
			if (Type.RealId is NonrefArrayType)
			{
				var ArrType = Type.RealId as NonrefArrayType;
				return ArrType.Length;
			}
			else if (Type.RealId is StructType)
			{
				var ValueType = Type.RealId as StructType;
				var Members = ValueType.StructuredScope.IdentifierList;
				return Members.Count;
			}
			else
			{
				throw new InvalidOperationException();
			}
		}

		public static Identifier GetMemberType(Identifier Type, int Index)
		{
			if (Type.RealId is NonrefArrayType)
			{
				var ArrType = Type.RealId as NonrefArrayType;
				return ArrType.TypeOfValues;
			}
			else if (Type.RealId is StructType)
			{
				var ValueType = Type.RealId as StructType;
				var Members = ValueType.StructuredScope.IdentifierList;
				var Var = Members[Index] as MemberVariable;
				return Var.TypeOfSelf;
			}
			else
			{
				throw new InvalidOperationException();
			}
		}

		public static bool IsMovableMember(Identifier Type, int Index)
		{
			if (Type.RealId is NonrefArrayType)
			{
				return true;
			}
			else if (Type.RealId is StructType)
			{
				var ValueType = Type.RealId as StructType;
				var Members = ValueType.StructuredScope.IdentifierList;
				return Members[Index] as MemberVariable != null;
			}
			else
			{
				throw new InvalidOperationException();
			}
		}

		public static x86DataLocation GetMember(x86DataLocation Position, Identifier Type, int Index)
		{
			if (Type.RealId is NonrefArrayType)
			{
				var ArrType = Type.RealId as NonrefArrayType;
				return Position.GetPart(ArrType.ElementSize * Index, ArrType.TypeOfValues.Size);
			}
			else if (Type.RealId is StructType)
			{
				var ValueType = Type.RealId as StructType;
				var Members = ValueType.StructuredScope.IdentifierList;
				var Var = Members[Index] as MemberVariable;
				if (Var == null) throw new ApplicationException("Not movable");

				var MemberType = Var.TypeOfSelf.RealId as Type;
				return Position.GetPart(Var.Offset, MemberType.Size);
			}
			else
			{
				throw new InvalidOperationException();
			}
		}
	}

	public static class x86Expressions
	{
		public static bool NeedLoadFloat(ExpressionNode Node, bool IsDst)
		{
			if (IsImmediateValue(Node)) return true;

			if (IsDst)
			{
				if (Node is IdExpressionNode) return true;

				var OpNode = Node as OpExpressionNode;
				if (OpNode == null) return false;

				var Op = OpNode.Operator;
				if (Op == Operator.Member || Op == Operator.Index)
					return true;
			}

			return false;
		}

		public static x86ConstLocation GetNullLocation(x86Architecture Arch, IdContainer Container)
		{
			var Value = new IntegerValue(0);
			var Type = Container.GlobalContainer.CommonIds.VoidPtr;
			return new x86ConstLocation(Arch, Value, Type, 0, Type.Size);
		}

		public static bool IsImmediateValue(ExpressionNode Node)
		{
			return Node is ConstExpressionNode || Node is DataPointerNode ||
				Node is LabelExpressionNode;
		}

		public static x86DataProperties GetDataProperties(ExpressionNode Node)
		{
			var Data = Node.Data.Get<x86NodeData>();
			if ((Data.Flags & x86NodeFlags.AllocateLocation) == 0)
				throw new InvalidOperationException();

			var Type = Node.Type.RealId as Type;
			return new x86DataProperties(Type.Size, Type.Align, Data.DataCalcPos, Data.TempCantBe);
		}

		public static x86DataProperties GetDataProperties(Identifier Type)
		{
			var RType = Type.RealId as Type;
			return new x86DataProperties(RType.Size, RType.Align,
				x86Identifiers.GetPossibleLocations(Type), null);
		}

		public static bool IsCondition(OpExpressionNode Node)
		{
			if (Node.Operator == Operator.Unknown)
			{
				var Data = Node.Data.Get<x86NodeData>();
				return IsConditionOp(Data.Operator);
			}
			else
			{
				return Operators.IsBoolRet(Node.Operator);
			}
		}

		public static bool IsCondition(ExpressionNode Node)
		{
			var OpNode = Node as OpExpressionNode;
			return OpNode == null ? false : IsCondition(OpNode);
		}

		public static bool IsBitTestOp(x86Operator Op)
		{
			return Op == x86Operator.BitTestNonZero || Op == x86Operator.BitTestZero;
		}
		public static bool IsConditionOp(x86Operator Op)
		{
			return IsBitTestOp(Op) || IsFlagOp(Op) || Op == x86Operator.IsFinite || 
				Op == x86Operator.IsInfinite || Op == x86Operator.IsNotNan || Op == x86Operator.IsNan;
		}

		public static bool IsFlagOp(x86Operator Op)
		{
			return Op == x86Operator.IsCarryFlagSet || Op == x86Operator.IsCarryFlagZero ||
				Op == x86Operator.IsParityFlagSet || Op == x86Operator.IsParityFlagZero ||
				Op == x86Operator.IsZeroFlagSet || Op == x86Operator.IsZeroFlagZero ||
				Op == x86Operator.IsSignFlagSet || Op == x86Operator.IsSignFlagZero ||
				Op == x86Operator.IsOverflowFlagSet || Op == x86Operator.IsOverflowFlagZero;
		}

		public static bool IsRoundOp(x86Operator Op)
		{
			return Op == x86Operator.Round || Op == x86Operator.Floor ||
				Op == x86Operator.Ceiling || Op == x86Operator.Truncate;
		}

		public static bool IsMinMaxOp(x86Operator Op)
		{
			return Op == x86Operator.Max || Op == x86Operator.Min;
		}

		public static bool IsTwoOperandSSEOp(x86Operator Op)
		{
			return IsMinMaxOp(Op);
		}

		public static bool IsTwoOperandNumberOp(Operator Op, x86Operator x86Op)
		{
			return Operators.IsRelEquality(Op) || Operators.IsBitArithmShift(Op) || IsTwoOperandSSEOp(x86Op);
		}

		public static x86Operator Negate(x86Operator Op)
		{
			if (Op == x86Operator.BitTestZero) return x86Operator.BitTestNonZero;
			else if (Op == x86Operator.BitTestNonZero) return x86Operator.BitTestZero;

			else if (Op == x86Operator.IsNan) return x86Operator.IsNotNan;
			else if (Op == x86Operator.IsNotNan) return x86Operator.IsNan;
			else if (Op == x86Operator.IsInfinite) return x86Operator.IsFinite;
			else if (Op == x86Operator.IsFinite) return x86Operator.IsInfinite;

			else if (Op == x86Operator.IsCarryFlagSet) return x86Operator.IsCarryFlagZero;
			else if (Op == x86Operator.IsCarryFlagZero) return x86Operator.IsCarryFlagSet;
			else if (Op == x86Operator.IsParityFlagSet) return x86Operator.IsParityFlagZero;
			else if (Op == x86Operator.IsParityFlagZero) return x86Operator.IsParityFlagSet;
			else if (Op == x86Operator.IsZeroFlagSet) return x86Operator.IsZeroFlagZero;
			else if (Op == x86Operator.IsZeroFlagZero) return x86Operator.IsZeroFlagSet;
			else if (Op == x86Operator.IsSignFlagSet) return x86Operator.IsSignFlagZero;
			else if (Op == x86Operator.IsSignFlagZero) return x86Operator.IsSignFlagSet;
			else if (Op == x86Operator.IsOverflowFlagSet) return x86Operator.IsOverflowFlagZero;
			else if (Op == x86Operator.IsOverflowFlagZero) return x86Operator.IsOverflowFlagSet;
			else throw new ApplicationException();
		}

		public static void Negate(ref Operator Op, ref x86Operator Op2)
		{
			if (Op == Operator.Unknown) Op2 = Negate(Op2);
			else Op = Operators.Negate(Op);
		}

		public static bool NeedReturnPointer(Identifier Type)
		{
			return Type.RealId is NonrefArrayType || Type.RealId is StructType;
		}

		public static bool NeedReturnPointer(ExpressionNode CallNode)
		{
			var OpNode = CallNode as OpExpressionNode;
			var Op = OpNode.Operator;
			var Ch = OpNode.Children;

			var FuncType = Ch[0].Type.RealId as TypeOfFunction;
			return NeedReturnPointer(FuncType.Children[0]);
		}

		public static bool NeedSelfParameter(ExpressionNode CallNode)
		{
			var OpNode = CallNode as OpExpressionNode;
			var Op = OpNode.Operator;
			var Ch = OpNode.Children;

			if (Expressions.IsSelfSpecified(Ch[0]))
				return true;

			if (Ch[0] is IdExpressionNode)
			{
				var IdCh0 = Ch[0] as IdExpressionNode;
				if (IdCh0.Identifier is Constructor && Op == Operator.NewObject)
					return true;
			}

			return false;
		}

		public static bool NeedsInstructions(ExpressionNode Node)
		{
			if (Node.LinkedNodes.Count > 0)
				return true;

			if (Node is IdExpressionNode || Node is ConstExpressionNode || Node is LinkingNode)
			{
				return false;
			}
			else if (Node is OpExpressionNode)
			{
				var OpNode = Node as OpExpressionNode;
				var Op = OpNode.Operator;
				var Ch = OpNode.Children;

				if (Op == Operator.Index)
				{
					return NeedsInstructions(Ch[0]) ||
						   NeedsInstructions(Ch[1]);
				}
				else if (Op == Operator.Member)
				{
					return NeedsInstructions(Ch[0]);
				}
			}

			return true;
		}

		public static x86DataLocation GetLocation(x86Architecture Arch, ExpressionNode Node)
		{
			var Data = Node.Data.Get<x86NodeData>();
			var Type = Node.Type.RealId as Type;
			var Size = Type.Size;

			if (Data != null && Data.Output != null)
			{
				if (Data.Output is x86PostCalcedLocation)
					return Data.ExtractedOutput;

				return Data.Output;
			}

			else if (Node is OpExpressionNode)
			{
				var OpNode = Node as OpExpressionNode;
				var Op = OpNode.Operator;
				var Ch = OpNode.Children;

				if (Op == Operator.Assignment)
				{
					return GetLocation(Arch, Ch[0]);
				}

				else if (Op == Operator.Index)
				{
					var Address = GetLocation(Arch, Ch[0]);
					var Offset = GetLocation(Arch, Ch[1]);
					if (Address == null || Offset == null)
						return null;

					var Ch0Type = Ch[0].Type.RealId;
					var AddressTempGReg = Data.TempData.GetGRegister(x86TempGRegPurposeType.Index, 0);
					var OffsetTempGReg = Data.TempData.GetGRegister(x86TempGRegPurposeType.Index, 1);

					if (Ch0Type is PointerType)
					{
						return GetIndexerPosition(Arch, Address, AddressTempGReg, Offset, 
							OffsetTempGReg, Size, Data.Displacement, Data.Scale);
					}
					else
					{
						throw new ApplicationException();
					}
				}

				else if (Op == Operator.Member)
				{
					var IdCh1 = Ch[1] as IdExpressionNode;
					if (IdCh1 == null) throw new ApplicationException();

					if (IdCh1.Identifier is Function && !IsVirtualMember(Node))
						return GetDefaultIdLocation(Arch, IdCh1.Identifier);

					var SrcPos = GetLocation(Arch, Ch[0]);
					if (SrcPos == null) return null;

					var Ch0Type = Ch[0].Type.RealId as Type;
					var AddressTempGReg = Data.TempData.GetGRegister(x86TempGRegPurposeType.Index, 0);
					if ((Ch0Type.TypeFlags & TypeFlags.ReferenceValue) != 0)
						SrcPos = GetIndexerPosition(Arch, SrcPos, AddressTempGReg, null, null, Ch0Type.InstanceSize);

					if (SrcPos == null)
						return null;

#warning WARNING, temp solution to allow ZinniaCore compilation
					SrcPos = SrcPos.GetPart(0);
					SrcPos.Size = int.MaxValue;

					var Splitable = SrcPos as x86SplittableLocation;
					if (Splitable == null) throw new ApplicationException();

					if (IdCh1.Identifier is MemberVariable)
					{
						var MemVar = IdCh1.Identifier as MemberVariable;
						return Splitable.GetPart(MemVar.Offset, Size);
					}
					else if (IdCh1.Identifier is MemberFunction)
					{
						var MemFunc = IdCh1.Identifier as MemberFunction;
						var Ptrs = Splitable.GetPart(0, Size);

						var RegSize = Arch.RegSize;
						var Global = MemFunc.Container.GlobalContainer;

						var OffsetValue = new IntegerValue(MemFunc.VirtualIndex * RegSize);
						var OffsetType = Global.CommonIds.GetIdentifier(typeof(SignedType), RegSize);
						var Offset = new x86ConstLocation(Arch, OffsetValue, OffsetType, 0, RegSize);
						return GetIndexerPosition(Arch, Ptrs, AddressTempGReg, Offset, null, RegSize);
					}

					throw new ApplicationException();
				}

				else if (Op == Operator.Unknown)
				{
					if (Data.Operator == x86Operator.Swap)
					{
						var Locations = new x86DataLocation[2];
						Locations[0] = GetLocation(Arch, Ch[0]);
						Locations[1] = GetLocation(Arch, Ch[1]);

						if (Locations[0] == null || Locations[1] == null)
							return null;

						return new x86MultiLocation(Arch, Size, Locations);
					}
				}
			}

			else if (Node is ConstExpressionNode)
			{
				var ConstNode = Node as ConstExpressionNode;
				return new x86ConstLocation(Arch, ConstNode, 0, Size);
			}

			else if (Node is IdExpressionNode)
			{
				var IdNode = Node as IdExpressionNode;
				var Id = IdNode.Identifier;
				
				if (!(Id is LocalVariable))
					return GetDefaultIdLocation(Arch, Id);

				var IdData = Id.Data.Get<x86IdentifierData>();
				var IdPos = IdData.Location;
				if (IdPos == null) return null;

				if ((Data.Flags & x86NodeFlags.IdentifierByRef) != 0)
				{
					Type = Id.Container.GlobalContainer.CommonIds.GetIdentifier(typeof(SignedType), Arch.RegSize);
					var Offset = new x86ConstLocation(Arch, new IntegerValue(0), Type, 0, Arch.RegSize);
					var AddressTempGReg = Data.TempData.GetGRegister(x86TempGRegPurposeType.Index, -1);
					return GetIndexerPosition(Arch, IdPos, AddressTempGReg, Offset, null, Size);
				}

				return IdPos;
			}
			
			else if (Node is DataPointerNode)
			{
				var IdDescNode = Node as DataPointerNode;
				if (IdDescNode.DescPointerType == DataPointerType.Assembly)
					return new x86NamedLabelPosition(Arch, IdDescNode.Assembly.DescLabel);
				else if (IdDescNode.DescPointerType == DataPointerType.Identifier)
					return GetDescLocation(Arch, IdDescNode.Id);
				else if (IdDescNode.DescPointerType == DataPointerType.IncBin)
					return new x86NamedLabelPosition(Arch, IdDescNode.IncBin.Label);
				else
					throw new NotImplementedException();
			}

			else if (Node is LabelExpressionNode)
			{
				var LabelNode = Node as LabelExpressionNode;
				return new x86NamedLabelPosition(Arch, LabelNode.Label);
			}

			return null;
		}

		public static bool IsVirtualMember(ExpressionNode Node)
		{
			var Op = Expressions.GetOperator(Node);
			if (Op != Operator.Member || (Node.Flags & ExpressionFlags.DisableVirtualMember) != 0)
				return false;

			var Ch = Node.Children;
			var IdCh0 = Ch[0] as IdExpressionNode;
			var IdCh1 = Ch[1] as IdExpressionNode;

			if ((IdCh1.Identifier.Flags & IdentifierFlags.Virtual) != 0)
			{
				if (!(IdCh1.Identifier is Constructor) && Ch[0].Type.UnderlyingClassOrRealId is ClassType &&
					!(IdCh0 != null && IdCh0.Identifier is BaseVariable))
				{
					return true;
				}
			}

			return false;
		}

		public static x86DataLocation GetLocation(x86Architecture Arch, Identifier Id)
		{
			var IdData = Id.Data.Get<x86IdentifierData>();
			var IdPos = IdData == null ? null : IdData.Location;
			if (IdPos != null) return IdPos;
			return GetDefaultIdLocation(Arch, Id);
		}

		static x86DataLocation GetDefaultIdLocation(x86Architecture Arch, Identifier Id)
		{
			Id = Id.RealId;
			if (Id is Type)
			{
				return GetDescLocation(Arch, Id);
			}
			else if (Id is Function)
			{
				return new x86NamedLabelPosition(Arch, Id.AssemblyName);
			}
			else if (Id is GlobalVariable)
			{
				var Label = new x86NamedLabelPosition(Arch, Id.AssemblyName);
                var Type = Id.TypeOfSelf.RealId as Type;
                return new x86IndexLocation(Arch, 0, Type.Size, Label);
			}
			else
			{
				throw new NotImplementedException();
			}
		}

		private static x86DataLocation GetDescLocation(x86Architecture Arch, Identifier Id)
		{
			Id = Id.UnderlyingStructureOrSelf;
			var Assembly = Id.Container.AssemblyScope.Assembly;

			if (Id.DescPosition == -1)
			{
				var Label = Assembly.DescLabel + " + ?? (" + Id.AssemblyName + ")";
				return new x86NamedLabelPosition(Arch, Label);
			}
			else
			{
				var Label = Assembly.DescLabel + " + " + (Id.DescPosition + Arch.RegSize * 2);
				return new x86NamedLabelPosition(Arch, Label);
			}
		}

		private static x86MemoryLocation GetIndexerPosition(x86Architecture Arch, x86DataLocation Address, 
			x86GRegLocation AddressTempGReg, x86DataLocation Offset, x86GRegLocation OffsetTempGReg,
			int Size, int Displacement = 0, byte Scale = 1)
		{
			if (Offset != null && Address.Size != Offset.Size) 
				throw new ApplicationException();

			var Ret = new x86IndexLocation(Arch, 0, Size, null);
			if (!Ret.Add(Address, AddressTempGReg))
				return null;

			if (Offset != null && !Ret.Add(Offset, OffsetTempGReg, Scale))
				return null;

			Ret.Offset += Displacement;
			return Ret;
		}

		public static bool SamePosition(x86Architecture Arch, ExpressionNode Self, x86DataLocation N, 
			x86OverlappingMode Mode = x86OverlappingMode.Whole)
		{
			var S = x86Expressions.GetLocation(Arch, Self);
			return S == null ? false : S.Compare(N, Mode);
		}

		public static bool SamePosition(x86Architecture Arch, ExpressionNode Self, ExpressionNode Node,
			x86OverlappingMode Mode = x86OverlappingMode.Whole)
		{
			var S = x86Expressions.GetLocation(Arch, Self);
			var N = x86Expressions.GetLocation(Arch, Node);
			return S == null || N == null ? false : S.Compare(N, Mode);
		}
	}
}