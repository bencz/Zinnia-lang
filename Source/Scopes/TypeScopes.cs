using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Zinnia.Base;

namespace Zinnia
{
	public abstract class TypeScope : IdentifierScope
	{
		public TypeScope(IdContainer Parent, CodeString Code)
			: base(Parent, Code)
		{
		}

		public override IdentifierAccess DefaultAccess
		{
			get { return IdentifierAccess.Private; }
		}
	}

	public class EnumScope : TypeScope
	{
		public EnumType EnumType;

		public override Identifier Identifier
		{
			get { return EnumType; }
		}

		public override IdentifierAccess DefaultAccess
		{
			get { return IdentifierAccess.Public; }
		}

		public EnumScope(IdContainer Parent, CodeString Code, EnumType Type)
			: base(Parent, Code)
		{
			this.EnumType = Type;
		}

		public override Variable OnCreateVariable(CodeString Name, Identifier Type, List<Modifier> Mods = null)
		{
			return new ConstVariable(this, Name, Type, null);
		}

		public override bool ProcessScope()
		{
			var TypeOfValues = EnumType.Children[0];
			if (TypeOfValues == null)
			{
				if (EnumType.Str_TypeOfValues.IsValid)
				{
					TypeOfValues = RecognizeIdentifier(EnumType.Str_TypeOfValues, GetIdOptions.DefaultForType);
					if (TypeOfValues == null) return false;

					if (!(TypeOfValues.RealId is NonFloatType))
					{
						State.Messages.Add(MessageId.EnumTypeError, EnumType.Str_TypeOfValues);
						return false;
					}

					if (Identifiers.IsLessAccessable(TypeOfValues, EnumType))
					{
						State.Messages.Add(MessageId.LessAccessable, EnumType.Name, TypeOfValues.Name.ToString());
						return false;
					}
				}
				else
				{
					TypeOfValues = GlobalContainer.CommonIds.Int32;
				}

				EnumType.Children[0] = TypeOfValues;
				EnumType.Update();
			}

			var LastValue = (ConstValue)null;
			for (var i = 0; i < IdentifierList.Count; i++)
			{
				var Const = IdentifierList[i] as ConstVariable;
				if (Const == null || Const.TypeOfSelf != EnumType)
					throw new ApplicationException();

				if (Const.ConstInitValue == null)
				{
					if (LastValue == null)
					{
						if (TypeOfValues is NonFloatType)
							Const.ConstInitValue = new IntegerValue(0);
						else throw new ApplicationException();
					}
					else
					{
						var NewValue = LastValue.Copy() as IntegerValue;
						Const.ConstInitValue = NewValue;
						NewValue.Value++;
					}
				}

				LastValue = Const.ConstInitValue;
				if (!LastValue.CheckBounds(State, TypeOfValues, Const.Name))
					return false;
			}

			return true;
		}

		public override PluginRoot GetPlugin()
		{
			var Plugin = base.GetPlugin();
			Plugin.GetPlugin<TypeMngrPlugin>().RetType = EnumType.TypeOfValues;
			Plugin.GetPlugin<IdRecognizerPlugin>().ConvertEnums = EnumType;
			return Plugin;
		}
	}

	public class StructuredScope : TypeScope
	{
		public StructuredType StructuredType;
		public FunctionOverloads ConstructorOverloads;

		public override Identifier Identifier
		{
			get { return StructuredType; }
		}

		public bool CheckCycleInStructs(Identifier Id)
		{
			var Member = Id as MemberVariable;
			if (Member == null) return true;

			var Type = Member.TypeOfSelf.RealId as StructuredType;
			if (Type == null) return true;

			if (Type == this.StructuredType)
			{
				State.Messages.Add(MessageId.CycleInStructured, Id.Name);
				return false;
			}

			var Members = Type.StructuredScope.IdentifierList;
			for (var i = 0; i < Members.Count; i++)
			{
				if (Members[i] is MemberVariable && !CheckCycleInStructs(Members[i]))
					return false;
			}

			return true;
		}

		public bool CheckCycleInStructs()
		{
			if (StructuredType is StructType)
			{
				for (var i = 0; i < IdentifierList.Count; i++)
					if (!CheckCycleInStructs(IdentifierList[i])) return false;
			}

			return true;
		}

		public StructuredScope(IdContainer Parent, CodeString Code, StructuredType Type)
			: base(Parent, Code)
		{
			this.StructuredType = Type;
		}
		
