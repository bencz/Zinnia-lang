using System;
using System.Collections.Generic;
using System.Linq;
using Zinnia.Base;

namespace Zinnia;

public struct IdentifierFound
{
    public IdContainer Container;
    public Identifier Identifier;

    public IdentifierFound(IdContainer Container, Identifier Identifier)
    {
        this.Container = Container;
        this.Identifier = Identifier;
    }
}

public struct OverloadSelectionData
{
    public bool Specified;
    public List<Identifier> Unnamed;
    public Dictionary<string, Identifier> Named;

    public OverloadSelectionData(List<Identifier> Unnamed, Dictionary<string, Identifier> Named = null)
    {
        Specified = true;
        this.Unnamed = Unnamed;
        this.Named = Named;
    }

    public static OverloadSelectionData ParameterLess
    {
        get
        {
            var Ret = new OverloadSelectionData();
            Ret.Specified = true;
            Ret.Unnamed = new List<Identifier>();
            return Ret;
        }
    }
}

public enum GetIdMode : byte
{
    Everywhere,
    Function,
    Container
}

public struct GetIdOptions
{
    public GetIdMode Mode;
    public OverloadSelectionData OverloadData;
    public Predicate<Identifier> Func;
    public bool EnableMessages;

    public static GetIdOptions Default => new() { EnableMessages = true };

    public static GetIdOptions DefaultForType
    {
        get
        {
            return new GetIdOptions
            {
                EnableMessages = true,
                Func = x => x.RealId is Type
            };
        }
    }

    public GetIdOptions(GetIdMode Mode, bool EnableMessages = true)
    {
        this.Mode = Mode;
        this.EnableMessages = EnableMessages;
        OverloadData = new OverloadSelectionData();
        Func = null;
    }
}

public static class Identifiers
{
    public static TypeOfFunction AddSelfParameter(Identifier Func, Identifier SelfType)
    {
        var Container = Func.Container;
        var FType = Func.RealId as TypeOfFunction;
        var RetCh = new Identifier[FType.Children.Length + 1];

        RetCh[0] = FType.Children[0];
        RetCh[1] = new FunctionParameter(Container, new CodeString(), SelfType);

        for (var i = 1; i < FType.Children.Length; i++)
            RetCh[i + 1] = FType.Children[i];

        return new TypeOfFunction(Container, FType.CallConv, RetCh);
    }

    public static bool IsSubtypeOrEquivalent(Identifier A, Identifier B)
    {
        return A.IsEquivalent(B) || IsSubtypeOf(A, B);
    }

    public static bool IsSubtypeOf(Identifier A, Identifier B)
    {
        if (!(A.RealId is Type)) throw new ArgumentException("A");
        if (!(B.RealId is Type)) throw new ArgumentException("B");

        var Bases = GetUnrealBases(A);
        for (var i = 0; i < Bases.Length; i++)
            if (Bases[i].Base.IsEquivalent(B))
                return true;

        var AUnderlying = A.UnderlyingStructureOrRealId;
        if (AUnderlying is StructuredType)
        {
            var Structured = AUnderlying as StructuredType;
            Bases = Structured.BaseStructures;

            for (var i = 0; i < Bases.Length; i++)
                if (IsSubtypeOrEquivalent(Bases[i].Base, B))
                    return true;
        }

        return false;
    }

    public static StructureBase[] GetUnrealBases(Identifier Id)
    {
        if (!(Id.RealId is Type)) throw new ArgumentException("Id");

        var Type = Id.RealId as Type;
        var Global = Id.Container.GlobalContainer;

        if ((Type.Flags & IdentifierFlags.Static) != 0)
            return Global.CommonIds.EmptyBase;

        if ((Type.TypeFlags & TypeFlags.ReferenceValue) == 0)
        {
            if (Type is EnumType) return Global.CommonIds.EnumBase;
            if (Type is TupleType) return Global.CommonIds.TupleBase;
            return Global.CommonIds.ValueTypeBase;
        }

        if (Type is RefArrayType) return Global.CommonIds.ArrayBase;
        return Global.CommonIds.EmptyBase;
    }

    public static Identifier GetBoxClass(Identifier Id)
    {
        var Type = Id.RealId as Type;
        if ((Type.TypeFlags & TypeFlags.ReferenceValue) != 0)
            throw new ArgumentException("Id");

        string Name;
        if (Type is EnumType) Name = "System.Enum";
        else if (Type is TupleType) Name = "System.Tuple";
        else Name = "System.ValueType";

        var State = Id.Container.State;
        return GetByFullNameFast<ClassType>(State, Name, false);
    }

    public static string GetFullName(Identifier Id, bool Overload = false)
    {
        var Lang = Id.Container.State.Language;
        if (Lang.FullNameGenerator == null) return null;
        return Lang.FullNameGenerator.GetFullName(Id, Overload);
    }

    public static bool AreParametersSame(Identifier A, Identifier B)
    {
        var FuncA = A.RealId as Function;
        var FuncB = B.RealId as Function;
        var AType = FuncA.Children[0].RealId as TypeOfFunction;
        var BType = FuncB.Children[0].RealId as TypeOfFunction;

        var Ch = AType.Children;
        if (Ch.Length != BType.Children.Length)
            return false;

        for (var i = 1; i < Ch.Length; i++)
            if (!Ch[i].TypeOfSelf.IsEquivalent(BType.Children[i].TypeOfSelf))
                return false;

        return true;
    }

    public static bool IsScalarOrVectorNumber(Identifier Id)
    {
        return ProcessTuple(Id, x => x.RealId is NumberType);
    }

