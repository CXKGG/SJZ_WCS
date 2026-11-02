namespace Model.PLC
{
    /// <summary>读取某个 DB 块的结果</summary>
    public class PlcReadResult
    {
        /// <summary>是否读取成功</summary>
        public bool Success { get; set; }

        /// <summary>读到的原始字节（失败时为空数组）</summary>
        public byte[] Data { get; set; } = Array.Empty<byte>();

        /// <summary>失败原因（如连接超时、PLC 返回错误）</summary>
        public string ErrorMessage { get; set; } = "";
    }
}
