using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Numerics;
using Zinnia.Base;

namespace Zinnia
{
    public enum ConstValueType : byte
    {
        Unknown,
        Integer,
        Float,
        Double,
        String,
        Char,
        Boolean,
        Zero,
        Structure,
        Null,
    }

    public static class Constants
    {
        public static bool CompareTupleValues(ConstValue Value, Predicate<ConstValue> Func)
        {
            if (Value is StructuredValue)
            {
                var SValue = Value as StructuredValue;
                for (var i = 0; i < SValue.Members.Count; i++)
                    if (!Func(SValue.Members[i])) return false;

                return true;
            }

            return Func(Value);
        }

        public static ConstExpressionNode GetDefaultValue(Identifier Id, CodeString Code)
        {
            var Type = Id.RealId is Type ? Id.RealId as Type : Id.TypeOfSelf;
            return new ConstExpressionNode(Type, new ZeroValue(), Code);
        }

        public static ExpressionNode GetDefaultValue(PluginRoot Plugin, Identifier Id, CodeString Code)
        {
            return Plugin.NewNode(GetDefaultValue(Id, Code));
        }

        public static ConstExpressionNode GetDefaultValue(IdContainer Container, CodeString Code)
        {
            return GetDefaultValue(Container.GlobalContainer.CommonIds.Auto, Code);
        }

        public static ConstValueType TranslateConstValueType(ConstValueType Type)
        {
            if (Type == ConstValueType.Null || Type == ConstValueType.Zero || Type == ConstValueType.Structure)
                return ConstValueType.Unknown;

            return Type;
        }

        public static bool RecognizeString(CodeString Code, PluginRoot Plugin, out String Out)
        {
            Out = null;
            ConstValue Value;

            var StringType = Plugin.State.GlobalContainer.CommonIds.String;
            if (!Constants.Recognize(Code, StringType, Plugin, out Value))
                return false;

            if (Value is StringValue) Out = (Value as StringValue).Value;
            else if (!(Value is NullValue)) throw new ApplicationException();
            return true;
        }

        public static bool Recognize(CodeString Code, Identifier Type, PluginRoot Plugin, out ConstValue Out)
        {
            Out = null;
            var State = Plugin.State;
            var Node = Expressions.Recognize(Code, Plugin, true);

            if (!(Node.Type.RealId is StringType))
            {
                var TypeMngrPlugin = Plugin.GetPlugin<TypeMngrPlugin>();
                Node = TypeMngrPlugin.Convert(Node, Type, Code);
                if (Node == null) return false;
            }

            if (!(Node is ConstExpressionNode))
            {
                State.Messages.Add(MessageId.MustBeConst, Code);
                return false;
            }

            var CNode = Node as ConstExpressionNode;
            Out = CNode.Value;
            return true;
        }

        public static ConstExpressionNode CreateConstNode(IdContainer Container, Type RetType, CodeString Code)
        {
            var Plugin = new PluginForGlobals(Container);
            Plugin.GetPlugin<TypeMngrPlugin>().RetType = RetType;
            return Expressions.CreateExpression(Code, Plugin) as ConstExpressionNode;
        }

        public static ConstExpressionNode CreateCStrNode(IdContainer Container, CodeString Code)
        {
            return CreateConstNode(Container, Container.GlobalContainer.CommonIds.String, Code);
        }

        public static ConstExpressionNode CreateCIntNode(IdContainer Container, CodeString Code)
        {
            var Type = Container.GlobalContainer.CommonIds.Int32;
            return CreateConstNode(Container, Type, Code);
        }

        public static ConstExpressionNode CreateCUIntNode(IdContainer Container, CodeString Code)
        {
            var Type = Container.GlobalContainer.CommonIds.UInt32;
            return CreateConstNode(Container, Type, Code);
        }

        public static ConstExpressionNode GetStringValue(IdContainer Container, string Value, CodeString Code)
        {
            var Tp = Container.GlobalContainer.CommonIds.String;
            return new ConstExpressionNode(Tp, new StringValue(Value), Code);
        }

        public static ConstExpressionNode GetBoolValue(IdContainer Container, bool Value, CodeString Code)
        {
            var Tp = Container.GlobalContainer.CommonIds.Boolean;
            return new ConstExpressionNode(Tp, new BooleanValue(Value), Code);
        }

        public static ConstExpressionNode GetCharValue(IdContainer Container, char Value, CodeString Code)
        {
            var Tp = Container.GlobalContainer.CommonIds.Char;
            return new ConstExpressionNode(Tp, new CharValue(Value), Code);
        }

        public static ConstExpressionNode GetNullValue(IdContainer Container, CodeString Code)
        {
            var Type = Container.GlobalContainer.CommonIds.Null;
            return new ConstExpressionNode(Type, new NullValue(), Code);
        }

        public static ConstExpressionNode GetUIntPtrValue(IdContainer Container, int Val, CodeString Code)
        {
            var Type = Container.GlobalContainer.CommonIds.UIntPtr;
            return new ConstExpressionNode(Type, new IntegerValue(Val), Code);
        }

