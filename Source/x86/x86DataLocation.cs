using System;
using System.Collections.Generic;
using System.Numerics;
using Zinnia.Base;

namespace Zinnia.x86;

public static class x86DataLocations
{
    public static x86DataLocation GetPartOrSelf(x86DataLocation Location, int Offset, int Size = -1)
    {
        if (Size == -1) Size = Location.Size - Offset;
        if (Offset == 0 && Location.Size == Size)
            return Location;

        if (Location is x86MultiLocation)
        {
            var MLocation = Location as x86MultiLocation;

            var Failed = false;
            var HasOtherParts = false;
            x86DataLocation Ret = null;

            MLocation.GetPartHelper(Offset, Size, (Part, PartOffset, PartSize) =>
            {
                var RetPart = GetPartOrSelf(Part, PartOffset, PartSize);
                if (RetPart == null || Failed)
                {
                    Failed = true;
                    return;
                }

                if (PartSize == Size) Ret = RetPart;
                else HasOtherParts = true;
            });

            if (Failed) return null;
            if (!HasOtherParts) return Ret;
        }

        return Location.GetPart(Offset, Size);
    }

    public static x86DataLocation CutDownFromEnd(x86DataLocation Location, int Size)
    {
        if (Size == 0) return null;
        if (Location.Size < Size)
            throw new ArgumentOutOfRangeException(null, "Size");

        if (Location.Size == Size) return Location;
        return Location.GetPart(Location.Size - Size, Size);
    }

    public static x86DataLocation CutDownFromEnd(ref x86DataLocation Location, int Size)
    {
        if (Size == 0) return null;
        if (Location.Size < Size)
            throw new ArgumentOutOfRangeException(null, "Size");

        if (Location.Size == Size)
        {
            var Ret = Location;
            Location = null;
            return Ret;
        }
        else
        {
            var Ret = Location.GetPart(Location.Size - Size, Size);
            Location = Location.GetPart(0, Location.Size - Size);
            return Ret;
        }
    }

    public static x86DataLocation CutDownFromStart(x86DataLocation Location, int Size)
    {
        if (Size == 0) return null;
        if (Location.Size < Size)
            throw new ArgumentOutOfRangeException(null, "Size");

        if (Location.Size == Size) return Location;
        return Location.GetPart(0, Size);
    }

    public static x86DataLocation CutDownFromStart(ref x86DataLocation Location, int Size)
    {
        if (Size == 0) return null;
        if (Location.Size < Size)
            throw new ArgumentOutOfRangeException(null, "Size");

        if (Location.Size == Size)
        {
            var Ret = Location;
            Location = null;
            return Ret;
        }
        else
        {
            var Ret = Location.GetPart(0, Size);
            Location = Location.GetPart(Size);
            return Ret;
        }
    }

    public static void SplitByPow2Size(ref x86DataLocation Location, int MinSize, int MaxSize,
        x86StoredDataType StoredDataType, Action<x86DataLocation, x86StoredDataType> Func)
    {
        x86DataLocation Ret = null;

        SplitByPow2Size(Location, MinSize, MaxSize, StoredDataType,
            (LocationPart, StoredDataTypePart) =>
            {
                if (LocationPart.Size >= MinSize)
                    Func(LocationPart, StoredDataTypePart);
                else Ret = LocationPart;
            }
        );

        Location = Ret;
    }

    public static void SplitByPow2Size(ref x86DataLocation Dst, ref x86DataLocation Src,
        int MinSize, int MaxSize, x86StoredDataType StoredDataType,
        Action<x86DataLocation, x86DataLocation, x86StoredDataType> Func)
    {
        x86DataLocation RetDst = null;
        x86DataLocation RetSrc = null;

        SplitByPow2Size(Dst, Src, MinSize, MaxSize, StoredDataType,
            (DstPart, SrcPart, StoredDataTypePart) =>
            {
                if (DstPart.Size >= MinSize)
                {
                    Func(DstPart, SrcPart, StoredDataTypePart);
                }
                else
                {
                    RetDst = DstPart;
                    RetSrc = SrcPart;
                }
            }
        );

        Dst = RetDst;
        Src = RetSrc;
    }

    public static void SplitByPow2Size(x86DataLocation Location, int MinSize, int MaxSize,
        x86StoredDataType StoredDataType, Action<x86DataLocation, x86StoredDataType> Func)
    {
        SplitBySize(Location, MaxSize, StoredDataType,
            (LocationPart, StoredDataTypePart) =>
            {
                var SizeLeft = LocationPart.Size;
                var Offset = 0;

                for (var s = SizeLeft; s >= MinSize; s /= 2)
                {
                    var LocationParts = LocationPart.GetPart(Offset, s);
                    if (LocationParts == null) throw new ApplicationException();

                    var StoredDataTypeParts = StoredDataTypePart.GetPart(0, s);
                    Func(LocationPart, StoredDataTypeParts);

                    Offset += s;
                    SizeLeft -= s;

                    if (SizeLeft == 0)
                        return;
                }

                if (SizeLeft > 0)
                {
                    var LocationLeft = LocationPart.GetPart(Offset, SizeLeft);
                    if (LocationLeft == null) throw new ApplicationException();

                    var StoredDataTypeLeft = StoredDataTypePart.GetPart(0, SizeLeft);
                    Func(LocationLeft, StoredDataTypeLeft);
                }
            }
        );
    }

    public static void SplitByPow2Size(x86DataLocation Dst, x86DataLocation Src,
        int MinSize, int MaxSize, x86StoredDataType StoredDataType,
        Action<x86DataLocation, x86DataLocation, x86StoredDataType> Func)
    {
        SplitBySize(Dst, Src, MaxSize, StoredDataType,
            (DstPart, SrcPart, StoredDataTypePart) =>
            {
                var SizeLeft = DstPart.Size;
                var Offset = 0;

                for (var s = SizeLeft; s >= MinSize; s /= 2)
                {
                    var DstParts = GetPartOrSelf(DstPart, Offset, s);
                    var SrcParts = GetPartOrSelf(SrcPart, Offset, s);
                    if (DstParts == null || SrcParts == null)
                        throw new ApplicationException();

                    var StoredDataTypeParts = StoredDataTypePart.GetPart(0, s);
                    Func(DstParts, SrcParts, StoredDataTypeParts);

                    Offset += s;
                    SizeLeft -= s;

                    if (SizeLeft == 0)
                        return;
                }

                if (SizeLeft > 0)
                {
                    var DstLeft = GetPartOrSelf(DstPart, Offset, SizeLeft);
                    var SrcLeft = GetPartOrSelf(SrcPart, Offset, SizeLeft);
                    if (DstLeft == null || SrcLeft == null)
                        throw new ApplicationException();

                    var StoredDataTypeLeft = StoredDataTypePart.GetPart(0, SizeLeft);
                    Func(DstLeft, SrcLeft, StoredDataTypeLeft);
                }
            }
        );
    }

