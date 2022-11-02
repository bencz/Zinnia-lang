using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Zinnia.Base;

namespace Zinnia
{
    public abstract class Modifier
    {
        public CodeString Code;

        public Modifier(CodeString Code)
        {
            this.Code = Code;
        }

        public abstract bool Apply(Identifier Id);

        public virtual bool Check(Identifier Id)
        {
            return true;
        }
    }

    public class AlignModifier : Modifier
    {
        public int Align;

        public AlignModifier(CodeString Code, int Align)
            : base(Code)
        {
            this.Align = Align;
        }

        public override bool Apply(Identifier Id)
        {
            var State = Id.Container.State;
            if (Id is Variable)
            {
                var Var = Id as Variable;
                Var.Align = Align;
            }
            else if (Id is StructType)
            {
                var Type = Id as StructType;
                Type.Align = Align;
            }
            else
            {
                State.Messages.Add(MessageId.ModifierCantBeUsed, Code);
                return false;
            }

            return true;
        }
    }

    public class CallingConventionModifier : Modifier
    {
        public CallingConvention CallingConvention;

        public CallingConventionModifier(CodeString Code, CallingConvention CallingConvention)
            : base(Code)
        {
            this.CallingConvention = CallingConvention;
        }

        public override bool Apply(Identifier Id)
        {
            var State = Id.Container.State;
            if (!(Id is Function))
            {
                State.Messages.Add(MessageId.ModifierCantBeUsed, Code);
                return false;
            }

            var Type = Id.Children[0] as TypeOfFunction;
            Type.CallConv = CallingConvention;
            return true;
        }
    }

    public class AccessModifier : Modifier
    {
        public IdentifierAccess Access;

        public AccessModifier(CodeString Code, IdentifierAccess Access)
            : base(Code)
        {
            this.Access = Access;

            if (Access == IdentifierAccess.Unknown)
                throw new ArgumentOutOfRangeException("Access");
        }

        public override bool Apply(Identifier Id)
        {
            var Container = Id.Container;
            var StructuredScope = Container.RealContainer as StructuredScope;
            var State = Container.State;

            if (Id is Function || Id is GlobalVariable || Id is ConstVariable ||
                Id is MemberVariable || Id is Property || Id is IdentifierAlias || Id is Type)
            {
                if (StructuredScope != null)
                {
                    if (Access == IdentifierAccess.Private && (Id.Flags & IdentifierFlags.Virtual) != 0)
                    {
                        State.Messages.Add(MessageId.PrivateVirtual, Code);
                        return false;
                    }
                }
                else
                {
                    if (Access != IdentifierAccess.Internal && Access != IdentifierAccess.Public)
                    {
                        State.Messages.Add(MessageId.ModifierCantBeUsed, Code);
                        return false;
                    }
                }
            }
            else
            {
                State.Messages.Add(MessageId.ModifierCantBeUsed, Code);
                return false;
            }

            Id.Access = Access;
            return true;
        }
    }

    public class FlagModifier : Modifier
    {
        public IdentifierFlags Flags;

        public FlagModifier(CodeString Code, IdentifierFlags Flags)
            : base(Code)
        {
            this.Flags = Flags;
        }

