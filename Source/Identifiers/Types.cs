using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Numerics;

namespace Zinnia
{
    public enum CallingConvention : byte
    {
        Unknown,
        StdCall,
        CDecl,
        ZinniaCall,
    }

    public enum TypeConversion : byte
    {
        Automatic,
        Convertable,
        Nonconvertable,
    }

    [Flags]
    public enum TypeFlags : byte
    {
        None = 0,
        CanBeArrayType = 1,
        CanBeReference = 2,
        CanBePointer = 4,
        CanBeVariable = 8,
        CanBeEverything = CanBeArrayType | CanBeReference |
            CanBePointer | CanBeVariable,

        UnfixedSize = 16,
        ReferenceValue = 32,
        NoDefaultBase = 64,
    }

    public abstract class Type : Identifier
    {
        public TypeFlags TypeFlags = TypeFlags.CanBeEverything;
        public ConstValueType ConstValueType = ConstValueType.Unknown;
        public int Size;
        public int Align;
        public int InstanceSize = -1;
        public bool LayoutCalculated = false;

        public override bool IsInstanceIdentifier
        {
            get { return false; }
        }

        public override Identifier TypeOfSelf
        {
            get { return Container.GlobalContainer.CommonIds.TypeOfType; }
        }

        public Type(IdContainer Container, CodeString Name)
            : base(Container, Name)
        {
            this.Name = Name;
        }

        public virtual bool CanOpApplied(Operator Op, Type SrcType)
        {
            if (CanOpApplied_Base(Op, SrcType))
                return true;

            var RType = RealId as Type;
            if (RType != this && RType != null)
                return RType.CanOpApplied_Base(Op, SrcType);

            return false;
        }

        internal virtual bool CanOpApplied_Base(Operator Op, Type SrcType)
        {
            if (Op == Operator.Assignment)
                return true;

            return false;
        }

        public override TypeConversion CanConvert(Identifier To)
        {
            if (Identifiers.IsBoxing(this, To))
                return TypeConversion.Automatic;

            if (IsEquivalent(To)) return TypeConversion.Automatic;

            if (To.RealId is TupleType)
            {
                var Tuple = To.RealId as TupleType;
                var Members = Tuple.StructuredScope.IdentifierList;
                var Conv = TypeConversion.Automatic;

                for (var i = 0; i < Members.Count; i++)
                {
                    var Member = Members[i] as MemberVariable;
                    var MemberType = Member.Children[0];

                    var MemberConv = CanConvert(MemberType);
                    if (MemberConv == TypeConversion.Nonconvertable)
                    {
                        Conv = TypeConversion.Nonconvertable;
                    }
                    else if (MemberConv == TypeConversion.Convertable)
                    {
                        if (Conv == TypeConversion.Automatic)
                            Conv = TypeConversion.Convertable;
                    }
                }

                if (Conv != TypeConversion.Nonconvertable)
                    return Conv;
            }

            if (RealId != this) return RealId.CanConvert(To);
            else return TypeConversion.Nonconvertable;
        }
    }

    public class TypeOfType : Type
    {
        public TypeOfType(IdContainer Container, CodeString Name)
            : base(Container, Name)
        {
            UndeclaredIdType = UndeclaredIdType.Type;
            TypeFlags = TypeFlags.None;
        }

        public override bool IsEquivalent(Identifier Id)
        {
            return Id.RealId is TypeOfType;
        }
    }

    public class AutomaticType : Type
    {
        public AutomaticType(IdContainer Container, CodeString Name)
            : base(Container, Name)
        {
            UndeclaredIdType = UndeclaredIdType.Auto;
            TypeFlags = TypeFlags.CanBeVariable;
        }

        public override bool IsEquivalent(Identifier Id)
        {
            return Id.RealId is AutomaticType;
        }

        internal override bool CanOpApplied_Base(Operator Op, Type SrcType)
        {
            if (Op == Operator.Assignment) return true;
            return base.CanOpApplied_Base(Op, SrcType);
        }

        public override TypeConversion CanConvert(Identifier To)
        {
            return TypeConversion.Automatic;
        }
    }

    public class NullType : AutomaticType
    {
        public NullType(IdContainer Container, CodeString Name)
            : base(Container, Name)
        {
            this.UndeclaredIdType = UndeclaredIdType.Null;
        }

        public override bool IsEquivalent(Identifier Id)
        {
            return Id.RealId is NullType;
        }