    public static void SplitBySize(x86DataLocation Dst, x86DataLocation Src, int DstSize, int SrcSize,
        x86StoredDataType ToStoredDataType, x86StoredDataType FromStoredDataType,
        Action<x86DataLocation, x86DataLocation, x86StoredDataType, x86StoredDataType> Func)
    {
        if (Dst.Size <= DstSize && Src.Size <= SrcSize)
        {
            Func(Dst, Src, ToStoredDataType, FromStoredDataType);
        }
        else
        {
            var SplittedDst = Split(Dst, DstSize);
            var SplittedSrc = Split(Src, SrcSize);
            if (SplittedDst.Length != SplittedSrc.Length)
                throw new ApplicationException();

            for (var i = 0; i < SplittedDst.Length; i++)
            {
                if (SplittedDst[i] == null || SplittedSrc[i] == null)
                    throw new ApplicationException();

                var ToStoredDataTypePart = ToStoredDataType.GetPart(i * DstSize, DstSize);
                var FromStoredDataTypePart = FromStoredDataType.GetPart(i * SrcSize, SrcSize);
                Func(SplittedDst[i], SplittedSrc[i], ToStoredDataTypePart, FromStoredDataTypePart);
            }
        }
    }


    public static void SplitBySize(x86DataLocation Dst, x86DataLocation Src, int Size,
        x86StoredDataType StoredDataType, Action<x86DataLocation, x86DataLocation, x86StoredDataType> Func)
    {
        if (Dst.Size != Src.Size)
            throw new ArgumentException("Dst.Size != Src.Size");

        if (Dst.Size <= Size)
        {
            Func(Dst, Src, StoredDataType);
        }
        else
        {
            var SplittedDst = Split(Dst, Size);
            var SplittedSrc = Split(Src, Size);
            if (SplittedDst.Length != SplittedSrc.Length)
                throw new ApplicationException();

            for (var i = 0; i < SplittedDst.Length; i++)
            {
                if (SplittedDst[i] == null || SplittedSrc[i] == null)
                    throw new ApplicationException();

                var StoredDataTypePart = StoredDataType.GetPart(i * Size, Size);
                Func(SplittedDst[i], SplittedSrc[i], StoredDataTypePart);
            }
        }
    }

    public static void SplitBySize(x86DataLocation Location, int Size,
        x86StoredDataType StoredDataType, Action<x86DataLocation, x86StoredDataType> Func)
    {
        if (Location.Size <= Size)
        {
            Func(Location, StoredDataType);
        }
        else
        {
            var Parts = Split(Location, Size);
            for (var i = 0; i < Parts.Length; i++)
                Func(Parts[i], StoredDataType.GetPart(i * Size, Parts[i].Size));
        }
    }

    public static void SplitByMultiLocation(x86DataLocation Location,
        x86StoredDataType StoredDataType, Action<x86DataLocation, x86StoredDataType> Func)
    {
        if (Location is x86MultiLocation)
        {
            var MLocation = Location as x86MultiLocation;
            var Offset = 0;

            for (var i = 0; i < MLocation.Locations.Length; i++)
            {
                var LocPart = MLocation.Locations[i];
                var PartStoredDataType = StoredDataType.GetPart(Offset, LocPart.Size);
                Func(LocPart, PartStoredDataType);
                Offset += LocPart.Size;
            }
        }
        else
        {
            throw new ArgumentException();
        }
    }

    public static void SplitByMultiLocation(x86DataLocation Dst,
        x86DataLocation Src, x86StoredDataType StoredDataType,
        Action<x86DataLocation, x86DataLocation, x86StoredDataType> Func)
    {
        if (Dst is x86MultiLocation)
        {
            var MDst = Dst as x86MultiLocation;
            var Offset = 0;

            for (var i = 0; i < MDst.Locations.Length; i++)
            {
                var DstPart = MDst.Locations[i];
                var SrcPart = Src.GetPart(Offset, DstPart.Size);
                var PartStoredDataType = StoredDataType.GetPart(Offset, DstPart.Size);
                Func(DstPart, SrcPart, PartStoredDataType);
                Offset += DstPart.Size;
            }
        }
        else if (Src is x86MultiLocation)
        {
            var MSrc = Src as x86MultiLocation;
            var Offset = 0;

            for (var i = 0; i < MSrc.Locations.Length; i++)
            {
                var SrcPart = MSrc.Locations[i];
                var DstPart = Dst.GetPart(Offset, SrcPart.Size);
                var PartStoredDataType = StoredDataType.GetPart(Offset, SrcPart.Size);
                Func(DstPart, SrcPart, PartStoredDataType);
                Offset += SrcPart.Size;
            }
        }
        else
        {
            throw new ArgumentException();
        }
    }

    public static x86DataLocation GetZeroData(x86Architecture Arch, IdContainer Container, int Size)
    {
        var Global = Container.GlobalContainer;
        var Type = Global.CommonIds.GetIdentifier<UnsignedType>(Size);
        return new x86ConstLocation(Arch, new ZeroValue(), Type, 0, Size);
    }

    public static void Foreach(x86DataLocation Location, Action<x86DataLocation> Func)
    {
        if (Location is x86MultiLocation)
        {
            var MLoc = Location as x86MultiLocation;
            for (var i = 0; i < MLoc.Locations.Length; i++)
                Func(MLoc.Locations[i]);
        }
        else
        {
            Func(Location);
        }
    }

    public static bool Check(x86DataLocation Location, Predicate<x86DataLocation> Func)
    {
        if (Location is x86MultiLocation)
        {
            var MLoc = Location as x86MultiLocation;
            for (var i = 0; i < MLoc.Locations.Length; i++)
                if (!Func(MLoc.Locations[i]))
                    return false;

            return true;
        }

        return Func(Location);
    }

