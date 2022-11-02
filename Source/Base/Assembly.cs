using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;

namespace Zinnia.Base
{
    public struct AssemblyPath
    {
        public string Name;
        public bool BuiltIn;

        public AssemblyPath(string Name, bool BuiltIn = false)
        {
            this.Name = Name;
            this.BuiltIn = BuiltIn;
        }
    }

    public class InvalidAssemblyException : Exception
    {
        public InvalidAssemblyException()
            : base()
        {
        }

        public InvalidAssemblyException(string Message)
            : base(Message)
        {
        }
    }

    public class Assembly
    {
        public CompilerState State;
        public string Name;
        public string DescName;
        public int Random;

        public int Index;
        public Dictionary<long, Identifier> Ids;
        public int GlobalPointer;

        public string DescLabel
        {
            get { return "_%" + DescName; }
        }

        public Assembly(CompilerState State, string Name, string DescName = null, int Index = -1)
        {
            this.Name = Name;
            this.DescName = DescName;
            this.Index = Index;

            if (Index != -1)
                Ids = new Dictionary<long, Identifier>();

            if (DescName == null)
                CalculateDescName();
        }

        void CalculateDescName()
        {
            var Chars = new char[Name.Length];
            for (var i = 0; i < Name.Length; i++)
            {
                var Chr = Name[i];
                if (char.IsLetterOrDigit(Chr)) Chars[i] = Chr;
                else Chars[i] = '_';
            }

            DescName = new string(Chars);
            if (DescName.Length == 0 || char.IsDigit(DescName[0]))
                DescName = "_" + DescName;
        }
    }

    public class AssemblyDescLoader
    {
        enum ReferenceDestination
        {
            Children,
            RealId,
            OverriddenId,
            Base,
        }

        struct LoaderReference
        {
            public Identifier DstId;
            public ReferenceDestination Dest;
            public int Index;

            public LoaderReference(Identifier DstId, ReferenceDestination Dest, int Index)
            {
                this.DstId = DstId;
                this.Dest = Dest;
                this.Index = Index;
            }
        }

        class LoaderIdentifier
        {
            public Assembly Assembly;
            public long Position;
            public List<LoaderReference> Refs;

            public void AddRef(LoaderReference Ref)
            {
                if (Refs == null)
                    Refs = new List<LoaderReference>();

                Refs.Add(Ref);
            }
        }

        CompilerState State;
        GlobalContainer Global;
        BinaryReader Reader;
        long BeginningPos;

        Assembly CurrentAssembly;
        Assembly[] ChildAssemblies;
        LoaderIdentifier[] LoaderIds;
        AutoAllocatedList<Identifier> UpdateList;

        void UpdateIds()
        {
            for (var i = 0; i < UpdateList.Count; i++)
                UpdateList[i].Update();
        }

        public Assembly LoadAssemblyDesc(GlobalContainer Global, Stream Stream)
        {
            this.Global = Global;
            this.State = Global.State;

            Reader = new BinaryReader(Stream);
            BeginningPos = Stream.Position;
            var IdListPtr = Reader.ReadInt64() + BeginningPos;

            var Name = ReadLEB128_String();
            var DescName = ReadLEB128_String();
            var Index = Global.LoadedAssemblies.Count;
            CurrentAssembly = new Assembly(State, Name, DescName, Index);
            CurrentAssembly.Random = Reader.ReadInt32();
            Global.LoadedAssemblies.Add(CurrentAssembly);
            ReadLEB128_Int();

            var Scope = new AssemblyScope(Global, CurrentAssembly);
            Global.GlobalNamespace.AddScope(Scope);
            Global.Children.Add(Scope);

            if (!ReadChildAssemblies(Global))
                return null;

            var ContentPos = Stream.Position;
            Stream.Seek(IdListPtr, SeekOrigin.Begin);
            ReadIdRefs();

            Stream.Seek(ContentPos, SeekOrigin.Begin);
            ReadIdList(Scope, Global.GlobalNamespace);

            Dereference();
            UpdateIds();
            return CurrentAssembly;
        }

        void ReadIdRefs()
        {
            var Count = ReadLEB128_Int();
            LoaderIds = new LoaderIdentifier[Count];

            for (var i = 0; i < Count; i++)
            {
                var LoaderId = new LoaderIdentifier();
                var AssemblyIndex = ReadLEB128_Int();
                if (AssemblyIndex == -1) LoaderId.Assembly = CurrentAssembly;
                else LoaderId.Assembly = ChildAssemblies[AssemblyIndex];

                LoaderId.Position = ReadLEB128_Long();
                LoaderIds[i] = LoaderId;
            }
        }

        void Dereference(Identifier Id, List<LoaderReference> List)
        {
            if (List == null) return;
            for (var i = 0; i < List.Count; i++)
                SetReferencedId(List[i], Id);
        }

        void Dereference()
        {
            for (var i = 0; i < LoaderIds.Length; i++)
            {
                var LoaderId = LoaderIds[i];
                var Id = LoaderId.Assembly.Ids[LoaderId.Position];
                Dereference(Id, LoaderId.Refs);
            }
        }