        public static ConstExpressionNode GetIntPtrValue(IdContainer Container, int Val, CodeString Code)
        {
            var Type = Container.GlobalContainer.CommonIds.IntPtr;
            return new ConstExpressionNode(Type, new IntegerValue(Val), Code);
        }

        public static ConstExpressionNode GetIntValue(IdContainer Container, int Val, CodeString Code, bool AutoConvert = false)
        {
            var IntType = Container.GlobalContainer.CommonIds.Int32;
            var Flags = AutoConvert ? ExpressionFlags.AutoConvert : ExpressionFlags.None;
            return new ConstExpressionNode(IntType, new IntegerValue(Val), Code, Flags);
        }

        public static ConstExpressionNode GetUIntValue(IdContainer Container, uint Val, CodeString Code)
        {
            var IntType = Container.GlobalContainer.CommonIds.UInt32;
            return new ConstExpressionNode(IntType, new IntegerValue(Val), Code);
        }

        public static ConstExpressionNode GetLongValue(IdContainer Container, long Val, CodeString Code)
        {
            var IntType = Container.GlobalContainer.CommonIds.Int64;
            return new ConstExpressionNode(IntType, new IntegerValue(Val), Code);
        }

        public static ConstExpressionNode GetDoubleValue(IdContainer Container, double Val, CodeString Code)
        {
            var DoubleType = Container.GlobalContainer.CommonIds.Double;
            return new ConstExpressionNode(DoubleType, new DoubleValue(Val), Code);
        }

        public static ConstExpressionNode GetFloatValue(IdContainer Container, float Val, CodeString Code)
        {
            var DoubleType = Container.GlobalContainer.CommonIds.Single;
            return new ConstExpressionNode(DoubleType, new FloatValue(Val), Code);
        }

        public static Type GetDefaultType(IdContainer Container, ConstValueType Type)
        {
            var Global = Container.GlobalContainer;
            if (Type == ConstValueType.Integer) return Container.GlobalContainer.CommonIds.Int32;
            if (Type == ConstValueType.Double) return Container.GlobalContainer.CommonIds.Double;
            if (Type == ConstValueType.Float) return Container.GlobalContainer.CommonIds.Single;
            if (Type == ConstValueType.String) return Global.CommonIds.String;
            if (Type == ConstValueType.Char) return Global.CommonIds.Char;
            if (Type == ConstValueType.Boolean) return Global.CommonIds.Boolean;
            throw new ApplicationException();
        }
    }

    public abstract class ConstValue
    {
        public abstract byte[] ToByteArray();
        public abstract ConstValue Copy();
        public abstract bool IsEqual(ConstValue Value);
        public abstract ConstValue DoOperation(ConstValue Src, Operator Op);
        public abstract ConstValue Convert(ConstValueType To);
        public abstract ConstValueType Type { get; }

        public virtual ConstValue GetMember(int Index)
        {
            throw new NotImplementedException();
        }

        public virtual ConstValue DoOperation(ConstValue Src, Operator Op, Identifier Type)
        {
            return DoOperation(Src, Op);
        }

        public virtual ConstValue Convert(Identifier To)
        {
            var Type = To.RealId as Type;
            return Convert(Type.ConstValueType);
        }

        public StructuredValue CreateStructure(int Count)
        {
            var Members = new List<ConstValue>();
            for (var i = 0; i < Count; i++)
                Members.Add(Copy());

            return new StructuredValue(Members);
        }

        public virtual double Double
        {
            get { throw new InvalidOperationException(); }
        }

        public virtual bool CheckBounds(Identifier Type)
        {
            return true;
        }

        public bool CheckBounds(CompilerState State, Identifier Type, CodeString Code)
        {
            if (!CheckBounds(Type))
            {
                State.Messages.Add(MessageId.ConstOutOfRange, Code);
                return false;
            }

            return true;
        }

        public ConstExpressionNode ToExpression(IdContainer Container, CodeString Code)
        {
            var T = Constants.GetDefaultType(Container, Type);
            if (!CheckBounds(Container.State, T, Code)) return null;
            return new ConstExpressionNode(T, this, Code);
        }

        public ConstExpressionNode ToExpression(CompilerState State, Identifier Type, CodeString Code)
        {
            if (!CheckBounds(State, Type, Code)) return null;
            return new ConstExpressionNode(Type, this, Code);
        }

        public ExpressionNode ToExpression(PluginRoot Plugin, CodeString Code)
        {
            var Node = ToExpression(Plugin.Container, Code);
            return Node == null ? null : Plugin.NewNode(Node);
        }

        public ExpressionNode ToExpression(PluginRoot Plugin, Identifier Type, CodeString Code)
        {
            var Node = ToExpression(Plugin.State, Type, Code);
            return Node == null ? null : Plugin.NewNode(Node);
        }