    public static bool Contains(IEnumerable<x86DataLocation> Locations, x86DataLocation Loc)
    {
        foreach (var e in Locations)
            if (e.Compare(Loc))
                return true;

        return false;
    }

    public static x86DataLocation Select(x86DataLocation[] List, x86DataLocation Preferred)
    {
        for (var i = 0; i < List.Length; i++)
            if (List[i].Compare(Preferred))
                return List[i];

        return List[0];
    }

    public static x86DataLocation[] Split(x86DataLocation Location, int Size)
    {
        if (Location == null)
            throw new ArgumentNullException("Location");

        if (Location.Size > Size && Location is x86SplittableLocation)
        {
            var Spl = Location as x86SplittableLocation;
            return Spl.Split(Size);
        }

        return new[] { Location };
    }
}

public enum x86OverlappingMode
{
    Partial,
    Whole
}

public abstract class x86DataLocation
{
    public x86Architecture Arch;
    public int Size;

    public x86DataLocation(x86Architecture Arch, int Size)
    {
        if (Size < 0)
            throw new ArgumentOutOfRangeException("Size");

        this.Arch = Arch;
        this.Size = Size;
    }

    public abstract x86DataLocationType DataType { get; }

    public abstract bool HasPart(int Offset, int Size);
    public abstract x86DataLocation GetPart(int Offset, int Size = -1);

    protected abstract bool CompareSingle(x86DataLocation Loc, x86OverlappingMode Mode = x86OverlappingMode.Whole);

    public bool Compare(x86DataLocation Loc, x86OverlappingMode Mode = x86OverlappingMode.Whole)
    {
        if (Mode == x86OverlappingMode.Partial)
        {
            var MSelf = this as x86MultiLocation;
            var MLoc = Loc as x86MultiLocation;

            if (MSelf != null && MLoc != null)
            {
                for (var i = 0; i < MSelf.Locations.Length; i++)
                for (var j = 0; j < MLoc.Locations.Length; j++)
                    if (MSelf.Locations[i].CompareSingle(MLoc.Locations[j], Mode))
                        return true;

                return false;
            }

            if (MSelf != null)
            {
                for (var i = 0; i < MSelf.Locations.Length; i++)
                    if (MSelf.Locations[i].CompareSingle(Loc, Mode))
                        return true;

                return false;
            }

            if (MLoc != null)
            {
                for (var i = 0; i < MLoc.Locations.Length; i++)
                    if (MLoc.Locations[i].CompareSingle(this, Mode))
                        return true;

                return false;
            }

            return CompareSingle(Loc, Mode);
        }

        if (Mode == x86OverlappingMode.Whole)
        {
            var MSelf = this as x86MultiLocation;
            var MLoc = Loc as x86MultiLocation;

            if (MSelf != null || MLoc != null)
            {
                if (MSelf == null || MLoc == null) return false;
                if (MSelf.Locations.Length != MLoc.Locations.Length)
                    return false;

                for (var i = 0; i < MSelf.Locations.Length; i++)
                    if (!MSelf.Locations[i].CompareSingle(MLoc.Locations[i], Mode))
                        return false;

                return true;
            }

            return CompareSingle(Loc, Mode);
        }

        throw new ApplicationException();
    }

    public virtual x86GRegisterList GetRegs(bool AddressRegs = false)
    {
        return new x86GRegisterList();
    }

    public override string ToString()
    {
        throw new ApplicationException();
    }

    public virtual bool IsMemory(x86OverlappingMode Mode = x86OverlappingMode.Partial)
    {
        return false;
    }
}

public class x86SSERegLocation : x86DataLocation
{
    public int Index;

    public x86SSERegLocation(x86Architecture Arch, int Index, int Size = 16)
        : base(Arch, Size)
    {
        if (Size != 16 && Size != 32)
            throw new ArgumentOutOfRangeException("Size must be 16 or 32", "Size");

        this.Index = Index;
    }

    public override x86DataLocationType DataType => x86DataLocationType.SSEReg;

    public override string ToString()
    {
        if (Size == 16) return "xmm" + Index;
        if (Size == 32) return "ymm" + Index;
        throw new ApplicationException();
    }

    protected override bool CompareSingle(x86DataLocation Loc, x86OverlappingMode Mode = x86OverlappingMode.Whole)
    {
        var SSE = Loc as x86SSERegLocation;
        if (SSE == null) return false;

        if (Mode == x86OverlappingMode.Whole && SSE.Size != Size)
            return false;

        return SSE.Index == Index;
    }

    public override x86DataLocation GetPart(int Offset, int Size = -1)
    {
        if (Size == -1) Size = this.Size - Offset;

        if (HasPart(Offset, Size))
            return new x86SSERegLocation(Arch, Index, Size);

        return null;
    }

    public override bool HasPart(int Offset, int Size)
    {
        if (Offset == 0)
        {
            if (Size == 16) return true;
            if (Size == 32 && (Arch.Extensions & x86Extensions.AVX) != 0)
                return true;
        }

        return false;
    }
}

public class x86FPURegLocation : x86DataLocation
{
    public int Index;

    public x86FPURegLocation(x86Architecture Arch, int Index)
        : base(Arch, -1)
    {
        this.Index = Index;
    }

    public override x86DataLocationType DataType => throw new NotImplementedException();

    public override string ToString()
    {
        return "st" + Index;
    }

    protected override bool CompareSingle(x86DataLocation Loc, x86OverlappingMode Mode = x86OverlappingMode.Whole)
    {
        var FPReg = Loc as x86FPURegLocation;
        return FPReg != null && FPReg.Index == Index;
    }

    public override bool HasPart(int Offset, int Size)
    {
        return false;
    }

    public override x86DataLocation GetPart(int Offset, int Size = -1)
    {
        return null;
    }
}

public abstract class x86PostCalcedLocation : x86SplittableLocation
{
    public ExpressionNode AssignedTo;

    public x86PostCalcedLocation(x86Architecture Arch, ExpressionNode AssignedTo, int Offset, int Size)
        : base(Arch, Offset, Size)
    {
        this.AssignedTo = AssignedTo;
    }

    public override x86DataLocationType DataType => x86DataLocationType.None;

    public abstract x86DataLocation Location { get; }

