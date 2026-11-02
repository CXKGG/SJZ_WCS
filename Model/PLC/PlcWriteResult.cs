namespace Model.PLC
{
    /// <summary>写入某个 DB 块的结果</summary>
    public class PlcWriteResult
    {
        /// <summary>是否写入成功</summary>
        public bool Success { get; set; }

        /// <summary>失败原因（如连接超时、PLC 拒绝写入）</summary>
        public string ErrorMessage { get; set; } = "";
        
    }
}
