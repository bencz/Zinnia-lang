using System;
using System.Collections.Generic;
using Zinnia.Base;

namespace Zinnia;

public abstract class IdContainer
{
    private string _AssemblyName;

    private AssemblyScope _AssemblyScope;

    private GlobalContainer _GblScope;

    private StructuredScope _TypeScope;
    public AutoAllocatedList<IdContainer> Children;
    public DataList Data = new();
    public AutoAllocatedList<FunctionOverloads> FunctionOverloads;
    public FunctionScope FunctionScope;

    public AutoAllocatedList<Identifier> IdentifierList;
    public int LocalIndex = -1;
    public IdContainer Parent;
    public CompilerState State;

    public IdContainer(IdContainer Parent)
    {
        this.Parent = Parent;

        if (Parent == null)
        {
            if (!(this is GlobalContainer))
                throw new ArgumentNullException("Parent");
        }
        else
        {
            if (State == null) State = Parent.State;
            if (FunctionScope == null) FunctionScope = Parent.FunctionScope;

            if (FunctionScope != null)
            {
                LocalIndex = FunctionScope.ContainerLocalIndexCount;
                FunctionScope.ContainerLocalIndexCount++;
            }
        }
    }

    public string AssemblyName
    {
        get
        {
            if (_AssemblyName == null)
                _AssemblyName = CalculateAssemblyName();

            return _AssemblyName;
        }
    }

    public virtual IdContainer RealContainer => this;

    public virtual CallingConvention DefaultCallConv => State.DefaultCallConv;

    public virtual IdentifierAccess DefaultAccess => IdentifierAccess.Unknown;

    public GlobalContainer GlobalContainer
    {
        get
        {
            if (_GblScope == null)
                _GblScope = GetParent<GlobalContainer>();

            return _GblScope;
        }
    }

    public AssemblyScope AssemblyScope
    {
        get
        {
            if (_AssemblyScope == null)
                _AssemblyScope = GetParent<AssemblyScope>();

            return _AssemblyScope;
        }
    }

    public StructuredScope StructuredScope
    {
        get
        {
            if (_TypeScope == null)
                _TypeScope = GetParent<StructuredScope>();

            return _TypeScope;
        }
    }

    public void ReplaceChild(IdContainer From, IdContainer To)
    {
        for (var i = 0; i < Children.Count; i++)
            if (Children[i] == From)
                Children[i] = To;
    }

    public virtual void ForEachId(Action<Identifier> Func)
    {
        IdentifierList.ForEach(Func);
    }

    public virtual bool TrueForAllId(Predicate<Identifier> Func)
    {
        return IdentifierList.TrueForAll(Func);
    }

    public void ForEachParent<T>(Action<T> Action, IdContainer Until = null)
        where T : IdContainer
    {
        if (this is T) Action(this as T);
        if (Parent != null && Parent != Until)
            Parent.ForEachParent(Action, Until);
    }

    public bool TrueForAllParent<T>(Predicate<T> Func, IdContainer Until = null)
        where T : IdContainer
    {
        if (this is T && !Func(this as T)) return false;
        if (Parent != null && Parent != Until)
            return Parent.TrueForAllParent(Func, Until);

        return true;
    }

    public void ForEach(Action<IdContainer> Action)
    {
        Action(this);

        for (var i = 0; i < Children.Count; i++)
            Children[i].ForEach(Action);
    }

    public int GetIndirectChildIndex(IdContainer Container)
    {
        var Index = GetChildIndex(Container);
        while (Index == -1)
        {
            Container = Container.Parent;
            if (Container == this || Container == null)
                return -1;

            Index = GetChildIndex(Container);
        }

        return Index;
    }

    public int GetChildIndex(IdContainer Container)
    {
        for (var i = 0; i < Children.Count; i++)
            if (Children[i] == Container)
                return i;

        return -1;
    }

    public virtual void AddIdentifier(Identifier Id)
    {
        if (FunctionScope != null)
        {
            Id.LocalIndex = FunctionScope.LocalIdentifiers.Count;
            FunctionScope.LocalIdentifiers.Add(Id);
        }

        IdentifierList.Add(Id);
    }