    protected override bool CompareSingle(x86DataLocation Loc, x86OverlappingMode Mode = x86OverlappingMode.Whole)
    {
        var Self = Location;
        if (Self == null) return false;
        return Location.Compare(Loc, Mode);
    }

    public override x86GRegisterList GetRegs(bool AddressRegs = false)
    {
        return Location.GetRegs(AddressRegs);
    }
}

public class x86AssignVarLoc : x86PostCalcedLocation
{
    public ExpressionNode Assigned;
    public ExpressionNode AssignNode;

    public x86AssignVarLoc(x86Architecture Arch, ExpressionNode AssignNode, ExpressionNode Assigned,
        ExpressionNode AssignedTo)
        : base(Arch, AssignedTo, 0, (Assigned.Type.RealId as Type).Size)
    {
        if (x86Expressions.NeedsInstructions(Assigned))
            throw new ArgumentException("The node needs instructions", "AssignNode");

        this.Assigned = Assigned;
        this.AssignNode = AssignNode;
    }

    public x86AssignVarLoc(x86Architecture Arch, ExpressionNode AssignNode, ExpressionNode Assigned,
        ExpressionNode AssignedTo, int Offset, int Size)
        : base(Arch, AssignedTo, Offset, Size)
    {
        if (x86Expressions.NeedsInstructions(Assigned))
            throw new ArgumentException("The node needs instructions", "AssignNode");

        this.Assigned = Assigned;
        this.AssignNode = AssignNode;
    }

    public override x86DataLocation Location
    {
        get
        {
            var Location = x86Expressions.GetLocation(Arch, Assigned);
            if (Location != null && !(Location is x86SSERegLocation))
                if (Offset != 0 || Location.Size != Size)
                    Location = Location.GetPart(Offset, Size);

            return Location;
        }
    }

    public override bool IsMemory(x86OverlappingMode Mode = x86OverlappingMode.Partial)
    {
        return Assigned.IsMemory(Arch);
    }

    public override x86DataLocation GetPart(int Offset, int Size = -1)
    {
        if (Size == -1) Size = this.Size - Offset;
        return new x86AssignVarLoc(Arch, AssignNode, Assigned, AssignedTo, this.Offset + Offset, Size);
    }

    public override bool HasPart(int Offset, int Size)
    {
        return true;
    }
}

public class x86LinkedNodeLocation : x86PostCalcedLocation
{
    public x86LinkedNodeData LData;
    public LinkedExprNode LNode;

    public x86LinkedNodeLocation(x86Architecture Arch, LinkedExprNode LNode)
        : base(Arch, LNode.Node, 0, (LNode.Node.Type.RealId as Type).Size)
    {
        this.LNode = LNode;
        LData = LNode.Data.Get<x86LinkedNodeData>();
    }

    public x86LinkedNodeLocation(x86Architecture Arch, LinkedExprNode LNode, int Offset, int Size)
        : base(Arch, LNode.Node, Offset, Size)
    {
        this.LNode = LNode;
        LData = LNode.Data.Get<x86LinkedNodeData>();
    }

    public override x86DataLocation Location
    {
        get
        {
            if (LData.Location != null && !(LData.Location is x86SSERegLocation))
                if (LData.Location.Size != Size || Offset != 0)
                    return LData.Location.GetPart(Offset, Size);

            return LData.Location;
        }
    }

    public override bool IsMemory(x86OverlappingMode Mode = x86OverlappingMode.Partial)
    {
        return LData.Location.IsMemory();
    }

    public override x86DataLocation GetPart(int Offset, int Size = -1)
    {
        if (Size == -1) Size = this.Size - Offset;
        return new x86LinkedNodeLocation(Arch, LNode, this.Offset + Offset, Size);
    }

    public override bool HasPart(int Offset, int Size)
    {
        return true;
    }
}

public abstract class x86SplittableLocation : x86DataLocation
{
    public int Offset;

    public x86SplittableLocation(x86Architecture Arch, int Offset, int Size)
        : base(Arch, Size)
    {
        this.Offset = Offset;
    }

    public virtual int AllOffset => Offset;

    public x86DataLocation[] Split(int Size)
    {
        var Count = (this.Size - 1) / Size + 1;
        var Ret = new x86DataLocation[Count];

        for (var i = 0; i < Count; i++)
        {
            var Offset = i * Size;

            if (Offset + Size > this.Size)
                Ret[i] = GetPart(Offset, this.Size - Offset);
            else Ret[i] = GetPart(Offset, Size);
        }

        return Ret;
    }

    protected override bool CompareSingle(x86DataLocation Loc, x86OverlappingMode Mode = x86OverlappingMode.Whole)
    {
        var Splittable = Loc as x86SplittableLocation;
        if (Mode == x86OverlappingMode.Whole) return Offset == Splittable.Offset && Size == Splittable.Size;

        if (Mode == x86OverlappingMode.Partial)
        {
            if (Offset + Size <= Splittable.Offset) return false;
            if (Offset >= Splittable.Offset + Splittable.Size) return false;
            return true;
        }

        throw new NotImplementedException();
    }
}

public class x86ConstLocation : x86SplittableLocation
{
    public Identifier Type;
    public ConstValue Value;

    public x86ConstLocation(x86Architecture Arch, ConstValue Value, Identifier Type, int Offset, int Size)
        : base(Arch, Offset, Size)
    {
        this.Value = Value;
        this.Type = Type;
    }

    public x86ConstLocation(x86Architecture Arch, ConstExpressionNode Node, int Offset, int Size)
        : base(Arch, Offset, Size)
    {
        Value = Node.Value;
        Type = Node.Type;
    }

    public ConstValue NonStructuredValue => GetNonStructuredValue(Value);

    public ulong Unsigned => Value == null ? 0 : NonStructuredValue.GetUnsigned(Offset, Size);

    public long Signed => Value == null ? 0 : NonStructuredValue.GetSigned(Offset, Size);

    public char Char => (Value as CharValue).Value;

    public BigInteger Integer => (Value as IntegerValue).Value;

    public double Double => (Value as DoubleValue).Value;

    public double Float => (Value as FloatValue).Value;

    public double CDouble => Value.Double;

    public string String => (Value as StringValue).Value;

    public bool Bool => (Value as BooleanValue).Value;

    public override x86DataLocationType DataType => x86DataLocationType.None;