    public static bool ProcessTuple(Identifier Id, Predicate<Identifier> Func)
    {
        var Tuple = Id.RealId as TupleType;
        if (Tuple != null)
            return Tuple.TrueForAllMembers(x => Func(x.Children[0]));

        return Func(Id);
    }

    public static void MoveIdentifier(Identifier Id, IdContainer NewContainer)
    {
        if (!Id.Container.IsSubContainerOf(NewContainer) || NewContainer.FunctionScope == null)
            throw new ArgumentException();

        Id.Container.IdentifierList.Remove(Id);
        Id.Container = NewContainer;
        NewContainer.IdentifierList.Add(Id);
    }

    public static bool IsNullable(Identifier Id)
    {
        var Type = Id.UnderlyingClassOrRealId as Type;
        if (!(Type is Type)) Type = Id.TypeOfSelf.UnderlyingClassOrRealId as Type;
        return (Type.TypeFlags & TypeFlags.ReferenceValue) != 0;
    }

    public static bool IsNullableType(Identifier Id)
    {
        var Type = Id.UnderlyingClassOrRealId as Type;
        return Type != null && (Type.TypeFlags & TypeFlags.ReferenceValue) != 0;
    }

    public static bool IsBoxing(Identifier From, Identifier To)
    {
        var TFrom = From.RealId as Type;
        if ((TFrom.TypeFlags & TypeFlags.ReferenceValue) != 0)
            return false;

        var RealOrUnderlying = From.UnderlyingStructureOrRealId;
        if (!(RealOrUnderlying is StructType || TFrom is EnumType))
            return false;

        var TTo = To.RealId as Type;
        return TTo is ObjectType || TTo.AssemblyName == "_System_ValueType" ||
               (TFrom is EnumType && TTo.AssemblyName == "_System_Enum") ||
               (TFrom is TupleType && TTo.AssemblyName == "_System_Tuple");
    }

    public static void LinkTypes(BuiltinType Type1, StructuredType Type2)
    {
        Type1.UnderlyingType = Type2;
        Type2.RealId = Type1;
    }

    public static Identifier CreateTupleMember(IdContainer Container, Identifier Type)
    {
        var Var = new MemberVariable(Container, new CodeString(), Type);
        Var.Access = Type.Access;
        return Var;
    }

    public static TupleType CreateTupleFromTypes(IdContainer Container, List<Identifier> Types)
    {
        var Members = new List<Identifier>();
        for (var i = 0; i < Types.Count; i++)
            Members.Add(CreateTupleMember(Container, Types[i]));

        return new TupleType(Container, Members);
    }

    public static void RemoveOutside(IdContainer Container, List<Identifier> List)
    {
        for (var i = 0; i < List.Count; i++)
        {
            var C = List[i].Container;
            if (C != Container && !Container.IsSubContainerOf(C))
            {
                List.RemoveAt(i);
                i--;
            }
        }
    }

    public static void RemoveOutside(IdContainer Container, AutoAllocatedList<Identifier> List)
    {
        if (List.List != null)
            RemoveOutside(Container, List.List);
    }

    public static List<IdentifierFound> Search(IdContainer Container, List<Identifier> List, string Name,
        Predicate<Identifier> Func = null)
    {
        var Out = new List<IdentifierFound>();
        Search(Container, List, Name, Out, Func);
        return Out;
    }

    public static List<IdentifierFound> Search(IdContainer Container, AutoAllocatedList<Identifier> List,
        string Name, Predicate<Identifier> Func = null)
    {
        var Out = new List<IdentifierFound>();
        if (List.List != null)
            Search(Container, List.List, Name, Out, Func);

        return Out;
    }

    private static bool Contains(List<IdentifierFound> List, Identifier Id)
    {
        for (var i = 0; i < List.Count; i++)
            if (List[i].Identifier == Id)
                return true;

        return false;
    }

    public static bool Search(IdContainer Container, List<Identifier> List, string Name,
        List<IdentifierFound> Out, Predicate<Identifier> Func = null)
    {
        var RetValue = false;

        if (Name == null)
        {
            for (var i = 0; i < List.Count; i++)
            {
                var Item = List[i];
                if (Item != null && (Func == null || Func(Item)) && !Contains(Out, Item))
                {
                    Out.Add(new IdentifierFound(Container, Item));
                    RetValue = true;
                }
            }
        }
        else
        {
            if (Func == null)
                for (var i = 0; i < List.Count; i++)
                {
                    var Item = List[i];
                    if (Item != null && Item.Name.IsValid && Item.Name.IsEqual(Name) && !Contains(Out, Item))
                    {
                        Out.Add(new IdentifierFound(Container, Item));
                        RetValue = true;
                    }
                }
            else
                for (var i = 0; i < List.Count; i++)
                {
                    var Item = List[i];
                    if (Item != null && Item.Name.IsValid && Item.Name.IsEqual(Name) && !Contains(Out, Item))
                    {
                        if (Func(Item)) Out.Add(new IdentifierFound(Container, Item));
                        RetValue = true;
                    }
                }
        }

        return RetValue;
    }

    public static bool Search(IdContainer Container, AutoAllocatedList<Identifier> List, string Name,
        List<IdentifierFound> Out, Predicate<Identifier> Func = null)
    {
        if (List.List != null)
            return Search(Container, List.List, Name, Out, Func);

        return false;
    }

    public static T GetByFullNameFast<T>(CompilerState State, string Name, bool EnableMessages = true)
        where T : Identifier
    {
        return GetByFullNameFast<T>(State.GlobalContainer, Name, EnableMessages);
    }

