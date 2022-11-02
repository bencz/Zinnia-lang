using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Numerics;
using Zinnia.Base;

namespace Zinnia
{
    public enum StructureBaseFlags : byte
    {
        None = 0,
        Virtual = 1,
        Unreal = 2,
    }

    public struct StructureBase
    {
        public StructureBaseFlags Flags;
        public Identifier Base;
        public CodeString Declaration;
        public CodeString Name;
        public int Offset;

        public StructureBase(CodeString Declaration, CodeString Name = new CodeString(),
            StructureBaseFlags Flags = StructureBaseFlags.None)
        {
            this.Base = null;
            this.Declaration = Declaration;
            this.Name = Name;
            this.Offset = -1;
            this.Flags = Flags;
        }

        public StructureBase(Identifier Identifier, StructureBaseFlags Flags = StructureBaseFlags.None)
        {
            this.Base = Identifier;
            this.Declaration = new CodeString();
            this.Name = new CodeString();
            this.Offset = -1;
            this.Flags = Flags;
        }
    }

    public struct VirtualListNode
    {
        public VirtualListNode[] Children;
        public MemberFunction[] Virtuals;

        public void Override(MemberFunction Old, MemberFunction New)
        {
            for (var i = 0; i < Virtuals.Length; i++)
                if (Virtuals[i] == Old) Virtuals[i] = New;

            for (var i = 0; i < Children.Length; i++)
                Children[i].Override(Old, New);
        }

        public VirtualListNode Copy()
        {
            var Ret = new VirtualListNode();
            Ret.Children = new VirtualListNode[Children.Length];
            for (var i = 0; i < Children.Length; i++)
                Ret.Children[i] = Children[i].Copy();

            Ret.Virtuals = Virtuals.ToArray();
            return Ret;
        }
    }

    public abstract class StructuredType : Type
    {
        public StructuredScope StructuredScope;
        public StructureBase[] BaseStructures;
        public Guid? Guid;
        public int VarSize;

        public VirtualListNode Virtuals;
        public AutoAllocatedList<MemberFunction> OldVirtuals;
        public bool HasFunctionTable;
        public int FunctionTableIndex = -1;

        public List<IdentifierFound> SearchBase(IdContainer Container, string Name, Predicate<Identifier> Func = null)
        {
            var Out = new List<IdentifierFound>();
            SearchBase(Container, Name, Out, Func);
            return Out;
        }

        public bool IsSubstructureOf(Identifier Id)
        {
            for (var i = 0; i < BaseStructures.Length; i++)
            {
                var BaseId = BaseStructures[i].Base;
                if (BaseId.IsEquivalent(Id)) return true;

                var Structure = BaseId.UnderlyingStructureOrRealId as StructuredType;
                if (Structure != null && Structure.IsSubstructureOf(Id)) return true;
            }

            return false;
        }

        public void SearchBase(IdContainer Container, string Name, List<IdentifierFound> Out, Predicate<Identifier> Func = null)
        {
            for (var i = 0; i < BaseStructures.Length; i++)
                if (Name == null || BaseStructures[i].Name.IsEqual(Name))
                {
                    if (Func == null || Func(BaseStructures[i].Base))
                        Out.Add(new IdentifierFound(Container, BaseStructures[i].Base));
                }

            if (Out.Count == 0)
            {
                for (var i = 0; i < BaseStructures.Length; i++)
                {
                    var BaseId = BaseStructures[i].Base;
                    var Structure = BaseId.UnderlyingStructureOrRealId as StructuredType;
                    if (Structure != null) Structure.SearchBase(Container, Name, Out, Func);
                }
            }
        }

        public override void Update()
        {
            Children = new Identifier[BaseStructures.Length];
            for (var i = 0; i < BaseStructures.Length; i++)
                Children[i] = BaseStructures[i].Base;

            base.Update();
        }

        public string FunctionTableLabel
        {
            get { return AssemblyName + "_%FunctionTable"; }
        }

        public bool HasNonstaticConstructor
        {
            get
            {
                for (var i = 0; i < StructuredScope.IdentifierList.Count; i++)
                {
                    var Ctor = StructuredScope.IdentifierList[i] as Constructor;
                    if (Ctor != null && (Ctor.Flags & IdentifierFlags.Static) == 0)
                        return true;
                }

                return false;
            }
        }

        public bool HasParameterLessCtor
        {
            get
            {
                var HasConstructor = false;
                for (var i = 0; i < StructuredScope.IdentifierList.Count; i++)
                {
                    var Ctor = StructuredScope.IdentifierList[i] as Constructor;
                    if (Ctor == null || (Ctor.Flags & IdentifierFlags.Static) != 0) continue;

                    var Type = Ctor.TypeOfSelf.RealId as TypeOfFunction;
                    if (Type.Children.Length == 1) return true;
                    HasConstructor = true;
                }

                return !HasConstructor;
            }
        }