    public virtual void GetAssembly(CodeGenerator CG, GetAssemblyMode Mode = GetAssemblyMode.Code)
    {
        for (var i = 0; i < IdentifierList.Count; i++)
            IdentifierList[i].GetAssembly(CG, Mode);

        for (var i = 0; i < Children.Count; i++)
            Children[i].GetAssembly(CG, Mode);
    }

    public List<string> GetGlobalPointers()
    {
        var Ret = new List<string>();
        GetGlobalPointers(Ret);
        return Ret;
    }

    public virtual void GetGlobalPointers(List<string> Out)
    {
        for (var i = 0; i < IdentifierList.Count; i++)
            IdentifierList[i].GetGlobalPointers(Out);

        for (var i = 0; i < Children.Count; i++)
            Children[i].GetGlobalPointers(Out);
    }

    protected virtual string CalculateAssemblyName()
    {
        if (Parent != null)
            return Parent.AssemblyName;

        return "";
    }

    private static bool SameTypes(VarDeclarationList A, VarDeclarationList B)
    {
        if (A.Count != B.Count)
            return false;

        for (var i = 0; i < A.Count; i++)
            if (!A[i].Type.IsEquivalent(B[i].Type))
                return false;

        return true;
    }

    public virtual bool CanIdDeclared(Identifier Id)
    {
        var StructuredContainer = Id.Container.RealContainer as StructuredScope;
        var IsLocal = Id.Container.FunctionScope != null;

        if (Id.Access == IdentifierAccess.Unknown)
        {
            if (!IsLocal)
                throw new ApplicationException("Only locals' access can be unknown");
        }
        else
        {
            if (IsLocal)
                throw new ApplicationException("Locals' access must be unknown");

            if (((int)Id.Access & 3) != 0 && StructuredContainer == null)
            {
                State.Messages.Add(MessageId.ProtPrivInNonStructured, Id.Declaration);
                return false;
            }
        }
        /*
        if (Id.Name.IsValid && !Id.Name.IsValidIdentifierName)
        {
            State.Messages.Add(MessageId.NotValidName, Id.Name);
            return false;
        }
        */

        if (Id is Namespace)
        {
            if (!(this is NamespaceScope))
                throw new ApplicationException("Namespaceses cannot be declared here");
        }
        else if (Id is Property)
        {
            var Property = Id as Property;
            var PropScope = Property.PropertyScope;
            var Type = Id.Children[0];

            if (Identifiers.IsLessAccessable(Type, Id))
            {
                State.Messages.Add(MessageId.LessAccessable, Id.Name, Type.Name.ToString());
                return false;
            }

            for (var i = 0; i < PropScope.IdentifierList.Count; i++)
            {
                var Ch = PropScope.IdentifierList[i];
                if (Ch != null && Identifiers.IsLessAccessable(Id, Ch))
                {
                    State.Messages.Add(MessageId.LessAccessable, Ch.Name, Id.Name.ToString());
                    return false;
                }
            }
        }
        else if (Id is Variable)
        {
            var Var = Id as Variable;
            var Type = Id.Children[0];
            /*
            if (Var is ConstVariable && Var.GlbInitVal == null)
                throw new ApplicationException("Constants must have a value");
            */
            if (!IsLocal && Identifiers.IsLessAccessable(Type, Id))
            {
                State.Messages.Add(MessageId.LessAccessable, Id.Name, Type.Name.ToString());
                return false;
            }
        }
        else if (Id is Function)
        {
            if (Id.Name.StartsWith("%Operator_"))
            {
                if ((Id.Flags & IdentifierFlags.Static) == 0 || Id.Access != IdentifierAccess.Public)
                {
                    State.Messages.Add(MessageId.OperatorModifiers, Id.Declaration);
                    return false;
                }

                if (!(this is StructuredScope))
                {
                    State.Messages.Add(MessageId.CannotDeclFunc, Id.Declaration);
                    return false;
                }
            }

            if ((Id is Constructor || Id is Destructor) && !(this is StructuredScope))
            {
                State.Messages.Add(MessageId.CannotDeclFunc, Id.Declaration);
                return false;
            }

            var Func = Id as Function;
            var FuncType = Func.TypeOfSelf.RealId as TypeOfFunction;
            var RetType = FuncType.Children[0];

            if (Identifiers.IsLessAccessable(RetType, Id))
            {
                State.Messages.Add(MessageId.LessAccessable, Id.Declaration, RetType.Name.ToString());
                return false;
            }

            var DefaultValues = false;
            var CantBeMore = false;
            for (var i = 1; i < FuncType.Children.Length; i++)
            {
                var e = FuncType.Children[i] as FunctionParameter;
                if (Identifiers.IsLessAccessable(e.Children[0], Id))
                {
                    State.Messages.Add(MessageId.LessAccessable, e.Declaration, e.TypeOfSelf.Name.ToString());
                    return false;
                }

                if (CantBeMore)
                {
                    State.Messages.Add(MessageId.ParamArrayMustBeTheLast, e.Declaration);
                    return false;
                }

                if ((e.ParamFlags & ParameterFlags.ParamArray) != 0)
                {
                    CantBeMore = true;
                }
                else if (e.ConstInitValue != null)
                {
                    DefaultValues = true;
                }
                else if (DefaultValues)
                {
                    State.Messages.Add(MessageId.ParamIsntOptional, e.Declaration);
                    return false;
                }
            }

            var Overload = Func.Overload;
            if (Overload.OverloadCount > 0)
                for (var i = 0; i < Overload.Functions.Count; i++)
                {
                    var e = Overload.Functions[i];
                    if (e == Id) continue;

                    if (Func is Constructor && e is Constructor)
                    {
                        if ((Func.Flags & IdentifierFlags.Static) != 0 && (e.Flags & IdentifierFlags.Static) != 0)
                        {
                            State.Messages.Add(MessageId.IdAlreadyDefined, Func.Declaration);
                            return false;
                        }

                        if ((Func.Flags & IdentifierFlags.Static) != 0 || (e.Flags & IdentifierFlags.Static) != 0)
                        {
                            continue;
                        }
                    }

                    if (Identifiers.AreParametersSame(Func, e))
                    {
                        State.Messages.Add(MessageId.SameOverloads, Id.Declaration);
                        return false;
                    }
                }

            return true;
        }
        else if (Id is IdentifierAlias)
        {
            if (Identifiers.IsLessAccessable(Id.RealId, Id))
            {
                State.Messages.Add(MessageId.LessAccessable, Id.Declaration, Id.RealId.Name.ToString());
                return false;
            }
        }
        else if (!(Id is Type))
        {
            throw new NotImplementedException();
        }

        if (Id.Name.IsValid && (Id.Flags & IdentifierFlags.Override) == 0)
            if (IsAlreadyDefined(Id.Name.ToString()))
            {
                State.Messages.Add(MessageId.IdAlreadyDefined, Id.Declaration);
                return false;
            }

        return true;
    }