    public static T GetByFullNameFast<T>(GlobalContainer Global, string Name, bool EnableMessages = true)
        where T : Identifier
    {
        var Ret = GetByFullNameFast(Global, Name, EnableMessages);
        if (Ret == null) return null;

        if (!(Ret is T))
        {
            if (EnableMessages)
            {
                var Messages = Global.State.Messages;
                Messages.Add(MessageId.UnknownId, new CodeString(Name));
            }

            return null;
        }

        return Ret as T;
    }

    public static Identifier GetByFullNameFast(CompilerState State, string Name, bool EnableMessages = true)
    {
        return GetByFullNameFast(State.GlobalContainer, Name, EnableMessages);
    }

    public static Identifier GetByFullNameFast(GlobalContainer Global, string Name, bool EnableMessages = true)
    {
        lock (Global.FastIds)
        {
            Identifier Id;
            if (Global.FastIds.TryGetValue(Name, out Id))
                return Id;

            var Options = new GetIdOptions { EnableMessages = EnableMessages };
            var Ret = GetFromMembers(Global.GlobalNamespace, new CodeString(Name), Options);
            if (Ret == null) return null;

            Global.FastIds.Add(Name, Ret);
            return Ret;
        }
    }

    public static Identifier GetByFullName(CompilerState State, string Name)
    {
        return GetFromMembers(State.GlobalContainer.GlobalNamespace,
            new CodeString(Name), GetIdOptions.Default);
    }

    public static Identifier GetByFullName(GlobalContainer Global, string Name)
    {
        return GetFromMembers(Global.GlobalNamespace, new CodeString(Name), GetIdOptions.Default);
    }

    public static Identifier GetByFullName(GlobalContainer Global, CodeString Name)
    {
        return GetFromMembers(Global.GlobalNamespace, Name, GetIdOptions.Default);
    }

    public static Identifier GetByFullName(GlobalContainer Global, CodeString Name, GetIdOptions Options)
    {
        return GetFromMembers(Global.GlobalNamespace, Name, Options);
    }

    public static Identifier GetFromMembers(Identifier CurrentId, CodeString Name, GetIdOptions Options)
    {
        var Container = CurrentId.Container;
        var State = Container.State;
        var Splitted = Name.Split('.');

        var OptCopy = Options;
        OptCopy.Func = null;

        for (var i = 0; i < Splitted.Count; i++)
        {
            var LOps = i < Splitted.Count - 1 ? OptCopy : Options;
            CurrentId = GetMember(State, CurrentId, Splitted[i], LOps);
            if (CurrentId == null) return null;
        }

        return CurrentId;
    }

    public static Identifier GetFromMembers(IdContainer Container, CodeString Name, GetIdOptions Options)
    {
        var State = Container.State;
        var Splitted = Name.Split('.');

        var OptCopy = Options;
        OptCopy.OverloadData.Specified = false;
        OptCopy.Func = null;

        var IdList = Container.GetIdentifier(Splitted[0].ToString(), OptCopy.Mode);
        var CurrentId = SelectIdentifier(State, IdList, Splitted[0], OptCopy);
        if (CurrentId == null) return null;

        for (var i = 1; i < Splitted.Count; i++)
        {
            var LOps = i < Splitted.Count - 1 ? OptCopy : Options;
            CurrentId = GetMember(State, CurrentId, Splitted[i], LOps);
            if (CurrentId == null) return null;
        }

        return CurrentId;
    }

    public static bool IsDefined(List<Identifier> List, string Name)
    {
        if (Name == null)
            return List.Count > 0;
        for (var i = 0; i < List.Count; i++)
        {
            var Id = List[i];
            if (Id.Name.IsValid && Id.Name.IsEqual(Name))
                return true;
        }

        return false;
    }

    public static bool IsDefined(AutoAllocatedList<Identifier> List, string Name)
    {
        return List.List == null ? false : IsDefined(List.List, Name);
    }

    public static bool IsMoreRestrictive(IdentifierAccess First, IdentifierAccess Second)
    {
        if (First == IdentifierAccess.Unknown || Second == IdentifierAccess.Unknown)
            throw new Exception("EROR");

        if (First == IdentifierAccess.Private && Second == IdentifierAccess.Internal) return false;
        if (First == IdentifierAccess.Internal && Second == IdentifierAccess.Private) return false;

        if (((int)First & 3) > ((int)Second & 3)) return true;
        if ((First & IdentifierAccess.Internal) != 0 && (Second & IdentifierAccess.Internal) == 0)
            return true;

        return false;
    }

    public static bool IsLessRestrictive(IdentifierAccess First, IdentifierAccess Second)
    {
        if (First == IdentifierAccess.Unknown || Second == IdentifierAccess.Unknown)
            throw new Exception("EROR");

        if (First == IdentifierAccess.Private && Second == IdentifierAccess.Internal) return false;
        if (First == IdentifierAccess.Internal && Second == IdentifierAccess.Private) return false;

        if (((int)First & 3) < ((int)Second & 3)) return true;
        if ((First & IdentifierAccess.Internal) == 0 && (Second & IdentifierAccess.Internal) != 0)
            return true;

        return false;
    }

    public static IdentifierAccess IdAccessIntersect(Identifier[] Ids)
    {
        var Access = Ids[0].Access;
        for (var i = 1; i < Ids.Length; i++)
            Access = IdAccessIntersect(Access, Ids[i].Access);

        return Access;
    }