        void SetReferencedId(LoaderReference Ref, Identifier Id)
        {
            if (Ref.Dest == ReferenceDestination.Children)
            {
                Ref.DstId.Children[Ref.Index] = Id;
            }
            else if (Ref.Dest == ReferenceDestination.RealId)
            {
                Ref.DstId.RealId = Id;
            }
            else if (Ref.Dest == ReferenceDestination.OverriddenId)
            {
                var MemberFunc = Ref.DstId as MemberFunction;
                MemberFunc.OverriddenId = Id as MemberFunction;
                if (MemberFunc.OverriddenId == null)
                    throw new ApplicationException();
            }
            else if (Ref.Dest == ReferenceDestination.Base)
            {
                var Structure = Ref.DstId as StructuredType;
                Structure.BaseStructures[Ref.Index].Base = Id;
            }
            else
            {
                throw new ApplicationException();
            }
        }

        void ReadReference(Identifier DstId, ReferenceDestination Dest, int Index)
        {
            ReadReference(new LoaderReference(DstId, Dest, Index));
        }

        void ReadReference(LoaderReference Ref)
        {
            var NonDeclared = (UndeclaredIdType)Reader.ReadByte();
            if (NonDeclared == UndeclaredIdType.Unknown)
            {
                ReadReferenceDeclared(Ref);
                return;
            }

            Identifier NewId;
            var Container = Ref.DstId.Container;

            if (NonDeclared == UndeclaredIdType.RefArrayType)
            {
                var Arr = new RefArrayType(Container, null, 0, false);
                ReadReference(Arr, ReferenceDestination.Children, 0);
                UpdateList.Add(Arr);

                Arr.Dimensions = ReadLEB128_Int();
                NewId = Arr;
            }
            else if (NonDeclared == UndeclaredIdType.NonrefArrayType)
            {
                var Arr = new NonrefArrayType(Container, null, null, false);
                ReadReference(Arr, ReferenceDestination.Children, 0);
                UpdateList.Add(Arr);

                var Dimensions = ReadLEB128_Int();
                if (Dimensions != 0)
                {
                    var Lengths = new int[Dimensions];
                    for (var i = 0; i < Dimensions; i++)
                        Lengths[i] = ReadLEB128_Int();

                    Arr.Lengths = Lengths;
                    Arr.Dimensions = Dimensions;
                }

                NewId = Arr;
            }
            else if (NonDeclared == UndeclaredIdType.Pointer)
            {
                NewId = new PointerType(Container, null, false);
                ReadReference(NewId, ReferenceDestination.Children, 0);
                UpdateList.Add(NewId);
            }
            else if (NonDeclared == UndeclaredIdType.Reference)
            {
                var Mode = (ReferenceMode)Reader.ReadByte();
                NewId = new ReferenceType(Container, null, Mode, false);
                ReadReference(NewId, ReferenceDestination.Children, 0);
                UpdateList.Add(NewId);
            }
            else if (NonDeclared == UndeclaredIdType.Tuple)
            {
                var Tuple = new TupleType(Container, (List<Identifier>)null);
                Tuple.InstanceSize = ReadLEB128_Int();
                if (Tuple.InstanceSize <= 0)
                    throw new InvalidAssemblyException("Invalid size");

                Tuple.Size = Tuple.InstanceSize;
                Tuple.Align = ReadLEB128_Int();
                Tuple.LayoutCalculated = true;
                if (!DataStoring.VerifyAlign(Tuple.Align))
                    throw new InvalidAssemblyException("Invalid alignment");

                var Named = Reader.ReadBoolean();
                var MemberCount = ReadLEB128_Int();
                for (var i = 0; i < MemberCount; i++)
                {
                    var Name = Named ? new CodeString(ReadLEB128_String()) : new CodeString();
                    var MemberVar = new MemberVariable(Tuple.StructuredScope, Name, null);
                    MemberVar.Access = IdentifierAccess.Public;
                    ReadReference(MemberVar, ReferenceDestination.Children, 0);
                    MemberVar.Offset = ReadLEB128_Int();
                    Tuple.StructuredScope.IdentifierList.Add(MemberVar);
                }

                UpdateList.Add(Tuple);
                NewId = Tuple;
            }
            else if (NonDeclared == UndeclaredIdType.PointerAndLength)
            {
                var PAndL = new PointerAndLength(Container, null, false);
                ReadReference(PAndL.PointerType, ReferenceDestination.Children, 0);
                UpdateList.Add(PAndL);
                NewId = PAndL;
            }
            else if (NonDeclared == UndeclaredIdType.NonstaticFunctionType)
            {
                var FType = new NonstaticFunctionType(Container, null, false);
                ReadReference(FType, ReferenceDestination.Children, 0);
                UpdateList.Add(FType);
                NewId = FType;
            }
            else if (NonDeclared == UndeclaredIdType.Function)
            {
                var Conv = (CallingConvention)Reader.ReadByte();
                NewId = new TypeOfFunction(Container, Conv, new Identifier[1], false);
                ReadReference(NewId, ReferenceDestination.Children, 0);

                ReadParameterReferences(NewId);
                UpdateList.Add(NewId);
            }
            else
            {
                NewId = Global.CommonIds.GetIdentifier(NonDeclared);
                if (NewId == null) throw new InvalidAssemblyException();
            }

            SetReferencedId(Ref, NewId);
        }