    public bool DeclareIdentifier(Identifier Id)
    {
        if (!CanIdDeclared(Id)) return false;
        AddIdentifier(Id);
        return true;
    }

    public bool DeclareVariables(CodeString Str, List<Modifier> Mods = null,
        VarDeclConvMode Mode = VarDeclConvMode.Nothing, GetIdMode IdMode = GetIdMode.Everywhere)
    {
        var List = VarDeclarationList.Create(this, Str, Mods);
        if (List == null) return false;

        return DeclareVariables(List, Mode, IdMode);
    }

    public bool DeclareVariables(VarDeclarationList List, VarDeclConvMode Mode = VarDeclConvMode.Nothing,
        GetIdMode IdMode = GetIdMode.Everywhere)
    {
        var Variables = List.ToVariables(GetPlugin(), BeginEndMode.Both, Mode);
        return DeclareVariables(Variables, IdMode);
    }

    public virtual bool DeclareVariables(Variable[] Variables, GetIdMode IdMode = GetIdMode.Everywhere)
    {
        var RetValue = true;
        for (var i = 0; i < Variables.Length; i++)
            if (Variables[i] == null || !DeclareIdentifier(Variables[i]))
                RetValue = false;

        return RetValue;
    }

    public Variable CreateVariableHelper(CodeString Name, Identifier Type, List<Modifier> Mods = null)
    {
        if (Mods != null)
        {
            if (Modifiers.Contains<ConstModifier>(Mods))
                return new ConstVariable(this, Name, Type, null);
            if ((Modifiers.GetFlags(Mods) & IdentifierFlags.Static) != 0)
                return new GlobalVariable(this, Name, Type);
        }

        return null;
    }