        public override bool Apply(Identifier Id)
        {
            var State = Id.Container.State;
            var AllFlags = Id.Flags | Flags;
            var Structure = Id.Container.RealContainer as StructuredScope;
            var IsFuncOrProp = Id is Function || Id is Property;

            if ((Flags & IdentifierFlags.Virtual) != 0)
            {
                if (!IsFuncOrProp || Structure == null || Id is Constructor || Id is Destructor)
                {
                    State.Messages.Add(MessageId.ModifierCantBeUsed, Code);
                    return false;
                }

                if ((AllFlags & IdentifierFlags.Static) != 0)
                {
                    State.Messages.Add(MessageId.IncompatibleMods, Code);
                    return false;
                }

                if (Id.Access == IdentifierAccess.Private)
                {
                    State.Messages.Add(MessageId.PrivateVirtual, Code);
                    return false;
                }
            }

            if ((Flags & IdentifierFlags.Override) != 0)
            {
                if (!IsFuncOrProp || Structure == null || Id is Constructor || Id is Destructor)
                {
                    State.Messages.Add(MessageId.ModifierCantBeUsed, Code);
                    return false;
                }

                if ((AllFlags & IdentifierFlags.Static) != 0)
                {
                    State.Messages.Add(MessageId.IncompatibleMods, Code);
                    return false;
                }

                AllFlags |= IdentifierFlags.Virtual;
            }

            if ((Flags & IdentifierFlags.Abstract) != 0)
            {
                if (IsFuncOrProp)
                {
                    if (Structure == null || Id is Constructor || Id is Destructor)
                    {
                        State.Messages.Add(MessageId.ModifierCantBeUsed, Code);
                        return false;
                    }

                    if ((AllFlags & IdentifierFlags.Static) != 0 || (AllFlags & IdentifierFlags.Override) != 0 ||
                        (AllFlags & IdentifierFlags.Sealed) != 0)
                    {
                        State.Messages.Add(MessageId.IncompatibleMods, Code);
                        return false;
                    }

                    if ((Structure.StructuredType.Flags & IdentifierFlags.Abstract) == 0)
                    {
                        State.Messages.Add(MessageId.AbstractInNonAbstract, Code);
                        return false;
                    }

                    AllFlags |= IdentifierFlags.Virtual;
                }
                else if (Id is ClassType)
                {
                    if ((AllFlags & IdentifierFlags.Sealed) != 0 || (AllFlags & IdentifierFlags.Static) != 0)
                    {
                        State.Messages.Add(MessageId.IncompatibleMods, Code);
                        return false;
                    }
                }
                else
                {
                    State.Messages.Add(MessageId.ModifierCantBeUsed, Code);
                    return false;
                }
            }

            if ((Flags & IdentifierFlags.Sealed) != 0)
            {
                if (IsFuncOrProp)
                {
                    if (Structure == null || Id is Constructor || Id is Destructor)
                    {
                        State.Messages.Add(MessageId.ModifierCantBeUsed, Code);
                        return false;
                    }

                    if ((AllFlags & IdentifierFlags.Static) != 0 || (AllFlags & IdentifierFlags.Abstract) != 0 ||
                        (AllFlags & IdentifierFlags.Override) == 0)
                    {
                        State.Messages.Add(MessageId.IncompatibleMods, Code);
                        return false;
                    }

                    AllFlags |= IdentifierFlags.Virtual;
                }
                else if (Id is ClassType)
                {
                    if ((AllFlags & IdentifierFlags.Abstract) != 0 || (AllFlags & IdentifierFlags.Static) != 0)
                    {
                        State.Messages.Add(MessageId.IncompatibleMods, Code);
                        return false;
                    }
                }
                else
                {
                    State.Messages.Add(MessageId.ModifierCantBeUsed, Code);
                    return false;
                }
            }

            if ((Flags & IdentifierFlags.Static) != 0)
            {
                if (IsFuncOrProp || Id is GlobalVariable)
                {
                    if (Structure == null || Id is Destructor)
                    {
                        State.Messages.Add(MessageId.ModifierCantBeUsed, Code);
                        return false;
                    }

                    if ((AllFlags & IdentifierFlags.Virtual) != 0)
                    {
                        State.Messages.Add(MessageId.IncompatibleMods, Code);
                        return false;
                    }

                    if (Id is ConstVariable)
                    {
                        State.Messages.Add(MessageId.ConstsCantBeStatic, Code);
                        return false;
                    }
                }
                else if (Id is ClassType)
                {
                    if ((AllFlags & IdentifierFlags.Abstract) != 0 || (AllFlags & IdentifierFlags.Sealed) != 0)
                    {
                        State.Messages.Add(MessageId.IncompatibleMods, Code);
                        return false;
                    }

                    var Class = Id as ClassType;
                    Class.TypeFlags &= TypeFlags.CanBeVariable;
                }
                else
                {
                    State.Messages.Add(MessageId.ModifierCantBeUsed, Code);
                    return false;
                }
            }

            if ((Flags & IdentifierFlags.ReadOnly) != 0)
            {
                var Var = Id as Variable;
                if (!(Var is GlobalVariable))
                {
                    State.Messages.Add(MessageId.ModifierCantBeUsed, Code);
                    return false;
                }
#warning WARNING
                /*
				if (!Var.InitString.IsValid)
				{
					State.Messages.Add(MessageId.UnassignedReadonly, Code);
					return false;
				}*/
            }

            if ((Flags & IdentifierFlags.HideBaseId) != 0)
            {
                if (Structure == null)
                {
                    State.Messages.Add(MessageId.ModifierCantBeUsed, Code);
                    return false;
                }
            }

            Id.Flags = AllFlags;
            return true;
        }
    }