        void ReadParameterReferences(Identifier Id)
        {
            var ParamCount = ReadLEB128_Int();
            if (Id.Children == null)
            {
                Id.Children = new Identifier[ParamCount + 1];
                ReadParameterReferences(Id, 0, ParamCount);
            }
            else
            {
                var OldLength = Id.Children.Length;
                var NewChildren = new Identifier[ParamCount + OldLength];
                Id.Children.CopyTo(NewChildren, 0);
                Id.Children = NewChildren;
                ReadParameterReferences(Id, OldLength, ParamCount);
            }
        }

        void ReadParameterReferences(Identifier Id, int Start, int ParamCount)
        {
            var RequiredParams = ReadLEB128_Int();

            for (var i = 0; i < ParamCount; i++)
                Id.Children[i + Start] = ReadParameterReference(Id, i >= RequiredParams);
        }

        FunctionParameter ReadParameterReference(Identifier Id, bool HasDefaultValue)
        {
            var Flags = (ParameterFlags)Reader.ReadByte();
            var Name = new CodeString(ReadLEB128_String());
            var Param = new FunctionParameter(Id.Container, Name, null);
            ReadReference(Param, ReferenceDestination.Children, 0);
            Param.ParamFlags = Flags;

            if (HasDefaultValue)
                Param.ConstInitValue = ReadConst();

            return Param;
        }

        void ReadReferenceDeclared(Identifier DstId, ReferenceDestination Dest, int Index)
        {
            ReadReferenceDeclared(new LoaderReference(DstId, Dest, Index));
        }

        void ReadReferenceDeclared(LoaderReference Ref)
        {
            var RefIndex = ReadLEB128_Int();
            if (RefIndex == -1) return;

            LoaderIds[RefIndex].AddRef(Ref);
        }

        bool ReadChildAssemblies(GlobalContainer Global)
        {
            var RetValue = true;
            var Count = ReadLEB128_Int();
            ChildAssemblies = new Assembly[Count];

            for (var i = 0; i < Count; i++)
            {
                var Name = ReadLEB128_String();
                var Random = Reader.ReadInt32();
                ReadLEB128_Int();

                var Assembly = Global.GetLoadedAssembly(Name);
                if (Assembly == null)
                {
                    var Path = new AssemblyPath(Name, true);
                    Assembly = Global.LoadAssembly(Path);
                }

                if (Assembly == null || Random != Assembly.Random)
                {
                    RetValue = false;
                    continue;
                }

                ChildAssemblies[i] = Assembly;
            }

            return RetValue;
        }

        Guid? ReadGuid()
        {
            var Contains = Reader.ReadBoolean();
            if (!Contains) return null;

            var Bytes = Reader.ReadBytes(16);
            return new Guid(Bytes);
        }

        BigInteger ReadLEB128_BigInt()
        {
            return LEB128Helper.Decode(Reader.ReadByte);
        }

        int ReadLEB128_Int()
        {
            return LEB128Helper.DecodeInt(Reader.ReadByte);
        }

        uint ReadLEB128_UInt()
        {
            return LEB128Helper.DecodeUInt(Reader.ReadByte);
        }

        long ReadLEB128_Long()
        {
            return LEB128Helper.DecodeLong(Reader.ReadByte);
        }

        ulong ReadLEB128_ULong()
        {
            return LEB128Helper.DecodeULong(Reader.ReadByte);
        }

        string ReadLEB128_String()
        {
            var Length = ReadLEB128_Int();
            var Arr = new char[Length];
            for (var i = 0; i < Arr.Length; i++)
                Arr[i] = (char)ReadLEB128_Int();

            return new String(Arr);
        }

        ConstValue ReadConst()
        {
            var T = (ConstValueType)Reader.ReadByte();

            if (T == ConstValueType.Structure)
            {
                var Count = ReadLEB128_Int();
                var Members = new List<ConstValue>();

                for (var i = 0; i < Count; i++)
                    Members.Add(ReadConst());

                return new StructuredValue(Members);
            }
            else
            {
                if (T == ConstValueType.Integer) return new IntegerValue(ReadLEB128_BigInt());
                else if (T == ConstValueType.Double) return new DoubleValue(Reader.ReadDouble());
                else if (T == ConstValueType.Float) return new FloatValue(Reader.ReadSingle());
                else if (T == ConstValueType.Boolean) return new BooleanValue(Reader.ReadBoolean());
                else if (T == ConstValueType.Char) return new CharValue(Reader.ReadChar());
                else if (T == ConstValueType.String) return new StringValue(ReadLEB128_String());
                else if (T == ConstValueType.Zero) return new ZeroValue();
                else if (T == ConstValueType.Null) return new NullValue();
                else throw new InvalidAssemblyException("Invalid constant type");
            }
        }

