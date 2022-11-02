using System;
using System.Collections.Generic;
using Zinnia.Base;

namespace Zinnia;

public enum NamespaceDeclType : byte
{
    Declare,
    Use
}

public class NamespaceDecl
{
    public CodeString Inner;
    public List<CodeString> Names;

    public Namespace Namespace;
    public NamespaceScope NewScope;
    public NamespaceScope Scope;
    public NamespaceDeclType Type;

    public NamespaceDecl(NamespaceScope Scope, NamespaceDeclType Type,
        List<CodeString> Names, CodeString Inner = new())
    {
        this.Scope = Scope;
        this.Type = Type;
        this.Names = Names;
        this.Inner = Inner;

        if (Names == null || Names.Count == 0 || !Names[0].IsValid)
            throw new ArgumentException("Name not specified", "Names");
    }

    public bool Declare(bool AddUsingLate = false)
    {
        var State = Scope.State;
        var Rec = State.Language.NamespaceDeclRecognizer;

        for (var i = 0; i < Names.Count; i++)
            if (!Names[i].IsValidIdentifierName)
            {
                State.Messages.Add(MessageId.NotValidName, Names[i]);
                return false;
            }

        if (Type == NamespaceDeclType.Declare)
        {
            var Options = new GetIdOptions();
            Options.Func = x => x is Namespace;

            Namespace = Scope.Namespace;
            NewScope = Scope;
            for (var i = 0; i < Names.Count; i++)
            {
                Namespace = Identifiers.GetMember(State, Namespace, Names[i], Options) as Namespace;

                if (Namespace == null)
                {
                    Namespace = new Namespace(NewScope, Names[i]);
                    if (!NewScope.DeclareIdentifier(Namespace))
                        return false;
                }

                var OldScope = NewScope;
                var NewInner = i == Names.Count - 1 ? Inner : new CodeString();

                NewScope = new NamespaceScope(OldScope, NewInner, Namespace);
                OldScope.Children.Add(NewScope);
                Namespace.AddScope(NewScope);
            }
        }
        else
        {
            var Options = GetIdOptions.Default;
            Options.Func = x => x is Namespace;

            Namespace = Identifiers.Recognize(Scope, Names[0], Options) as Namespace;
            if (Namespace == null) return false;

            for (var i = 1; i < Names.Count; i++)
            {
                Namespace = Identifiers.GetMember(State, Namespace, Names[i]) as Namespace;
                if (Namespace == null) return false;
            }

            if (!AddUsingLate)
                Scope.UsedNamespaces.Add(Namespace);
        }

        return true;
    }
}

public class NamespaceDeclList : List<NamespaceDecl>
{
    public static NamespaceDeclList Create(NamespaceScope Scope)
    {
        var Ret = new NamespaceDeclList();
        var Rec = Scope.State.Language.NamespaceDeclRecognizer;
        if (Rec != null)
        {
            if (!Rec.Recognize(Scope, Ret)) return null;
            if (!Ret.Resolve()) return null;
        }

        return Ret;
    }

    public static NamespaceDeclList CreateAndDeclareRecursively(IdContainer Container)
    {
        var Ret = new NamespaceDeclList();
        if (Container.State.Language.NamespaceDeclRecognizer != null)
        {
            if (!CreateAndDeclareRecursively(Container, Ret))
                return null;

            if (!Ret.Resolve()) return null;
        }

        return Ret;
    }

    private static bool CreateAndDeclareRecursively(IdContainer Container, NamespaceDeclList Out)
    {
        var RetValue = true;
        var State = Container.State;
        var Scope = Container as NamespaceScope;

        for (var i = 0; i < Container.Children.Count; i++)
        {
            var e = Container.Children[i];
            if (!CreateAndDeclareRecursively(e, Out))
                RetValue = false;
        }

        if (Scope != null && Scope.Code.IsValid)
        {
            var NewList = new NamespaceDeclList();
            var Rec = State.Language.NamespaceDeclRecognizer;
            if (!Rec.Recognize(Scope, NewList) || !NewList.Declare())
                RetValue = false;

            for (var i = 0; i < NewList.Count; i++)
            {
                var Decl = NewList[i];
                if (Decl.Type == NamespaceDeclType.Declare)
                    if (!CreateAndDeclareRecursively(Decl.NewScope, Out))
                        RetValue = false;

                Out.Add(Decl);
            }
        }

        return RetValue;
    }