    public static IdentifierAccess IdAccessIntersect(List<Identifier> Ids)
    {
        var Access = Ids[0].Access;
        for (var i = 1; i < Ids.Count; i++)
            Access = IdAccessIntersect(Access, Ids[i].Access);

        return Access;
    }

    public static IdentifierAccess IdAccessIntersect(AutoAllocatedList<Identifier> Ids)
    {
        if (Ids.List != null) return IdAccessIntersect(Ids.List);
        return IdentifierAccess.Public;
    }

    public static IdentifierAccess IdAccessIntersect(IdentifierAccess First, IdentifierAccess Second)
    {
        var Ret = Second;
        if (((int)First & 3) > ((int)Second & 3))
            Ret = First;

        if (((int)First & 3) != (int)IdentifierAccess.Private)
            if ((First & IdentifierAccess.Internal) != 0 || (Second & IdentifierAccess.Internal) != 0)
                Ret |= IdentifierAccess.Internal;

        return Ret;
    }

    public static IdentifierAccess GetRealAccess(IdContainer Container, Identifier Id)
    {
        var Current = Id.Container;
        var Access = Id.Access;

        while (Current != Container)
        {
            var TypeScope = Current as TypeScope;
            if (TypeScope != null)
            {
                var ScopeId = TypeScope.Identifier;
                Access = IdAccessIntersect(Access, ScopeId.Access);
            }

            if ((Current = Current.Parent) == null)
                throw new ApplicationException();
        }

        return Access;
    }

    public static bool IsLessAccessable(Identifier Id, Identifier Id2)
    {
        var Container = Id.Container.GetCommonContainer(Id2.Container);
        if (Container == null) throw new ApplicationException();

        var Access = GetRealAccess(Container, Id);
        var Access2 = GetRealAccess(Container, Id2);
        return IsMoreRestrictive(Access, Access2);
    }

    public static Type GetType(Identifier Id)
    {
        var Var = Id.RealId as Variable;
        var Type = Var != null ? Var.TypeOfSelf : Id;
        return Type.RealId as Type;
    }

    public static bool ContainsMember(Identifier Id, string Name, Predicate<Identifier> Func = null)
    {
        return SearchMember(null, Id, Name, Func).Count > 0;
    }

    public static List<IdentifierFound> SearchBaseMember(IdContainer Container,
        Identifier Id, string Name, Predicate<Identifier> Func = null)
    {
        var List = new List<IdentifierFound>();
        SearchBaseMember(Container, Id, Name, List, Func);
        return List;
    }

    public static bool SearchBaseMember(IdContainer Container, Identifier Id, string Name,
        List<IdentifierFound> Out, Predicate<Identifier> Func = null)
    {
        Id = Id.UnderlyingStructureOrRealId;
        if (!(Id.RealId is Type)) throw new ArgumentException("Id");

        var Global = Id.Container.GlobalContainer;
        if ((Global.Flags & GlobalContainerFlags.StructureMembersParsed) == 0)
            throw new InvalidOperationException();

        var RetValue = false;
        if (Id.RealId is StructuredType)
        {
            var Structure = Id.RealId as StructuredType;
            for (var i = 0; i < Structure.BaseStructures.Length; i++)
            {
                var Base = Structure.BaseStructures[0].Base;
                if (SearchMember(Container, Base, Name, Out, Func))
                    RetValue = true;
            }
        }

        if (!RetValue)
        {
            var UnrealBases = GetUnrealBases(Id);
            for (var i = 0; i < UnrealBases.Length; i++)
            {
                var Base = UnrealBases[0].Base;
                if (SearchMember(Container, Base, Name, Out, Func))
                    RetValue = true;
            }
        }

        return RetValue;
    }

    public static List<IdentifierFound> SearchMember(IdContainer Container, Identifier Id,
        string Name, Predicate<Identifier> Func = null)
    {
        var List = new List<IdentifierFound>();
        SearchMember(Container, Id, Name, List, Func);
        return List;
    }

    public static bool SearchMember(IdContainer Container, Identifier Id, string Name,
        List<IdentifierFound> Out, Predicate<Identifier> Func = null)
    {
        Id = Id.UnderlyingStructureOrRealId;
        if (!(Id.RealId is Type || Id.RealId is Namespace || Id.RealId is Property))
            throw new ArgumentException("Id");

        var RetValue = false;
        if (Id.RealId is TupleType && new StringSlice(Name).IsNumber)
        {
            var Tuple = Id.RealId as TupleType;
            var Members = Tuple.StructuredScope.IdentifierList;

            var i = Convert.ToInt32(Name);
            if (i >= 0 && i < Members.Count)
            {
                var Item = Members[i];
                if (Item != null && (Func == null || Func(Item)))
                {
                    Out.Add(new IdentifierFound(Container, Members[i]));
                    RetValue = true;
                }
            }
        }

        if (!RetValue && Id.HasScopes)
            foreach (var e in Id.EnumScopes)
                if (Search(Container, e.IdentifierList, Name, Out, Func))
                    RetValue = true;

        var Global = Id.Container.GlobalContainer;
        if ((Global.Flags & GlobalContainerFlags.StructureMembersParsed) != 0)
        {
            var Underlying = Id.UnderlyingStructureOrRealId;
            if (!RetValue && Underlying is StructuredType)
            {
                var Structure = Underlying as StructuredType;
                for (var i = 0; i < Structure.BaseStructures.Length; i++)
                {
                    var Base = Structure.BaseStructures[0].Base;
                    if (SearchMember(Container, Base, Name, Out, Func))
                        RetValue = true;
                }
            }

            if (!RetValue && Id.RealId is Type)
            {
                var UnrealBases = GetUnrealBases(Id);
                for (var i = 0; i < UnrealBases.Length; i++)
                {
                    var Base = UnrealBases[0].Base;
                    if (SearchMember(Container, Base, Name, Out, Func))
                        RetValue = true;
                }
            }
        }

        return RetValue;
    }