        Identifier ReadIdentifer(IdContainer Container, Identifier Parent)
        {
            var IdScope = Container.GetParent<IdentifierScope>();
            if (IdScope == null || IdScope.Identifier != Parent)
                throw new ApplicationException();

            var IdPos = Reader.BaseStream.Position - BeginningPos;
            if (IdPos != ReadLEB128_Long()) throw new InvalidAssemblyException();

#warning CHECK
            var ParentIdRef = ReadLEB128_Int();

            var Byte = Reader.ReadByte();
            var DeclaredIdType = (DeclaredIdType)(Byte & 15);
            var Access = (IdentifierAccess)(Byte >> 4);

            var FlagData = Reader.ReadUInt16();
            var Flags = (IdentifierFlags)FlagData & IdentifierFlags.All;
            var HasName = (FlagData & 16384) != 0;
            var HasOverloads = (FlagData & 32768) != 0;
            var HasSpecialName = (Flags & IdentifierFlags.SpecialName) != 0;

            var Name = HasName ? new CodeString(ReadLEB128_String()) : new CodeString();
            var AssemblyName = HasSpecialName ? ReadLEB128_String() : null;

            var StructuredScope = Container as StructuredScope;
            var IsGlobal = (Flags & IdentifierFlags.Static) != 0 || !(Container.RealContainer is StructuredScope);

            var Ret = (Identifier)null;
            if (DeclaredIdType == DeclaredIdType.Alias)
            {
                Ret = new IdentifierAlias(Container, Name, null);
                ReadReference(Ret, ReferenceDestination.RealId, -1);
                UpdateList.Add(Ret);
            }
            else if (DeclaredIdType == DeclaredIdType.Class || DeclaredIdType == DeclaredIdType.Struct)
            {
                StructuredType Structured;
                if (DeclaredIdType == DeclaredIdType.Class)
                    Ret = Structured = new ClassType(Container, Name);
                else Ret = Structured = new StructType(Container, Name);

                Structured.InstanceSize = ReadLEB128_Int();
                if (Structured.InstanceSize <= 0)
                    throw new InvalidAssemblyException("Invalid size");

                Structured.Align = ReadLEB128_Int();
                Structured.LayoutCalculated = true;
                if (!DataStoring.VerifyAlign(Structured.Align))
                    throw new InvalidAssemblyException("Invalid alingment");

                if (DeclaredIdType == DeclaredIdType.Struct)
                    Structured.Size = Structured.InstanceSize;

                Structured.Guid = ReadGuid();
                var BaseCount = ReadLEB128_Int();
                Structured.BaseStructures = new StructureBase[BaseCount];
                for (var i = 0; i < BaseCount; i++)
                    ReadReferenceDeclared(Structured, ReferenceDestination.Base, i);

                Structured.FunctionTableIndex = ReadLEB128_Int();
                var Scope = new StructuredScope(Container, new CodeString(), Structured);
                Container.Children.Add(Scope);
                Structured.StructuredScope = Scope;
                ReadIdList(Scope, Structured);
                UpdateList.Add(Ret);
            }
            else if (DeclaredIdType == DeclaredIdType.Enum || DeclaredIdType == DeclaredIdType.Flag)
            {
                EnumType Enum;
                if (DeclaredIdType == Zinnia.DeclaredIdType.Enum)
                    Ret = Enum = new EnumType(Container, Name, new CodeString());
                else Ret = Enum = new FlagType(Container, Name, new CodeString());
                ReadReference(Enum, ReferenceDestination.Children, 0);

                var Scope = new EnumScope(Container, new CodeString(), Enum);
                Container.Children.Add(Scope);
                Enum.EnumScope = Scope;

                var MemberCount = ReadLEB128_Int();
                if (MemberCount != 0)
                {
                    if (MemberCount < 0)
                        throw new InvalidAssemblyException();

                    for (var i = 0; i < MemberCount; i++)
                    {
                        var ConstName = new CodeString(ReadLEB128_String());
                        if (!ConstName.IsValidIdentifierName)
                            throw new InvalidAssemblyException("Invalid identifier name");

                        if (Identifiers.IsDefined(Scope.IdentifierList, ConstName.ToString()))
                            throw new InvalidAssemblyException("Identifier already defined");

                        var Const = new ConstVariable(Container, ConstName, Enum, ReadConst());
                        Scope.IdentifierList.Add(Const);
                    }
                }

                UpdateList.Add(Enum);
            }
            else if (DeclaredIdType == DeclaredIdType.Function || DeclaredIdType == DeclaredIdType.Constructor)
            {
                Function Function;
                FunctionOverloads Overloads;
                if (DeclaredIdType == Zinnia.DeclaredIdType.Function)
                {
                    Overloads = Container.GetOverload(Name.ToString());
                    if (IsGlobal) Ret = Function = new Function(Container, Name, null, Overloads);
                    else Ret = Function = new MemberFunction(Container, Name, null, Overloads);
                }
                else
                {
                    if (HasName) throw new InvalidAssemblyException("Constructors cannot have name");
                    if (StructuredScope == null) throw new InvalidAssemblyException("Invalid container");

                    if (StructuredScope.ConstructorOverloads == null)
                        StructuredScope.ConstructorOverloads = new FunctionOverloads(null);

                    Overloads = StructuredScope.ConstructorOverloads;
                    Ret = Function = new Constructor(Container, null, Overloads, new CodeString());
                }

                ReadReference(Function, ReferenceDestination.Children, 0);

                var OverloadIndex = HasOverloads ? ReadLEB128_Int() : 0;
                for (var i = 0; i < Overloads.Functions.Count; i++)
                {
                    var OverloadFunc = Overloads.Functions[i];
                    if (OverloadFunc.OverloadIndex == OverloadIndex)
                        throw new InvalidAssemblyException("Function with the same overload index");
                    /*
                    if (Function.AreParametersSame(OverloadFunc))
                        throw new InvalidAssemblyException("Function with the same name and parameters");*/
                }

                Function.OverloadIndex = OverloadIndex;
                Overloads.Functions.Add(Function);

                if ((Flags & IdentifierFlags.Virtual) != 0)
                {
                    var MemberFunc = Function as MemberFunction;
                    MemberFunc.VirtualIndex = ReadLEB128_Int();
                    if ((Flags & IdentifierFlags.Override) != 0)
                        ReadReferenceDeclared(MemberFunc, ReferenceDestination.OverriddenId, -1);
                }

                Function.GlobalPointerIndex = ReadLEB128_Int();
            }
            else if (DeclaredIdType == DeclaredIdType.Destructor)
            {
                if (HasName) throw new InvalidAssemblyException("Destructors cannot have name");
                if (StructuredScope == null) throw new InvalidAssemblyException("Invalid container");

                var Function = new Destructor(Container, null, new CodeString());
                ReadReference(Function, ReferenceDestination.Children, 0);
                Function.GlobalPointerIndex = ReadLEB128_Int();
                Ret = Function;
            }
            else if (DeclaredIdType == DeclaredIdType.Variable)
            {
                if (IsGlobal) Ret = new GlobalVariable(Container, Name, null);
                else Ret = new MemberVariable(Container, Name, null);

                ReadReference(Ret, ReferenceDestination.Children, 0);

                if (!IsGlobal)
                {
                    var MemVar = Ret as MemberVariable;
                    MemVar.Offset = ReadLEB128_Int();
                }
                else
                {
                    var Global = Ret as GlobalVariable;
                    Global.GlobalPointerIndex = ReadLEB128_Int();
                }
            }
            else if (DeclaredIdType == DeclaredIdType.Constant)
            {
                var Const = new ConstVariable(Container, Name, null, null);
                ReadReference(Const, ReferenceDestination.Children, 0);
                Const.ConstInitValue = ReadConst();
                Ret = Const;
            }
            else if (DeclaredIdType == DeclaredIdType.Property)
            {
                var Property = new Property(Container, Name, (Type)null);
                var PScope = new PropertyScope(Container, new CodeString(), Property);
                Container.Children.Add(PScope);
                Property.PropertyScope = PScope;
                ReadReference(Property, ReferenceDestination.Children, 0);
                ReadParameterReferences(Property);

                var Data = Reader.ReadByte();
                if ((Data & 1) != 0) PScope.Getter = ReadIdentifer(PScope, Property) as Function;
                if ((Data & 2) != 0) PScope.Setter = ReadIdentifer(PScope, Property) as Function;
                Ret = Property;
            }
            else if (DeclaredIdType == DeclaredIdType.Namespace)
            {
                var Scope = Container as NamespaceScope;
                if (Scope == null) throw new InvalidAssemblyException("Invalid container");
                if (Access != IdentifierAccess.Public) throw new InvalidAssemblyException("Invalid access");

                var Options = new GetIdOptions() { Func = x => x is Namespace };
                var Namespace = Identifiers.GetMember(State, Parent, Name, Options) as Namespace;
                if (Namespace == null) Ret = Namespace = new Namespace(Container, Name);

                var NewScope = new NamespaceScope(Container, new CodeString(), Namespace);
                Container.Children.Add(NewScope);
                Namespace.AddScope(NewScope);

                ReadIdList(NewScope, Namespace);
            }
            else
            {
                throw new InvalidAssemblyException("Invalid identifier type");
            }

            if (Ret != null)
            {
                if (HasSpecialName)
                    Ret.AssemblyName = AssemblyName;

                Ret.Access = Access;
                Ret.Flags = Flags;
                Ret.DescPosition = IdPos;
                CurrentAssembly.Ids.Add(IdPos, Ret);
            }

            return Ret;
        }

