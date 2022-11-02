using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Zinnia.Base;

namespace Zinnia
{
	public delegate bool OnVarCreatedFunc(Variable Var);

	public abstract class IdentifierScope : NonCodeScope
	{
		public IdentifierScope(IdContainer Parent, CodeString Code)
			: base(Parent, Code)
		{
		}

		public abstract Identifier Identifier { get; }
        /*
		public override bool IsAlreadyDefined(string Name, Predicate<Identifier> Func = null)
		{
			return Identifier.SearchScopesId(this, Name, Func).Count > 0;
		}
        */
		public override bool GetContainerId(string Name, List<IdentifierFound> Out, Predicate<Identifier> Func = null)
		{
			return Identifiers.SearchMember(this, Identifier, Name, Out, Func);
		}

		protected override string CalculateAssemblyName()
		{
			var Id = Identifier;
			if (!Id.Name.IsValid) return base.CalculateAssemblyName();
			else return base.CalculateAssemblyName() + "_" + Id.Name;
		}
	}

	public class NamespaceScope : IdentifierScope
	{
		public Namespace Namespace;
		public AutoAllocatedList<Namespace> UsedNamespaces;

		public override IdentifierAccess DefaultAccess
		{
			get { return IdentifierAccess.Internal; }
		}

		public override Identifier Identifier
		{
			get { return Namespace; }
		}

		public NamespaceScope(IdContainer Parent, CodeString Code, Namespace Namespace)
			: base(Parent, Code)
		{
			this.Namespace = Namespace;
		}

		public override bool GetContainerId(string Name, List<IdentifierFound> Out, Predicate<Identifier> Func = null)
		{
			var RetValue = base.GetContainerId(Name, Out, Func);
			for (var i = 0; i < UsedNamespaces.Count; i++)
			{
				if (Identifiers.SearchMember(this, UsedNamespaces[i], Name, Out, Func))
					RetValue = true;
			}

			return RetValue;
		}
	}

	public abstract class ScopeNode : IdContainer
	{
		public CodeString Code;

		public ScopeNode(IdContainer Parent, CodeString Code)
			: base(Parent)
		{
			this.Code = Code;
		}

		public int SourceLength
		{
			get { return Code.IsValid ? Code.Length : 0; }
		}
	}

	public abstract class NonCodeScope : ScopeNode
	{
		public virtual void GetMTProcIds(List<Identifier> Out)
		{
			for (var i = 0; i < IdentifierList.Count; i++)
			{
				var Id = IdentifierList[i];
				if (Id is Function || Id is IdentifierAlias || Id is Variable)
					Out.Add(Id);
			}
		}

		public virtual bool ProcessScope()
		{
			return State.Language.DeclarationRecognizer.Recognize(this);
		}

		public NonCodeScope(IdContainer Parent, CodeString Code)
			: base(Parent, Code)
		{
		}
	}

	public class AssemblyScope : NamespaceScope
	{
		public Assembly Assembly;

		public AssemblyScope(GlobalContainer Parent, Assembly Assembly, CodeFile CodeFile = null)
			: base(Parent, CodeFile == null ? new CodeString() : new CodeString(CodeFile), Parent.GlobalNamespace)
		{
			this.Assembly = Assembly;
		}

		public override void GetAssembly(CodeGenerator CG, GetAssemblyMode Mode = GetAssemblyMode.Code)
		{
			if (Assembly == GlobalContainer.OutputAssembly)
				base.GetAssembly(CG, Mode);
		}

		public override void GetGlobalPointers(List<string> Out)
		{
			if (Assembly == GlobalContainer.OutputAssembly)
				base.GetGlobalPointers(Out);
		}
	}

	public class PropertyScope : IdentifierScope
	{
		public Property Property;
		public int GetterIndex = -1;
		public int SetterIndex = -1;

		protected override string CalculateAssemblyName()
		{
			if (!Property.Name.IsValid) return base.CalculateAssemblyName() + "_%Indexer";
			else return base.CalculateAssemblyName();
		}