        public override TypeConversion CanConvert(Identifier To)
        {
            var RTo = To.RealId as Type;
            if (RTo is PointerType || RTo is TypeOfFunction ||
                (RTo.TypeFlags & Zinnia.TypeFlags.ReferenceValue) != 0)
            {
                return TypeConversion.Automatic;
            }

            return TypeConversion.Nonconvertable;
        }
    }

    public class PointerType : Type
    {
        public Type Child
        {
            get { return Children[0].RealId as Type; }
        }

        public override void Update()
        {
            GenerateName();
            CalculateAccess();
        }

        public PointerType(IdContainer Container, CodeString Name, Identifier Child)
            : base(Container, Name)
        {
            Setup();
            this.Children = new Identifier[] { Child };
        }

        public PointerType(IdContainer Container, Identifier Child, bool DoUpdate = true)
            : base(Container, new CodeString())
        {
            Setup();

            this.Children = new Identifier[] { Child };
            if (Child != null) Access = Child.Access;
            if (DoUpdate) Update();
        }

        private void Setup()
        {
            UndeclaredIdType = UndeclaredIdType.Pointer;
            ConstValueType = ConstValueType.Unknown;
            Size = Container.State.Arch.RegSize;
            Align = this.Size;
        }

        internal override bool CanOpApplied_Base(Operator Op, Type SrcType)
        {
            if (Operators.IsRelEquality(Op) && SrcType is PointerType) return true;

            if (!(Child is VoidType))
            {
                if (Operators.IsIncDec(Op)) return true;
                if ((Op == Operator.Add || Op == Operator.Subract) && SrcType is NonFloatType) return true;
                if (Op == Operator.Index) return true;
            }

            return base.CanOpApplied_Base(Op, SrcType);
        }

        public override bool IsEquivalent(Identifier Id)
        {
            var PType = Id.RealId as PointerType;
            return PType != null && Child.IsEquivalent(PType.Child);
        }

        public override TypeConversion CanConvert(Identifier To)
        {
            if (To.RealId is PointerType)
            {
                var PTo = To.RealId as PointerType;
                if (PTo.Child is VoidType || PTo.Child.IsEquivalent(Child))
                    return TypeConversion.Automatic;
                else return TypeConversion.Convertable;
            }

            if (To.RealId is NonFloatType && (To.RealId as NonFloatType).Size == Size)
                return TypeConversion.Convertable;

            if (To.RealId is TypeOfFunction && Child is VoidType)
                return TypeConversion.Convertable;

            return base.CanConvert(To);
        }
    }

    public abstract class BuiltinType : Type
    {
        public StructuredType UnderlyingType;

        public BuiltinType(IdContainer Container, CodeString Name)
            : base(Container, Name)
        {

        }

        public override bool IsEquivalent(Identifier Id)
        {
            return UnderlyingType == Id || base.IsEquivalent(Id);
        }

        public override bool HasScopes
        {
            get
            {
                return UnderlyingType != null && UnderlyingType.HasScopes;
            }
        }

        public override IEnumerable<ScopeNode> EnumScopes
        {
            get
            {
                if (HasScopes)
                {
                    foreach (var e in UnderlyingType.EnumScopes)
                        yield return e;
                }
            }
        }

        public override bool CanOpApplied(Operator Op, Type SrcType)
        {
            if (CanOpApplied_Base(Op, SrcType))
                return true;

            if (UnderlyingType != null)
            {
                if (UnderlyingType.CanOpApplied_Base(Op, SrcType))
                    return true;
            }

            return false;
        }

        public override TypeConversion CanConvert(Identifier To)
        {
            return base.CanConvert(To);
        }
    }

    public class ObjectType : BuiltinType
    {
        public ObjectType(IdContainer Container, CodeString Name)
            : base(Container, Name)
        {
            this.UndeclaredIdType = UndeclaredIdType.Object;
            this.TypeFlags |= TypeFlags.ReferenceValue;
            this.ConstValueType = ConstValueType.Unknown;
            this.Size = this.Align = Container.State.Arch.RegSize;
        }

        internal override bool CanOpApplied_Base(Operator Op, Type SrcType)
        {
            return base.CanOpApplied_Base(Op, SrcType) || Operators.IsRefEquality(Op) ||
                Op == Operator.Equality || Op == Operator.Inequality;
        }

        public override bool IsEquivalent(Identifier Id)
        {
            return Id.RealId is ObjectType || base.IsEquivalent(Id);
        }

        public override TypeConversion CanConvert(Identifier To)
        {
            if (To.RealId is ObjectType) return TypeConversion.Automatic;
            if (Identifiers.IsBoxing(To, this)) return TypeConversion.Convertable;

            if (Identifiers.IsSubtypeOf(To, this))
                return TypeConversion.Convertable;

            return base.CanConvert(To);
        }