        public override void GetAssembly(CodeGenerator CG, GetAssemblyMode Mode)
        {
            if (Mode == GetAssemblyMode.InitedValues && HasFunctionTable)
            {
                CG.InsContainer.Label(FunctionTableLabel);
                for (var i = 0; i < OldVirtuals.Count; i++)
                    CG.DeclareLabelPtr(OldVirtuals[i].AssemblyName);

                CG.InsContainer.Add("\n");
            }

            base.GetAssembly(CG, Mode);
        }

        public StructuredType(IdContainer Container, CodeString Name)
            : base(Container, Name)
        {
        }

        public MemberVariable GetMemberAt(int Offset, int Size = 1)
        {
            if (Offset < 0) throw new ArgumentOutOfRangeException("Offset");

            var Members = StructuredScope.IdentifierList;
            for (var i = 0; i < Members.Count; i++)
            {
                var NextOffset = InstanceSize;
                if (i < Members.Count - 1)
                {
                    var NextVar = Members[i + 1] as MemberVariable;
                    if (NextVar != null) NextOffset = NextVar.Offset;
                }

                var Var = Members[i] as MemberVariable;
                if (Var != null && Var.Offset >= Offset && Offset + Size <= NextOffset)
                    return Var;
            }

            return null;
        }

        public bool TrueForAllMembers(Predicate<Identifier> Func)
        {
            return StructuredScope.IdentifierList.TrueForAll(Func);
        }

        internal override bool CanOpApplied_Base(Operator Op, Type SrcType)
        {
            if (Op == Operator.Member || Op == Operator.NewObject) return true;
            if (Op == Operator.Equality || Op == Operator.Inequality) return true;
            return base.CanOpApplied_Base(Op, SrcType);
        }

        public bool NewCalculateLayout()
        {
            if (!LayoutCalculated)
            {
                if (!base.CalculateLayout())
                    return false;

                LayoutCalculated = true;
            }

            return true;
        }

        public override bool CalculateLayout()
        {
            if (!LayoutCalculated)
            {
                if (!base.CalculateLayout())
                    return false;

                VarSize = 0;
                LayoutCalculated = true;
                return CalculateOffsets();
            }

            return true;
        }

        public virtual bool CalculateOffsets()
        {
            var Members = StructuredScope.IdentifierList;
            for (var i = 0; i < Members.Count; i++)
            {
                var MemVar = Members[i] as MemberVariable;
                if (MemVar != null)
                {
                    var Type = MemVar.TypeOfSelf.RealId as Type;

                    var Struct = Type as StructType;
                    if (Struct != null && !Struct.CalculateLayout()) return false;

                    MemVar.Offset = DataStoring.AlignWithIncrease(VarSize, Type.Align);
                    VarSize = MemVar.Offset + Type.Size;
                }
            }

            InstanceSize = Container.State.CalcPow2Size(VarSize);
            return true;
        }

        public override bool HasScopes
        {
            get { return true; }
        }

        public override IEnumerable<ScopeNode> EnumScopes
        {
            get { yield return StructuredScope; }
        }

        public void CalcVirtuals()
        {
            OldVirtuals.Clear();

            var BaseStructures = this.BaseStructures;
            if ((TypeFlags & Zinnia.TypeFlags.ReferenceValue) == 0)
                BaseStructures = Identifiers.GetUnrealBases(this);

            if (BaseStructures.Length == 1)
            {
                var Base = BaseStructures[0].Base;
                var SBase = Base.UnderlyingStructureOrRealId as StructuredType;

                SBase.CalcVirtuals();
                OldVirtuals.AddRange(SBase.OldVirtuals);
            }
            else if (BaseStructures.Length > 1)
            {
                throw new NotImplementedException();
            }

            var Members = StructuredScope.IdentifierList;
            for (var i = 0; i < Members.Count; i++)
            {
                var Id = Members[i];
                if ((Id.Flags & IdentifierFlags.Virtual) != 0)
                {
                    if (Id is MemberFunction)
                    {
                        ProcessVirtual(Id as MemberFunction);
                    }
                    else if (Id is Property)
                    {
                        var Property = Id as Property;
                        var PropertyMembers = Property.PropertyScope.IdentifierList;

                        for (var j = 0; j < PropertyMembers.Count; j++)
                        {
                            var Func = PropertyMembers[j] as MemberFunction;
                            if (Func != null) ProcessVirtual(Func);
                        };
                    }
                }
            }

            if ((Flags & IdentifierFlags.Abstract) == 0)
                HasFunctionTable = OldVirtuals.Count > 0;
        }