    public FunctionOverloads GetOverload(string Name)
    {
        for (var i = 0; i < FunctionOverloads.Count; i++)
        {
            var Overload = FunctionOverloads[i];
            if (Overload.Name == Name) return Overload;
        }

        var Ret = new FunctionOverloads(Name);
        FunctionOverloads.Add(Ret);
        return Ret;
    }

    public Namespace CreateNamespace(CodeString Name, List<Modifier> Mods = null)
    {
        var Ret = new Namespace(this, Name);
        if (Mods != null && !Modifiers.Apply(Mods, Ret)) return null;
        return Ret;
    }

    public Property CreateProperty(CodeString Name, Identifier Type, FunctionParameter[] Parameters = null,
        List<Modifier> Mods = null)
    {
        var ParameterCount = Parameters == null ? 0 : Parameters.Length;
        var Children = new Identifier[ParameterCount + 1];
        Children[0] = Type;

        if (ParameterCount > 0)
            for (var i = 0; i < ParameterCount; i++)
                Children[i + 1] = Parameters[i];

        return CreateProperty(Name, Children, Mods);
    }

    public Property CreateProperty(CodeString Name, Identifier[] Children, List<Modifier> Mods = null)
    {
        var Type = Children[0].RealId as Type;
        if ((Type.TypeFlags & TypeFlags.CanBeVariable) == 0)
        {
            State.Messages.Add(MessageId.CannotBeThisType, Name);
            return null;
        }

        var Ret = new Property(this, Name, Children);
        if (Mods != null && !Modifiers.Apply(Mods, Ret)) return null;
        return Ret;
    }

    public Variable CreateVariable(CodeString Name, Identifier Type, List<Modifier> Mods = null)
    {
        var RType = Type.RealId as Type;
        if ((RType.TypeFlags & TypeFlags.CanBeVariable) == 0)
        {
            State.Messages.Add(MessageId.CannotBeThisType, Name);
            return null;
        }

        var Ret = OnCreateVariable(Name, Type, Mods);
        if (Ret == null) return null;

        if (Mods != null && !Modifiers.Apply(Mods, Ret)) return null;
        return Ret;
    }

    public Variable CreateAndDeclareVariable(CodeString Name, Identifier Type, List<Modifier> Mods = null)
    {
        var Ret = CreateVariable(Name, Type, Mods);
        if (Ret == null || !DeclareIdentifier(Ret)) return null;
        return Ret;
    }

    public Function CreateFunction(CodeString Name, TypeOfFunction FuncType, List<Modifier> Mods = null)
    {
        var Overload = GetOverload(Name.ToString());
        var Ret = OnCreateFunction(Name, FuncType, Overload, Mods);
        if (Ret == null) return null;

        if (!AdjustFunction(Ret, Mods)) return null;
        return Ret;
    }

    public bool AdjustFunction(Function Func, List<Modifier> Mods = null)
    {
        var Overload = Func.Overload;
        if (Overload != null)
        {
            Func.OverloadIndex = Overload.OverloadCount;
            Overload.Functions.Add(Func);
        }

        if (Mods != null)
        {
            if (!Modifiers.Apply(Mods, Func)) return false;
            Func.TypeOfSelf.GenerateName();
        }

        return true;
    }