        public override bool CalculateLayout()
        {
            if (!LayoutCalculated && UnderlyingType != null)
            {
                if (!base.CalculateLayout()) return false;
                if (!UnderlyingType.CalculateLayout()) return false;

                InstanceSize = UnderlyingType.InstanceSize;
                LayoutCalculated = true;
                return true;
            }

            return true;
        }
    }

    public class StringType : BuiltinType
    {
        public StringType(IdContainer Container, CodeString Name)
            : base(Container, Name)
        {
            this.UndeclaredIdType = UndeclaredIdType.String;
            this.TypeFlags |= TypeFlags.ReferenceValue;
            this.ConstValueType = ConstValueType.String;
            this.Size = this.Align = Container.State.Arch.RegSize;
        }

        internal override bool CanOpApplied_Base(Operator Op, Type SrcType)
        {
            return base.CanOpApplied_Base(Op, SrcType) || ((Op == Operator.Add ||
                Op == Operator.Equality || Op == Operator.Inequality) && SrcType is StringType);
        }

        public override bool IsEquivalent(Identifier Id)
        {
            return Id.RealId is StringType || base.IsEquivalent(Id);
        }

        public override TypeConversion CanConvert(Identifier To)
        {
            if (To.RealId is ObjectType) return TypeConversion.Automatic;
            return base.CanConvert(To);
        }

        public override bool CalculateLayout()
        {
            if (!LayoutCalculated && UnderlyingType != null)
            {
                if (!base.CalculateLayout()) return false;
                if (!UnderlyingType.CalculateLayout()) return false;

                InstanceSize = UnderlyingType.InstanceSize;
                LayoutCalculated = true;
                return true;
            }

            return true;
        }
    }

    public class VoidType : BuiltinType
    {
        public VoidType(IdContainer Container, CodeString Name)
            : base(Container, Name)
        {
            UndeclaredIdType = UndeclaredIdType.Void;
            TypeFlags = TypeFlags.CanBePointer;
        }

        public override bool IsEquivalent(Identifier Id)
        {
            return Id.RealId is VoidType || base.IsEquivalent(Id);
        }

        public override TypeConversion CanConvert(Identifier To)
        {
            return TypeConversion.Nonconvertable;
        }

        internal override bool CanOpApplied_Base(Operator Op, Type SrcType)
        {
            return false;
        }
    }

    public class NamespaceType : Type
    {
        public NamespaceType(IdContainer Container, CodeString Name)
            : base(Container, Name)
        {
            UndeclaredIdType = UndeclaredIdType.Namespace;
            TypeFlags = TypeFlags.None;
        }

        public override bool IsEquivalent(Identifier Id)
        {
            return Id.RealId is NamespaceType;
        }

        public override TypeConversion CanConvert(Identifier To)
        {
            return TypeConversion.Nonconvertable;
        }

        internal override bool CanOpApplied_Base(Operator Op, Type SrcType)
        {
            return false;
        }
    }

    public abstract class NumberType : BuiltinType
    {
        public NumberType(IdContainer Container, CodeString Name, int Size)
            : base(Container, Name)
        {
            this.Size = Size;
            this.Align = Size;
        }

        internal override bool CanOpApplied_Base(Operator Op, Type SrcType)
        {
            if (Operators.IsIncDec(Op))
                return true;

            if (Operators.IsRelEquality(Op) || Operators.IsArithmetical(Op) || Operators.IsRange(Op))
            {
                if (SrcType is NumberType)
                    return Container.GetRetType(this, SrcType) != null;

                if (SrcType is TupleType)
                {
                    var Tuple = SrcType as TupleType;
                    return Tuple.TrueForAllMembers(x =>
                    {
                        var xType = x.TypeOfSelf.RealId as Type;
                        return this.CanOpApplied(Op, xType);
                    });
                }
            }

            if (base.CanOpApplied_Base(Op, SrcType)) return true;
            return false;
        }

        public override bool IsEquivalent(Identifier Id)
        {
            if (base.IsEquivalent(Id))
                return true;

            var NType = Id.RealId as NumberType;
            if (NType == null || !GetType().IsEquivalentTo(NType.GetType()))
                return false;

            return Size == NType.Size;
        }

        public override TypeConversion CanConvert(Identifier To)
        {
            var NTo = To.RealId as NumberType;
            if (NTo != null) return TypeConversion.Convertable;
            return base.CanConvert(To);
        }
    }

    public abstract class NonFloatType : NumberType
    {
        public BigInteger MinValue;
        public BigInteger MaxValue;