		public Function Getter
		{
			get
			{
				if (GetterIndex == -1) return null;
				return IdentifierList[GetterIndex] as Function;
			}

			set
			{
				if (value == null)
				{
					if (GetterIndex != -1)
					{
						IdentifierList.RemoveAt(GetterIndex);
						GetterIndex = -1;
					}

					return;
				}

				if (GetterIndex == -1)
				{
					GetterIndex = IdentifierList.Count;
					IdentifierList.Add(value);
					return;
				}

				IdentifierList[GetterIndex] = value;
			}
		}

		public Function Setter
		{
			get
			{
				if (SetterIndex == -1) return null;
				return IdentifierList[SetterIndex] as Function;
			}

			set
			{
				if (value == null)
				{
					if (SetterIndex != -1)
					{
						IdentifierList.RemoveAt(SetterIndex);
						SetterIndex = -1;
					}

					return;
				}

				if (SetterIndex == -1)
				{
					SetterIndex = IdentifierList.Count;
					IdentifierList.Add(value);
					return;
				}

				IdentifierList[SetterIndex] = value;
			}
		}

		public PropertyScope(IdContainer Parent, CodeString Code, Property Property)
			: base(Parent, Code)
		{
			this.Property = Property;
		}

		public override IdContainer RealContainer
		{
			get { return Parent.RealContainer; }
		}

		public override IdentifierAccess DefaultAccess
		{
			get { return IdentifierAccess.Public; }
		}

		public override Identifier Identifier
		{
			get { return Property; }
		}

		public override Variable OnCreateVariable(CodeString Name, Identifier Type, List<Modifier> Mods = null)
		{
			throw new NotImplementedException();
		}

		public override Function OnCreateFunction(CodeString Name, TypeOfFunction FuncType,
			FunctionOverloads Overload, List<Modifier> Mods = null)
		{
			throw new ApplicationException();
		}

		bool CreateScopeForAccessor(Function Func, CodeString Code)
		{
			if (Func.HasCode)
			{
				Func.FunctionScope = new FunctionScope(this, Func, Code);
				if (!Func.FunctionScope.Initialize()) return false;
				
				var Parameters = Func.FunctionScope.Parameters;
				for (var i = 0; i < Parameters.Count; i++)
					Parameters[i].SetUsed();
			}

			return true;
		}

		Function CreateFunctionForAccessor(string Name, CodeString Declaration, TypeOfFunction Type, IdentifierAccess Access)
		{
			if (Identifiers.IsLessRestrictive(Access, Property.Access))
			{
				State.Messages.Add(MessageId.PropertyAccessLevel, Name);
				return null;
			}

			Function Ret;
			if (!(Parent is StructuredScope) || (Property.Flags & IdentifierFlags.Static) != 0)
				Ret = new Function(this, new CodeString(Name), Type, null);
			else Ret = new MemberFunction(this, new CodeString(Name), Type, null);

			Ret.Flags = Property.Flags;
			Ret.Access = Access;
			Ret.Declaration = Declaration;
			return Ret;
		}

		public Function CreateGetter(CodeString Declaration, CodeString Code, IdentifierAccess Access = IdentifierAccess.Public)
		{
			var Children = new Identifier[Property.Children.Length];
			for (var i = 0; i < Property.Children.Length; i++)
				Children[i] = Property.Children[i];

			var Type = new TypeOfFunction(this, DefaultCallConv, Children);
			var Ret = CreateFunctionForAccessor("get", Declaration, Type, Access);
			if (Ret == null || !CreateScopeForAccessor(Ret, Code)) return null;

			Getter = Ret;
			return Ret;
		}

