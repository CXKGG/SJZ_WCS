using System.Text;

namespace Service.Protocol
{
    /// <summary>
    /// S7 数据区基础读写：按 DB 字节偏移直接读写，不做对象/反射转换。
    /// <para>规则与 TIA 一致：整型为大端；Bool 是某字节里的 1 个位；S7 String 是 [最大字符数][实际字符数] + ASCII，总长 = 2 + 最大字符数。</para>
    /// <para>调用方负责保证 offset 不越界；codec 的 Parse/ToBytes 已按协议长度校验。</para>
    /// </summary>
    public static class S7Codec
    {
        /// <summary>读 1 个位（Bool）：返回 data[byteOffset] 的第 bit 位（bit 0~7），该位为 1 时返回 true。</summary>
        public static bool ReadBool(byte[] data, int byteOffset, int bit)
        {
            return (data[byteOffset] & (1 << bit)) != 0;
        }

        /// <summary>写 1 个位（Bool）：value 为 true 时把第 bit 位置 1，否则清 0（不影响同字节其它位）。</summary>
        public static void WriteBool(byte[] data, int byteOffset, int bit, bool value)
        {
            if (value)
            {
                data[byteOffset] |= (byte)(1 << bit);
            }
            else
            {
                data[byteOffset] &= (byte)~(1 << bit);
            }
        }

        /// <summary>读 Int16（TIA Int，2 字节大端）：高字节在前。</summary>
        public static short ReadInt16(byte[] data, int byteOffset)
        {
            return (short)((data[byteOffset] << 8) | data[byteOffset + 1]);
        }

        /// <summary>写 Int16（2 字节大端）。</summary>
        public static void WriteInt16(byte[] data, int byteOffset, short value)
        {
            data[byteOffset] = (byte)(value >> 8);
            data[byteOffset + 1] = (byte)value;
        }

        /// <summary>读 Int32（TIA DInt，4 字节大端）。</summary>
        public static int ReadInt32(byte[] data, int byteOffset)
        {
            return (data[byteOffset] << 24) | (data[byteOffset + 1] << 16) | (data[byteOffset + 2] << 8) | data[byteOffset + 3];
        }

        /// <summary>写 Int32（4 字节大端）。</summary>
        public static void WriteInt32(byte[] data, int byteOffset, int value)
        {
            data[byteOffset] = (byte)(value >> 24);
            data[byteOffset + 1] = (byte)(value >> 16);
            data[byteOffset + 2] = (byte)(value >> 8);
            data[byteOffset + 3] = (byte)value;
        }

        /// <summary>
        /// 读 S7 String：位置 byteOffset 起是头 2 字节（最大字符数、实际字符数），随后 ASCII。
        /// 头字节不可信（最大长度异常或实际长度 &gt; 最大长度）时，按 NUL 截断兜底，避免读出 PLC 未初始化时的脏数据。
        /// </summary>
        public static string ReadString(byte[] data, int byteOffset, int maxLength)
        {
            if (maxLength <= 0)
            {
                return "";
            }

            int declaredMax = data[byteOffset];
            int actualLength = data[byteOffset + 1];
            int length;
            if (declaredMax > 0 && declaredMax <= maxLength && actualLength <= maxLength)
            {
                length = actualLength;
            }
            else
            {
                length = 0;
                while (length < maxLength && data[byteOffset + 2 + length] != 0)
                {
                    length++;
                }
            }

            return Encoding.ASCII.GetString(data, byteOffset + 2, length).TrimEnd('\0');
        }

        /// <summary>写 S7 String：头字节写最大字符数与实际字符数，内容取 ASCII 前 maxLength 个字符，不足补 0。</summary>
        public static void WriteString(byte[] data, int byteOffset, string? value, int maxLength)
        {
            if (maxLength <= 0)
            {
                return;
            }

            byte[] bytes = Encoding.ASCII.GetBytes(value ?? "");
            int length = Math.Min(bytes.Length, maxLength);
            data[byteOffset] = (byte)maxLength;
            data[byteOffset + 1] = (byte)length;
            Array.Copy(bytes, 0, data, byteOffset + 2, length);
        }
    }
}