        public NonFloatType(IdContainer Container, CodeString Name, int Size)
            : base(Container, Name, Size)
        {
            this.ConstValueType = ConstValueType.Integer;
        }

        internal override bool CanOpApplied_Base(Operator Op, Type SrcType)
        {
            if ((Op == Operator.Add || Op == Operator.Subract) && SrcType is PointerType &&
                !((SrcType as PointerType).Child is VoidType)) return true;

            if (base.CanOpApplied_Base(Op, SrcType) || Op == Operator.Complement) return true;
            if ((Operators.IsShift(Op) || Operators.IsBitwise(Op)) && SrcType is NonFloatType) return true;
            return false;
        }

        public override TypeConversion CanConvert(Identifier To)
        {
            if (To.RealId is FloatType) return TypeConversion.Automatic;
            if (To.RealId is CharType) return TypeConversion.Convertable;

            if (To.RealId is EnumType)
            {
                var Res = CanConvert((To.RealId as EnumType).TypeOfValues);
                if (Res == TypeConversion.Nonconvertable) return Res;
                else return TypeConversion.Convertable;
            }

            var PTo = To.RealId as PointerType;
            if (PTo != null && PTo.Size == Size)
                return TypeConversion.Convertable;

            return base.CanConvert(To);
        }
    }

    public class SignedType : NonFloatType
    {
        public SignedType(IdContainer Container, CodeString Name, int Size)
            : base(Container, Name, Size)
        {
            MaxValue = BigInteger.Pow(2, Size * 8 - 1) - 1;
            MinValue = -BigInteger.Pow(2, Size * 8 - 1);

            if (Size == 1) UndeclaredIdType = UndeclaredIdType.SByte;
            else if (Size == 2) UndeclaredIdType = UndeclaredIdType.Int16;
            else if (Size == 4) UndeclaredIdType = UndeclaredIdType.Int32;
            else if (Size == 8) UndeclaredIdType = UndeclaredIdType.Int64;
            else throw new ApplicationException();
        }

        internal override bool CanOpApplied_Base(Operator Op, Type SrcType)
        {
            return Op == Operator.UnaryPlus || Op == Operator.Negation || base.CanOpApplied_Base(Op, SrcType);
        }

        public override TypeConversion CanConvert(Identifier To)
        {
            var STo = To.RealId as SignedType;
            if (STo != null && STo.Size >= Size)
                return TypeConversion.Automatic;

            return base.CanConvert(To);
        }
    }

    public class UnsignedType : NonFloatType
    {
        public UnsignedType(IdContainer Container, CodeString Name, int Size)
            : base(Container, Name, Size)
        {
            MaxValue = BigInteger.Pow(2, Size * 8) - 1;
            MinValue = new BigInteger(0);

            if (Size == 1) UndeclaredIdType = UndeclaredIdType.Byte;
            else if (Size == 2) UndeclaredIdType = UndeclaredIdType.UInt16;
            else if (Size == 4) UndeclaredIdType = UndeclaredIdType.UInt32;
            else if (Size == 8) UndeclaredIdType = UndeclaredIdType.UInt64;
            else throw new ApplicationException();
        }

        public override TypeConversion CanConvert(Identifier To)
        {
            if (To.RealId is NumberType)
            {
                var NTo = To.RealId as NumberType;
                if (NTo is UnsignedType && NTo.Size >= Size)
                    return TypeConversion.Automatic;

                if (NTo is SignedType && NTo.Size > Size)
                    return TypeConversion.Automatic;
            }

            return base.CanConvert(To);
        }
    }

    public class CharType : BuiltinType
    {
        public CharType(IdContainer Container, CodeString Name)
            : base(Container, Name)
        {
            this.Size = 2;
            this.Align = 2;
            this.ConstValueType = ConstValueType.Char;
            this.UndeclaredIdType = UndeclaredIdType.Char;
        }

        internal override bool CanOpApplied_Base(Operator Op, Type SrcType)
        {
            if (SrcType == null)
            {
                if (Operators.IsIncDec(Op)) return true;
                if (Op == Operator.Complement || Op == Operator.Negation || Op == Operator.UnaryPlus)
                    return true;
            }
            else if (SrcType.RealId is NumberType || SrcType.RealId is CharType)
            {
                if (Operators.IsRelEquality(Op) || Operators.IsArithmetical(Op)) return true;
                if ((Operators.IsShift(Op) || Operators.IsBitwise(Op))) return true;
            }

            return base.CanOpApplied_Base(Op, SrcType);
        }

