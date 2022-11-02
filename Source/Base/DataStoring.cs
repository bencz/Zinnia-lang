using System;
using System.Numerics;

namespace Zinnia.Base
{
    public static class DataStoring
    {
        public static BigInteger GetAlignmentMask(int Size, int Align)
        {
            var ZeroBits = Helper.Pow2Sqrt(Align);
            var SizeMask = GetSizeMask(Size);
            return (SizeMask << ZeroBits) & SizeMask;
        }

        public static int AlignWithDecrease(int Value, int Align)
        {
            return Value - Value % Align;
        }

        public static int AlignWithIncrease(int Value, int Align)
        {
            return AlignWithDecrease(Value + Align - 1, Align);
        }

        public static BigInteger AlignWithDecrease(BigInteger Value, int Align)
        {
            return Value - Value % Align;
        }

        public static BigInteger AlignWithIncrease(BigInteger Value, int Align)
        {
            return AlignWithDecrease(Value + Align - 1, Align);
        }

        public static ExpressionNode AlignWithDecrease(PluginRoot Plugin, ExpressionNode Node, int Align, CodeString Code)
        {
            var RType = Node.Type.RealId as Type;
            var Mask = GetAlignmentMask(RType.Size, Align);
            var AndCh1Value = new IntegerValue(Mask);

            var AndCh1 = Plugin.NewNode(new ConstExpressionNode(Node.Type, AndCh1Value, Code));
            if (AndCh1 == null) return null;

            var AndCh = new ExpressionNode[] { Node, AndCh1 };
            return Plugin.NewNode(new OpExpressionNode(Operator.BitwiseAnd, AndCh, Code));
        }

        public static ExpressionNode AlignWithIncrease(PluginRoot Plugin, ExpressionNode Node, int Align, CodeString Code)
        {
            var RType = Node.Type.RealId as Type;
            var AddCh1Value = new IntegerValue(Align - 1);

            var AddCh1 = Plugin.NewNode(new ConstExpressionNode(Node.Type, AddCh1Value, Code));
            if (AddCh1 == null) return null;

            var AddCh = new ExpressionNode[] { Node, AddCh1 };
            var AddNode = Plugin.NewNode(new OpExpressionNode(Operator.Add, AddCh, Code));
            if (AddNode == null) return null;

            return AlignWithDecrease(Plugin, AddNode, Align, Code);
        }

        public static bool VerifyAlign(int Align)
        {
            return Align == 1 || Align == 2 || Align == 4 || Align == 8 || Align == 16 || Align == 32;
        }

        public static string[] Str_SizeMask = new string[] { "", "FF", "FFFF", "FFFFFF",
            "FFFFFFFF", "FFFFFFFFFF", "FFFFFFFFFFFF", "FFFFFFFFFFFFFF", "FFFFFFFFFFFFFFFF" };

        public static ulong[] Ulong_SizeMask = new ulong[] { 0x0, 0xFF, 0xFFFF, 0xFFFFFF,
            0xFFFFFFFF, 0xFFFFFFFFFF, 0xFFFFFFFFFFFF, 0xFFFFFFFFFFFFFF, 0xFFFFFFFFFFFFFFFF };

        public static BigInteger WrapToType(BigInteger x, Identifier Type)
        {
            var RType = Type.RealId as Type;
            var Bytes = x.ToByteArray();
            var Ret = new byte[RType.Size];

            var MinLength = Math.Min(Bytes.Length, Ret.Length);
            for (var i = 0; i < MinLength; i++)
                Ret[i] = Bytes[i];

            if (x < 0)
            {
                for (var i = Bytes.Length; i < Ret.Length; i++)
                    Ret[i] = byte.MaxValue;
            }

            if ((Ret[Ret.Length - 1] & 0x80) != 0 && RType is UnsignedType)
            {
                var NRet = new byte[Ret.Length + 1];
                for (var i = 0; i < Ret.Length; i++)
                    NRet[i] = Ret[i];

                Ret = NRet;
            }

            return new BigInteger(Ret);
        }

        public static BigInteger GetSizeMask(int Size)
        {
            if (Size <= 8) return Ulong_SizeMask[Size];

            var Ret = new BigInteger();
            for (var i = 0; i < Size; i++)
                Ret = (Ret << 8) + 0xFF;

            return Ret;
        }
    }

