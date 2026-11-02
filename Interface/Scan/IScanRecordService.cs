using Model.Dtos;

namespace Interface.Scan
{
    /// <summary>
    /// 扫码上报服务：接收上位机（扫码端）上报的产品信息，校验后落 scanrecord 表。
    /// </summary>
    public interface IScanRecordService
    {
        /// <summary>
        /// 接收一条扫码上报：校验 5 个字段、按追溯码 + 内部管理码判重、写入 scanrecord 表。
        /// </summary>
        /// <param name="request">对端上报的 5 个字段</param>
        /// <param name="sourceIp">上报方 IP（Controller 从请求连接取值，可为空）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>处理结果（含错误码、记录主键、是否重复）</returns>
        Task<ScanReportResult> ReportAsync(ScanReportRequest request, string sourceIp,
            CancellationToken cancellationToken = default);
    }
}
