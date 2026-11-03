namespace Model.PLC
{
    /// <summary>读取某个 DB 块的结果</summary>
    public class PlcReadResult
    {
        // 是否读取成功
        public bool Success { get; set; }

        // 读到的原始字节（失败时为空数组）
        public byte[] Data { get; set; } = Array.Empty<byte>();

        // 失败原因（如连接超时、PLC 返回错误）
        public string ErrorMessage { get; set; } = "";
    }
}