        public override TypeConversion CanConvert(Identifier To)
        {
            if (To.RealId is NonFloatType)
                return TypeConversion.Convertable;

            return base.CanConvert(To);
        }

        public override bool IsEquivalent(Identifier Id)
        {
            return Id.RealId is CharType || base.IsEquivalent(Id);
        }
    }

    public class FloatType : NumberType
    {
        public FloatType(IdContainer Container, CodeString Name, int Size)
            : base(Container, Name, Size)
        {
            if (Size == 4)
            {
                this.ConstValueType = ConstValueType.Float;
                this.UndeclaredIdType = UndeclaredIdType.Single;
            }
            else if (Size == 8)
            {
                this.ConstValueType = ConstValueType.Double;
                this.UndeclaredIdType = UndeclaredIdType.Double;
            }
            else
            {
                throw new ApplicationException();
            }
        }

        internal override bool CanOpApplied_Base(Operator Op, Type SrcType)
        {
            return base.CanOpApplied_Base(Op, SrcType) || Op == Operator.UnaryPlus || Op == Operator.Negation;
        }

        public override TypeConversion CanConvert(Identifier To)
        {
            var FTo = To.RealId as FloatType;
            if (FTo != null && FTo.Size >= Size)
                return TypeConversion.Automatic;

            return base.CanConvert(To);
        }
    }

    public class BooleanType : BuiltinType
    {
        public BooleanType(IdContainer Container, CodeString Name)
            : base(Container, Name)
        {
            this.Size = 1;
            this.Align = 1;
            this.ConstValueType = ConstValueType.Boolean;
            this.UndeclaredIdType = UndeclaredIdType.Boolean;
        }

        internal override bool CanOpApplied_Base(Operator Op, Type SrcType)
        {
            if (Op == Operator.Not) return true;
            if (SrcType is BooleanType && (Operators.IsLogical(Op) || Op == Operator.Equality || Op == Operator.Inequality))
                return true;

            return base.CanOpApplied_Base(Op, SrcType);
        }

        public override bool IsEquivalent(Identifier Id)
        {
            return Id.RealId is BooleanType || base.IsEquivalent(Id);
        }
    }

    public class FlagType : EnumType
    {
        public FlagType(IdContainer Container, CodeString Name, Type TypeOfValues)
            : base(Container, Name, TypeOfValues)
        {
            this.DeclaredIdType = DeclaredIdType.Flag;
        }

        public FlagType(IdContainer Container, CodeString Name, CodeString Str_TypeOfValues)
            : base(Container, Name, Str_TypeOfValues)
        {
            this.Str_TypeOfValues = Str_TypeOfValues;
            this.DeclaredIdType = DeclaredIdType.Flag;
        }

        internal override bool CanOpApplied_Base(Operator Op, Type SrcType)
        {
            return base.CanOpApplied_Base(Op, SrcType) || Operators.IsBitwise(Op);
        }
    }

    public class EnumType : Type
    {
        public CodeString Str_TypeOfValues;
        public EnumScope EnumScope;

        public Type TypeOfValues
        {
            get
            {
                if (Children[0] == null) return null;
                return Children[0].RealId as Type;
            }
        }

        public override void Update()
        {
            this.Size = TypeOfValues.Size;
            this.Align = TypeOfValues.Align;
        }

        public override bool HasScopes
        {
            get { return true; }
        }

        public override IEnumerable<ScopeNode> EnumScopes
        {
            get { yield return EnumScope; }
        }

        public EnumType(IdContainer Container, CodeString Name, Identifier TypeOfValues)
            : base(Container, Name)
        {
            this.Children = new Identifier[] { TypeOfValues };
            this.ConstValueType = ConstValueType.Integer;
            this.DeclaredIdType = DeclaredIdType.Enum;
            Update();
        }

        public EnumType(IdContainer Container, CodeString Name, CodeString Str_TypeOfValues)
            : base(Container, Name)
        {
            this.Str_TypeOfValues = Str_TypeOfValues;
            this.ConstValueType = ConstValueType.Integer;
            this.DeclaredIdType = DeclaredIdType.Enum;
            this.Children = new Identifier[] { null };
        }

        public ConstVariable GetValue(string Name)
        {
            var Res = EnumScope.GetContainerId(Name, x => x is ConstVariable);
            if (Res.Count == 0) return null;
            else if (Res.Count == 1) return Res[0].Identifier as ConstVariable;
            else throw new ApplicationException();
        }

        public ConstVariable GetValue(CompilerState State, CodeString Name)
        {
            var Ret = GetValue(Name.ToString());
            if (Ret == null)
                State.Messages.Add(MessageId.UnknownId, Name);

            return Ret;
        }