		public bool ProcessIdentifiers()
		{
			var Result = true;
			for (var i = 0; i < IdentifierList.Count; i++)
			{
				if (!ProcessId(IdentifierList[i]))
					Result = false;
			}

            if (!Result) return false;

            var Abstracts = new List<Identifier>();
            for (var i = 0; i < StructuredType.BaseStructures.Length; i++)
            {
                var Base = StructuredType.BaseStructures[i].Base;
                var StructuredBase = Base.UnderlyingStructureOrRealId as StructuredType;
                for (var j = 0; j < StructuredBase.StructuredScope.IdentifierList.Count; j++)
                {
                    var Id = StructuredBase.StructuredScope.IdentifierList[j];
                    if ((Id.Flags & IdentifierFlags.Abstract) != 0) Abstracts.Add(Id);
                }
            }

            for (var i = 0; i < StructuredType.StructuredScope.IdentifierList.Count; i++)
            {
                var Id = StructuredType.StructuredScope.IdentifierList[i];
                if ((Id.Flags & IdentifierFlags.Override) != 0)
                    Abstracts.Remove(Id.OverriddenId);
            }

            for (var i = 0; i < Abstracts.Count; i++)
            {
                var Name = Identifiers.GetFullName(Abstracts[i]);
                State.Messages.Add(MessageId.BaseIdNotImplemented, StructuredType.Name, Name);
            }

            if (Abstracts.Count > 0) Result = false;
			return Result;
		}

		bool ProcessId(Identifier Id)
		{
            if ((Id.Flags & IdentifierFlags.Override) != 0)
            {
                var OverriddenId = (Identifier)null;
                if (Id is MemberFunction)
                {
                    var MemberFunc = Id as MemberFunction;
					var List = Identifiers.SearchBaseMember(null, StructuredType, Id.Name.ToString(),
                        x =>
                        {
                            if (!(x is MemberFunction)) return false;
                            if (x.Access != MemberFunc.Access) return false;
                            return x.TypeOfSelf.IsEquivalent(Id.TypeOfSelf);
                        });

                    if (List.Count == 1)
                    {
                        OverriddenId = List[0].Identifier;
                        MemberFunc.OverriddenId = OverriddenId;
                    }
                    else if (List.Count != 0)
                    {
                        throw new ApplicationException();
                    }
                }
                else if (Id is Property)
                {
                    var Property = Id as Property;
                    var PropScope = Property.PropertyScope;

					var List = Identifiers.SearchBaseMember(null, StructuredType, Id.Name.ToString(), 
						x =>
						{
							var xProperty = x as Property;
							if (xProperty == null) return false;

							var xScope = xProperty.PropertyScope;
							var xGetter = xScope.Getter;
							var xSetter = xScope.Setter;

							if ((x.Flags & IdentifierFlags.Static) != 0) return false;
							if (x.Access != Property.Access) return false;
							if ((PropScope.Getter == null) != (xGetter == null)) return false;
							if ((PropScope.Setter == null) != (xSetter == null)) return false;

							if (PropScope.Getter != null)
							{
								if (PropScope.Getter.Access != xGetter.Access) return false;
								if (!PropScope.Getter.TypeOfSelf.IsEquivalent(xGetter.TypeOfSelf)) return false;
							}

							if (PropScope.Setter != null)
							{
								if (PropScope.Setter.Access != xSetter.Access) return false;
								if (!PropScope.Setter.TypeOfSelf.IsEquivalent(xSetter.TypeOfSelf)) return false;
							}

							return true;
						});

                    if (List.Count == 1)
                    {
                        var OverriddenProp = List[0].Identifier as Property;
                        var OverriddenScope = OverriddenProp.PropertyScope;
                        Property.OverriddenId = OverriddenProp;

                        if (PropScope.Getter != null)
                        {
                            var OverriddenFunc = OverriddenScope.Getter as MemberFunction;
                            var MemberGetter = PropScope.Getter as MemberFunction;
                            MemberGetter.OverriddenId = OverriddenFunc;
                        }

                        if (PropScope.Setter != null)
                        {
                            var OverriddenFunc = OverriddenScope.Setter as MemberFunction;
                            var MemberSetter = PropScope.Setter as MemberFunction;
                            MemberSetter.OverriddenId = OverriddenFunc;
                        }

                        OverriddenId = OverriddenProp;
                    }
                    else if (List.Count != 0)
                    {
                        throw new ApplicationException();
                    }
                }
                else
                {
                    throw new ApplicationException();
                }

                if (OverriddenId == null)
                {
                    State.Messages.Add(MessageId.NoOverridable, Id.Declaration);
                    return false;
                }
                else if ((OverriddenId.Flags & IdentifierFlags.Virtual) == 0)
                {
                    State.Messages.Add(MessageId.OverrideNonvirtual, Id.Declaration);
                    return false;
                }
                else if ((OverriddenId.Flags & IdentifierFlags.Sealed) != 0)
                {
                    State.Messages.Add(MessageId.OverrideSealed, Id.Declaration);
                    return false;
                }
            }
            else if (!(Id is Constructor || Id is Destructor) && Id.Name.IsValid)
            {
                var List = new List<IdentifierFound>();
                for (var i = 0; i < StructuredType.BaseStructures.Length; i++)
                {
                    var Base = StructuredType.BaseStructures[i].Base;
					List.AddRange(Identifiers.SearchMember(null, Base, Id.Name.ToString(), 
						x =>
						{
							if (x.DeclaredIdType == DeclaredIdType.Function &&
								Id.DeclaredIdType == DeclaredIdType.Function)
							{
								if (!Identifiers.AreParametersSame(x, Id))
									return false;
							}

							return x.Name.IsValid;
						}
					));
                }

                if (List.Count > 0)
                {
                    if ((Id.Flags & IdentifierFlags.HideBaseId) == 0)
                        State.Messages.Add(MessageId.HidingRequired, Id.Name);
                }
                else
                {
                    if ((Id.Flags & IdentifierFlags.HideBaseId) != 0)
                        State.Messages.Add(MessageId.HidingUnnecessary, Id.Name);
                }
            }

			return true;
		}