    private static ConstValue GetNonStructuredValue(ConstValue Value)
    {
        var SValue = Value as StructuredValue;
        if (SValue != null && SValue.Members.Count == 1)
            return GetNonStructuredValue(SValue.Members[0]);

        return Value;
    }

    public override x86DataLocation GetPart(int Offset, int Size = -1)
    {
        if (Size == -1) Size = this.Size - Offset;
        /*
        if (Offset >= (Type.RealId as Type).Size)
        {
            var Global = Type.Container.GlobalContainer;
            var MemberType = Global.CommonIds.GetIdentifier<UnsignedType>(Size);
            return new x86ConstLocation(Arch, new ZeroValue(), MemberType, 0, Size);
        }
        */
        if (Type.RealId is StructType)
        {
            var StructuredType = Type.RealId as StructType;
            var Member = StructuredType.GetMemberAt(Offset, Size);
            if (Member == null) return x86DataLocations.GetZeroData(Arch, Type.Container, Size);

            var SValue = Value as StructuredValue;
            var NewOffset = Offset - Member.Offset;
            var Index = StructuredType.StructuredScope.IdentifierList.IndexOf(Member);
            var MemberType = Member.TypeOfSelf.RealId as Type;

            if (SValue == null && Value is ZeroValue)
                return new x86ConstLocation(Arch, new ZeroValue(), MemberType, NewOffset, Size);

            return new x86ConstLocation(Arch, SValue.Members[Index], MemberType, NewOffset, Size);
        }

        if (Type.RealId is NonrefArrayType)
        {
            var ArrayType = Type.RealId as NonrefArrayType;
            var TypeOfVals = ArrayType.TypeOfValues;

            var SValue = Value as StructuredValue;
            var MemberOffset = Offset / TypeOfVals.Size * TypeOfVals.Size;
            var NewOffset = Offset - MemberOffset;
            var Index = Offset / TypeOfVals.Size;

            if (NewOffset + Size > TypeOfVals.Size)
                return x86DataLocations.GetZeroData(Arch, Type.Container, Size);

            if (SValue == null && Value is ZeroValue)
                return new x86ConstLocation(Arch, new ZeroValue(), TypeOfVals, NewOffset, Size);

            if (SValue.Members.Count <= Index)
                return x86DataLocations.GetZeroData(Arch, Type.Container, Size);

            return new x86ConstLocation(Arch, SValue.Members[Index], TypeOfVals, NewOffset, Size);
        }

        return new x86ConstLocation(Arch, Value, Type, this.Offset + Offset, Size);
    }

    public override bool HasPart(int Offset, int Size)
    {
        var StructuredType = Type.RealId as StructuredType;
        if (StructuredType != null)
            return StructuredType.GetMemberAt(Offset, Size) != null;

        return true;
    }

    public override string ToString()
    {
        if (Type.RealId is SignedType) return Signed.ToString();
        return Unsigned.ToString();

        /*if (Value is BoolValue) return (Value as BoolValue).Value ? "1" : "0";
        if (Value is CharValue) return ((int)(Value as CharValue).Value).ToString();
        return Value.ToString();*/
    }

    protected override bool CompareSingle(x86DataLocation Loc, x86OverlappingMode Mode = x86OverlappingMode.Whole)
    {
        var ConstPos = Loc as x86ConstLocation;
        if (ConstPos == null) return false;

        return ConstPos.Value.IsEqual(Value) && base.CompareSingle(Loc, Mode);
    }
}

public class x86GRegLocation : x86DataLocation
{
    public int Offset, Index;

    public x86GRegLocation(x86Architecture Arch, int Index, x86RegisterMask Mask)
        : this(Arch, Index, Mask.Offset, Mask.Size)
    {
    }

    public x86GRegLocation(x86Architecture Arch, int Index, int Offset, int Size)
        : base(Arch, Size)
    {
        if (Index < 0 || Index >= Arch.RegCount) throw new ArgumentOutOfRangeException("Index");
        if (Offset != 0 && Offset != 1) throw new ArgumentOutOfRangeException("Offset");
        if (Size < 0 || Size > Arch.RegSize) throw new ArgumentOutOfRangeException("Size");
        if (!Arch.IsGRegisterExists(Index, Offset, Size)) throw new ApplicationException("Register does not exists");

        this.Index = Index;
        this.Offset = Offset;
    }

    public x86GRegLocation(x86Architecture Arch, int Index, int Size)
        : base(Arch, Size)
    {
        if (Index < 0 || Index >= Arch.RegCount) throw new ArgumentOutOfRangeException("Index");
        if (Size < 0 || Size > Arch.RegSize) throw new ArgumentOutOfRangeException("Size");
        this.Index = Index;
    }

    public override x86DataLocationType DataType
    {
        get
        {
            var Ret = x86DataLocationType.General;
            if (HasPart(0, 1)) Ret |= x86DataLocationType.OneByte;
            return Ret;
        }
    }

    public x86RegisterMask Mask => new(Offset, Size);

    public override string ToString()
    {
        return x86Architecture.GetGRegisterName(Index, Offset, Size);
    }

    protected override bool CompareSingle(x86DataLocation Loc, x86OverlappingMode Mode = x86OverlappingMode.Whole)
    {
        var RegPos = Loc as x86GRegLocation;
        if (RegPos == null) return false;

        if (RegPos.Index != Index) return false;
        if (Size == 1 && RegPos.Size == 1)
        {
            if (Offset != RegPos.Offset) return false;
        }
        else if (Mode == x86OverlappingMode.Whole)
        {
            if (RegPos.Size != Size) return false;
            if (RegPos.Offset != Offset) return false;
        }

        return true;
    }

    public override x86GRegisterList GetRegs(bool AddressRegs = false)
    {
        var Ret = new x86GRegisterList(Arch.RegCount);
        Ret.SetUsed(this);
        return Ret;
    }

    public override x86DataLocation GetPart(int Offset, int Size = -1)
    {
        if (Size == -1) Size = this.Size - Offset;
        if (!HasPart(Offset, Size)) return null;

        var NewOffset = this.Offset + Offset;
        return new x86GRegLocation(Arch, Index, NewOffset, Size);
    }

    public override bool HasPart(int Offset, int Size)
    {
        if (this.Size < Offset + Size) return false;

        var NewOffset = this.Offset + Offset;
        return Arch.IsGRegisterExists(Index, NewOffset, Size);
    }

    public bool Verify(x86GRegisterList CantBe)
    {
        return !CantBe.Initialized || CantBe.IsFree(this);
    }
}