        void ReadIdList(IdContainer Container, Identifier Parent)
        {
            var Count = ReadLEB128_Int();
            if (Count <= 0)
            {
                if (Count == 0) return;
                throw new InvalidAssemblyException();
            }

            for (var i = 0; i < Count; i++)
            {
                var Id = ReadIdentifer(Container, Parent);
                if (Id == null) continue;

                Container.IdentifierList.Add(Id);
            }
        }
    }

    public class AssemblyDescCreator
    {
        GlobalContainer Global;
        BinaryWriter Writer;
        long BeginningPos;
        List<Identifier> Identifiers;

        private void ReferenceDeclared(Identifier Id)
        {
            if (Id == Global.GlobalNamespace)
            {
                WriteLEB128(-1);
                return;
            }

            if (Id.ReferenceIndex == -1)
            {
                Id.ReferenceIndex = Identifiers.Count;
                Identifiers.Add(Id);
            }

            WriteLEB128(Id.ReferenceIndex);
        }

        private void Reference(Identifier Id)
        {
            var NonDeclared = Id.UndeclaredIdType;
            Writer.Write((byte)NonDeclared);

            if (NonDeclared == UndeclaredIdType.Unknown)
            {
                ReferenceDeclared(Id);
            }
            else if (NonDeclared == UndeclaredIdType.RefArrayType)
            {
                var Arr = Id as RefArrayType;
                Reference(Arr.TypeOfValues);
                WriteLEB128(Arr.Dimensions);
            }
            else if (NonDeclared == UndeclaredIdType.NonrefArrayType)
            {
                var FArr = Id as NonrefArrayType;
                Reference(FArr.TypeOfValues);

                if (FArr.Lengths == null)
                {
                    WriteLEB128(0);
                }
                else
                {
                    WriteLEB128(FArr.Lengths.Length);
                    for (var i = 0; i < FArr.Lengths.Length; i++)
                        WriteLEB128(FArr.Lengths[i]);
                }
            }
            else if (NonDeclared == UndeclaredIdType.Pointer)
            {
                Reference((Id as PointerType).Child);
            }
            else if (NonDeclared == UndeclaredIdType.Reference)
            {
                var RefType = Id as ReferenceType;
                Writer.Write((byte)RefType.Mode);
                Reference(RefType.Child);
            }
            else if (NonDeclared == UndeclaredIdType.Tuple)
            {
                var Tuple = Id as TupleType;
                WriteLEB128(Tuple.InstanceSize);
                WriteLEB128(Tuple.Align);
                Writer.Write(Tuple.Named);

                var Members = Tuple.StructuredScope.IdentifierList;
                WriteLEB128(Members.Count);
                for (var i = 0; i < Members.Count; i++)
                {
                    var MemberVar = Members[i] as MemberVariable;
                    if (Tuple.Named) WriteLEB128(MemberVar.Name.ToString());
                    Reference(MemberVar.TypeOfSelf);
                    WriteLEB128(MemberVar.Offset);
                }
            }
            else if (NonDeclared == UndeclaredIdType.PointerAndLength)
            {
                var PAndL = Id as PointerAndLength;
                Reference(PAndL.Child);
            }
            else if (NonDeclared == UndeclaredIdType.NonstaticFunctionType)
            {
                var PAndL = Id as NonstaticFunctionType;
                Reference(PAndL.Child);
            }
            else if (NonDeclared == UndeclaredIdType.Function)
            {
                var FType = Id as TypeOfFunction;
                var Ch = FType.Children;

                Writer.Write((byte)FType.CallConv);
                Reference(FType.RetType);
                ReferenceParameters(Ch, 1, Ch.Length - 1, FType.RequiredParameters);
            }
        }