    public static class LEB128Helper
    {
        public static ulong DecodeULong(Func<byte> GetByte)
        {
            var Result = 0UL;
            var Shift = (byte)0;

            while (true)
            {
                var Byte = GetByte();
                Result |= (ulong)(Byte & 0x7F) << Shift;
                if ((Byte & 0x80) == 0) break;
                Shift += 7;
            }

            return Result;
        }

        public static long DecodeLong(Func<byte> GetByte)
        {
            var Result = 0L;
            var Shift = (byte)0;
            var Byte = (byte)0;

            while (true)
            {
                Byte = GetByte();
                Result |= (long)(Byte & 0x7F) << Shift;
                Shift += 7;
                if ((Byte & 0x80) == 0) break;
            }

            if (Shift < sizeof(long) * 8 && (Byte & 0x40) != 0)
                Result |= -1L << Shift;

            return Result;
        }

        public static uint DecodeUInt(Func<byte> GetByte)
        {
            var Result = 0U;
            var Shift = (byte)0;

            while (true)
            {
                var Byte = GetByte();
                Result |= (uint)(Byte & 0x7F) << Shift;
                if ((Byte & 0x80) == 0) break;
                Shift += 7;
            }

            return Result;
        }

        public static int DecodeInt(Func<byte> GetByte)
        {
            var Result = 0;
            var Shift = (byte)0;
            var Byte = (byte)0;

            while (true)
            {
                Byte = GetByte();
                Result |= (int)(Byte & 0x7F) << Shift;
                Shift += 7;
                if ((Byte & 0x80) == 0) break;
            }

            if (Shift < sizeof(int) * 8 && (Byte & 0x40) != 0)
                Result |= -1 << Shift;

            return Result;
        }

        public static BigInteger Decode(Func<byte> GetByte)
        {
            var Result = (BigInteger)0;
            var Shift = 0;
            var Byte = (byte)0;

            while (true)
            {
                Byte = GetByte();
                Result |= ((BigInteger)(Byte & 0x7F) << Shift);
                Shift += 7;
                if ((Byte & 0x80) == 0) break;
            }

            if ((Byte & 0x40) != 0)
                Result |= -((BigInteger)1 << Shift);

            return Result;
        }

        public static void Encode(int Value, Action<byte> EmitByte)
        {
            var More = true;
            while (More)
            {
                var Byte = (byte)(Value & 0x7F);
                var Signed = (Byte & 0x40) != 0;
                Value >>= 7;

                if ((Value == 0 && !Signed) || (Value == -1 && Signed)) More = false;
                else Byte |= 0x80;
                EmitByte(Byte);
            }
        }

        public static void Encode(uint Value, Action<byte> EmitByte)
        {
            do
            {
                var Byte = (byte)(Value & 0x7F);
                if ((Value >>= 7) != 0)
                    Byte |= 0x80;

                EmitByte(Byte);
            } while (Value != 0);
        }

        public static void Encode(long Value, Action<byte> EmitByte)
        {
            var More = true;
            while (More)
            {
                var Byte = (byte)(Value & 0x7F);
                var Signed = (Byte & 0x40) != 0;
                Value >>= 7;

                if ((Value == 0 && !Signed) || (Value == -1 && Signed)) More = false;
                else Byte |= 0x80;
                EmitByte(Byte);
            }
        }

        public static void Encode(ulong Value, Action<byte> EmitByte)
        {
            do
            {
                var Byte = (byte)(Value & 0x7F);
                if ((Value >>= 7) != 0)
                    Byte |= 0x80;

                EmitByte(Byte);
            } while (Value != 0);
        }

        public static void Encode(BigInteger Value, Action<byte> EmitByte)
        {
            var More = true;
            while (More)
            {
                var Byte = (byte)(Value & 0x7F);
                var Signed = (Byte & 0x40) != 0;
                Value >>= 7;

                if ((Value == 0 && !Signed) || (Value == -1 && Signed)) More = false;
                else Byte |= 0x80;
                EmitByte(Byte);
            }
        }
    }
}