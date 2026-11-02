using System.Globalization;

namespace Service.Protocol
{
    /// <summary>
    /// 十六进制编解码工具：接口层用字符串传 DB 原始字节（便于人工排查报文），这里统一做转换。
    /// </summary>
    public static class HexCodec
    {
        /// <summary>字节数组转十六进制字符串（大写、无分隔符，如 "01A2FF"）。</summary>
        public static string ToHex(byte[] data)
        {
            return Convert.ToHexString(data);
        }

        /// <summary>
        /// 十六进制字符串转字节数组；允许空格、短横线、0x 前缀分隔。
        /// 输入非法时抛 <see cref="FormatException"/>，由控制器转成 400。
        /// </summary>
        /// <param name="hex">十六进制文本，空字符串表示空数组</param>
        public static byte[] FromHex(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
            {
                return Array.Empty<byte>();
            }

            string normalized = hex.Replace("0x", "", StringComparison.OrdinalIgnoreCase);
            normalized = normalized.Replace(" ", "", StringComparison.Ordinal);
            normalized = normalized.Replace("-", "", StringComparison.Ordinal);
            normalized = normalized.Trim();

            if (normalized.Length % 2 != 0)
            {
                throw new FormatException("十六进制字符串长度必须是偶数（每 2 个字符表示 1 个字节）。");
            }

            byte[] result = new byte[normalized.Length / 2];
            for (int i = 0; i < result.Length; i++)
            {
                string part = normalized.Substring(i * 2, 2);
                bool parsed = byte.TryParse(part, NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out byte value);
                if (!parsed)
                {
                    throw new FormatException($"十六进制字符串在第 {i * 2} 个字符处非法：\"{part}\"。");
                }

                result[i] = value;
            }

            return result;
        }
    }
}