    public bool Declare()
    {
        var RetValue = true;
        for (var i = 0; i < Count; i++)
        {
            var Decl = this[i];
            if (Decl.Type == NamespaceDeclType.Declare && !Decl.Declare())
                RetValue = false;
        }

        return RetValue;
    }

    public bool Resolve()
    {
        var RetValue = true;
        for (var i = 0; i < Count; i++)
        {
            var Decl = this[i];
            if (Decl.Type == NamespaceDeclType.Use && !Decl.Declare(true))
                RetValue = false;
        }

        for (var i = 0; i < Count; i++)
        {
            var Decl = this[i];
            if (Decl.Type == NamespaceDeclType.Use)
                Decl.Scope.UsedNamespaces.Add(Decl.Namespace);
        }

        return RetValue;
    }
}

public class ConstDeclaration
{
    public IdContainer Container;
    public List<CodeString> Dependencies;
    public List<Modifier> Mods;
    public CodeString Name;

    public CodeString Str_Value;
    public Identifier Type;
    public ExpressionNode Value;

    public ConstDeclaration(IdContainer Container, CodeString Name, Identifier Type,
        CodeString Str_Value, ExpressionNode Value, List<Modifier> Mods)
    {
        this.Container = Container;
        this.Name = Name;
        this.Type = Type;
        this.Mods = Mods;

        this.Str_Value = Str_Value;
        this.Value = Value;
        Dependencies = null;
    }

    public ConstDeclaration(IdContainer Container, CodeString Name, Identifier Type, CodeString Str_Value,
        List<Modifier> Mods)
        : this(Container, Name, Type, Str_Value, null, Mods)
    {
    }

    public ConstDeclaration(IdContainer Container, CodeString Name, Identifier Type, ExpressionNode Value,
        List<Modifier> Mods)
        : this(Container, Name, Type, new CodeString(), Value, Mods)
    {
    }

    private PluginRoot GetPlugin()
    {
        return new PluginForConstants(Container, true);
    }

    public bool CalcValue()
    {
        var Plugin = GetPlugin();
        Value = Expressions.CreateExpression(Str_Value, Plugin);
        Dependencies = Plugin.GetPlugin<IdRecognizerPlugin>().Dependencies.List;
        return Value != null;
    }

    public SimpleRecResult Declare()
    {
        var State = Container.State;
        if (!Name.IsValidIdentifierName)
        {
            State.Messages.Add(MessageId.NotValidName, Name);
            return SimpleRecResult.Failed;
        }

        if (Value == null)
        {
            if (Type.RealId is AutomaticType)
            {
                State.Messages.Add(MessageId.Untyped, Name);
                return SimpleRecResult.Failed;
            }

            var Var = Container.CreateAndDeclareVariable(Name, Type, Mods);
            return Var == null ? SimpleRecResult.Failed : SimpleRecResult.Succeeded;
        }

        if ((Value = Value.CallNewNode(GetPlugin())) == null)
            return SimpleRecResult.Failed;

        if (Value is ConstExpressionNode)
        {
            var ConstVal = Value as ConstExpressionNode;
            if (Type.RealId is AutomaticType) Type = ConstVal.Type;

            if (!ConstVal.Value.CheckBounds(State, Type, Str_Value))
                return SimpleRecResult.Failed;

            var Var = Container.CreateAndDeclareVariable(Name, Type, Mods);
            if (Var == null) return SimpleRecResult.Failed;

            Var.ConstInitValue = ConstVal.Value;
            return SimpleRecResult.Succeeded;
        }

        return SimpleRecResult.Unknown;
    }
}

public class ConstDeclarationList : List<ConstDeclaration>
{
    public static ConstDeclarationList Create(NonCodeScope Scope)
    {
        var Ret = new ConstDeclarationList();
        var Rec = Scope.State.Language.ConstDeclRecognizer;
        return Rec != null && !Rec.Recognize(Scope, Ret) ? null : Ret;
    }

    public static ConstDeclarationList CreateAndDeclareRecursively(IdContainer Container)
    {
        var Ret = new ConstDeclarationList();
        if (Container.State.Language.ConstDeclRecognizer != null)
        {
            if (!CreateAndDeclareRecursively(Container, Ret))
                return null;

            if (!CalcValue(Ret)) return null;
            if (!Ret.Resolve()) return null;
        }

        return Ret;
    }