        private void ProcessVirtual(MemberFunction Func)
        {
            Func.SetUsed();
            if ((Func.Flags & IdentifierFlags.Override) != 0)
            {
                var Overridden = Func.OverriddenId.RealId as MemberFunction;
                Func.VirtualIndex = Overridden.VirtualIndex;
                OldVirtuals[Func.VirtualIndex] = Func;
            }
            else
            {
                Func.VirtualIndex = OldVirtuals.Count;
                OldVirtuals.Add(Func);
            }
        }

        public override void GetGlobalPointers(List<string> Out)
        {
            if (HasFunctionTable)
            {
                FunctionTableIndex = Out.Count;
                Out.Add(FunctionTableLabel);
            }
            else
            {
                FunctionTableIndex = -1;
            }
        }
    }

    public class ClassType : StructuredType
    {
        public override bool CalculateOffsets()
        {
            CalcVirtuals();

            if (BaseStructures.Length > 0)
            {
                if (BaseStructures.Length > 1)
                    throw new NotImplementedException();

                var Base = BaseStructures[0].Base;
                var SBase = Base.UnderlyingStructureOrRealId as StructuredType;
                if (!SBase.CalculateLayout()) return false;

                VarSize = SBase.InstanceSize;
            }
            else
            {
                VarSize = 0;
            }

            return base.CalculateOffsets();
        }

        public ClassType(IdContainer Container, CodeString Name, Identifier[] Children = null)
            : base(Container, Name)
        {
            this.Size = Container.State.Arch.RegSize;
            this.ConstValueType = ConstValueType.Unknown;
            this.DeclaredIdType = DeclaredIdType.Class;
            this.TypeFlags |= TypeFlags.ReferenceValue;

            this.Align = this.Size;
            this.Children = Children;
        }

        public override TypeConversion CanConvert(Identifier To)
        {
            if (Identifiers.IsSubtypeOf(this, To)) return TypeConversion.Automatic;
            else if (Identifiers.IsSubtypeOf(To, this)) return TypeConversion.Convertable;
            else if (Identifiers.IsBoxing(To, this)) return TypeConversion.Convertable;
            else return base.CanConvert(To);
        }

        internal override bool CanOpApplied_Base(Operator Op, Type SrcType)
        {
            return base.CanOpApplied_Base(Op, SrcType) || Operators.IsRefEquality(Op);
        }
    }

    public class StructType : StructuredType
    {
        public StructType(IdContainer Container, CodeString Name, Identifier[] Children = null)
            : base(Container, Name)
        {
            this.Children = Children;
            this.DeclaredIdType = DeclaredIdType.Struct;
        }

        public override TypeConversion CanConvert(Identifier To)
        {
            if (Identifiers.IsBoxing(this, To))
                return TypeConversion.Automatic;

            return base.CanConvert(To);
        }

        public bool CheckAlign()
        {
            var State = Container.State;
            for (var i = 0; i < StructuredScope.IdentifierList.Count; i++)
            {
                var Member = StructuredScope.IdentifierList[i] as MemberVariable;
                if (Member != null && Member.Align > Align)
                {
                    State.Messages.Add(MessageId.NotAlignedEnough, Member.Declaration);
                    return false;
                }
            }

            return true;
        }

        public override bool CalculateLayout()
        {
            var Members = StructuredScope.IdentifierList;
            for (var i = 0; i < Members.Count; i++)
                if (!Members[i].CalculateLayout()) return false;

            return base.CalculateLayout();
        }

        public override bool CalculateOffsets()
        {
            CalcVirtuals();
            if (!base.CalculateOffsets())
                return false;

            Size = InstanceSize;
            if (Align == 0)
                Align = Math.Min(Size, Container.State.Arch.MaxStructPow2Size);

            return CheckAlign();
        }
    }

    public class NonstaticFunctionType : TupleType
    {
        public Identifier Child
        {
            get { return Children[0]; }
        }