        byte[] AdjustBytes(int Size, bool Signed)
        {
            var Bytes = ToByteArray();
            if (Bytes.Length < Size)
            {
                var Old = Bytes;
                Bytes = new byte[Size];

                for (var i = 0; i < Old.Length; i++)
                    Bytes[i] = Old[i];

                if (Signed && Bytes.Length > 0 && (Bytes[Old.Length - 1] & 0x80) != 0)
                {
                    for (var i = Old.Length; i < Size; i++)
                        Bytes[i] = 255;
                }
            }

            return Bytes;
        }

        public virtual unsafe long GetSigned(int Offset, int Size)
        {
            var Bytes = AdjustBytes(Offset + Size, true);
            fixed (byte* pBytes = Bytes)
            {
                var pBytesOffset = pBytes + Offset;
                if (Size == 1) return (long)(*(sbyte*)pBytesOffset);
                else if (Size == 2) return (long)(*(short*)pBytesOffset);
                else if (Size == 4) return (long)(*(int*)pBytesOffset);
                else if (Size == 8) return (*(long*)pBytesOffset);
                else throw new ApplicationException();
            }
        }

        public virtual unsafe ulong GetUnsigned(int Offset, int Size)
        {
            var Bytes = AdjustBytes(Offset + Size, false);
            fixed (byte* pBytes = Bytes)
            {
                var pBytesOffset = pBytes + Offset;
                if (Size == 1) return (ulong)(*(byte*)pBytesOffset);
                else if (Size == 2) return (ulong)(*(ushort*)pBytesOffset);
                else if (Size == 4) return (ulong)(*(uint*)pBytesOffset);
                else if (Size == 8) return (*(ulong*)pBytesOffset);
                else throw new ApplicationException();
            }
        }
    }

    public class StructuredValue : ConstValue
    {
        public List<ConstValue> Members;

        public StructuredValue(List<ConstValue> Members)
        {
            this.Members = Members;
        }

        public StructuredValue()
        {
            this.Members = new List<ConstValue>();
        }

        public override ConstValue GetMember(int Index)
        {
            return Members[Index];
        }

        public override byte[] ToByteArray()
        {
            throw new NotImplementedException();
        }

        public override ConstValue Copy()
        {
            return new StructuredValue(Members.ToList());
        }

        public override string ToString()
        {
            return "{" + String.Join(", ", Members) + "}";
        }

        public override bool IsEqual(ConstValue Value)
        {
            var StructuredValue = Value as StructuredValue;
            if (StructuredValue == null) return false;

            if (StructuredValue.Members.Count != Members.Count)
                return false;

            for (var i = 0; i < Members.Count; i++)
            {
                if (!Members[i].IsEqual(StructuredValue.Members[i]))
                    return false;
            }

            return true;
        }

        public override ConstValueType Type
        {
            get { return ConstValueType.Structure; }
        }

        public override ConstValue Convert(ConstValueType To)
        {
            throw new ApplicationException();
        }

        public StructuredValue Convert(ConstValueType[] To)
        {
            if (To.Length != Members.Count)
                throw new ArgumentException("Invalid length", "To");

            var Ret = new List<ConstValue>();
            for (var i = 0; i < Members.Count; i++)
                Ret.Add(Members[i].Convert(To[i]));

            if (Ret.Contains(null)) return null;
            else return new StructuredValue(Ret);
        }

        public override ConstValue Convert(Identifier To)
        {
            if (To.RealId is StructuredType)
            {
                var SType = To.RealId as StructuredType;
                var SMembers = SType.StructuredScope.IdentifierList;
                if (SMembers.Count != Members.Count) return null;

                var Ret = new List<ConstValue>();
                for (var i = 0; i < Members.Count; i++)
                    Ret.Add(Members[i].Convert(SMembers[i].TypeOfSelf));

                if (Ret.Contains(null)) return null;
                else return new StructuredValue(Ret);
            }

            return base.Convert(To);
        }

        StructuredValue ForAllMembers(Func<ConstValue, ConstValue> Func)
        {
            var NewMembers = new List<ConstValue>();
            for (var i = 0; i < Members.Count; i++)
            {
                var NewVal = Func(Members[i]);
                if (NewVal == null) return null;
                NewMembers.Add(NewVal);
            }

            return new StructuredValue(NewMembers);
        }

        StructuredValue ForAllMembers(StructuredValue Src, Func<ConstValue, ConstValue, ConstValue> Func)
        {
            var NewMembers = new List<ConstValue>();
            for (var i = 0; i < Members.Count; i++)
            {
                var NewVal = Func(Members[i], Src.Members[i]);
                if (NewVal == null) return null;
                NewMembers.Add(NewVal);
            }

            return new StructuredValue(NewMembers);
        }