    private static bool CreateAndDeclareRecursively(IdContainer Container, ConstDeclarationList Out)
    {
        var RetValue = true;
        var State = Container.State;
        var Scope = Container as NonCodeScope;

        if (Scope != null && Scope.Code.IsValid)
            if (!State.Language.ConstDeclRecognizer.Recognize(Scope, Out))
                RetValue = false;

        for (var i = 0; i < Container.Children.Count; i++)
        {
            var e = Container.Children[i];
            if (!CreateAndDeclareRecursively(e, Out))
                RetValue = false;
        }

        return RetValue;
    }

    public static bool CalcValue(List<ConstDeclaration> List)
    {
        var RetValue = true;
        for (var i = 0; i < List.Count; i++)
        {
            var e = List[i];
            if (e.Value == null && e.Str_Value.IsValid && !e.CalcValue())
                RetValue = false;
        }

        return RetValue;
    }

    public bool Resolve()
    {
        var RetValue = true;
        var List = new List<ConstDeclaration>(this);
        var Count = 1;

        while (Count > 0)
        {
            Count = 0;
            for (var i = 0; i < List.Count; i++)
            {
                var Res = List[i].Declare();
                if (Res == SimpleRecResult.Succeeded) Count++;
                if (Res == SimpleRecResult.Failed) RetValue = false;

                if (Res != SimpleRecResult.Unknown)
                {
                    List.RemoveAt(i);
                    i--;
                }
            }
        }

        if (List.Count > 0)
        {
            RetValue = false;
            foreach (var e in List)
            {
                var State = e.Container.State;

                if (e.Dependencies != null && e.Dependencies.Count > 0)
                    foreach (var IdName in e.Dependencies)
                        State.Messages.Add(MessageId.UnknownId, IdName);
                else
                    State.Messages.Add(MessageId.CannotCalcConst, e.Str_Value);
            }
        }

        return RetValue;
    }
}

public class AliasDeclaration
{
    public IdContainer Container;
    public List<Modifier> Mods;
    public CodeString NewName;
    public CodeString OldName;

    public AliasDeclaration(IdContainer Container, CodeString NewName, CodeString OldName,
        List<Modifier> Mods = null)
    {
        this.Container = Container;
        this.NewName = NewName;
        this.OldName = OldName;
        this.Mods = Mods;
    }

    public SimpleRecResult Declare()
    {
        var Options = new GetIdOptions(GetIdMode.Everywhere, false);
        var Id = Container.RecognizeIdentifier(OldName, Options);
        if (Id == null) return SimpleRecResult.Unknown;

        var Alias = new IdentifierAlias(Container, NewName, Id);
        if (Mods != null && !Modifiers.Apply(Mods, Alias))
            return SimpleRecResult.Failed;

        if (!Container.DeclareIdentifier(Alias))
            return SimpleRecResult.Failed;

        return SimpleRecResult.Succeeded;
    }
}

public class AliasDeclarationList : List<AliasDeclaration>
{
    public bool RecognizeRecursively(IdContainer Container)
    {
        var RetValue = true;
        var State = Container.State;
        var Scope = Container as NonCodeScope;
        if (State.Language.AliasDeclRecognizer == null)
            return true;

        if (Scope != null && Scope.Code.IsValid)
            if (!State.Language.AliasDeclRecognizer.Recognize(Scope, this))
                RetValue = false;

        for (var i = 0; i < Container.Children.Count; i++)
            if (!RecognizeRecursively(Container.Children[i]))
                RetValue = false;

        return RetValue;
    }

    public bool Recognize(NonCodeScope Scope)
    {
        var State = Scope.State;
        if (State.Language.AliasDeclRecognizer != null)
            if (!State.Language.AliasDeclRecognizer.Recognize(Scope, this))
                return false;

        return true;
    }

    public bool Recognize(TypeDeclarationList List)
    {
        var RetValue = true;
        for (var i = 0; i < List.Count; i++)
        {
            var NewType = List[i].DeclaredType as StructuredType;
            if (NewType == null) continue;

            if (!Recognize(NewType.StructuredScope))
                RetValue = false;
        }

        return RetValue;
    }