    public static Identifier GetMember(CompilerState State, Identifier Id,
        string Right, CodeString Code)
    {
        var List = SearchMember(null, Id, Right);
        return SelectIdentifier(State, List, Code);
    }

    public static Identifier GetMember(CompilerState State, Identifier Id,
        string Right, CodeString Code, GetIdOptions Options)
    {
        var List = SearchMember(null, Id, Right, Options.Func);
        return SelectIdentifier(State, List, Code, Options);
    }

    public static Identifier GetMember(CompilerState State, Identifier Id,
        CodeString Right, GetIdOptions Options)
    {
        var List = SearchMember(null, Id, Right.ToString(), Options.Func);
        return SelectIdentifier(State, List, Right, Options);
    }

    public static Identifier GetMember(CompilerState State, Identifier Id,
        string Right, GetIdOptions Options)
    {
        var List = SearchMember(null, Id, Right, Options.Func);
        return SelectIdentifier(State, List, new CodeString(Right), Options);
    }

    public static Identifier GetMember(CompilerState State, Identifier Id, CodeString Right)
    {
        var List = SearchMember(null, Id, Right.ToString());
        return SelectIdentifier(State, List, Right, GetIdOptions.Default);
    }

    public static Identifier GetMember(IdContainer Container, CodeString Left,
        CodeString Right, GetIdOptions Options)
    {
        var LeftOptions = Options;
        LeftOptions.Func = x => x.HasScopes;

        var Id = Recognize(Container, Left, LeftOptions);
        if (Id == null) return null;

        return GetMember(Container.State, Id, Right, Options);
    }

    public static Identifier GetMember(IdContainer Container, CodeString Left, CodeString Right)
    {
        return GetMember(Container, Left, Right, GetIdOptions.Default);
    }

    private static int GetFunctionValue(Function Func, OverloadSelectionData Data)
    {
        var State = Func.Container.State;
        var FuncType = Func.TypeOfSelf.RealId as TypeOfFunction;
        var FuncCh = FuncType.Children;
        var FuncParamCount = FuncCh.Length - 1;
        var Specified = new bool[FuncParamCount];

        var Value = 0;
        if (Data.Unnamed != null)
        {
            var Processed = false;
            if ((State.Language.Flags & LangaugeFlags.ConvertParametersToTuple) != 0
                && Data.Unnamed.Count > 1 && Data.Named == null && FuncParamCount == 1)
            {
                var Tuple = FuncCh[1].TypeOfSelf.RealId as TupleType;
                if (Tuple != null)
                {
                    var Members = Tuple.StructuredScope.IdentifierList;
                    var NewValue = Members.Count == Data.Unnamed.Count ? 4 : 0;

                    if (NewValue == 4)
                    {
                        for (var i = 0; i < Data.Unnamed.Count; i++)
                        {
                            var Type = Data.Unnamed[i].RealId as Type;
                            var Res = Type.CanConvert(Members[i].TypeOfSelf);
                            if (Res == TypeConversion.Convertable)
                            {
                                if (NewValue > 1) NewValue = 1;
                            }
                            else if (Res == TypeConversion.Nonconvertable)
                            {
                                NewValue = 0;
                                break;
                            }
                        }

                        if (NewValue > 0)
                        {
                            Value += NewValue;
                            Processed = true;
                            Specified[0] = true;
                        }
                    }
                }
            }

            if (!Processed)
            {
                var LastParam = FuncCh[FuncCh.Length - 1] as FunctionParameter;

                for (var i = 0; i < Data.Unnamed.Count; i++)
                {
                    if (LastParam != null && (LastParam.ParamFlags & ParameterFlags.ParamArray) != 0)
                    {
                        if (i == FuncParamCount - 1)
                        {
                            var PValue = GetParamValue(Data.Unnamed[i], LastParam.Children[0]);
                            if (PValue > 0)
                            {
                                Value += PValue;
                                Specified[FuncParamCount - 1] = true;
                                break;
                            }
                        }

                        if (i >= FuncParamCount - 1)
                        {
                            var Type = GetParamArrayBaseType(LastParam.Children[0]);
                            Value += GetParamValue(Data.Unnamed[i], Type);
                            Specified[FuncParamCount - 1] = true;
                            continue;
                        }
                    }
                    else
                    {
                        if (i >= FuncParamCount)
                        {
                            Value = 0;
                            break;
                        }
                    }

                    var Param = FuncCh[i + 1] as FunctionParameter;
                    Value += GetParamValue(Data.Unnamed[i], Param.Children[0]);
                    Specified[i] = true;
                }
            }
        }

        if (Data.Named != null)
            foreach (var e in Data.Named)
            {
                var Param = FuncType.GetParameter(e.Key);
                if (Param == null) continue;

                var Index = FuncType.GetChildIndex(Param);
                if (Specified[Index]) continue;

                Value += GetParamValue(e.Value, Param.Children[0]);
                Specified[Index] = true;
            }

        for (var i = 0; i < FuncParamCount; i++)
        {
            var Param = FuncCh[i + 1] as FunctionParameter;
            if (!Specified[i] && Param.ConstInitValue == null) Value -= 3;
        }

        return Value;
    }