		public Function CreateSetter(CodeString Declaration, CodeString ValueName, CodeString Code,
			IdentifierAccess Access = IdentifierAccess.Public)
		{
			var PChildren = Property.Children.Length;
			var Children = new Identifier[PChildren + 1];
			Children[0] = GlobalContainer.CommonIds.Void;
			Children[PChildren] = new FunctionParameter(this, ValueName, Property.TypeOfSelf);

			for (var i = 1; i < PChildren; i++)
				Children[i] = Property.Children[i];

			var Type = new TypeOfFunction(this, DefaultCallConv, Children);
			var Ret = CreateFunctionForAccessor("set", Declaration, Type, Access);
			if (Ret == null || !CreateScopeForAccessor(Ret, Code)) return null;

			Setter = Ret;
			return Ret;
		}

		public override bool ProcessScope()
		{
			return true;
		}

		public bool AutoImplementGetter(Identifier Id)
		{
			var Getter = this.Getter;
			var FScope = Getter.FunctionScope;
			FScope.Flags |= FunctionScopeFlags.DisableParsing;
			
			var Plugin = FScope.GetPlugin();
			if (!Plugin.Begin()) return false;

			var Value = Expressions.CreateReference(FScope, Id, Plugin, Getter.Name);
			if (Value == null || Plugin.End(ref Value) == PluginResult.Failed) 
				return false;

			var Comm = new Command(FScope, Getter.Name, CommandType.Return);
			Comm.Expressions = new List<ExpressionNode>() { Value };
			Comm.Label = FScope.ReturnLabel;
			FScope.Children.Add(Comm);
			return true;
		}

		public bool AutoImplementSetter(Identifier Id)
		{
			var Setter = this.Setter;
			var FScope = Setter.FunctionScope;
			FScope.Flags |= FunctionScopeFlags.DisableParsing;

			var Plugin = FScope.GetPlugin();
			if (!Plugin.Begin()) return false;

			var Dst = Expressions.CreateReference(FScope, Id, Plugin, Getter.Name);
			var Value = Plugin.NewNode(new IdExpressionNode(FScope.Parameters[0], Setter.Name));
			if (Dst == null || Value == null) return false;

			var Expr = Expressions.SetValue(Dst, Value, Plugin, Setter.Name, true);
			if (Expr == null) return false;

			var Comm = new Command(FScope, Setter.Name, CommandType.Expression);
			Comm.Expressions = new List<ExpressionNode>() { Expr };
			FScope.Children.Add(Comm);
			return true;
		}

		public bool AutoImplement()
		{
			if (Property.Children.Length != 1)
				throw new ApplicationException();

			var Type = Property.Children[0];
			var Name = new CodeString(Property.Name.ToString() + "_%Value");

			Identifier Id;
			if ((Property.Flags & IdentifierFlags.Static) != 0)
			{
				Id = new GlobalVariable(StructuredScope, Name, Type);
				Id.Flags |= IdentifierFlags.Static;
			}
			else
			{
				Id = new MemberVariable(StructuredScope, Name, Type);
			}

			if (!StructuredScope.DeclareIdentifier(Id))
				return false;

			if (!AutoImplementGetter(Id)) return false;
			if (!AutoImplementSetter(Id)) return false;
			return true;
		}

		public bool ProcessAutoImplementation()
		{
			var Getter = this.Getter;
			var Setter = this.Setter;

			var GScope = Getter != null ? Getter.FunctionScope : null;
			var SScope = Setter != null ? Setter.FunctionScope : null;

			if ((Property.Flags & IdentifierFlags.Abstract) != 0 || (Property.Flags & IdentifierFlags.Extern) != 0)
				return true;

			if (Getter != null && Setter != null && !GScope.Code.IsValid && !SScope.Code.IsValid)
			{
				if (Property.Children.Length > 1)
				{
					State.Messages.Add(MessageId.UnimplementedWithIndices, Property.Declaration);
					return false;
				}

				return AutoImplement();
			}
			else
			{
				if (Getter != null && !GScope.Code.IsValid)
					State.Messages.Add(MessageId.EmptyScope, Getter.Declaration);

				if (Setter != null && !SScope.Code.IsValid)
					State.Messages.Add(MessageId.EmptyScope, Setter.Declaration);

				return true;
			}
		}

	}
}