    public bool ExecuteOnce(out bool Found)
    {
        Found = false;
        var RetValue = true;
        for (var i = 0; i < Count; i++)
        {
            var Res = this[i].Declare();
            if (Res != SimpleRecResult.Unknown)
            {
                if (Res == SimpleRecResult.Failed)
                    RetValue = false;

                Found = true;
                RemoveAt(i);
                i--;
            }
        }

        return RetValue;
    }

    public bool Declare(bool All = true)
    {
        bool Loop;
        do
        {
            if (!ExecuteOnce(out Loop))
                return false;
        } while (Loop);

        return All ? ShowMessages() : true;
    }

    public bool ShowMessages()
    {
        var RetValue = true;
        for (var i = 0; i < Count; i++)
        {
            var Decl = this[i];
            var State = Decl.Container.State;
            State.Messages.Add(MessageId.UnknownId, Decl.OldName);
            RetValue = false;
        }

        return RetValue;
    }
}

public enum TypeDeclType
{
    Class,
    Struct,
    Enum,
    Flag
}

public class TypeDeclaration
{
    public StructureBase[] Bases;
    public IdContainer Container;
    public Type DeclaredType;
    public CodeString Inner;
    public List<Modifier> Mods;
    public CodeString Name;
    public TypeDeclType Type;

    public TypeDeclaration(IdContainer Container, CodeString Name, TypeDeclType Type,
        StructureBase[] Bases, CodeString Inner, List<Modifier> Mods)
    {
        this.Container = Container;
        this.Name = Name;
        this.Bases = Bases;
        this.Type = Type;
        this.Mods = Mods;
        this.Inner = Inner;
    }

    public SimpleRecResult Declare()
    {
        var State = Container.State;
        var Arch = State.Arch;

        //------------------------------------------------------------------
        if (Type == TypeDeclType.Struct || Type == TypeDeclType.Class)
        {
            var NewType = (StructuredType)null;
            if (Type == TypeDeclType.Struct)
            {
                NewType = new StructType(Container, Name);

                if (Bases.Length > 0)
                {
                    for (var i = 0; i < Bases.Length; i++)
                        State.Messages.Add(MessageId.CannotInherit, Bases[i].Name);

                    return SimpleRecResult.Failed;
                }
            }
            else
            {
                NewType = new ClassType(Container, Name);
            }

            if (!Modifiers.Apply(Mods, NewType)) return SimpleRecResult.Failed;
            if (!Container.DeclareIdentifier(NewType)) return SimpleRecResult.Failed;

            NewType.BaseStructures = Bases;
            NewType.StructuredScope = new StructuredScope(Container, Inner, NewType);
            Container.Children.Add(NewType.StructuredScope);
            DeclaredType = NewType;
            return SimpleRecResult.Succeeded;
        }

        //------------------------------------------------------------------

        if (Type == TypeDeclType.Enum || Type == TypeDeclType.Flag)
        {
            var NewType = (EnumType)null;
            if (Bases.Length == 1)
            {
                if (Type == TypeDeclType.Flag) NewType = new FlagType(Container, Name, Bases[0].Name);
                else NewType = new EnumType(Container, Name, Bases[0].Name);

                if (Bases[0].Base != null)
                    NewType.Children[0] = Bases[0].Base;

                if (Bases[0].Flags != StructureBaseFlags.None)
                {
                    State.Messages.Add(MessageId.NotExpected, Bases[0].Declaration);
                    return SimpleRecResult.Failed;
                }
            }
            else if (Bases.Length > 1)
            {
                for (var i = 1; i < Bases.Length; i++)
                    State.Messages.Add(MessageId.NotExpected, Bases[i].Declaration);

                return SimpleRecResult.Failed;
            }
            else
            {
                if (Type == TypeDeclType.Flag) NewType = new FlagType(Container, Name, new CodeString());
                else NewType = new EnumType(Container, Name, new CodeString());
            }

            if (!Modifiers.Apply(Mods, NewType)) return SimpleRecResult.Failed;
            if (!Container.DeclareIdentifier(NewType)) return SimpleRecResult.Failed;

            NewType.EnumScope = new EnumScope(Container, Inner, NewType);
            Container.Children.Add(NewType.EnumScope);
            DeclaredType = NewType;
            return SimpleRecResult.Succeeded;
        }

        //------------------------------------------------------------------

        throw new ApplicationException();
    }
}