    public class ParamFlagModifier : Modifier
    {
        public ParameterFlags Flags;

        public ParamFlagModifier(CodeString Code, ParameterFlags Flags)
            : base(Code)
        {
            this.Flags = Flags;
        }

        public override bool Apply(Identifier Id)
        {
            var FuncParam = Id as FunctionParameter;
            var State = Id.Container.State;
            if (FuncParam == null)
            {
                State.Messages.Add(MessageId.ModifierCantBeUsed, Code);
                return false;
            }

            var AllFlags = FuncParam.ParamFlags | Flags;
            if ((Flags & ParameterFlags.ParamArray) != 0)
            {
                var Type = Id.Children[0].RealId;
                if (Type is ArrayType)
                {
                    var ArrType = Type as ArrayType;
                    if (ArrType.Dimensions != 1)
                    {
                        State.Messages.Add(MessageId.ModifierCantBeUsed, Code);
                        return false;
                    }
                }
                else if (!(Type is PointerAndLength || Type is PointerType))
                {
                    State.Messages.Add(MessageId.ModifierCantBeUsed, Code);
                    return false;
                }
            }

            FuncParam.ParamFlags = AllFlags;
            return true;
        }
    }

    public class ConstModifier : Modifier
    {
        public ConstModifier(CodeString Code)
            : base(Code)
        {
        }

        public override bool Apply(Identifier Id)
        {
            var State = Id.Container.State;
            if (!(Id is ConstVariable))
            {
                State.Messages.Add(MessageId.ModifierCantBeUsed, Code);
                return false;
            }

            return true;
        }
    }

    public class AssemblyNameModifier : Modifier
    {
        public string NewName;

        public AssemblyNameModifier(CodeString Code, string NewName)
            : base(Code)
        {
            this.NewName = NewName;
        }

        public override bool Apply(Identifier Id)
        {
            Id.AssemblyName = NewName;
            return true;
        }
    }

    public class GuidModifier : Modifier
    {
        public Guid NewGuid;

        public GuidModifier(CodeString Code, Guid NewGuid)
            : base(Code)
        {
            this.NewGuid = NewGuid;
        }

        public override bool Apply(Identifier Id)
        {
            var State = Id.Container.State;
            var Structure = Id as StructuredType;

            if (Structure == null)
            {
                State.Messages.Add(MessageId.ModifierCantBeUsed, Code);
                return false;
            }

            Structure.Guid = NewGuid;
            return true;
        }
    }

    public class NoBaseModifier : Modifier
    {
        public NoBaseModifier(CodeString Code)
            : base(Code)
        {
        }

        public override bool Apply(Identifier Id)
        {
            var State = Id.Container.State;
            var Class = Id as ClassType;

            if (Class == null)
            {
                State.Messages.Add(MessageId.ModifierCantBeUsed, Code);
                return false;
            }

            Class.TypeFlags |= TypeFlags.NoDefaultBase;
            return true;
        }
    }

    public static class Modifiers
    {
        public static bool Contains<T>(List<Modifier> Modifiers)
        {
            for (var i = 0; i < Modifiers.Count; i++)
                if (Modifiers[i] is T) return true;

            return false;
        }