		public override bool CanIdDeclared(Identifier Id)
		{
			if (!(Id is Constructor) && Id.Name.IsEqual(StructuredType.Name))
			{
				State.Messages.Add(MessageId.CantDeclare, Id.Name);
				return false;
			}

			if ((Id is Variable || Id is Function) && !(Id is ConstVariable))
			{
				if ((Id.Flags & IdentifierFlags.Static) == 0 && (StructuredType.Flags & IdentifierFlags.Static) != 0)
				{
					State.Messages.Add(MessageId.NonStaticInStaticClass, Id.Declaration);
					return false;
				}
			}

			if (Id is Constructor && StructuredType is StructType)
			{
				var Ctor = Id as Constructor;
				var Type = Ctor.TypeOfSelf.RealId as TypeOfFunction;
				if (Type.Children.Length == 1)
				{
					State.Messages.Add(MessageId.StructParamLessCtor, Id.Declaration);
					return false;
				}
			}

			return base.CanIdDeclared(Id);
		}

		public Constructor CreateDeclaredConstructor(CodeString Declaration, FunctionParameter[] Params, List<Modifier> Mods = null)
		{
			if (ConstructorOverloads == null)
				ConstructorOverloads = new FunctionOverloads(null);

			var RetType = (Type)GlobalContainer.CommonIds.Void;
			if (StructuredType is ClassType)
			{
				if ((Mods == null || (Modifiers.GetFlags(Mods) & IdentifierFlags.Static) == 0) &&
					(StructuredType.Flags & (IdentifierFlags.Static | IdentifierFlags.Abstract)) == 0)
				{
					RetType = StructuredType;
				}
			}

			var FuncType = new TypeOfFunction(this, DefaultCallConv, RetType, Params);
			var Func = new Constructor(this, FuncType, ConstructorOverloads, Declaration);

			if (!AdjustAndDeclareFunction(Func, Mods)) return null;
			return Func;
		}

		public Constructor CreateDeclaredConstructorAndScope(CodeString Declaration, 
			FunctionParameter[] Params, CodeString Inner, List<Modifier> Mods = null)
		{
			var Func = CreateDeclaredConstructor(Declaration, Params, Mods);
			if (Func == null || CreateScopeForFunction(Func, Inner) == null) return null;
			return Func;
		}