public class TypeDeclarationList : List<TypeDeclaration>
{
    public static TypeDeclarationList Create(NonCodeScope Scope)
    {
        var Ret = new TypeDeclarationList();
        var Rec = Scope.State.Language.TypeDeclRecognizer;
        return Rec != null && !Rec.Recognize(Scope, Ret) ? null : Ret;
    }

    public static TypeDeclarationList CreateAndDeclareRecursively(IdContainer Container)
    {
        var Ret = new TypeDeclarationList();
        if (Container.State.Language.TypeDeclRecognizer != null)
            if (!CreateAndDeclareRecursively(Container, Ret))
                return null;

        return Ret;
    }

    private static bool CreateAndDeclareRecursively(IdContainer Container, TypeDeclarationList Out)
    {
        var RetValue = true;
        var State = Container.State;
        var Scope = Container as NonCodeScope;

        if (Scope != null && Scope.Code.IsValid)
        {
            var NewList = new TypeDeclarationList();
            var Rec = State.Language.TypeDeclRecognizer;
            if (!Rec.Recognize(Scope, NewList) || !NewList.Declare())
                RetValue = false;

            Out.AddRange(NewList);
        }

        for (var i = 0; i < Container.Children.Count; i++)
        {
            var e = Container.Children[i];
            if (!CreateAndDeclareRecursively(e, Out))
                RetValue = false;
        }

        return RetValue;
    }

    public bool Declare()
    {
        var RetValue = true;
        for (var i = 0; i < Count; i++)
        {
            var Res = this[i].Declare();
            if (Res != SimpleRecResult.Succeeded)
            {
                if (Res == SimpleRecResult.Failed) RetValue = false;
                else throw new ApplicationException();
            }
        }

        return RetValue;
    }
}

public enum VarDeclConvMode : byte
{
    Nothing = 0,
    Normal = 1,
    Assignment = 2
}

public struct VarDeclaration
{
    public CodeString Declaration;
    public CodeString Name;
    public CodeString InitString;

    public CodeString TypeName;
    public Identifier Type;
    public List<Modifier> Modifiers;

    public VarDeclaration(CodeString Declaration, CodeString TypeName, Identifier Type,
        CodeString Name, CodeString InitString, List<Modifier> Modifiers = null)
    {
        this.Declaration = Declaration;
        this.TypeName = TypeName;
        this.InitString = InitString;

        this.Type = Type;
        this.Name = Name;
        this.Modifiers = Modifiers;
    }

    public ExpressionNode GetVal(PluginRoot Plugin, BeginEndMode Mode = BeginEndMode.Both)
    {
        return Expressions.CreateExpression(InitString, Plugin, Mode);
    }

    public bool CheckName(CompilerState State)
    {
        if (Name.IsValid && !Name.IsValidIdentifierName)
        {
            State.Messages.Add(MessageId.NotValidName, Declaration);
            return false;
        }

        return true;
    }

    public Variable ToVariable(IdContainer Container, bool Declare = false)
    {
        if (!CheckName(Container.State))
            return null;

        var Ret = Container.CreateVariable(Name, Type, Modifiers);
        if (Ret == null) return null;

        if (Declare && !Container.DeclareIdentifier(Ret))
            return null;

        Ret.InitString = InitString;
        return Ret;
    }

    public FunctionParameter ToFuncParam(PluginRoot Plugin, VarDeclConvMode Mode = VarDeclConvMode.Nothing)
    {
        if (!CheckName(Plugin.State))
            return null;

        var Ret = new FunctionParameter(Plugin.Container, Name, Type);
        Ret.Declaration = Declaration;
        Ret.InitString = InitString;

        if (Modifiers != null && !Zinnia.Modifiers.Apply(Modifiers, Ret))
            return null;

        if (Mode != VarDeclConvMode.Nothing)
            if (!Ret.CalcValue(Plugin, BeginEndMode.Both, Mode == VarDeclConvMode.Assignment))
                return null;

        return Ret;
    }