        StructuredValue ForAllMembers(Identifier Type, Func<ConstValue, Identifier, ConstValue> Func)
        {
            var SType = Type.RealId as StructuredType;
            var SMembers = SType.StructuredScope.IdentifierList;
            if (SMembers.Count != Members.Count)
                throw new ArgumentException(null, "Type");

            var NewMembers = new List<ConstValue>();
            for (var i = 0; i < Members.Count; i++)
            {
                var NewType = SMembers[i].TypeOfSelf;
                var NewVal = Func(Members[i], NewType);
                if (NewVal == null) return null;
                NewMembers.Add(NewVal);
            }

            return new StructuredValue(NewMembers);
        }

        StructuredValue ForAllMembers(StructuredValue Src, Identifier Type, Func<ConstValue, ConstValue, Identifier, ConstValue> Func)
        {
            var SType = Type.RealId as StructuredType;
            var SMembers = SType.StructuredScope.IdentifierList;
            if (SMembers.Count != Members.Count)
                throw new ArgumentException(null, "Type");

            var NewMembers = new List<ConstValue>();
            for (var i = 0; i < Members.Count; i++)
            {
                var NewType = SMembers[i].TypeOfSelf;
                var NewVal = Func(Members[i], Src.Members[i], NewType);
                if (NewVal == null) return null;
                NewMembers.Add(NewVal);
            }

            return new StructuredValue(NewMembers);
        }

        public override ConstValue DoOperation(ConstValue Src, Operator Op, Identifier Type)
        {
            if (Op == Operator.UnaryPlus || Op == Operator.Negation || Op == Operator.Complement)
                return ForAllMembers(Type, (x, t) => x.DoOperation(null, Op, t));

            if (!(Src is StructuredValue))
                Src = Src.CreateStructure(Members.Count);

            var SSrc = Src as StructuredValue;
            return ForAllMembers(SSrc, Type, (x, y, t) => x.DoOperation(y, Op, t));
        }

        public override ConstValue DoOperation(ConstValue Src, Operator Op)
        {
            if (Op == Operator.UnaryPlus || Op == Operator.Negation || Op == Operator.Complement)
                return ForAllMembers(x => x.DoOperation(null, Op));

            if (!(Src is StructuredValue))
                Src = Src.CreateStructure(Members.Count);

            var SSrc = Src as StructuredValue;
            return ForAllMembers(SSrc, (x, y) => x.DoOperation(y, Op));
        }

        public override bool CheckBounds(Identifier Type)
        {
            if (Type.RealId is StructType)
            {
                var ValueType = Type.RealId as StructType;
                var Ids = ValueType.StructuredScope.IdentifierList;
                if (Ids.Count != Members.Count) return false;

                for (var i = 0; i < Members.Count; i++)
                {
                    if (!Members[i].CheckBounds(Ids[i].TypeOfSelf))
                        return false;
                }
            }
            else if (Type.RealId is NonrefArrayType)
            {
                var NonrefArrayType = Type.RealId as NonrefArrayType;
                var TypeOfValues = NonrefArrayType.TypeOfValues;
                var Dimensions = NonrefArrayType.Lengths;
                if (Dimensions == null) return false;

                var Length = 1;
                for (var i = 0; i < Dimensions.Length; i++)
                    Length *= Dimensions[i];

                if (Length != Members.Count)
                    return false;

                for (var i = 0; i < Members.Count; i++)
                {
                    if (!Members[i].CheckBounds(TypeOfValues))
                        return false;
                }

                return true;
            }
            else
            {
                return false;
            }

            return true;
        }
    }

    public class IntegerValue : ConstValue
    {
        public BigInteger Value;

        public IntegerValue(BigInteger Value)
        {
            this.Value = Value;
        }

        public override double Double
        {
            get { return (double)Value; }
        }

        public override ConstValueType Type
        {
            get { return ConstValueType.Integer; }
        }

        public override byte[] ToByteArray()
        {
            return Value.ToByteArray();
        }

        public override string ToString()
        {
            return Value.ToString();
        }

        public override ConstValue Copy()
        {
            return new IntegerValue(Value);
        }

        public override bool IsEqual(ConstValue Value)
        {
            var IValue = Value as IntegerValue;
            return IValue != null && IValue.Value == this.Value;
        }

        public override ConstValue DoOperation(ConstValue Src, Operator Op, Identifier Type)
        {
            if (Op == Operator.Complement)
            {
                return new IntegerValue(DataStoring.WrapToType(~Value, Type));
            }
            else if (Operators.IsShift(Op))
            {
                var Val = Value << (int)(Src as IntegerValue).Value;
                return new IntegerValue(DataStoring.WrapToType(Val, Type));
            }

            return base.DoOperation(Src, Op, Type);
        }

