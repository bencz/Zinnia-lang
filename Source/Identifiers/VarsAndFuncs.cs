using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Zinnia
{
    public class FunctionOverloads
    {
        public string Name;
        public List<Function> Functions = new List<Function>();

        public int OverloadCount
        {
            get { return Functions.Count; }
        }

        public FunctionOverloads(string Name)
        {
            this.Name = Name;
        }
    }

    public abstract class Variable : Identifier
    {
        public ExpressionNode InitValue;
        public ConstValue ConstInitValue;
        public CodeString InitString;
        public int SpecifiedAlign;

        public int Align
        {
            get
            {
                var Type = Children[0].RealId as Type;
                return Math.Max(SpecifiedAlign, Type.Align);
            }

            set
            {
                this.SpecifiedAlign = value;
            }
        }

        public Variable(IdContainer Container, CodeString Name, Identifier Type)
            : base(Container, Name)
        {
            this.DeclaredIdType = DeclaredIdType.Variable;
            Children = new Identifier[] { Type };
        }

        public override Identifier TypeOfSelf
        {
            get { return Children[0]; }
        }

        public bool CalcValue(PluginRoot Plugin, BeginEndMode Mode = BeginEndMode.Both, bool CreateAssignNodes = false, bool EnableUntyped = false)
        {
            InitValue = null;
            if (InitString.IsValid)
            {
                var NMode = Mode & BeginEndMode.Begin;
                InitValue = Expressions.CreateExpression(InitString, Plugin, NMode);
                if (InitValue == null) return false;

                if (CreateAssignNodes)
                {
                    var Node = Expressions.CreateReference(Plugin.Container, this, Plugin, InitString);
                    if (Node == null) return false;

                    InitValue = Expressions.SetValue(Node, InitValue, Plugin, InitString, false);
                    if (InitValue == null) return false;
                }

                InitValue = Plugin.FinishNode(InitValue);
                if (InitValue == null) return false;

                if (!CreateAssignNodes && TypeOfSelf.RealId is AutomaticType)
                {
                    if (!EnableUntyped && InitValue.Type.RealId is AutomaticType)
                    {
                        Plugin.State.Messages.Add(MessageId.Untyped, Name);
                        return false;
                    }

                    Children[0] = InitValue.Type;
                }

                var TypeMgrnPlugin = Plugin.GetPlugin<TypeMngrPlugin>();
                if (!TypeOfSelf.IsEquivalent(InitValue.Type) && !(TypeOfSelf.RealId is AutomaticType))
                {
                    InitValue = TypeMgrnPlugin.Convert(InitValue, TypeOfSelf, InitString);
                    if (InitValue == null) return false;
                }

                if ((Mode & BeginEndMode.End) != 0)
                {
                    InitValue = Plugin.End(InitValue);
                    if (InitValue == null) return false;
                }

                var ConstVal = InitValue as ConstExpressionNode;
                if (ConstVal != null) ConstInitValue = ConstVal.Value;
            }
            else if (TypeOfSelf.RealId is AutomaticType)
            {
                if (!EnableUntyped)
                {
                    Plugin.State.Messages.Add(MessageId.Untyped, Name);
                    return false;
                }
            }

            return true;
        }
    }

    public class GlobalVariable : Variable
    {
        public int GlobalPointerIndex = -1;

        public GlobalVariable(IdContainer Container, CodeString Name, Identifier Type)
            : base(Container, Name, Type)
        {
        }

        public override void GetAssembly(CodeGenerator CG, GetAssemblyMode Mode)
        {
            if ((Flags & IdentifierFlags.Extern) == 0)
            {
                if ((Mode == GetAssemblyMode.InitedValues && ConstInitValue != null) ||
                    (Mode == GetAssemblyMode.UninitedValues && ConstInitValue == null))
                {
                    CG.Align(Align);
                    CG.InsContainer.Label(AssemblyName);
                    CG.Declare(TypeOfSelf, ConstInitValue);
                    CG.InsContainer.Add("\n");
                }
            }
        }

        public override void GetGlobalPointers(List<string> Out)
        {
            GlobalPointerIndex = Out.Count;
            Out.Add(AssemblyName);
        }
    }

    public class LocalVariable : Variable
    {
        public bool PreAssigned;

        public LocalVariable(IdContainer Container, CodeString Name, Identifier Type)
            : base(Container, Name, Type)
        {
        }

        protected override string CalculateAssemblyName(bool Decorations)
        {
            throw new ApplicationException();
        }
    }

    public class ParamVariable : LocalVariable
    {
        public ParamVariable(IdContainer Container, CodeString Name, Identifier Type)
            : base(Container, Name, Type)
        {
            var RefType = Type.RealId as ReferenceType;
            if (RefType == null || RefType.Mode != ReferenceMode.IdGetsAssigned)
                PreAssigned = true;
        }
    }

    public class SelfVariable : LocalVariable
    {
        public SelfVariable(IdContainer Container, Identifier Type)
            : base(Container, new CodeString(), Type)
        {
            var Lang = Container.State.Language;
            Name = new CodeString(Lang.CodeProcessor.SelfName);
            Flags |= IdentifierFlags.ReadOnly;
            PreAssigned = true;
        }
    }

    public class BaseVariable : LocalVariable
    {
        public BaseVariable(IdContainer Container, Identifier Type)
            : base(Container, new CodeString(), Type)
        {
            var Lang = Container.State.Language;
            Name = new CodeString(Lang.CodeProcessor.BaseName);
            Flags |= IdentifierFlags.ReadOnly;
            PreAssigned = true;
        }
    }

    public class MemberVariable : Variable
    {
        public int Offset = -1;

        public MemberVariable(IdContainer Container, CodeString Name, Identifier Type)
            : base(Container, Name, Type)
        {
        }
    }

    public class ConstVariable : Variable
    {
        public override bool IsInstanceIdentifier
        {
            get { return false; }
        }

        public ConstVariable(IdContainer Container, CodeString Name, Identifier Type, ConstValue Value)
            : base(Container, Name, Type)
        {
            this.ConstInitValue = Value;
            this.DeclaredIdType = DeclaredIdType.Constant;
        }
    }

    [Flags]
    public enum ParameterFlags : byte
    {
        None = 0,
        ParamArray = 1,
    }

    public class FunctionParameter : Variable
    {
        public ParameterFlags ParamFlags;

        public FunctionParameter(IdContainer Container, CodeString Name, Identifier Type)
            : base(Container, Name, Type)
        {
            DeclaredIdType = DeclaredIdType.Unknown;
            if (Type != null) Access = Type.Access;
        }
    }

    public enum FunctionState : byte
    {
        Unknown,
        CodeProcessStarted,
        CodeProcessEnded,
        AssemblyGenerationStarted,
        AssemblyGenerationEnded,
    }

    public class Function : Identifier
    {
        public FunctionScope FunctionScope;
        public FunctionOverloads Overload;
        public int OverloadIndex = 0;
        public int GlobalPointerIndex = -1;
        public FunctionState FunctionState;

        public override Identifier TypeOfSelf
        {
            get { return Children[0]; }
        }

        public bool HasCode
        {
            get
            {
                return (Flags & IdentifierFlags.Abstract) == 0 && (Flags & IdentifierFlags.Extern) == 0;
            }
        }

        public bool NeedsToBeCompiled
        {
            get
            {
                return Container.IsScopeOfAssembly() && HasCode;
            }
        }

        public Function(IdContainer Container, CodeString Name, TypeOfFunction Type, FunctionOverloads Overload)
            : base(Container, Name)
        {
            this.Overload = Overload;
            this.Children = new Identifier[] { Type };
            this.DeclaredIdType = DeclaredIdType.Function;
        }

        protected override string CalculateAssemblyName(bool Decorations)
        {
            var Ret = base.CalculateAssemblyName(Decorations);
            if (OverloadIndex == 0 || !Decorations) return Ret;
            else return Ret + "%" + OverloadIndex.ToString();
        }

        public override void GetAssembly(CodeGenerator CG, GetAssemblyMode Mode)
        {
            base.GetAssembly(CG, Mode);

            if ((Mode & GetAssemblyMode.Code) != 0 && NeedsToBeCompiled)
            {
                CG.InsContainer.Label(AssemblyName);
                Container.State.Arch.GetAssembly(CG, this);
                CG.InsContainer.Add("\n");
            }
        }

        public override bool HasScopes
        {
            get { return FunctionScope != null; }
        }

        public override IEnumerable<ScopeNode> EnumScopes
        {
            get
            {
                if (FunctionScope != null)
                    yield return FunctionScope;
            }
        }

        public override void GetGlobalPointers(List<string> Out)
        {
            if (HasCode)
            {
                GlobalPointerIndex = Out.Count;
                Out.Add(AssemblyName);
            }
        }
    }

    public class MemberFunction : Function
    {
        public int VirtualIndex = -1;

        public MemberFunction(IdContainer Container, CodeString Name, TypeOfFunction Type,
            FunctionOverloads Overload)
            : base(Container, Name, Type, Overload)
        {
        }
    }

    public class Constructor : Function
    {
        public Constructor(IdContainer Container, TypeOfFunction Type, FunctionOverloads Overload, CodeString Declaration)
            : base(Container, new CodeString(), Type, Overload)
        {
            this.Declaration = Declaration;
            this.DeclaredIdType = DeclaredIdType.Constructor;
        }

        protected override string CalculateAssemblyName(bool Decorations)
        {
            var Ret = Container.AssemblyName;
            Ret += (Flags & IdentifierFlags.Static) != 0 ? "_%StaticConstructor" : "_%Constructor";

            if (OverloadIndex == 0 || !Decorations) return Ret;
            else return Ret + "%" + OverloadIndex.ToString();
        }
    }

    public class Destructor : Function
    {
        public Destructor(IdContainer Container, TypeOfFunction Type, CodeString Declaration)
            : base(Container, new CodeString(), Type, null)
        {
            this.Declaration = Declaration;
            this.DeclaredIdType = DeclaredIdType.Destructor;
        }
    }

    public class Property : Identifier
    {
        public PropertyScope PropertyScope;

        public Property(IdContainer Container, CodeString Name, Type Type)
            : base(Container, Name)
        {
            this.Children = new Identifier[] { Type };
            this.DeclaredIdType = DeclaredIdType.Property;
        }

        public Property(IdContainer Container, CodeString Name, Identifier[] Children)
            : base(Container, Name)
        {
            this.Children = Children;
            this.DeclaredIdType = DeclaredIdType.Property;
        }

        public override Identifier TypeOfSelf
        {
            get { return Children[0]; }
        }

        public override bool HasScopes
        {
            get { return true; }
        }

        public override IEnumerable<ScopeNode> EnumScopes
        {
            get { yield return PropertyScope; }
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

    public class Namespace : Identifier
    {
        public List<NamespaceScope> NamespaceScopes;

        public Namespace(IdContainer Container, CodeString Name)
            : base(Container, Name)
        {
            this.DeclaredIdType = DeclaredIdType.Namespace;
            Access = IdentifierAccess.Public;
        }

        public void AddScope(IEnumerable<NamespaceScope> Scopes)
        {
            if (NamespaceScopes == null)
                NamespaceScopes = new List<NamespaceScope>();

            NamespaceScopes.AddRange(Scopes);
        }

        public void AddScope(NamespaceScope Scope)
        {
            if (NamespaceScopes == null)
                NamespaceScopes = new List<NamespaceScope>();

            NamespaceScopes.Add(Scope);
        }

        public override Identifier TypeOfSelf
        {
            get { return Container.GlobalContainer.CommonIds.NamespaceType; }
        }

        public override bool HasScopes
        {
            get { return NamespaceScopes != null; }
        }

        public override IEnumerable<ScopeNode> EnumScopes
        {
            get { return NamespaceScopes; }
        }
    }
}