    public Variable ToVariable(PluginRoot Plugin, BeginEndMode BEMode = BeginEndMode.Both,
        VarDeclConvMode Mode = VarDeclConvMode.Nothing, bool UsePlugin = false, bool Declare = false,
        bool EnableUntyped = false)
    {
        if (!CheckName(Plugin.State))
            return null;

        Variable Ret;
        if (UsePlugin)
        {
            Ret = Plugin.CreateVariable(Type, Name);
            if (Ret == null) return null;

            Ret.InitString = InitString;
            if (Declare && !Plugin.DeclareIdentifier(Ret))
                return null;
        }
        else
        {
            Ret = ToVariable(Plugin.Container, Declare);
            if (Ret == null) return null;
        }

        if (Mode != VarDeclConvMode.Nothing)
            if (!Ret.CalcValue(Plugin, BEMode, Mode == VarDeclConvMode.Assignment, EnableUntyped))
                return null;

        return Ret;
    }

    public Property ToProperty(IdContainer Container)
    {
        if (!CheckName(Container.State)) return null;
        return Container.CreateProperty(Name, Type, null, Modifiers);
    }
}

[Flags]
public enum VarDeclarationListFlags : byte
{
    None,
    EnableUnnamed = 1,
    EnableAutoType = 2,
    EnableMessages = 4,
    EnableInitValue = 8,
    EnableVoidOnly = 16,
    Default = EnableAutoType | EnableMessages | EnableInitValue
}

public class VarDeclarationList : List<VarDeclaration>
{
    public List<Modifier> DefaultModifiers;

    public VarDeclarationList(List<Modifier> DefaultModifiers = null)
    {
        this.DefaultModifiers = DefaultModifiers;
    }

    public static VarDeclarationList Create(IdContainer Container, CodeString Code,
        List<Modifier> DefaultModifiers = null,
        VarDeclarationListFlags Flags = VarDeclarationListFlags.Default)
    {
        var Ret = new VarDeclarationList(DefaultModifiers);
        var Rec = Container.State.Language.VarDeclRecognizer;
        if (Rec != null)
        {
            var EnableMessages = (Flags & VarDeclarationListFlags.EnableMessages) != 0;
            if (!Rec.Recognize(Container, Code, EnableMessages, Ret)) return null;
            if (!Ret.Process(Container, Flags)) return null;
        }
        else
        {
            throw new ApplicationException("A variable declaration recognizer is not avaiable");
        }

        return Ret;
    }

    public bool IsDefined(string Name, int Until = -1)
    {
        if (Until == -1) Until = Count;
        for (var i = 0; i < Until; i++)
            if (this[i].Name.IsEqual(Name))
                return true;

        return false;
    }

    public bool IsDefined(CodeString Name, int Until = -1)
    {
        if (Until == -1) Until = Count;
        for (var i = 0; i < Until; i++)
            if (this[i].Name.IsEqual(Name))
                return true;

        return false;
    }

    public bool Process(IdContainer Container, VarDeclarationListFlags Flags = VarDeclarationListFlags.Default)
    {
        var RetValue = true;
        var State = Container.State;
        var Type = (Identifier)null;
        var FirstIsVoid = false;

        for (var i = 0; i < Count; i++)
        {
            var e = this[i];
            if (FirstIsVoid)
            {
                State.Messages.Add(MessageId.NotExpected, e.Declaration);
                RetValue = false;
                continue;
            }

            if (e.Name.IsValid)
            {
                if (!e.Name.IsValidIdentifierName)
                {
                    if ((Flags & VarDeclarationListFlags.EnableMessages) != 0)
                        State.Messages.Add(MessageId.NotValidName, e.Name);

                    RetValue = false;
                }

                if (IsDefined(e.Name, i))
                {
                    if ((Flags & VarDeclarationListFlags.EnableMessages) != 0)
                        State.Messages.Add(MessageId.IdAlreadyDefined, e.Name);

                    RetValue = false;
                }
            }
            else if ((Flags & VarDeclarationListFlags.EnableUnnamed) == 0)
            {
                if ((Flags & VarDeclarationListFlags.EnableMessages) != 0)
                    State.Messages.Add(MessageId.MustBeNamed, e.Declaration);

                RetValue = false;
            }

            if (e.TypeName.IsValid)
            {
                Type = e.Type;
                Type.SetUsed();

                var RType = Type.RealId as Type;
                if (RType is AutomaticType)
                {
                    if ((Flags & VarDeclarationListFlags.EnableAutoType) == 0)
                    {
                        if ((Flags & VarDeclarationListFlags.EnableMessages) != 0)
                            State.Messages.Add(MessageId.Untyped, e.TypeName);

                        RetValue = false;
                    }
                }
                else if (RType == null || (RType.TypeFlags & TypeFlags.CanBeVariable) == 0)
                {
                    if (RType is VoidType && (Flags & VarDeclarationListFlags.EnableVoidOnly) != 0)
                    {
                        if (e.Name.IsValid)
                        {
                            if ((Flags & VarDeclarationListFlags.EnableMessages) != 0)
                                State.Messages.Add(MessageId.NotExpected, e.Name);

                            RetValue = false;
                            continue;
                        }

                        if (e.InitString.IsValid)
                        {
                            if ((Flags & VarDeclarationListFlags.EnableMessages) != 0)
                                State.Messages.Add(MessageId.NotExpected, e.InitString);

                            RetValue = false;
                            continue;
                        }

                        FirstIsVoid = true;
                    }
                    else
                    {
                        if ((Flags & VarDeclarationListFlags.EnableMessages) != 0)
                            State.Messages.Add(MessageId.CannotBeThisType, e.TypeName);

                        RetValue = false;
                    }
                }
            }
            else
            {
                if (Type != e.Type) throw new ApplicationException();
            }

            if (e.InitString.IsValid && (Flags & VarDeclarationListFlags.EnableInitValue) == 0)
            {
                if ((Flags & VarDeclarationListFlags.EnableMessages) != 0)
                    State.Messages.Add(MessageId.CannotHaveInitVal, e.InitString);

                RetValue = false;
            }
        }

        return RetValue;
    }