        public override ConstValue DoOperation(ConstValue Src, Operator Op)
        {
            var ISrc = Src as IntegerValue;
            if (Op == Operator.Add) return new IntegerValue(Value + ISrc.Value);
            if (Op == Operator.Subract) return new IntegerValue(Value - ISrc.Value);
            if (Op == Operator.Multiply) return new IntegerValue(Value * ISrc.Value);
            if (Op == Operator.Divide) return new IntegerValue(Value / ISrc.Value);
            if (Op == Operator.Modolus) return new IntegerValue(Value % ISrc.Value);
            if (Op == Operator.ShiftLeft) return ISrc.Value > 0xFFFF ? null : new IntegerValue(Value << (int)ISrc.Value);
            if (Op == Operator.ShiftRight) return ISrc.Value > 0xFFFF ? null : new IntegerValue(Value >> (int)ISrc.Value);

            if (Op == Operator.BitwiseAnd) return new IntegerValue(Value & ISrc.Value);
            if (Op == Operator.BitwiseOr) return new IntegerValue(Value | ISrc.Value);
            if (Op == Operator.BitwiseXor) return new IntegerValue((Value | ISrc.Value) & ~(Value & ISrc.Value));

            if (Op == Operator.Complement) return new IntegerValue(~Value);
            if (Op == Operator.Negation) return new IntegerValue(-Value);
            if (Op == Operator.UnaryPlus) return new IntegerValue(Value);

            if (Op == Operator.Equality) return new BooleanValue(Value == ISrc.Value);
            if (Op == Operator.Inequality) return new BooleanValue(Value != ISrc.Value);
            if (Op == Operator.Greater) return new BooleanValue(Value > ISrc.Value);
            if (Op == Operator.GreaterEqual) return new BooleanValue(Value >= ISrc.Value);
            if (Op == Operator.Less) return new BooleanValue(Value < ISrc.Value);
            if (Op == Operator.LessEqual) return new BooleanValue(Value <= ISrc.Value);
            throw new ApplicationException();
        }

        public override ConstValue Convert(ConstValueType To)
        {
            if (To == ConstValueType.Integer) return new IntegerValue(Value);
            if (To == ConstValueType.Double) return new DoubleValue((double)Value);
            if (To == ConstValueType.Float) return new FloatValue((float)Value);
            if (To == ConstValueType.String) return new StringValue(Value.ToString());

            if (To == ConstValueType.Char)
            {
                if (Value > char.MaxValue || Value < char.MinValue) return null;
                else return new CharValue((char)Value);
            }

            throw new ApplicationException();
        }

        public override bool CheckBounds(Identifier Type)
        {
            if (Type.RealId is NonFloatType)
            {
                var NonFlt = Type.RealId as NonFloatType;
                return NonFlt.MinValue <= Value && NonFlt.MaxValue >= Value;
            }
            else if (Type.RealId is PointerType || Type.RealId is ClassType)
            {
                return true;
            }
            else if (Type.RealId is EnumType)
            {
                var EType = Type.RealId as EnumType;
                if (EType.TypeOfValues == null) return true;
                else return CheckBounds(EType.TypeOfValues);
            }
            else if (Type.RealId is ObjectType)
            {
                return false;
            }

            throw new ApplicationException();
        }
    }

    public class DoubleValue : ConstValue
    {
        public double Value;

        public DoubleValue(double Value)
        {
            this.Value = Value;
        }

        public override double Double
        {
            get { return Value; }
        }

        public override ConstValueType Type
        {
            get { return ConstValueType.Double; }
        }

        public unsafe override byte[] ToByteArray()
        {
            fixed (double* PValue = &Value)
            {
                var Ret = new byte[sizeof(double)];
                for (var i = 0; i < sizeof(double); i++)
                    Ret[i] = ((byte*)PValue)[i];

                return Ret;
            }
        }

        public unsafe byte[] GetFloatBytes()
        {
            var Float = (float)Value;
            var PValue = &Float;
            var Ret = new byte[sizeof(float)];
            for (var i = 0; i < sizeof(float); i++)
                Ret[i] = ((byte*)PValue)[i];

            return Ret;
        }

        public override string ToString()
        {
            return Value.ToString();
        }

        public override ConstValue Copy()
        {
            return new DoubleValue(Value);
        }

        public override bool IsEqual(ConstValue Value)
        {
            var FValue = Value as DoubleValue;
            return FValue != null && FValue.Value == this.Value;
        }

        public override ConstValue DoOperation(ConstValue Src, Operator Op)
        {
            var FSrc = Src as DoubleValue;
            if (Op == Operator.Add) return new DoubleValue(Value + FSrc.Value);
            if (Op == Operator.Subract) return new DoubleValue(Value - FSrc.Value);
            if (Op == Operator.Multiply) return new DoubleValue(Value * FSrc.Value);
            if (Op == Operator.Divide) return new DoubleValue(Value / FSrc.Value);
            if (Op == Operator.Modolus) return new DoubleValue(Value % FSrc.Value);
            if (Op == Operator.Negation) return new DoubleValue(-Value);
            if (Op == Operator.UnaryPlus) return new DoubleValue(Value);

            if (Op == Operator.Equality) return new BooleanValue(Value == FSrc.Value);
            if (Op == Operator.Inequality) return new BooleanValue(Value != FSrc.Value);
            if (Op == Operator.Greater) return new BooleanValue(Value > FSrc.Value);
            if (Op == Operator.GreaterEqual) return new BooleanValue(Value >= FSrc.Value);
            if (Op == Operator.Less) return new BooleanValue(Value < FSrc.Value);
            if (Op == Operator.LessEqual) return new BooleanValue(Value <= FSrc.Value);
            throw new ApplicationException();
        }