    public bool AdjustAndDeclareFunction(Function Func, List<Modifier> Mods = null)
    {
        if (!AdjustFunction(Func, Mods)) return false;
        return DeclareIdentifier(Func);
    }

    public virtual Function OnCreateFunction(CodeString Name, TypeOfFunction FuncType,
        FunctionOverloads Overload = null, List<Modifier> Mods = null)
    {
        return new Function(this, Name, FuncType, Overload);
    }

    public virtual Variable OnCreateVariable(CodeString Name, Identifier Type, List<Modifier> Mods = null)
    {
        var Ret = CreateVariableHelper(Name, Type, Mods);
        if (Ret == null) Ret = new GlobalVariable(this, Name, Type);
        return Ret;
    }

    public Function CreateFunction(CodeString Name, CodeString SRetType, FunctionParameter[] Params,
        List<Modifier> Mods = null)
    {
        var RetType = RecognizeIdentifier(SRetType, GetIdOptions.DefaultForType);
        if (RetType == null) return null;

        if (RetType.RealId is AutomaticType)
        {
            State.Messages.Add(MessageId.VarFuncRetType, SRetType);
            return null;
        }

        var FuncType = new TypeOfFunction(this, DefaultCallConv, RetType, Params);
        return CreateFunction(Name, FuncType, Mods);
    }

    public Function CreateAndDeclareFunction(CodeString Name, TypeOfFunction Type, List<Modifier> Mods = null)
    {
        var Func = CreateFunction(Name, Type, Mods);
        if (Func == null) return null;

        if (!DeclareIdentifier(Func)) return null;
        return Func;
    }

    public Function CreateAndDeclareFunction(CodeString Name, CodeString SRetType,
        FunctionParameter[] Params, List<Modifier> Mods = null)
    {
        var Func = CreateFunction(Name, SRetType, Params, Mods);
        if (Func == null) return null;

        if (!DeclareIdentifier(Func)) return null;
        return Func;
    }

    public Function CreateDeclaredFunctionAndScope(CodeString Name, TypeOfFunction Type, CodeString Inner,
        List<Modifier> Mods = null)
    {
        var Func = CreateAndDeclareFunction(Name, Type, Mods);
        if (Func == null) return null;

        if (Func.HasCode)
        {
            Func.FunctionScope = new FunctionScope(this, Func, Inner);
            if (!Func.FunctionScope.Initialize()) return null;
        }

        return Func;
    }

    public Function CreateDeclaredFunctionAndScope(CodeString Name, CodeString SRetType,
        FunctionParameter[] Params, CodeString Inner, List<Modifier> Mods = null)
    {
        var Func = CreateAndDeclareFunction(Name, SRetType, Params, Mods);
        if (Func == null || CreateScopeForFunction(Func, Inner) == null) return null;
        return Func;
    }

    public FunctionScope CreateScopeForFunction(Function Function, CodeString Inner)
    {
        if (Function.NeedsToBeCompiled)
        {
            Function.FunctionScope = new FunctionScope(this, Function, Inner);
            if (!Function.FunctionScope.Initialize()) return null;
        }
        else if (Inner.IsValid)
        {
            State.Messages.Add(MessageId.FuncCannotHaveInnerScope, Function.Declaration);
            return null;
        }

        return Function.FunctionScope;
    }

    public IdContainer GetCommonContainer(IdContainer Container)
    {
        if (Container == this || Container.IsSubContainerOf(this)) return this;
        if (Parent != null) return Parent.GetCommonContainer(Container);
        return null;
    }

    public bool IsSubContainerOf(IdContainer Container)
    {
        var Current = Parent;
        while (Container != Current)
        {
            if (Current == null) return false;
            Current = Current.Parent;
        }

        return true;
    }

    public Command GetParent(CommandType Type, IdContainer Until = null)
    {
        return GetParent<Command>(x => x.Type == Type, Until);
    }

    public T GetParent<T>(IdContainer Until)
        where T : IdContainer
    {
        return GetParent<T>(x => true, Until);
    }