        public ConstVariable GetValue(BigInteger Value)
        {
            var Members = EnumScope.IdentifierList;
            for (var i = 0; i < Members.Count; i++)
            {
                var EnumVal = Members[i] as ConstVariable;
                if (EnumVal == null) continue;

                var Val = EnumVal.ConstInitValue as IntegerValue;
                if (Val != null && Val.Value == Value) return EnumVal;
            }

            return null;
        }

        internal override bool CanOpApplied_Base(Operator Op, Type SrcType)
        {
            return base.CanOpApplied_Base(Op, SrcType) ||
                Op == Operator.Equality || Op == Operator.Inequality;
        }

        public override TypeConversion CanConvert(Identifier To)
        {
            if (TypeOfValues.CanConvert(To) != TypeConversion.Nonconvertable)
                return TypeConversion.Convertable;

            return base.CanConvert(To);
        }
    }

    public enum ReferenceMode : byte
    {
        Unsafe,
        IdMustBeAssigned,
        IdGetsAssigned,
    }

    public class ReferenceType : Type
    {
        public ReferenceMode Mode;

        public Type Child
        {
            get { return Children[0].RealId as Type; }
        }

        public override void Update()
        {
            GenerateName();
            CalculateAccess();
        }

        public ReferenceType(IdContainer Container, CodeString Name, Identifier Child, ReferenceMode Mode, bool DoUpdate = true)
            : base(Container, Name)
        {
            Setup();
            this.Children = new Identifier[] { Child };
            this.Mode = Mode;
            if (Child != null) Access = Child.Access;
            if (DoUpdate) Update();
        }

        private void Setup()
        {
            UndeclaredIdType = UndeclaredIdType.Reference;
            ConstValueType = ConstValueType.Integer;
            TypeFlags = TypeFlags.CanBeVariable;
            Size = Container.State.Arch.RegSize;
            Align = Size;
        }

        public ReferenceType(IdContainer Container, Identifier Child, ReferenceMode Mode, bool Calc = true)
            : base(Container, new CodeString())
        {
            Setup();
            this.Children = new Identifier[] { Child };
            this.Mode = Mode;
            if (Child != null) Access = Child.Access;
            if (Calc) Update();
        }

        public override bool IsEquivalent(Identifier Id)
        {
            var RType = Id.RealId as ReferenceType;
            if (RType == null) return false;

            if (Mode != RType.Mode) return false;
            return Child.IsEquivalent(RType.Child);
        }

        public override TypeConversion CanConvert(Identifier To)
        {
            var RType = To.RealId as ReferenceType;
            if (RType == null) return TypeConversion.Nonconvertable;

            if (Child.IsEquivalent(RType.Child)) return TypeConversion.Automatic;
            if (Child is AutomaticType) return TypeConversion.Automatic;
            return TypeConversion.Nonconvertable;
        }
    }

    public class TypeOfFunction : Type
    {
        public CallingConvention CallConv;

        public Type RetType
        {
            get { return Children[0].RealId as Type; }
        }

        public override void Update()
        {
            GenerateName();
            CalculateAccess();
        }

        public TypeOfFunction(IdContainer Container, CallingConvention Conv, Identifier[] Children, bool Calc = true)
            : base(Container, new CodeString())
        {
            if (Conv == CallingConvention.Unknown)
                throw new ApplicationException();

            this.UndeclaredIdType = UndeclaredIdType.Function;
            this.Size = Container.State.Arch.RegSize;
            this.Align = this.Size;

            this.Children = Children;
            this.CallConv = Conv;
            if (Calc) Update();
        }

        public TypeOfFunction(IdContainer Container, CallingConvention Conv, Identifier RetType,
            FunctionParameter[] Params, bool Calc = true) : base(Container, new CodeString())
        {
            if (Conv == CallingConvention.Unknown)
                throw new ApplicationException();

            if (Params != null)
            {
                Children = new Identifier[Params.Length + 1];
                Children[0] = RetType;
                Params.CopyTo(Children, 1);
            }
            else
            {
                Children = new Identifier[] { RetType };
            }

            this.UndeclaredIdType = UndeclaredIdType.Function;
            this.Size = Container.State.Arch.RegSize;
            this.Align = this.Size;
            this.CallConv = Conv;
            if (Calc) Update();
        }

        public override TypeConversion CanConvert(Identifier To)
        {
            if (To.RealId is PointerType)
            {
                var PTo = To.RealId as PointerType;
                if (PTo.Child is VoidType) return TypeConversion.Convertable;
            }
            else if (To.RealId is NonstaticFunctionType)
            {
                var FTo = To.RealId as NonstaticFunctionType;
                if (IsEquivalent(FTo.Child)) return TypeConversion.Automatic;
            }

            return base.CanConvert(To);
        }