        public override ConstValue Convert(ConstValueType To)
        {
            if (To == ConstValueType.Double) return new DoubleValue(Value);
            if (To == ConstValueType.Float) return new FloatValue((float)Value);
            if (To == ConstValueType.Integer) return new IntegerValue((BigInteger)Value);
            if (To == ConstValueType.String) return new StringValue(Value.ToString());
            throw new ApplicationException();
        }

        public override bool CheckBounds(Identifier Type)
        {
            if (Type.RealId is FloatType) return true;
            else if (Type.RealId is ObjectType) return false;
            else throw new ApplicationException();
        }
    }

    public class FloatValue : ConstValue
    {
        public float Value;

        public FloatValue(float Value)
        {
            this.Value = Value;
        }

        public override ConstValueType Type
        {
            get { return ConstValueType.Float; }
        }

        public override double Double
        {
            get { return Value; }
        }

        public unsafe override byte[] ToByteArray()
        {
            fixed (float* PValue = &Value)
            {
                var Ret = new byte[sizeof(double)];
                for (var i = 0; i < sizeof(double); i++)
                    Ret[i] = ((byte*)PValue)[i];

                return Ret;
            }
        }

        public override string ToString()
        {
            return Value.ToString();
        }

        public override ConstValue Copy()
        {
            return new FloatValue(Value);
        }

        public override bool IsEqual(ConstValue Value)
        {
            var FValue = Value as FloatValue;
            return FValue != null && FValue.Value == this.Value;
        }

        public override ConstValue DoOperation(ConstValue Src, Operator Op)
        {
            var FSrc = Src as FloatValue;
            if (Op == Operator.Add) return new FloatValue(Value + FSrc.Value);
            if (Op == Operator.Subract) return new FloatValue(Value - FSrc.Value);
            if (Op == Operator.Multiply) return new FloatValue(Value * FSrc.Value);
            if (Op == Operator.Divide) return new FloatValue(Value / FSrc.Value);
            if (Op == Operator.Modolus) return new FloatValue(Value % FSrc.Value);
            if (Op == Operator.Negation) return new FloatValue(-Value);
            if (Op == Operator.UnaryPlus) return new FloatValue(Value);

            if (Op == Operator.Equality) return new BooleanValue(Value == FSrc.Value);
            if (Op == Operator.Inequality) return new BooleanValue(Value != FSrc.Value);
            if (Op == Operator.Greater) return new BooleanValue(Value > FSrc.Value);
            if (Op == Operator.GreaterEqual) return new BooleanValue(Value >= FSrc.Value);
            if (Op == Operator.Less) return new BooleanValue(Value < FSrc.Value);
            if (Op == Operator.LessEqual) return new BooleanValue(Value <= FSrc.Value);
            throw new ApplicationException();
        }

        public override ConstValue Convert(ConstValueType To)
        {
            if (To == ConstValueType.Double) return new DoubleValue((double)Value);
            if (To == ConstValueType.Float) return new FloatValue(Value);
            if (To == ConstValueType.Integer) return new IntegerValue((BigInteger)Value);
            if (To == ConstValueType.String) return new StringValue(Value.ToString());
            throw new ApplicationException();
        }

        public override bool CheckBounds(Identifier Type)
        {
            if (Type.RealId is FloatType) return true;
            else if (Type.RealId is ObjectType) return false;
            else throw new ApplicationException();
        }
    }

    public class StringValue : ConstValue
    {
        public string Value;

        public StringValue(string Value)
        {
            this.Value = Value;
        }

        public override ConstValueType Type
        {
            get { return ConstValueType.String; }
        }

        public override byte[] ToByteArray()
        {
            throw new NotImplementedException();
        }

        public override string ToString()
        {
            return Value;
        }

        public override ConstValue Copy()
        {
            return new StringValue(Value);
        }

        public override bool IsEqual(ConstValue Value)
        {
            var SData = Value as StringValue;
            return SData != null && SData.Value == this.Value;
        }

        public override ConstValue DoOperation(ConstValue Src, Operator Op)
        {
            var SSrc = Src as StringValue;
            if (Op == Operator.Add) return new StringValue(Value + SSrc.Value);
            if (Op == Operator.Equality) return new BooleanValue(Value == SSrc.Value);
            if (Op == Operator.Inequality) return new BooleanValue(Value != SSrc.Value);
            throw new ApplicationException();
        }

        public override ConstValue Convert(ConstValueType To)
        {
            if (To == ConstValueType.String) return new StringValue(Value);
            throw new ApplicationException();
        }

        public override bool CheckBounds(Identifier Type)
        {
            if (Type.RealId is StringType) return true;
            else if (Type.RealId is ObjectType) return false;
            else throw new ApplicationException();
        }
    }