        public static IdentifierFlags GetFlags(List<Modifier> Modifiers)
        {
            var Ret = IdentifierFlags.None;
            for (var i = 0; i < Modifiers.Count; i++)
            {
                var FlagMod = Modifiers[i] as FlagModifier;
                if (FlagMod != null) Ret |= FlagMod.Flags;
            }

            return Ret;
        }

        public static bool IsExternOrAbstract(IdentifierFlags Flags)
        {
            return (Flags & IdentifierFlags.Extern) != 0 || (Flags & IdentifierFlags.Abstract) != 0;
        }

        public static bool IsExternOrAbstract(List<Modifier> Modifiers)
        {
            return IsExternOrAbstract(GetFlags(Modifiers));
        }

        public static bool Apply(List<Modifier> Modifiers, Identifier Id)
        {
            var RetValue = true;
            for (var i = 0; i < Modifiers.Count; i++)
                if (!Modifiers[i].Apply(Id)) RetValue = false;

            for (var i = 0; i < Modifiers.Count; i++)
                if (!Modifiers[i].Check(Id)) RetValue = false;

            return RetValue;
        }

        public delegate SimpleRecResult CustomRecognizeFunc(ref CodeString Code);
        public static bool CustomRecognize(ref CodeString Code, CustomRecognizeFunc Func)
        {
            var Found = true;
            while (Found)
            {
                Found = false;

                var Res = Func(ref Code);
                if (Res == SimpleRecResult.Failed) return false;
                if (Res == SimpleRecResult.Succeeded) Found = true;
            }

            return true;
        }

        public static List<Modifier> Recognize(IdContainer Container, ref CodeString Code)
        {
            var Out = new List<Modifier>();
            var Lang = Container.State.Language;

            if (Lang.ModRecognizers != null)
            {
                var Recs = Lang.ModRecognizers;
                CustomRecognize(ref Code, (ref CodeString xCode) =>
                {
                    for (var i = 0; i < Recs.Length; i++)
                    {
                        var Res = Recs[i].Recognize(Container, ref xCode, Out);
                        if (Res != SimpleRecResult.Unknown) return Res;
                    }

                    return SimpleRecResult.Unknown;
                });
            }

            return Out;
        }

        public static bool GetCallConv(IdContainer Container, ref CodeString Code, out CallingConvention Ret)
        {
            var Mods = Recognize(Container, ref Code);
            if (Mods == null)
            {
                Ret = CallingConvention.Unknown;
                return false;
            }

            return GetCallConv(Container.State, Mods, out Ret);
        }

        public static bool GetCallConv(CompilerState State, List<Modifier> Mods, out CallingConvention Ret)
        {
            var RetValue = true;
            Ret = CallingConvention.Unknown;

            for (var i = 0; i < Mods.Count; i++)
            {
                var Mod = Mods[i] as CallingConventionModifier;
                if (Mod == null)
                {
                    State.Messages.Add(MessageId.NotExpected, Mods[i].Code);
                    RetValue = false;
                    continue;
                }

                Ret = Mod.CallingConvention;
            }

            return RetValue;
        }

        public static bool GetIdAccess(IdContainer Container, ref CodeString Code, out IdentifierAccess Ret)
        {
            var Mods = Recognize(Container, ref Code);
            if (Mods == null)
            {
                Ret = IdentifierAccess.Unknown;
                return false;
            }

            return GetIdAccess(Container.State, Mods, out Ret);
        }

        public static bool GetIdAccess(CompilerState State, List<Modifier> Mods, out IdentifierAccess Ret)
        {
            var RetValue = true;
            Ret = IdentifierAccess.Unknown;

            for (var i = 0; i < Mods.Count; i++)
            {
                var Mod = Mods[i] as AccessModifier;
                if (Mod == null)
                {
                    State.Messages.Add(MessageId.NotExpected, Mods[i].Code);
                    RetValue = false;
                    continue;
                }

                Ret = Mod.Access;
            }

            return RetValue;
        }
    }
}