        public NonstaticFunctionType(IdContainer Container, Identifier Child, bool DoUpdate = true)
            : base(Container, new CodeString(), true)
        {
            Children = new Identifier[1] { Child };
            if (Child != null && !(Child is TypeOfFunction))
                throw new ArgumentException(null, "Child");

            this.UndeclaredIdType = UndeclaredIdType.NonstaticFunctionType;
            var Scope = new StructuredScope(Container, new CodeString(), this);
            StructuredScope = Scope;

            var SelfType = Container.GlobalContainer.CommonIds.Object;
            var Self = new MemberVariable(Scope, new CodeString("Self"), SelfType);
            Self.Access = IdentifierAccess.Public;
            Scope.IdentifierList.Add(Self);

            var PointerType = Container.GlobalContainer.CommonIds.VoidPtr;
            var Pointer = new MemberVariable(Scope, new CodeString("Pointer"), PointerType);
            Pointer.Access = IdentifierAccess.Public;
            Scope.IdentifierList.Add(Pointer);

            if (DoUpdate) Update();
        }

        internal override bool CanOpApplied_Base(Operator Op, Type SrcType)
        {
            if (Op == Operator.Call) return true;
            return base.CanOpApplied_Base(Op, SrcType);
        }
    }

    public class PointerAndLength : TupleType
    {
        public Identifier PointerType
        {
            get
            {
                var Members = StructuredScope.IdentifierList;
                return Members[0].Children[0];
            }
        }

        public Identifier Child
        {
            get
            {
                return PointerType.RealId.Children[0];
            }
        }

        public PointerAndLength(TupleType Tuple, bool DoUpdate = true)
            : base(Tuple.Container, new CodeString(), true)
        {
            this.UndeclaredIdType = UndeclaredIdType.PointerAndLength;
            StructuredScope = Tuple.StructuredScope;
            if (DoUpdate) Update();
        }

        public PointerAndLength(IdContainer Container, Identifier Child, bool DoUpdate = true)
            : base(Container, new CodeString(), true)
        {
            this.UndeclaredIdType = UndeclaredIdType.PointerAndLength;
            var Scope = new StructuredScope(Container, new CodeString(), this);
            StructuredScope = Scope;

            var PointerType = new PointerType(Container, Child, DoUpdate);
            var Pointer = new MemberVariable(Scope, new CodeString("Pointer"), PointerType);
            Pointer.Access = IdentifierAccess.Public;
            Scope.IdentifierList.Add(Pointer);

            var LengthType = Container.GlobalContainer.CommonIds.UIntPtr;
            var Length = new MemberVariable(Scope, new CodeString("Length"), LengthType);
            Length.Access = IdentifierAccess.Public;
            Scope.IdentifierList.Add(Length);

            if (DoUpdate) Update();
        }

        public override TypeConversion CanConvert(Identifier To)
        {
            var RChild = Child.RealId as Type;
            if (To.RealId is PointerType)
            {
                var PTo = To.RealId as PointerType;
                if (PTo.Child is VoidType) return TypeConversion.Automatic;
                else return RChild.CanConvert(PTo.Child);
            }

            return base.CanConvert(To);
        }

        public override bool IsEquivalent(Identifier Id)
        {
            var PAndLId = Id.RealId as PointerAndLength;
            return PAndLId != null && PAndLId.Child.IsEquivalent(Child);
        }
    }

    public class TupleType : StructType
    {
        public bool Named = false;

        public TupleType(IdContainer Container, CodeString Name, bool Named)
            : base(Container, Name)
        {
            this.UndeclaredIdType = UndeclaredIdType.Tuple;
            this.DeclaredIdType = DeclaredIdType.Unknown;
            this.Named = Named;

            this.BaseStructures = new StructureBase[0];
        }

        public TupleType(IdContainer Container, List<Identifier> Members)
            : base(Container, new CodeString())
        {
            this.UndeclaredIdType = UndeclaredIdType.Tuple;
            this.DeclaredIdType = DeclaredIdType.Unknown;
            var Scope = new StructuredScope(Container, new CodeString(), this);

            if (Members != null)
            {
                Scope.IdentifierList = Members;
                for (var i = 0; i < Members.Count; i++)
                    Members[i].Container = Scope;
            }

            this.StructuredScope = Scope;
            this.BaseStructures = new StructureBase[0];
            Update();
        }

        public TupleType(IdContainer Container, StructuredScope StructuredTypeScope)
            : base(Container, new CodeString())
        {
            this.UndeclaredIdType = UndeclaredIdType.Tuple;
            this.DeclaredIdType = DeclaredIdType.Unknown;
            this.StructuredScope = StructuredTypeScope;
            Update();
        }

        public bool OnlyHasNumberMembers
        {
            get
            {
                var Members = StructuredScope.IdentifierList;
                for (var i = 0; i < Members.Count; i++)
                {
                    if (!(Members[i].TypeOfSelf.RealId is NumberType))
                        return false;
                }

                return true;
            }
        }

