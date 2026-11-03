namespace Model.PLC
{
    /// <summary>写入某个 DB 块的结果</summary>
    public class PlcWriteResult
    {
        // 是否写入成功
        public bool Success { get; set; }

        // 失败原因（如连接超时、PLC 拒绝写入）
        public string ErrorMessage { get; set; } = "";
        
    }
}