        public override bool IsEquivalent(Identifier Id)
        {
            var FType = Id as TypeOfFunction;
            if (FType == null) return false;

            if (CallConv != FType.CallConv) return false;
            if (!RetType.IsEquivalent(FType.RetType)) return false;

            var C = Children.Length;
            if (C != FType.Children.Length) return false;

            for (var i = 1; i < C; i++)
            {
                if (!Children[i].TypeOfSelf.IsEquivalent(FType.Children[i].TypeOfSelf))
                    return false;
            }

            return true;
        }

        internal override bool CanOpApplied_Base(Operator Op, Type SrcType)
        {
            if (Op == Operator.Call || Op == Operator.Equality || Op == Operator.Inequality)
                return true;

            return base.CanOpApplied_Base(Op, SrcType);
        }

        public Type[] GetTypes()
        {
            var Ret = new Type[Children.Length - 1];
            for (var i = 1; i < Children.Length; i++)
                Ret[i - 1] = Children[i].TypeOfSelf.RealId as Type;

            return Ret;
        }

        public int RequiredParameters
        {
            get
            {
                for (var i = 1; i < Children.Length; i++)
                {
                    var Param = Children[i] as FunctionParameter;
                    if (Param.ConstInitValue != null) return i - 1;
                }

                return Children.Length - 1;
            }
        }

        public FunctionParameter GetParameter(string Name)
        {
            for (var i = 1; i < Children.Length; i++)
            {
                var Param = Children[i] as FunctionParameter;
                if (Param.Name.IsEqual(Name)) return Param;
            }

            return null;
        }
    }

    public abstract class ArrayType : BuiltinType
    {
        public int Dimensions;

        public ArrayType(IdContainer Container, Identifier TypeOfVals)
            : base(Container, new CodeString())
        {
            this.Children = new Identifier[] { TypeOfVals };
        }

        public Type TypeOfValues
        {
            get { return Children[0].RealId as Type; }
        }

        public override TypeConversion CanConvert(Identifier To)
        {
            if (To.RealId is PointerType)
            {
                var PTo = To.RealId as PointerType;
                if (PTo.Child is VoidType)
                    return TypeConversion.Automatic;

                var ThisChild = TypeOfValues;
                if (ThisChild is AutomaticType || PTo.Child.IsEquivalent(ThisChild))
                    return TypeConversion.Automatic;
            }

            return base.CanConvert(To);
        }
    }

    public class RefArrayType : ArrayType
    {
        public RefArrayType(IdContainer Container, Identifier TypeOfVals, int Dimensions, bool Update = true)
            : base(Container, TypeOfVals)
        {
            var Arch = Container.State.Arch;
            this.Align = Arch.RegSize;
            this.Size = Arch.RegSize;
            this.UndeclaredIdType = UndeclaredIdType.RefArrayType;
            this.Dimensions = Dimensions;
            this.TypeFlags |= TypeFlags.ReferenceValue;
            if (Update) this.Update();
        }

        public override void Update()
        {
            GenerateName();
            CalculateAccess();
        }

        public override bool IsEquivalent(Identifier Id)
        {
            if (Id.RealId is RefArrayType)
            {
                var Arr = Id.RealId as RefArrayType;
                if (Arr.Dimensions != Dimensions) return false;
                if (!Arr.TypeOfValues.IsEquivalent(TypeOfValues))
                    return false;

                return true;
            }

            return base.IsEquivalent(Id);
        }

        public int OffsetToDimensions
        {
            get
            {
                var Base = Container.GlobalContainer.CommonIds.Array;
                if (Base == null) return 0;

                return (Identifiers.GetMember(Container.State, Base,
                    new CodeString("Dimensions")) as MemberVariable).Offset;
            }
        }

        public int OffsetToData
        {
            get
            {
                var Arch = Container.State.Arch;
                return OffsetToDimensions + Arch.RegSize * Dimensions;
            }
        }

        public override TypeConversion CanConvert(Identifier To)
        {
            if (Children[0].RealId is AutomaticType && To.RealId is RefArrayType)
                return TypeConversion.Automatic;

            var Base = Container.GlobalContainer.CommonIds.Array;
            if (To.RealId is ObjectType || To.IsEquivalent(Base))
                return TypeConversion.Automatic;

            if (To.RealId is PointerAndLength && Dimensions == 1)
            {
                var PAndLTo = To.RealId as PointerAndLength;
                var PAndLChild = PAndLTo.Child.RealId as Type;
                var ThisChild = TypeOfValues;

                if (ThisChild is AutomaticType || PAndLChild.IsEquivalent(ThisChild))
                    return TypeConversion.Automatic;
            }

            return base.CanConvert(To);
        }