    public T GetParent<T>(Predicate<T> Func, IdContainer Until)
        where T : IdContainer
    {
        if (Parent == Until)
            return null;

        var Current = Parent;
        while (!(Current is T) || !Func(Current as T))
        {
            if (Current == null || Current.Parent == Until)
                return null;

            Current = Current.Parent;
        }

        return Current as T;
    }

    public bool IsSubContainerOf<T>(Predicate<T> Func, IdContainer Until = null)
        where T : IdContainer
    {
        return GetParent(Func, Until) != null;
    }

    public virtual PluginRoot GetPlugin()
    {
        return new PluginForGlobals(this);
    }

    public T GetParent<T>(Predicate<T> Func = null) where T : IdContainer
    {
        var Scope = this;
        while (Scope != null)
        {
            var TScope = Scope as T;
            if (TScope != null && (Func == null || Func(TScope)))
                return TScope;

            Scope = Scope.Parent;
        }

        return null;
    }

    public bool Contains(IdContainer Container)
    {
        for (var i = 0; i < Children.Count; i++)
            if (Children[i].Contains(Container))
                return true;

        return false;
    }

    public bool IsScopeOfAssembly(Assembly Assembly)
    {
        var Scope = AssemblyScope;
        if (Scope == null) return false;
        return Scope.Assembly == Assembly;
    }

    public bool IsNotLoadedAssemblyScope()
    {
        var Scope = AssemblyScope;
        return Scope == null || Scope.Assembly == GlobalContainer.OutputAssembly;
    }

    public bool IsScopeOfAssembly()
    {
        return IsScopeOfAssembly(GlobalContainer.OutputAssembly);
    }

    public Identifier GetRetType(Identifier Type0, Identifier Type1)
    {
        var RType0 = Type0.RealId;
        var RType1 = Type1.RealId;

        if (RType0 is AutomaticType || RType0 is CharType) return Type1;
        if (RType1 is AutomaticType || RType1 is CharType) return Type0;
        if (RType0 is StringType || RType0 is PointerType) return Type0;
        if (RType1 is StringType || RType1 is PointerType) return Type1;

        if (RType0 is NumberType && RType1 is NumberType)
            return GetNumberRetType(RType0 as NumberType, RType1 as NumberType);

        var Tuple0 = RType0 as TupleType;
        var Tuple1 = RType1 as TupleType;
        if (Tuple0 != null && Tuple1 != null)
        {
            var Members0 = Tuple0.StructuredScope.IdentifierList;
            var Members1 = Tuple1.StructuredScope.IdentifierList;
            if (Members0.Count == Members1.Count)
            {
                var Members = new List<Identifier>();
                for (var i = 0; i < Members0.Count; i++)
                {
                    var Var0 = Members0[i] as MemberVariable;
                    var Var1 = Members1[i] as MemberVariable;

                    var T = GetRetType(Var0.TypeOfSelf, Var1.TypeOfSelf);
                    if (T == null) return null;

                    var MemVar = new MemberVariable(this, new CodeString(), T);
                    MemVar.Access = T.Access;
                    Members.Add(MemVar);
                }

                return new TupleType(this, Members);
            }
        }

        return Type0;
    }

    public Identifier GetNumberRetType(Identifier Type0, Identifier Type1)
    {
        var RType0 = Type0.RealId as Type;
        var RType1 = Type1.RealId as Type;

        var mSize = RType0.Size;
        if (RType1.Size > mSize) mSize = RType1.Size;

        if (RType0 is FloatType && RType1 is FloatType)
            return GlobalContainer.CommonIds.GetIdentifier<FloatType>(mSize);

        if (RType0 is FloatType) return Type0;
        if (RType1 is FloatType) return Type1;

        if (RType0 is SignedType && RType1 is SignedType)
            return GlobalContainer.CommonIds.GetIdentifier<SignedType>(mSize);

        SignedType Signed;
        UnsignedType Unsigned;
        if (RType0 is SignedType)
        {
            Signed = RType0 as SignedType;
            Unsigned = RType1 as UnsignedType;
        }
        else
        {
            Signed = RType1 as SignedType;
            Unsigned = RType0 as UnsignedType;
        }

        if (Signed != null && Unsigned != null)
        {
            if (Signed.Size > Unsigned.Size) return Signed;
            return GlobalContainer.CommonIds.GetIdentifier<SignedType>(mSize * 2);
        }

        return GlobalContainer.CommonIds.GetIdentifier<UnsignedType>(mSize);
    }