    public static Identifier GetParamArrayBaseType(Identifier Type)
    {
        if (Type.RealId is ArrayType || Type.RealId is PointerType) return Type.Children[0];

        if (Type.RealId is PointerAndLength)
        {
            var PAndL = Type.RealId as PointerAndLength;
            return PAndL.Child;
        }

        throw new ApplicationException();
    }

    private static int GetParamValue(Identifier From, Identifier To)
    {
        if (From.IsEquivalent(To)) return 4;

        var RFrom = From.RealId;
        var RTo = To.RealId;

        if (RFrom is ArrayType && RTo is ArrayType && RFrom.Children[0].RealId is AutomaticType)
            return 3;

        var Res = RFrom.CanConvert(RTo);
        if (Res == TypeConversion.Automatic) return 2;
        if (Res == TypeConversion.Convertable) return 1;
        if (Res == TypeConversion.Nonconvertable) return 0;
        throw new ApplicationException();
    }

    private static int GetIdentifierPriority(Identifier Id)
    {
        Id = Id.RealId;
        if (Id is Namespace) return 0;
        if (Id is Type) return 1;
        return 2;
    }

    private static void Exclude(List<IdentifierFound> List, int[] Priorities)
    {
        var MaxPriority = int.MinValue;
        for (var i = 0; i < List.Count; i++)
            if (MaxPriority < Priorities[i])
                MaxPriority = Priorities[i];

        var j = 0;
        for (var i = 0; i < Priorities.Length; i++)
        {
            if (Priorities[i] < MaxPriority)
            {
                List.RemoveAt(j);
                continue;
            }

            j++;
        }
    }

    private static void ExcludeParamArrays(List<IdentifierFound> List)
    {
        var Priorities = new int[List.Count];
        for (var i = 0; i < List.Count; i++)
        {
            if (List[i].Identifier is Function)
            {
                var FuncType = List[i].Identifier.Children[0] as TypeOfFunction;
                var LastParam = FuncType.Children[FuncType.Children.Length - 1] as FunctionParameter;
                if (LastParam != null && (LastParam.ParamFlags & ParameterFlags.ParamArray) != 0)
                {
                    Priorities[i] = 0;
                    continue;
                }
            }

            Priorities[i] = 1;
        }

        Exclude(List, Priorities);
    }

    private static void ExcludeBasedOnIdPriority(List<IdentifierFound> List)
    {
        var Priorities = new int[List.Count];
        for (var i = 0; i < List.Count; i++)
            Priorities[i] = GetIdentifierPriority(List[i].Identifier);

        Exclude(List, Priorities);
    }

    private static void ExcludeBasedOnParamMatching(List<IdentifierFound> List, OverloadSelectionData Data)
    {
        if (Data.Specified)
        {
            var Priorities = new int[List.Count];
            for (var i = 0; i < List.Count; i++)
            {
                var Func = List[i].Identifier.RealId as Function;
                if (Func == null) Priorities[i] = -1;
                else Priorities[i] = GetFunctionValue(Func, Data);
            }

            Exclude(List, Priorities);
        }
    }

    private static void ExcludeBasedOnContainer(List<IdentifierFound> List)
    {
        for (var i = 0; i < List.Count; i++)
        {
            if (List[i].Container == null)
                continue;

            var Remove = false;
            for (var j = 0; j < List.Count; j++)
            {
                var JContainer = List[j].Container;
                if (JContainer != null && JContainer.IsSubContainerOf(List[i].Container))
                {
                    Remove = true;
                    break;
                }
            }

            if (Remove)
            {
                List.RemoveAt(i);
                i--;
            }
        }
    }

    private static Identifier SelectIdentifierHelper(CompilerState State, List<IdentifierFound> List,
        CodeString Code, OverloadSelectionData Data, bool EnableMessages)
    {
        if (List.Count == 0) throw new ApplicationException();
        if (List.Count == 1) return List[0].Identifier;

        var NewList = List.ToList();
        ExcludeBasedOnIdPriority(NewList);
        ExcludeBasedOnParamMatching(NewList, Data);
        ExcludeParamArrays(NewList);
        ExcludeBasedOnContainer(NewList);

        if (NewList.Count > 1)
        {
            if (EnableMessages)
                State.Messages.Add(MessageId.AmbiguousReference, Code);

            return null;
        }

        return NewList[0].Identifier;
    }

    public static Identifier SelectIdentifier(CompilerState State, List<IdentifierFound> List,
        CodeString Code, GetIdOptions Options)
    {
        if (List.Count == 0)
        {
            if (Options.EnableMessages)
                State.Messages.Add(MessageId.UnknownId, Code);

            return null;
        }

        return SelectIdentifierHelper(State, List, Code, Options.OverloadData, Options.EnableMessages);
    }

    public static Identifier SelectIdentifier(CompilerState State, List<IdentifierFound> List, CodeString Code)
    {
        return SelectIdentifier(State, List, Code, GetIdOptions.Default);
    }

    private static bool CanAccessPrivate(IdContainer Container, Identifier Id)
    {
        var RealContainer = Id.Container.RealContainer;
        while (RealContainer != Container)
        {
            Container = Container.Parent;
            if (Container == null) return false;
        }

        return true;
    }

    private static bool CanAccessProtected(IdContainer Container, Identifier Id)
    {
        var TypeScope = Container.StructuredScope;
        if (TypeScope != null) return CanAccessProtected(TypeScope, Id);

        return false;
    }

