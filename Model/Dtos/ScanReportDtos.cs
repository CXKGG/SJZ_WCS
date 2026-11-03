namespace Model.Dtos
{
    /// <summary>
    /// 扫码上报请求体：对端固定只传这 5 个字段（字段名按 JSON 驼峰：productionDate 等）。
    /// </summary>
    public class ScanReportRequest
    {
        // 生产日期，格式 YYYY-MM-DD（也兼容 YYYYMMDD）
        public string ProductionDate { get; set; } = "";

        // 产品批号，最长 32
        public string ProductBatchNo { get; set; } = "";

        // 有效日期，格式 YYYY-MM-DD（也兼容 YYYYMMDD）
        public string ExpiryDate { get; set; } = "";

        // 产品追溯码，最长 64
        public string TraceCode { get; set; } = "";

        // 内部管理码，最长 64
        public string InternalCode { get; set; } = "";
    }

    /// <summary>
    /// 扫码上报处理结果：服务层返回，Controller 再转成对端看到的响应体。
    /// </summary>
    public class ScanReportResult
    {
        // 是否成功
        public bool Success { get; set; }

        // 业务码：200 成功、400 参数错误、500 系统异常（与 HTTP 状态码一致）
        public int Code { get; set; }

        // 错误说明（成功时为 ok）
        public string Message { get; set; } = "";

        // 本次接收的条数：新增入库为 1，重复上报为 0
        public int Count { get; set; }

        // 记录接收时间（新增为本次时间，重复为原记录时间）
        public DateTime ReceivedAt { get; set; }

        // 是否重复上报（按追溯码 + 内部管理码判重）
        public bool Duplicate { get; set; }

        // true 表示服务内部异常（HTTP 500），false 表示参数类错误（HTTP 400）
        public bool IsSystemError { get; set; }
    }

    /// <summary>
    /// 扫码上报响应体（返回给对端的 JSON 结构）：{ code, success, message, data }。
    /// </summary>
    public class ScanReportResponse
    {
        // 业务码：200 成功、400 参数错误、500 系统异常
        public int Code { get; set; }

        // 是否成功
        public bool Success { get; set; }

        // 提示信息，成功为 ok，失败为具体原因
        public string Message { get; set; } = "";

        // 数据部分（失败时为 null）
        public ScanReportData? Data { get; set; }
    }

    /// <summary>
    /// 扫码上报成功后返回的数据部分。
    /// </summary>
    public class ScanReportData
    {
        // 本次接收条数：新增入库为 1，重复上报为 0
        public int Count { get; set; }

        // 接收时间，格式 yyyy-MM-dd HH:mm:ss（含时分秒）
        public string Date { get; set; } = "";

        // 是否重复上报
        public bool Duplicate { get; set; }
    }
}