    public class CharValue : ConstValue
    {
        public char Value;

        public CharValue(char Value)
        {
            this.Value = Value;
        }

        public override ConstValueType Type
        {
            get { return ConstValueType.Char; }
        }

        public override string ToString()
        {
            return Value.ToString();
        }

        public unsafe override byte[] ToByteArray()
        {
            fixed (char* PValue = &Value)
            {
                var Ret = new byte[sizeof(char)];
                for (var i = 0; i < sizeof(char); i++)
                    Ret[i] = ((byte*)PValue)[i];

                return Ret;
            }
        }

        public override ConstValue Copy()
        {
            return new CharValue(Value);
        }

        public override bool IsEqual(ConstValue Value)
        {
            var CValue = Value as CharValue;
            return CValue != null && CValue.Value == this.Value;
        }

        public override ConstValue DoOperation(ConstValue Src, Operator Op)
        {
            var CSrc = Src as CharValue;
            if (Op == Operator.Add) return new CharValue((char)(Value + CSrc.Value));
            if (Op == Operator.Subract) return new CharValue((char)(Value - CSrc.Value));
            if (Op == Operator.Multiply) return new CharValue((char)(Value * CSrc.Value));
            if (Op == Operator.Divide) return new CharValue((char)(Value / CSrc.Value));
            if (Op == Operator.Modolus) return new CharValue((char)(Value % CSrc.Value));
            //if (Op == Operator.ShiftLeft) return CSrc.Value > 0xFFFF ? null : new CharValue(Value << (int)CSrc.Value);
            //if (Op == Operator.ShiftRight) return CSrc.Value > 0xFFFF ? null : new CharValue(Value >> (int)CSrc.Value);

            if (Op == Operator.BitwiseAnd) return new CharValue((char)(Value & CSrc.Value));
            if (Op == Operator.BitwiseOr) return new CharValue((char)(Value | CSrc.Value));
            if (Op == Operator.BitwiseXor) return new CharValue((char)((Value | CSrc.Value) & ~(Value & CSrc.Value)));

            if (Op == Operator.Complement) return new CharValue((char)(~Value));
            if (Op == Operator.Negation) return new CharValue((char)(-Value));
            if (Op == Operator.UnaryPlus) return new CharValue(Value);

            if (Op == Operator.Equality) return new BooleanValue(Value == CSrc.Value);
            if (Op == Operator.Inequality) return new BooleanValue(Value != CSrc.Value);
            if (Op == Operator.Greater) return new BooleanValue(Value > CSrc.Value);
            if (Op == Operator.GreaterEqual) return new BooleanValue(Value >= CSrc.Value);
            if (Op == Operator.Less) return new BooleanValue(Value < CSrc.Value);
            if (Op == Operator.LessEqual) return new BooleanValue(Value <= CSrc.Value);
            throw new ApplicationException();
        }

        public override ConstValue Convert(ConstValueType To)
        {
            if (To == ConstValueType.Integer) return new IntegerValue(new BigInteger((int)Value));
            if (To == ConstValueType.Char) return new CharValue(Value);
            if (To == ConstValueType.String) return new StringValue(Value.ToString());
            throw new ApplicationException();
        }

        public override bool CheckBounds(Identifier Type)
        {
            if (Type.RealId is CharType) return true;
            else if (Type.RealId is ObjectType) return false;
            else throw new ApplicationException();
        }
    }

    public class BooleanValue : ConstValue
    {
        public bool Value;

        public BooleanValue(bool Value)
        {
            this.Value = Value;
        }

        public override ConstValueType Type
        {
            get { return ConstValueType.Boolean; }
        }

        public override string ToString()
        {
            return Value.ToString();
        }

        public unsafe override byte[] ToByteArray()
        {
            fixed (bool* PValue = &Value)
            {
                var Ret = new byte[sizeof(bool)];
                for (var i = 0; i < sizeof(bool); i++)
                    Ret[i] = ((byte*)PValue)[i];

                return Ret;
            }
        }

        public override ConstValue Copy()
        {
            return new BooleanValue(Value);
        }

        public override bool IsEqual(ConstValue Value)
        {
            var BValue = Value as BooleanValue;
            return BValue != null && BValue.Value == this.Value;
        }

        public override ConstValue DoOperation(ConstValue Src, Operator Op)
        {
            var BSrc = Src as BooleanValue;
            if (Op == Operator.Equality) return new BooleanValue(Value == BSrc.Value);
            if (Op == Operator.Inequality) return new BooleanValue(Value != BSrc.Value);

            if (Op == Operator.And) return new BooleanValue(Value && BSrc.Value);
            if (Op == Operator.Or) return new BooleanValue(Value || BSrc.Value);
            if (Op == Operator.Not) return new BooleanValue(!Value);
            throw new ApplicationException();
        }

        public override ConstValue Convert(ConstValueType To)
        {
            if (To == ConstValueType.Boolean) return new BooleanValue(Value);
            if (To == ConstValueType.String) return new StringValue(Value.ToString());
            throw new ApplicationException();
        }