        internal override bool CanOpApplied_Base(Operator Op, Type SrcType)
        {
            if (Operators.IsRefEquality(Op) || Op == Operator.Equality ||
                Op == Operator.Inequality || Op == Operator.Member)
            {
                return true;
            }

            return base.CanOpApplied_Base(Op, SrcType);
        }

        public override bool CalculateLayout()
        {
            if (!LayoutCalculated)
            {
                if (!base.CalculateLayout()) return false;

                var Base = Container.GlobalContainer.CommonIds.Array;
                var SBase = Base.UnderlyingStructureOrRealId as StructuredType;
                if (!Base.CalculateLayout()) return false;

                InstanceSize = SBase.InstanceSize;
                LayoutCalculated = true;
            }

            return true;
        }
    }

    public class NonrefArrayType : ArrayType
    {
        public int[] Lengths;
        public int ElementSize;
        public int Pow2Size;

        public int Length
        {
            get
            {
                var Ret = Lengths[0];
                for (var i = 1; i < Lengths.Length; i++)
                    Ret *= Lengths[i];

                return Ret;
            }
        }

        public NonrefArrayType(IdContainer Container, Identifier TypeOfVals, int[] Lengths, bool Calc = true)
            : base(Container, TypeOfVals)
        {
            this.Lengths = Lengths;
            this.Dimensions = Lengths == null ? 1 : Lengths.Length;
            this.UndeclaredIdType = UndeclaredIdType.NonrefArrayType;

            if (Lengths == null) TypeFlags |= TypeFlags.UnfixedSize;
            if (Calc) Update();
        }

        public override void Update()
        {
            this.Align = TypeOfValues.Align;
            GenerateName();
            CalculateAccess();
        }

        public override TypeConversion CanConvert(Identifier To)
        {
            if (To.RealId is NonrefArrayType)
            {
                if (Children[0].RealId is AutomaticType)
                    return TypeConversion.Automatic;

                var FTo = To.RealId as NonrefArrayType;
                if (FTo.TypeOfValues.IsEquivalent(TypeOfValues) && Lengths == null)
                    return TypeConversion.Convertable;
            }

            if (To.RealId is PointerAndLength && Lengths != null && Lengths.Length == 1)
            {
                var PAndLTo = To.RealId as PointerAndLength;
                var PAndLChild = PAndLTo.Child.RealId as Type;
                var ThisChild = TypeOfValues;

                if (ThisChild is AutomaticType || PAndLChild.IsEquivalent(ThisChild))
                    return TypeConversion.Automatic;
            }

            if (To.RealId is RefArrayType && Lengths != null)
            {
                var ArrTo = To.RealId as RefArrayType;
                var ArrChild = ArrTo.TypeOfValues;
                var ThisChild = TypeOfValues;

                if (ArrTo.Dimensions == Lengths.Length)
                {
                    if (ThisChild is AutomaticType || ArrChild.IsEquivalent(ThisChild))
                        return TypeConversion.Automatic;
                }
            }

            return base.CanConvert(To);
        }

        internal override bool CanOpApplied_Base(Operator Op, Type SrcType)
        {
            if (Op == Operator.Index) return true;

            if (Lengths == null) return false;
            return base.CanOpApplied_Base(Op, SrcType);
        }

        public void CalcSize()
        {
            if (Lengths == null) return;

            var ValType = TypeOfValues;
            ElementSize = DataStoring.AlignWithIncrease(ValType.Size, ValType.Align);

            var Size = ElementSize;
            foreach (var e in Lengths)
                Size *= e;

            Pow2Size = Container.State.CalcPow2Size(Size);
            this.Size = Pow2Size;
        }

        public override bool IsEquivalent(Identifier Id)
        {
            var FArr = Id.RealId as NonrefArrayType;
            if (FArr != null && FArr.TypeOfValues.IsEquivalent(TypeOfValues))
            {
                if (FArr.Lengths == null && Lengths == null)
                    return true;

                if (FArr.Lengths.Length != Lengths.Length)
                    return false;

                for (var i = 0; i < Lengths.Length; i++)
                    if (Lengths[i] != FArr.Lengths[i]) return false;

                return true;
            }

            return false;
        }

        public override bool CalculateLayout()
        {
            CalcSize();
            return base.CalculateLayout();
        }
    }
}