        void ReferenceParameters(Identifier[] Ch, int Start, int Count, int RequiredParams)
        {
            WriteLEB128(Ch.Length - 1);
            WriteLEB128(RequiredParams);

            for (var i = Start; i < Start + Count; i++)
                ReferenceParameter(Ch[i] as FunctionParameter);
        }

        void ReferenceParameter(FunctionParameter Param)
        {
            Writer.Write((byte)Param.ParamFlags);
            if (!Param.Name.IsValid) WriteLEB128(0);
            else WriteLEB128(Param.Name.ToString());
            Reference(Param.Children[0]);
            if (Param.ConstInitValue != null)
                WriteConst(Param.ConstInitValue);
        }

        void WriteLEB128(BigInteger Value)
        {
            LEB128Helper.Encode(Value, Writer.Write);
        }

        void WriteLEB128(int Value)
        {
            LEB128Helper.Encode(Value, Writer.Write);
        }

        void WriteLEB128(uint Value)
        {
            LEB128Helper.Encode(Value, Writer.Write);
        }

        void WriteLEB128(long Value)
        {
            LEB128Helper.Encode(Value, Writer.Write);
        }

        void WriteLEB128(ulong Value)
        {
            LEB128Helper.Encode(Value, Writer.Write);
        }

        void WriteConst(ConstValue Value)
        {
            var T = Value.Type;
            Writer.Write((byte)T);

            if (T == ConstValueType.Structure)
            {
                var SVal = Value as StructuredValue;
                WriteLEB128(SVal.Members.Count);

                for (var i = 0; i < SVal.Members.Count; i++)
                    WriteConst(SVal.Members[i]);
            }
            else if (T == ConstValueType.Integer)
            {
                var IntVal = Value as IntegerValue;
                WriteLEB128(IntVal.Value);
            }
            else
            {
                if (T == ConstValueType.Double) Writer.Write((Value as DoubleValue).Value);
                else if (T == ConstValueType.Float) Writer.Write((Value as FloatValue).Value);
                else if (T == ConstValueType.Boolean) Writer.Write((Value as BooleanValue).Value);
                else if (T == ConstValueType.Char) Writer.Write((Value as CharValue).Value);
                else if (T == ConstValueType.String) WriteLEB128((Value as StringValue).Value);
                else if (T != ConstValueType.Zero && T != ConstValueType.Null)
                    throw new ApplicationException();
            }
        }