		public bool ProcessConstructors()
		{
			var HasConstructor = StructuredType.HasNonstaticConstructor;

			if (!HasConstructor)
			{
				for (var i = 0; i < StructuredType.BaseStructures.Length; i++)
				{
					var Base = StructuredType.BaseStructures[i].Base;
					var SBase = Base.UnderlyingStructureOrRealId as StructuredType;

					if (!SBase.HasParameterLessCtor)
					{
						State.Messages.Add(MessageId.NoParamLessConstructor, 
							StructuredType.BaseStructures[i].Name);

						return false;
					}
				}
			}

			if (!HasConstructor && NeedsConstructor)
			{
				var Name = new CodeString(StructuredType.Name.ToString());
				var Func = CreateDeclaredConstructorAndScope(Name, null, new CodeString());
				if (Func == null) return false;

				Func.Access = IdentifierAccess.Public;
			}

			return true;
		}

		public bool ProcessBase()
		{
            var Type = StructuredType;
            for (var i = 0; i < Type.BaseStructures.Length; i++)
			{
                var BaseData = Type.BaseStructures[i];
                if ((Type.TypeFlags & TypeFlags.NoDefaultBase) != 0)
				{
					State.Messages.Add(MessageId.NobaseClassbase, BaseData.Name);
					return false;
				}

				BaseData.Base = Parent.RecognizeIdentifier(BaseData.Name, GetIdOptions.DefaultForType);
				if (BaseData.Base == null) return false;

				var RealBase = BaseData.Base.RealId;
                if (!Type.GetType().IsEquivalentTo(RealBase.GetType()))
				{
					State.Messages.Add(MessageId.CannotInherit, BaseData.Name);
					return false;
				}
				else if ((RealBase.Flags & IdentifierFlags.Static) != 0)
				{
					State.Messages.Add(MessageId.CannotInheritStatic, BaseData.Name);
					return false;
				}
				else if ((RealBase.Flags & IdentifierFlags.Sealed) != 0)
				{
					State.Messages.Add(MessageId.CannotInheritSealed, BaseData.Name);
					return false;
				}

                Type.BaseStructures[i] = BaseData;
			}

            if (Type.BaseStructures.Length == 0 &&  (Type.Flags & IdentifierFlags.Static) == 0 &&
                Type is ClassType && (Type.TypeFlags & TypeFlags.NoDefaultBase) == 0)
			{
				var NewBase = Identifiers.GetByFullNameFast<ClassType>(State, "System.Object");
                if (NewBase == null) return false;

                Type.BaseStructures = new StructureBase[1];
                Type.BaseStructures[0] = new StructureBase(NewBase);
			}

            for (var i = 0; i < Type.BaseStructures.Length; i++)
			{
                var BaseData = Type.BaseStructures[i];
                if (Identifiers.IsLessAccessable(BaseData.Base, Type))
				{
                    State.Messages.Add(MessageId.LessAccessable, Type.Name, BaseData.Name.ToString());
					return false;
				}
			}

            Type.Update();
			return true;
		}

		public override bool ProcessScope()
		{
			for (var i = 0; i < StructuredType.BaseStructures.Length; i++)
			{
				var BaseData = StructuredType.BaseStructures[i];
				if (Identifiers.IsSubtypeOf(BaseData.Base, StructuredType))
				{
					State.Messages.Add(MessageId.CycleInStructured, BaseData.Name);
					return false;
				}
			}

			if (!base.ProcessScope()) return false;
			if (!CheckCycleInStructs()) return false;
			if (!ProcessConstructors()) return false;
			return true;
		}

		bool NeedsConstructor
		{
			get
			{
				if (StructuredType is ClassType)
				{
					if ((StructuredType.Flags & IdentifierFlags.Abstract) == 0 &&
						(StructuredType.Flags & IdentifierFlags.Static) == 0)
					{
						return true;
					}
				}

				for (var i = 0; i < IdentifierList.Count; i++)
				{
					var V = IdentifierList[i] as MemberVariable;
					if (V != null && V.InitString.IsValid)
						return true;
				}

				return false;
			}
		}

		public override Variable OnCreateVariable(CodeString Name, Identifier Type, List<Modifier> Mods = null)
		{
			var Ret = CreateVariableHelper(Name, Type, Mods);
			if (Ret == null) Ret = new MemberVariable(this, Name, Type);
			return Ret;
		}

		public override Function OnCreateFunction(CodeString Name, TypeOfFunction FuncType,
			FunctionOverloads Overload, List<Modifier> Mods = null)
		{
			if (Mods != null && (Modifiers.GetFlags(Mods) & IdentifierFlags.Static) != 0)
				return new Function(this, Name, FuncType, Overload);
			else return new MemberFunction(this, Name, FuncType, Overload);
		}
	}
}