    private static bool CanAccessProtected(StructuredScope Scope, Identifier Id)
    {
        if (Scope.IdentifierList.Contains(Id))
            return true;

        var Type = Scope.StructuredType;
        for (var i = 0; i < Type.BaseStructures.Length; i++)
        {
            var Base = Type.BaseStructures[i].Base;
            var SBase = Base.UnderlyingStructureOrRealId as StructuredType;

            if (CanAccessProtected(SBase.StructuredScope, Id))
                return true;
        }

        return false;
    }

    public static bool VerifyAccess(IdContainer Container, Identifier Id, CodeString Str, bool EnableMessages = true)
    {
        var State = Container.State;
        if (Id.Access == IdentifierAccess.Public || Id.Access == IdentifierAccess.Unknown)
            return true;

        if ((Id.Access & IdentifierAccess.Internal) != 0)
            if (!Id.Container.IsScopeOfAssembly())
            {
                if (EnableMessages)
                    State.Messages.Add(MessageId.CantAccessInternal, Str);

                return false;
            }

        if ((Id.Access & IdentifierAccess.Protected) != 0)
            if (!CanAccessProtected(Container, Id))
            {
                if (EnableMessages)
                    State.Messages.Add(MessageId.CantAccessProtected, Str);

                return false;
            }

        if ((Id.Access & IdentifierAccess.Private) != 0)
        {
            if (!Id.Container.IsScopeOfAssembly())
            {
                if (EnableMessages)
                    State.Messages.Add(MessageId.PrivateInOtherLib, Str);

                return false;
            }

            if (!CanAccessPrivate(Container, Id))
            {
                if (EnableMessages)
                    State.Messages.Add(MessageId.CantAccessPrivate, Str);

                return false;
            }
        }

        return true;
    }

    public static Identifier Recognize(IdContainer Container, CodeString Code)
    {
        var Rec = Container.State.Language.IdRecognizers;
        return Recognize(Container, Code, GetIdOptions.Default, Rec);
    }

    public static Identifier Recognize(IdContainer Container, CodeString Code, GetIdOptions Options)
    {
        var Rec = Container.State.Language.IdRecognizers;
        return Recognize(Container, Code, Options, Rec);
    }

    public static Identifier Recognize(IdContainer Container, CodeString Code,
        GetIdOptions Options, IList<IIdRecognizer> Recognizers)
    {
        for (var i = 0; i < Recognizers.Count; i++)
        {
            Identifier Out = null;
            var Res = Recognizers[i].Recognize(Container, Code, Options, ref Out);
            if (Res != SimpleRecResult.Unknown)
            {
                if (Res == SimpleRecResult.Succeeded)
                {
                    if (Out == null)
                        throw new ApplicationException("Recognized identifier name cannot be null");

                    return Out;
                }

                return null;
            }
        }

        if (Options.EnableMessages)
            Container.State.Messages.Add(MessageId.UnknownId, Code);

        return null;
    }
}

public enum DeclaredIdType : byte
{
    Unknown,
    Alias,
    Class,
    Struct,
    Enum,
    Flag,
    Function,
    Constructor,
    Destructor,
    Variable,
    Constant,
    Property,
    Namespace
}

public enum UndeclaredIdType : byte
{
    Unknown,
    Pointer,
    Reference,
    Tuple,
    Function,
    NonrefArrayType,
    RefArrayType,
    PointerAndLength,
    NonstaticFunctionType,

    SByte,
    Int16,
    Int32,
    Int64,

    Byte,
    UInt16,
    UInt32,
    UInt64,

    IntPtr,
    UIntPtr,

    Single,
    Double,

    Boolean,
    Object,
    String,
    Char,

    Void,
    Type,
    Auto,
    Null,
    Namespace
}

[Flags]
public enum IdentifierAccess : byte
{
    Public = 0,
    Protected = 1,
    Private = 2,
    Internal = 4,
    InternalProtected = Internal | Protected,
    Unknown = Internal | Private
}

[Flags]
public enum IdentifierFlags : ushort
{
    None = 0,
    Virtual = 1,
    Override = 2,
    Abstract = 4,
    Sealed = 8,
    Static = 16,
    Extern = 32,
    ReadOnly = 64,
    SpecialName = 128,
    HideBaseId = 256,
    All = Virtual | Override | Abstract | Sealed | Static | Extern | ReadOnly | SpecialName

    // AssemblyDesc: 16384, 32768
}

public abstract class Identifier
{
    private string _AssemblyName;
    private string _AssemblyNameWithoutDecorations;

    public IdentifierAccess Access = IdentifierAccess.Private;
    public Identifier[] Children;
    public IdContainer Container;
    public DataList Data = new();
    public CodeString Declaration;
    public DeclaredIdType DeclaredIdType = DeclaredIdType.Unknown;
    public long DescPosition = -1;
    public IdentifierFlags Flags = IdentifierFlags.None;
    public int LocalIndex = -1;
    public CodeString Name;
    public Identifier OverriddenId;
    public Identifier RealId;
    public int ReferenceIndex = -1;
    public UndeclaredIdType UndeclaredIdType = UndeclaredIdType.Unknown;

    public bool Used;

    public Identifier(IdContainer Container, CodeString Name)
    {
        if (Container == null)
            throw new ArgumentNullException("Container");

        this.Name = Name;
        Declaration = Name;
        this.Container = Container;
        Access = Container.DefaultAccess;
        RealId = this;
    }

    public abstract Identifier TypeOfSelf { get; }