public abstract class x86LabelPosition : x86DataLocation
{
    public x86LabelPosition(x86Architecture Arch)
        : base(Arch, Arch.RegSize)
    {
    }

    public override x86DataLocationType DataType => x86DataLocationType.None;

    public override bool HasPart(int Offset, int Size)
    {
        return Offset == 0 && this.Size == Size;
    }
}

public class x86IndexedLabelLocation : x86LabelPosition
{
    public int Label;

    public x86IndexedLabelLocation(x86Architecture Arch, int Label)
        : base(Arch)
    {
        this.Label = Label;
    }

    protected override bool CompareSingle(x86DataLocation Loc, x86OverlappingMode Mode = x86OverlappingMode.Whole)
    {
        var IPos = Loc as x86IndexedLabelLocation;
        if (IPos == null) return false;

        return IPos.Label == Label;
    }

    public override string ToString()
    {
        return "_" + Label;
    }

    public override x86DataLocation GetPart(int Offset, int Size = -1)
    {
        if (Size == -1) Size = this.Size - Offset;
        if (!HasPart(Offset, Size)) return null;
        return new x86IndexedLabelLocation(Arch, Label);
    }
}

public class x86NamedLabelPosition : x86LabelPosition
{
    public string Label;

    public x86NamedLabelPosition(x86Architecture Arch, string Label)
        : base(Arch)
    {
        this.Label = Label;
    }

    protected override bool CompareSingle(x86DataLocation Loc, x86OverlappingMode Mode = x86OverlappingMode.Whole)
    {
        var NPos = Loc as x86NamedLabelPosition;
        if (NPos == null) return false;

        return NPos.Label == Label;
    }

    public override string ToString()
    {
        return Label;
    }

    public override x86DataLocation GetPart(int Offset, int Size = -1)
    {
        if (Size == -1) Size = this.Size - Offset;
        if (!HasPart(Offset, Size)) return null;
        return new x86NamedLabelPosition(Arch, Label);
    }
}

public class x86IndexLocation : x86MemoryLocation
{
    public AutoAllocatedList<x86MoveStruct> Moves;

    public x86IndexLocation(x86Architecture Arch, int Offset, int Size, x86DataLocation Displacement,
        x86GRegLocation Base, x86GRegLocation Index, byte Scale = 1)
        : base(Arch, Offset, Size, Displacement, Base, Index, Scale)
    {
    }

    public x86IndexLocation(x86Architecture Arch, int Offset, int Size, x86DataLocation DisplacementOrBase,
        x86GRegLocation Index = null, byte Scale = 1)
        : base(Arch, Offset, Size, DisplacementOrBase, Index, Scale)
    {
    }

    public x86IndexLocation(x86Architecture Arch, int Offset, int Size, int RegIndex, int RegSize = -1)
        : base(Arch, Offset, Size, RegIndex, RegSize)
    {
    }

    public void AddMove(x86DataLocation Dst, x86DataLocation Src)
    {
        var StoredDataType = new x86StoredDataType(x86TypeKind.Unsigned, Dst.Size);
        Moves.Add(new x86MoveStruct(Dst, Src, new x86TemporaryData(),
            x86ExecutorType.General, StoredDataType));
    }

    public void AddMovesWithSrcs(x86DataLocation Dst, x86DataLocation Src)
    {
        var IndexSrc = Src as x86IndexLocation;
        if (IndexSrc != null)
            Moves.AddRange(IndexSrc.Moves);

        var StoredDataType = new x86StoredDataType(x86TypeKind.Unsigned, Dst.Size);
        Moves.Add(new x86MoveStruct(Dst, Src, new x86TemporaryData(),
            x86ExecutorType.General, StoredDataType));
    }

    public override bool Add(x86MemoryLocation Location, x86GRegLocation TempGReg, byte Scale = 1)
    {
        if (TempGReg != null)
        {
            AddMovesWithSrcs(TempGReg, Location);
            Add(TempGReg, Scale);
            return true;
        }

        return false;
    }

    public override x86DataLocation GetPart(int Offset, int Size = -1)
    {
        if (Size == -1) Size = this.Size - Offset;

        if (!HasPart(Offset, Size)) return null;
        var Ret = new x86IndexLocation(Arch, this.Offset + Offset,
            Size, Displacement, Base, Index, Scale);

        Ret.Moves = Moves.Copy();
        return Ret;
    }

    protected override bool CompareSingle(x86DataLocation Loc, x86OverlappingMode Mode = x86OverlappingMode.Whole)
    {
        var IndexLoc = Loc as x86IndexLocation;
        if (IndexLoc == null) return false;

        if (IndexLoc.Moves.Count != Moves.Count)
            return false;

        for (var i = 0; i < Moves.Count; i++)
            if (!Moves[i].Src.Compare(IndexLoc.Moves[i].Src))
                return false;

        return base.CompareSingle(Loc, Mode);
    }
}

public class x86MemoryLocation : x86SplittableLocation
{
    public int Align = int.MaxValue;
    public x86GRegLocation Base;
    public x86DataLocation Displacement;
    public x86GRegLocation Index;
    public byte Scale = 1;

    public x86MemoryLocation(x86Architecture Arch, int Offset, int Size, x86DataLocation Displacement,
        x86GRegLocation Base, x86GRegLocation Index, byte Scale = 1)
        : base(Arch, Offset, Size)
    {
        if (Scale != 1 && Index == null) throw new ApplicationException();
        if (!(Displacement == null || Displacement is x86LabelPosition || Displacement is x86ConstLocation))
            throw new ApplicationException();

        if (Base == null && Index != null && Scale == 1)
        {
            Base = Index;
            Index = null;
        }

        this.Displacement = Displacement;
        this.Base = Base;
        this.Index = Index;
        this.Scale = Scale;
    }

    public x86MemoryLocation(x86Architecture Arch, int Offset, int Size, x86DataLocation DisplacementOrBase,
        x86GRegLocation Index = null, byte Scale = 1)
        : base(Arch, Offset, Size)
    {
        if (DisplacementOrBase != null)
        {
            if (DisplacementOrBase is x86GRegLocation) Base = DisplacementOrBase as x86GRegLocation;
            else if (DisplacementOrBase is x86LabelPosition || DisplacementOrBase is x86ConstLocation)
                Displacement = DisplacementOrBase;
            else throw new ApplicationException();
        }

        this.Index = Index;
        this.Scale = Scale;
    }