        public override void CalculateAccess()
        {
            Access = IdentifierAccess.Public;
            var Members = StructuredScope.IdentifierList;
            for (var i = 0; i < Members.Count; i++)
            {
                var MemberAccess = Members[i].TypeOfSelf.Access;
                Access = Identifiers.IdAccessIntersect(Access, MemberAccess);
            }
        }

        public override void Update()
        {
            var Members = StructuredScope.IdentifierList;
            if (Members.Count > 0)
            {
                Named = Members[0].Name.IsValid;
                for (var i = 0; i < Members.Count; i++)
                {
                    if (Members[i].Name.IsValid != Named)
                        throw new ApplicationException();

                    var MemberType = Members[i].TypeOfSelf;
                    var RMemberType = MemberType.RealId as Type;

                    if ((RMemberType.TypeFlags & TypeFlags.CanBeVariable) == 0 ||
                        RMemberType is AutomaticType)
                    {
                        TypeFlags &= ~TypeFlags.CanBeEverything;
                    }
                }
            }
            else
            {
                Named = false;
            }

            GenerateName();
            CalculateAccess();
        }

        public override bool IsEquivalent(Identifier Id)
        {
            var V = Id.RealId as TupleType;
            if (V != null)
            {
                var Members = StructuredScope.IdentifierList;
                var VMembers = V.StructuredScope.IdentifierList;

                if (VMembers.Count != Members.Count) return false;
                for (var i = 0; i < Members.Count; i++)
                {
                    var Var1 = VMembers[i] as MemberVariable;
                    var Var2 = Members[i] as MemberVariable;
                    if (Var1 == null || Var2 == null) return false;

                    if (!Var2.TypeOfSelf.IsEquivalent(Var1.TypeOfSelf)) return false;
                }

                return true;
            }

            return base.IsEquivalent(Id);
        }

        public override TypeConversion CanConvert(Identifier To)
        {
            var Members = StructuredScope.IdentifierList;

            var V = To.RealId as TupleType;
            if (V != null)
            {
                var VMembers = V.StructuredScope.IdentifierList;
                if (VMembers.Count != Members.Count) return TypeConversion.Nonconvertable;
                else if (Members.Count == 0) return TypeConversion.Automatic;

                var Automatic = true;
                for (var i = 0; i < Members.Count; i++)
                {
                    var Var1 = Members[i] as MemberVariable;
                    var Var2 = VMembers[i] as MemberVariable;
                    if (Var1 == null || Var2 == null) return TypeConversion.Nonconvertable;

                    var Res = Var1.TypeOfSelf.CanConvert(Var2.TypeOfSelf);
                    if (Res == TypeConversion.Convertable) Automatic = false;
                    else if (Res == TypeConversion.Nonconvertable) return Res;
                }

                if (Automatic) return TypeConversion.Automatic;
                else return TypeConversion.Convertable;
            }

            return base.CanConvert(To);
        }

        internal override bool CanOpApplied_Base(Operator Op, Type SrcType)
        {
            if (Operators.IsRelEquality(Op) || Operators.IsArithmetical(Op) || Operators.IsRange(Op))
            {
                var TupleSrc = SrcType.RealId as TupleType;
                if (TupleSrc == null)
                {
                    return TrueForAllMembers(x =>
                    {
                        var xType = x.TypeOfSelf.RealId as Type;
                        return xType.CanOpApplied(Op, SrcType);
                    });
                }
                else
                {
                    var Members = StructuredScope.IdentifierList;
                    var VMembers = TupleSrc.StructuredScope.IdentifierList;

                    if (VMembers.Count != Members.Count) return false;
                    else if (Members.Count == 0) return true;

                    for (var i = 0; i < Members.Count; i++)
                    {
                        var Var1 = Members[i] as MemberVariable;
                        var Var2 = VMembers[i] as MemberVariable;
                        if (Var1 == null || Var2 == null) return false;

                        var Type1 = Var1.TypeOfSelf.RealId as Type;
                        var Type2 = Var2.TypeOfSelf.RealId as Type;
                        if (!Type1.CanOpApplied(Op, Type2)) return false;
                    }

                    return true;
                }
            }

            if (Op == Operator.UnaryPlus || Op == Operator.Negation ||
                Op == Operator.Complement || Operators.IsIncDec(Op))
            {
                var Members = StructuredScope.IdentifierList;
                for (var i = 0; i < Members.Count; i++)
                {
                    var Var = Members[i] as MemberVariable;
                    var Type = Var.TypeOfSelf.RealId as Type;
                    if (Var == null || !Type.CanOpApplied(Op, null))
                        return false;
                }

                return true;
            }

            return base.CanOpApplied_Base(Op, SrcType);
        }
    }

}