    public Identifier UnderlyingClassOrSelf
    {
        get
        {
            var BuiltinType = this as BuiltinType;
            if (BuiltinType != null && BuiltinType.UnderlyingType is ClassType)
                return BuiltinType.UnderlyingType;

            return this;
        }
    }

    public Identifier UnderlyingClassOrRealId
    {
        get
        {
            var BuiltinType = RealId as BuiltinType;
            if (BuiltinType != null && BuiltinType.UnderlyingType is ClassType)
                return BuiltinType.UnderlyingType;

            return RealId;
        }
    }

    public Identifier UnderlyingStructureOrSelf
    {
        get
        {
            var BuiltinType = this as BuiltinType;
            if (BuiltinType != null && BuiltinType.UnderlyingType != null)
                return BuiltinType.UnderlyingType;

            return this;
        }
    }

    public Identifier UnderlyingStructureOrRealId
    {
        get
        {
            var BuiltinType = RealId as BuiltinType;
            if (BuiltinType != null && BuiltinType.UnderlyingType != null)
                return BuiltinType.UnderlyingType;

            return RealId;
        }
    }

    public virtual bool IsInstanceIdentifier =>
        (Flags & IdentifierFlags.Static) == 0 && Container.RealContainer is StructuredScope;

    public virtual bool HasScopes
    {
        get
        {
            if (RealId == this) return false;
            return RealId.HasScopes;
        }
    }

    public virtual IEnumerable<ScopeNode> EnumScopes
    {
        get
        {
            if (RealId == this) return null;
            return RealId.EnumScopes;
        }
    }

    public string AssemblyName
    {
        get
        {
            if (_AssemblyName == null)
                _AssemblyName = CalculateAssemblyName(true);

            return _AssemblyName;
        }

        set
        {
            Flags |= IdentifierFlags.SpecialName;
            _AssemblyName = value;
        }
    }

    public string AssemblyNameWithoutDecorations
    {
        get
        {
            if ((Flags & IdentifierFlags.SpecialName) != 0)
                return _AssemblyName;
            if (_AssemblyNameWithoutDecorations != null)
                return _AssemblyNameWithoutDecorations;

            _AssemblyNameWithoutDecorations = CalculateAssemblyName(false);
            return _AssemblyNameWithoutDecorations;
        }
    }

    public virtual TypeConversion CanConvert(Identifier To)
    {
        if (RealId != this) return RealId.CanConvert(To);
        throw new InvalidOperationException();
    }

    public virtual bool IsEquivalent(Identifier Id)
    {
        return Id == this || RealId == Id || Id.RealId == this;
    }

    public int GetChildIndex(Identifier Id)
    {
        if (Children != null)
            for (var i = 0; i < Children.Length; i++)
                if (Children[i] == Id)
                    return i;

        return -1;
    }

    public void GenerateName()
    {
        var Name = Container.State.GenerateName(this);
        this.Name = new CodeString(Name);
    }

    public virtual void CalculateAccess()
    {
        if (Children == null || Children.Length == 0)
            Access = Container.DefaultAccess;
        else Access = Identifiers.IdAccessIntersect(Children);
    }

    public virtual void Update()
    {
    }

    public void SetUsed()
    {
        Used = true;

        if (Children != null)
            for (var i = 0; i < Children.Length; i++)
                if (Children[i] != null)
                    Children[i].SetUsed();
    }

    public virtual bool CalculateLayout()
    {
        if (Children != null)
            for (var i = 0; i < Children.Length; i++)
                if (Children[i].DeclaredIdType == DeclaredIdType.Unknown)
                    if (!Children[i].CalculateLayout())
                        return false;

        return true;
    }

    protected virtual string CalculateAssemblyName(bool Decorations)
    {
        return Container.AssemblyName + "_" + Name;
    }

    public virtual void GetAssembly(CodeGenerator CG, GetAssemblyMode Mode)
    {
    }

    public virtual void GetGlobalPointers(List<string> Out)
    {
    }
}

public class IdentifierAlias : Identifier
{
    public IdentifierAlias(IdContainer Container, CodeString Name, Identifier Child)
        : base(Container, Name)
    {
        DeclaredIdType = DeclaredIdType.Alias;
        RealId = Child;
    }

    public override Identifier TypeOfSelf => RealId.TypeOfSelf;

    public override bool IsEquivalent(Identifier Id)
    {
        return RealId.IsEquivalent(Id);
    }

    public override bool CalculateLayout()
    {
        return RealId.CalculateLayout();
    }
}

public abstract class PackedId : Identifier
{
    public PackedId(IdContainer Container, CodeString Name, Identifier Type)
        : base(Container, Name)
    {
        Children = new[] { Type };
    }

    public override Identifier TypeOfSelf => Children[0];

    public abstract ExpressionNode Extract(PluginRoot Plugin);
}

public class PackedMemberId : PackedId
{
    public Identifier Member;
    public ExpressionNode Node;

    public PackedMemberId(IdContainer Container, CodeString Name, Identifier Type, ExpressionNode Node,
        Identifier Member)
        : base(Container, Name, Type)
    {
        this.Member = Member;
        this.Node = Node;
    }

    public override ExpressionNode Extract(PluginRoot Plugin)
    {
        var Id = Node.Copy(Plugin, Mode: BeginEndMode.None);
        var Member = Plugin.NewNode(new IdExpressionNode(this.Member, Name));
        if (Id == null || Member == null) return null;

        var NewCh = new[] { Id, Member };
        return Plugin.NewNode(new OpExpressionNode(Operator.Member, NewCh, Name));
    }
}