        ushort GetFlagData(Identifier Id)
        {
            var FlagData = (ushort)Id.Flags;
            if (Id.Name.IsValid) FlagData |= 16384;

            var Func = Id as Function;
            if (Func != null && Func.OverloadIndex != 0)
                FlagData |= 32768;

            return FlagData;
        }

        void WriteIdentifier(IEnumerable<IdContainer> List, Identifier Parent)
        {
            var Count = 0;
            foreach (var e in List)
                Count += e.IdentifierList.Count;

            WriteLEB128(Count);
            foreach (var e in List)
            {
                for (var i = 0; i < e.IdentifierList.Count; i++)
                    WriteIdentifier(e.IdentifierList[i], Parent);
            }
        }

        void WriteIdentifier(AutoAllocatedList<Identifier> List, Identifier Parent)
        {
            if (List.List == null)
            {
                WriteLEB128(0);
                return;
            }

            WriteIdentifier(List.List, Parent);
        }

        void WriteIdentifier(List<Identifier> List, Identifier Parent)
        {
            WriteLEB128(List.Count);
            for (var i = 0; i < List.Count; i++)
                WriteIdentifier(List[i], Parent);
        }

        void WriteIdentifier(Identifier Id, Identifier Parent)
        {
            Id.DescPosition = Writer.BaseStream.Position - BeginningPos;
            var DeclaredIdType = Id.DeclaredIdType;

            WriteLEB128(Id.DescPosition);
            if (Parent.DeclaredIdType == DeclaredIdType.Unknown) WriteLEB128(-1);
            else ReferenceDeclared(Parent);

            Writer.Write((byte)((byte)DeclaredIdType | ((byte)Id.Access << 4)));
            Writer.Write(GetFlagData(Id));

            if (Id.Name.IsValid) WriteLEB128(Id.Name.ToString());
            if ((Id.Flags & IdentifierFlags.SpecialName) != 0)
                WriteLEB128(Id.AssemblyName);

            if (DeclaredIdType == DeclaredIdType.Alias)
            {
                Reference(Id.RealId);
            }
            else if (DeclaredIdType == DeclaredIdType.Class || DeclaredIdType == DeclaredIdType.Struct)
            {
                var Structured = Id as StructuredType;
                WriteLEB128(Structured.InstanceSize);
                WriteLEB128(Structured.Align);

                WriteGuid(Structured);
                WriteLEB128(Structured.BaseStructures.Length);
                for (var i = 0; i < Structured.BaseStructures.Length; i++)
                    ReferenceDeclared(Structured.BaseStructures[i].Base);

                WriteLEB128(Structured.FunctionTableIndex);
                WriteIdentifier(Structured.StructuredScope.IdentifierList, Id);
            }
            else if (DeclaredIdType == DeclaredIdType.Enum || DeclaredIdType == DeclaredIdType.Flag)
            {
                var EnumType = Id as EnumType;
                Reference(EnumType.TypeOfValues);

                var Members = EnumType.EnumScope.IdentifierList;
                WriteLEB128(Members.Count);
                for (var i = 0; i < Members.Count; i++)
                {
                    var e = Members[i] as ConstVariable;
                    if (e == null) throw new ApplicationException();

                    WriteLEB128(e.Name.ToString());
                    WriteConst(e.ConstInitValue);
                }
            }
            else if (DeclaredIdType == DeclaredIdType.Function || DeclaredIdType == DeclaredIdType.Constructor)
            {
                var Func = Id as Function;
                Reference(Func.Children[0]);

                if (Func.OverloadIndex != 0)
                    WriteLEB128(Func.OverloadIndex);

                if ((Id.Flags & IdentifierFlags.Virtual) != 0)
                {
                    var MemberFunc = Id as MemberFunction;
                    WriteLEB128(MemberFunc.VirtualIndex);
                    if ((Id.Flags & IdentifierFlags.Override) != 0)
                        ReferenceDeclared(MemberFunc.OverriddenId);
                }

                WriteLEB128(Func.GlobalPointerIndex);
            }
            else if (DeclaredIdType == DeclaredIdType.Destructor)
            {
                var Func = Id as Function;
                Reference(Func.Children[0]);
                WriteLEB128(Func.GlobalPointerIndex);
            }
            else if (DeclaredIdType == DeclaredIdType.Variable)
            {
                Reference(Id.Children[0]);
                if (Id is MemberVariable) WriteLEB128((Id as MemberVariable).Offset);
                else if (Id is GlobalVariable) WriteLEB128((Id as GlobalVariable).GlobalPointerIndex);
            }
            else if (DeclaredIdType == DeclaredIdType.Constant)
            {
                var ConstVar = Id as ConstVariable;
                Reference(ConstVar.Children[0]);
                WriteConst(ConstVar.ConstInitValue);
            }
            else if (DeclaredIdType == DeclaredIdType.Property)
            {
                var Property = Id as Property;
                var PScope = Property.PropertyScope;
                Reference(Property.Children[0]);
                ReferenceParameters(Property.Children, 1, Property.Children.Length - 1,
                    Property.RequiredParameters);

                var Data = (byte)0;
                if (PScope.GetterIndex != -1) Data |= 1;
                if (PScope.SetterIndex != -1) Data |= 2;
                Writer.Write(Data);

                if (PScope.GetterIndex != -1) WriteIdentifier(PScope.Getter, Property);
                if (PScope.SetterIndex != -1) WriteIdentifier(PScope.Setter, Property);
            }
            else if (DeclaredIdType == DeclaredIdType.Namespace)
            {
                var Namespace = Id as Namespace;
                if (Namespace.NamespaceScopes != null)
                    WriteIdentifier(Namespace.NamespaceScopes, Id);
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        private void WriteGuid(StructuredType Structured)
        {
            if (Structured.Guid != null)
            {
                Writer.Write(true);
                Writer.Write(Structured.Guid.Value.ToByteArray());
            }
            else
            {
                Writer.Write(false);
            }
        }

        public void WriteToPos(long Position, long Value)
        {
            var Old = Writer.BaseStream.Position;
            Writer.BaseStream.Position = Position;
            Writer.Write(Value);
            Writer.BaseStream.Position = Old;
        }

        public void CreateAssemblyDesc(GlobalContainer Global, Stream Stream)
        {
            this.Global = Global;
            BeginningPos = Stream.Position;
            Writer = new BinaryWriter(Stream, new UTF8Encoding());

            var IdListPosPtr = Stream.Position;
            Writer.Seek(8, SeekOrigin.Current);

            WriteLEB128(Global.OutputAssembly.Name);
            WriteLEB128(Global.OutputAssembly.DescName);
            Writer.Write(Global.OutputAssembly.Random);

            if (Global.AssemblyEntry == null) WriteLEB128(-1);
            else WriteLEB128(Global.AssemblyEntry.GlobalPointerIndex);
            WriteChildAssemblies();

            if (Identifiers != null) Identifiers.Clear();
            else Identifiers = new List<Identifier>();
            WriteContent();

            WriteToPos(IdListPosPtr, Stream.Position - BeginningPos);
            WriteIdRefs();
        }

        private void WriteContent()
        {
            if (!Global.GlobalNamespace.HasScopes) return;

            var IdCount = 0;
            foreach (var e in Global.GlobalNamespace.EnumScopes)
            {
                var AssemblyScope = e as AssemblyScope;
                if (AssemblyScope != null && AssemblyScope.Assembly == Global.OutputAssembly)
                    IdCount += AssemblyScope.IdentifierList.Count;
            }

            WriteLEB128(IdCount);
            foreach (var e in Global.GlobalNamespace.EnumScopes)
            {
                var AssemblyScope = e as AssemblyScope;
                if (AssemblyScope != null && AssemblyScope.Assembly == Global.OutputAssembly)
                {
                    for (var i = 0; i < AssemblyScope.IdentifierList.Count; i++)
                        WriteIdentifier(AssemblyScope.IdentifierList[i], Global.GlobalNamespace);
                }
            }
        }

        private void WriteIdRefs()
        {
            WriteLEB128(Identifiers.Count);
            for (var i = 0; i < Identifiers.Count; i++)
            {
                var Id = Identifiers[i];
                var AssemblyScope = Id.Container.AssemblyScope;
                WriteLEB128(AssemblyScope.Assembly.Index);
                WriteLEB128(Id.DescPosition);

                if (Id.DescPosition < 0)
                    throw new ApplicationException();
            }
        }

        private void WriteChildAssemblies()
        {
            WriteLEB128(Global.LoadedAssemblies.Count);
            for (var i = 0; i < Global.LoadedAssemblies.Count; i++)
            {
                var Assembly = Global.LoadedAssemblies[i];
                WriteLEB128(Assembly.Name);
                Writer.Write(Assembly.Random);
                WriteLEB128(Assembly.GlobalPointer);
                Assembly.Index = i;
            }
        }

        private void WriteLEB128(string String)
        {
            WriteLEB128(String.Length);
            for (var i = 0; i < String.Length; i++)
                WriteLEB128((int)String[i]);
        }
    }
}