    private Identifier GetPredeclaredIdentifier(string Name)
    {
        var Ids = GlobalContainer.CommonIds.Predeclared;
        for (var i = 0; i < Ids.Count; i++)
            if (Ids[i].Name.IsEqual(Name))
                return Ids[i];

        return null;
    }

    public Identifier RecognizeIdentifier(CodeString Code)
    {
        return Identifiers.Recognize(this, Code);
    }

    public Identifier RecognizeIdentifier(CodeString Code, GetIdOptions Options)
    {
        return Identifiers.Recognize(this, Code, Options);
    }

    public Identifier RecognizeIdentifier(CodeString Code, GetIdOptions Options, IList<IIdRecognizer> Recognizers)
    {
        return Identifiers.Recognize(this, Code, Options, Recognizers);
    }

    public virtual bool GetContainerId(string Name, List<IdentifierFound> Out, Predicate<Identifier> Func = null)
    {
        return Identifiers.Search(this, IdentifierList, Name, Out, Func);
    }

    public List<IdentifierFound> GetContainerId(string Name, Predicate<Identifier> Func = null)
    {
        var Out = new List<IdentifierFound>();
        GetContainerId(Name, Out, Func);
        return Out;
    }

    public virtual bool IsAlreadyDefined(string Name, Predicate<Identifier> Func = null)
    {
        return Identifiers.Search(this, IdentifierList, Name, Func).Count > 0;
    }

    public bool GetIdentifier(string Name, List<IdentifierFound> Out, GetIdMode Mode = GetIdMode.Everywhere,
        bool SearchedInBasicTypes = false, Predicate<Identifier> Func = null)
    {
        /*if (FunctionScope != null && Mode != GetIdMode.Scope)
        {
            Predicate<T> LFunc = x =>
            {
                if (x.Container != this && !IsSubContainerOf(x.Container))
                    return false;

                return Func == null || Func(x);
            };

            FunctionScope.LocalIdentifiers.Get<T>(Name, Out, LFunc);
            if (Mode == GetIdMode.Everywhere && FunctionScope.Parent != null)
                FunctionScope.Parent.GetIdentifier(Name, Out, Mode, SearchedInBasicTypes, Func);
        }
        else
        {*/
        if (!SearchedInBasicTypes)
        {
            var R = GetPredeclaredIdentifier(Name);
            if (R != null && (Func == null || Func(R)))
            {
                Out.Add(new IdentifierFound(GlobalContainer, R));
                return true;
            }

            SearchedInBasicTypes = true;
        }

        var RetValue = GetContainerId(Name, Out, Func);
        if (Parent != null && Mode != GetIdMode.Container)
        {
            var F = FunctionScope;
            if (Mode != GetIdMode.Function || (Parent.FunctionScope == F && F != null))
                if (Parent.GetIdentifier(Name, Out, Mode, SearchedInBasicTypes, Func))
                    RetValue = true;
        }

        return RetValue;
        //}
    }

    public List<IdentifierFound> GetIdentifier(string Name, GetIdMode Mode = GetIdMode.Everywhere,
        Predicate<Identifier> Func = null)
    {
        var Out = new List<IdentifierFound>();
        GetIdentifier(Name, Out, Mode, Func: Func);
        return Out;
    }

    public bool IsIdDefined(string Name, GetIdMode Mode = GetIdMode.Everywhere, Predicate<Identifier> Func = null)
    {
        return GetIdentifier(Name, Mode, Func).Count > 0;
    }

    public bool IsIdDefined(Identifier Id)
    {
        if (IdentifierList.Contains(Id)) return true;
        if (Parent != null) return Parent.IsIdDefined(Id);
        return false;
    }
}