    public Variable[] ToVariables(PluginRoot Plugin, BeginEndMode BEMode = BeginEndMode.Both,
        VarDeclConvMode Mode = VarDeclConvMode.Nothing, bool UsePlugin = false, bool Declare = false,
        bool EnableUntyped = false)
    {
        var Ret = new Variable[Count];
        for (var i = 0; i < Count; i++)
        {
            if ((BEMode & BeginEndMode.Begin) != 0) Plugin.Reset();
            Ret[i] = this[i].ToVariable(Plugin, BEMode, Mode, UsePlugin, Declare, EnableUntyped);
        }

        return Ret;
    }

    public Variable[] ToVariables(IdContainer Container, bool Declare = false, bool EnableUntyped = false)
    {
        var Ret = new Variable[Count];
        for (var i = 0; i < Count; i++)
            Ret[i] = this[i].ToVariable(Container, Declare);

        return Ret;
    }

    public FunctionParameter[] ToFuncParams(PluginRoot Plugin, VarDeclConvMode Mode = VarDeclConvMode.Nothing)
    {
        var Ret = new FunctionParameter[Count];
        for (var i = 0; i < Count; i++)
        {
            Plugin.Reset();
            Ret[i] = this[i].ToFuncParam(Plugin, Mode);
        }

        return Ret;
    }

    public ConstDeclaration[] ToConstDecls(IdContainer Container, List<Modifier> Mods = null)
    {
        var Ret = new ConstDeclaration[Count];
        for (var i = 0; i < Count; i++)
        {
            var e = this[i];
            Ret[i] = new ConstDeclaration(Container, e.Name, e.Type, e.InitString, Mods);
        }

        return Ret;
    }

    public TupleType ToTupleType(IdContainer Container, bool EnableMessages = true)
    {
        var State = Container.State;
        var Members = new List<Identifier>();
        for (var i = 0; i < Count; i++)
        {
            var Decl = this[i];
            if (Decl.InitString.IsValid)
            {
                if (EnableMessages)
                    State.Messages.Add(MessageId.CannotHaveInitVal, Decl.InitString);

                return null;
            }

            var RType = Decl.Type.RealId as Type;
            if ((RType.TypeFlags & TypeFlags.CanBeVariable) == 0)
            {
                if (EnableMessages)
                    State.Messages.Add(MessageId.CannotBeThisType, Decl.TypeName);

                return null;
            }

            var Var = new MemberVariable(Container, Decl.Name, Decl.Type);
            Var.Access = Decl.Type.Access;
            Members.Add(Var);
        }

        return new TupleType(Container, Members);
    }

    public bool VerifyInitVal(IdContainer Container)
    {
        var RetValue = true;
        var State = Container.State;

        for (var i = 0; i < Count; i++)
        {
            var e = this[i];
            if (!e.InitString.IsValid)
            {
                State.Messages.Add(MessageId.MustHaveInitVal, e.Name);
                RetValue = false;
            }
        }

        return RetValue;
    }
}