    public x86MemoryLocation(x86Architecture Arch, int Offset, int Size, int RegIndex, int RegSize = -1)
        : this(Arch, Offset, Size, new x86GRegLocation(Arch, RegIndex, RegSize == -1 ? Arch.RegSize : RegSize))
    {
    }

    public override x86DataLocationType DataType => x86DataLocationType.Memory;

    public virtual x86DataLocation GetAddress()
    {
        if (AllOffset == 0)
        {
            if (Displacement != null)
            {
                if (Base == null && Index == null)
                    return Displacement;
            }
            else if (Base != null)
            {
                if (Index == null && Displacement == null)
                    return Base;
            }
            else if (Index != null)
            {
                if (Base == null && Displacement == null && Scale == 1)
                    return Index;
            }
        }

        return null;
    }

    public void Add(x86DataLocation Position, byte Scale = 1)
    {
        if (Position is x86ConstLocation)
        {
            var ConstPos = Position as x86ConstLocation;
            Offset += (int)ConstPos.Signed * Scale;
        }
        else if (Position is x86LabelPosition)
        {
            if (Displacement != null || Scale != 1)
                throw new ApplicationException();

            Displacement = Position;
        }
        else if (Position is x86GRegLocation)
        {
            var RegPos = Position as x86GRegLocation;
            if (Base == null && Scale == 1)
            {
                Base = RegPos;
            }
            else if (Index == null)
            {
                Index = RegPos;
                this.Scale = Scale;
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

    public virtual bool Add(x86MemoryLocation Location, x86GRegLocation TempGReg, byte Scale = 1)
    {
        return false;
    }

    public bool Add(x86DataLocation Location, x86GRegLocation TempGReg, byte Scale = 1)
    {
        var MemPosition = Location as x86MemoryLocation;
        if (MemPosition == null)
        {
            Add(Location, Scale);
            return true;
        }

        return Add(MemPosition, TempGReg, Scale);
    }

    protected override bool CompareSingle(x86DataLocation Loc, x86OverlappingMode Mode = x86OverlappingMode.Whole)
    {
        var M = Loc as x86MemoryLocation;
        if (M == null) return false;

        if (Displacement == null != (M.Displacement == null)) return false;
        if (Displacement != null && !Displacement.Compare(M.Displacement)) return false;

        if (Base == null != (M.Base == null)) return false;
        if (Base != null && !Base.Compare(M.Base)) return false;

        if (Index == null != (M.Index == null)) return false;
        if (Index != null && (!Index.Compare(M.Index) || Scale != M.Scale)) return false;
        return base.CompareSingle(Loc, Mode);
    }

    public override x86DataLocation GetPart(int Offset, int Size = -1)
    {
        if (Size == -1) Size = this.Size - Offset;

        if (!HasPart(Offset, Size)) return null;
        return new x86MemoryLocation(Arch, this.Offset + Offset, Size,
            Displacement, Base, Index, Scale);
    }

    public override bool HasPart(int Offset, int Size)
    {
        return Offset + Size <= this.Size;
    }

    public override bool IsMemory(x86OverlappingMode Mode = x86OverlappingMode.Partial)
    {
        return true;
    }

    public override string ToString()
    {
        var Ret = (string)null;
        if (Displacement != null) Ret += Displacement.ToString();
        if (Base != null) Ret += Ret == null ? Base.ToString() : " + " + Base;
        if (Index != null) Ret += Ret == null ? Index.ToString() : " + " + Index;
        if (Scale > 1) Ret += Ret == null ? Scale.ToString() : " * " + Scale;

        var Offset = AllOffset;
        if (Offset > 0) Ret += Ret == null ? Offset.ToString() : " + " + Offset;
        else if (Offset < 0) Ret += Ret == null ? Offset.ToString() : " - " + -Offset;
        return x86Architecture.GetTypeString(Size) + "[" + Ret + "]";
    }

    public override x86GRegisterList GetRegs(bool AddressRegs = false)
    {
        if (AddressRegs)
        {
            var Ret = new x86GRegisterList(Arch.RegCount);
            if (Base != null) Ret.SetUsed(Base);
            if (Index != null) Ret.SetUsed(Index);
            return Ret;
        }

        return new x86GRegisterList();
    }
}

public class x86StackLocation : x86MemoryLocation //x86IndexLocation
{
    public x86FuncScopeData Data;
    public bool FuncParam;
    public FunctionScope FuncScope;

    public x86StackLocation(x86Architecture Arch, FunctionScope FuncScope, int Offset, int Size, bool FuncParam)
        : base(Arch, Offset, Size, null)
    {
        this.FuncScope = FuncScope;
        this.FuncParam = FuncParam;

        Data = FuncScope.Data.Get<x86FuncScopeData>();
        Base = GetBaseRegister(Data, FuncParam);
    }

    public override int AllOffset => base.AllOffset + StackOffset;

    public int StackOffset
    {
        get
        {
            if ((Data.Flags & x86FuncScopeFlags.StackLocationsValid) == 0)
                throw new ApplicationException("Stack locations are not valid");

            if (FuncParam & ((Data.Flags & x86FuncScopeFlags.SaveParameterPointer) != 0))
                return Data.PushedRegisters + 4;

            if ((Data.Flags & x86FuncScopeFlags.SaveFramePointer) != 0)
                return FuncParam ? Data.PushedRegisters + 4 : -Data.SubractedFromESP;

            if (FuncParam) return Data.CallParameters + Data.PushedRegisters + Data.SubractedFromESP + 4;
            return Data.UnallocatedSpace > 0 ? -Data.UnallocatedSpace : Data.CallParameters;
        }
    }

    public static x86GRegLocation GetBaseRegister(x86FuncScopeData Data, bool Parameter)
    {
        if (Parameter && (Data.Flags & x86FuncScopeFlags.SaveParameterPointer) != 0)
            return Data.ParameterPointer;

        return (Data.Flags & x86FuncScopeFlags.SaveFramePointer) != 0 ? Data.FramePointer : Data.StackPointer;
    }

    public void UpdateBaseRegister()
    {
        Base = GetBaseRegister(Data, FuncParam);
    }

    public override bool HasPart(int Offset, int Size)
    {
        return Offset + Size <= this.Size;
    }

    public override x86DataLocation GetPart(int Offset, int Size = -1)
    {
        if (Size == -1) Size = this.Size - Offset;

        if (!HasPart(Offset, Size)) return null;
        return new x86StackLocation(Arch, FuncScope, this.Offset + Offset, Size, FuncParam);
    }

    protected override bool CompareSingle(x86DataLocation Loc, x86OverlappingMode Mode = x86OverlappingMode.Whole)
    {
        var StackPos = Loc as x86StackLocation;
        if (StackPos == null) return false;

        return StackPos.FuncParam == FuncParam && base.CompareSingle(Loc, Mode);
    }

    public override string ToString()
    {
        if (!Base.Compare(GetBaseRegister(Data, FuncParam)))
            throw new ApplicationException();

        return base.ToString();
    }
}

public class x86UnknownMemory : x86MemoryLocation
{
    public x86UnknownMemory(x86Architecture Arch, int Offset, int Size)
        : base(Arch, Offset, Size, null)
    {
    }

    protected override bool CompareSingle(x86DataLocation Loc, x86OverlappingMode Mode = x86OverlappingMode.Whole)
    {
        return false;
    }

    public override string ToString()
    {
        throw new InvalidOperationException();
    }

    public override bool HasPart(int Offset, int Size)
    {
        return this.Size >= Offset + Size;
    }

    public override x86DataLocation GetPart(int Offset, int Size = -1)
    {
        if (Size == -1) Size = this.Size - Offset;
        if (!HasPart(Offset, Size)) return null;

        return new x86UnknownMemory(Arch, 0, Size);
    }
}

public class x86MultiLocation : x86SplittableLocation
{
    public x86DataLocation[] Locations;

    public x86MultiLocation(x86Architecture Arch, params int[] Regs)
        : base(Arch, 0, Regs.Length * Arch.RegSize)
    {
        var RegSize = Arch.RegSize;
        Locations = new x86DataLocation[Regs.Length];
        for (var i = 0; i < Locations.Length; i++)
            Locations[i] = new x86GRegLocation(Arch, Regs[i], RegSize);
    }

    public x86MultiLocation(x86Architecture Arch, int Size, x86DataLocation[] Positions)
        : base(Arch, 0, Size)
    {
        Locations = Positions;
    }

    public override x86DataLocationType DataType => x86DataLocationType.None;

    public bool AllPartsTypeSame
    {
        get
        {
            if (Locations.Length == 0)
                return true;

            var Type = Locations[0].DataType;
            if (Type == x86DataLocationType.None)
                return false;

            for (var i = 1; i < Locations.Length; i++)
                if (Locations[i].DataType != Type)
                    return false;

            return true;
        }
    }

    public bool AllPartsSizeSame
    {
        get
        {
            if (Locations.Length == 0)
                return true;

            var Size = Locations[0].Size;
            for (var i = 1; i < Locations.Length; i++)
                if (Locations[i].Size != Size)
                    return false;

            return true;
        }
    }

    public void GetPartHelper(int Offset, int Size, Action<x86DataLocation, int, int> Func)
    {
        var CurrentOffset = 0;
        var SizeLeft = Size;
        var OffsetLeft = Offset;

        for (var i = 0; i < Locations.Length; i++)
        {
            var Part = Locations[i];
            if (CurrentOffset + Part.Size > OffsetLeft)
            {
                var RetPartOffset = OffsetLeft - CurrentOffset;
                var RetPartSize = Math.Min(Part.Size - RetPartOffset, SizeLeft);
                Func(Part, RetPartOffset, RetPartSize);

                SizeLeft -= RetPartSize;
                OffsetLeft += RetPartSize;
                if (SizeLeft == 0) break;
            }

            CurrentOffset += Part.Size;
        }
    }

    public override x86DataLocation GetPart(int Offset, int Size = -1)
    {
        if (Size == -1) Size = this.Size - Offset;

        var Failed = false;
        x86DataLocation OnlyRetValue = null;
        List<x86DataLocation> RetLocations = null;

        GetPartHelper(Offset, Size, (Part, PartOffset, PartSize) =>
        {
            var RetPart = Part.GetPart(PartOffset, PartSize);
            if (RetPart == null || Failed)
            {
                Failed = true;
                return;
            }

            if (PartSize == Size)
            {
                OnlyRetValue = RetPart;
            }
            else
            {
                if (RetLocations == null)
                    RetLocations = new List<x86DataLocation>();

                RetLocations.Add(RetPart);
            }
        });

        if (Failed) return null;
        if (OnlyRetValue != null) return OnlyRetValue;
        return new x86MultiLocation(Arch, Size, RetLocations.ToArray());
    }

    public override bool HasPart(int Offset, int Size)
    {
        var Failed = false;
        GetPartHelper(Offset, Size, (Part, PartOffset, PartSize) =>
        {
            var RetPart = Part.GetPart(PartOffset, PartSize);
            if (RetPart == null || Failed) Failed = true;
        });

        return !Failed;
    }

    protected override bool CompareSingle(x86DataLocation Loc, x86OverlappingMode Mode = x86OverlappingMode.Whole)
    {
        throw new ApplicationException();
    }

    public override string ToString()
    {
        return "{" + string.Join<x86DataLocation>(", ", Locations) + "}";
    }

    public override bool IsMemory(x86OverlappingMode Mode = x86OverlappingMode.Partial)
    {
        for (var i = 0; i < Locations.Length; i++)
            if (Locations[i] is x86MemoryLocation)
            {
                if (Mode == x86OverlappingMode.Partial)
                    return true;
            }
            else
            {
                if (Mode == x86OverlappingMode.Whole)
                    return false;
            }

        return Mode == x86OverlappingMode.Whole;
    }

    public override x86GRegisterList GetRegs(bool AddressRegs = false)
    {
        var Ret = new x86GRegisterList(Arch.RegCount);
        for (var i = 0; i < Locations.Length; i++)
        {
            var Pos = Locations[i];
            if (Pos is x86GRegLocation)
            {
                Ret.SetUsed(Pos as x86GRegLocation);
            }
            else if (Pos is x86MultiLocation)
            {
                var MPos = Pos as x86MultiLocation;
                var Regs = MPos.GetRegs();
                if (Regs.Initialized) Ret.SetUsed(Regs);
            }
        }

        return Ret;
    }
}