        public override bool CheckBounds(Identifier Type)
        {
            if (Type.RealId is BooleanType) return true;
            else if (Type.RealId is ObjectType) return false;
            else throw new ApplicationException();
        }
    }

    public class ZeroValue : ConstValue
    {
        public ZeroValue()
        {
        }

        public override ConstValueType Type
        {
            get { return ConstValueType.Zero; }
        }

        public override ConstValue GetMember(int Index)
        {
            return new ZeroValue();
        }

        public override bool IsEqual(ConstValue Value)
        {
            return Value is ZeroValue;
        }

        public override ConstValue Copy()
        {
            return new ZeroValue();
        }

        public override bool CheckBounds(Identifier Type)
        {
            return true;
        }

        public override ConstValue Convert(ConstValueType To)
        {
            return new ZeroValue();
        }

        public override ConstValue DoOperation(ConstValue Src, Operator Op)
        {
            throw new ApplicationException();
        }

        public override byte[] ToByteArray()
        {
            throw new ApplicationException();
        }

        public override unsafe long GetSigned(int Offset, int Size)
        {
            return 0;
        }

        public override unsafe ulong GetUnsigned(int Offset, int Size)
        {
            return 0;
        }

        public override string ToString()
        {
            return "0";
        }
    }

    public class NullValue : ConstValue
    {
        public NullValue()
        {
        }

        public override ConstValueType Type
        {
            get { return ConstValueType.Null; }
        }

        public override ConstValue GetMember(int Index)
        {
            throw new ApplicationException();
        }

        public override bool IsEqual(ConstValue Value)
        {
            return Value is NullValue;
        }

        public override ConstValue Copy()
        {
            return new NullValue();
        }

        public override bool CheckBounds(Identifier Type)
        {
            if (Type.RealId is ObjectType || Type.RealId is StringType || Type.RealId is RefArrayType ||
                Type.RealId is ClassType || Type.RealId is PointerType || Type.RealId is NullType)
            {
                return true;
            }

            throw new ApplicationException();
        }

        public override ConstValue Convert(Identifier To)
        {
            if (To.RealId is ObjectType || To.RealId is StringType || To.RealId is RefArrayType ||
                To.RealId is ClassType || To.RealId is PointerType || To.RealId is NullType)
            {
                return new NullValue();
            }

            throw new ApplicationException();
        }

        public override ConstValue Convert(ConstValueType To)
        {
            throw new ApplicationException();
        }

        public override ConstValue DoOperation(ConstValue Src, Operator Op)
        {
            throw new ApplicationException();
        }

        public override byte[] ToByteArray()
        {
            throw new ApplicationException();
        }

        public override unsafe long GetSigned(int Offset, int Size)
        {
            return 0;
        }

        public override unsafe ulong GetUnsigned(int Offset, int Size)
        {
            return 0;
        }

        public override string ToString()
        {
            return "0";
        }
    }

    public class ConstExpressionNode : ExpressionNode
    {
        public ConstValue Value;

        public char Char
        {
            get { return (Value as CharValue).Value; }
        }

        public BigInteger Integer
        {
            get { return (Value as IntegerValue).Value; }
        }

        public double Double
        {
            get { return (Value as DoubleValue).Value; }
        }

        public double Float
        {
            get { return (Value as FloatValue).Value; }
        }

        public double CDouble
        {
            get { return Value.Double; }
        }

        public string String
        {
            get { return (Value as StringValue).Value; }
        }

        public bool Bool
        {
            get { return (Value as BooleanValue).Value; }
        }

        public bool CheckBounds()
        {
            return Value.CheckBounds(Type.RealId as Type);
        }

        public bool CheckBounds(CompilerState State)
        {
            return Value.CheckBounds(State, Type, Code);
        }

        public override ConditionResult ConditionResult
        {
            get { return Bool ? ConditionResult.True : ConditionResult.False; }
        }

        public bool IsEqual(ConstExpressionNode Node)
        {
            return Value.IsEqual(Node.Value);
        }

        public ConstExpressionNode(Identifier Type, ConstValue Value, CodeString Code, ExpressionFlags Flags = ExpressionFlags.None)
            : base(Code, Flags)
        {
            if (!(Type is AutomaticType))
            {
                var RType = Type.RealId as Type;
                var T = Constants.TranslateConstValueType(Value.Type);

                if ((RType.ConstValueType != T && T != ConstValueType.Unknown) || !Value.CheckBounds(Type))
                    throw new ApplicationException();
            }

            this.Type = Type;
            this.Value = Value;
        }

        protected override ExpressionNode Copy_WithoutLinkedNodes(CompilerState State, CodeString Code, Func<ExpressionNode, ExpressionNode> Func)
        {
            if (!Code.IsValid) Code = this.Code;
            return new ConstExpressionNode(Type, Value, Code, Flags);
        }

        public override string ToString()
        {
            return Value.ToString();
        }
    }
}
