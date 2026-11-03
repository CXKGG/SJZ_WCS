using Interface.Logs;
using Interface.Scan;
using Microsoft.AspNetCore.Mvc;
using Model.Dtos;
using Model.Logs;

namespace SJZ_WCS.Controllers
{
    /// <summary>
    /// 扫码上报接口：上位机（扫码端）主动调用，上报生产日期、产品批号、有效日期、产品追溯码、内部管理码。
    /// <para>
    /// 协议：HTTP POST + JSON，成功 200，参数错误 400，服务异常 500。
    /// 重复上报（同追溯码 + 同内部管理码）返回 200，duplicate=true，不重复入库。
    /// </para>
    /// </summary>
    [ApiController]
    [Route("api/scan")]
    public class ScanController : ControllerBase
    {
        private readonly IScanRecordService _scanRecordService;
        private readonly ILogService _logService;

        public ScanController(IScanRecordService scanRecordService, ILogService logService)
        {
            _scanRecordService = scanRecordService;
            _logService = logService;
        }

        /// <summary>
        /// 接收一条扫码上报。
        /// POST /api/scan/report
        /// </summary>
        /// <param name="request">5 个字段的请求体</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>标准响应：{ code, success, message, data }</returns>
        [HttpPost("report")]
        public async Task<IActionResult> Report([FromBody] ScanReportRequest request,
            CancellationToken cancellationToken)
        {
            string sourceIp = "";
            if (HttpContext.Connection.RemoteIpAddress != null)
            {
                /// 记录系统日志时，记录对端 IP（扫码端）便于追溯
                sourceIp = HttpContext.Connection.RemoteIpAddress.ToString();
            }
            // 调用服务层处理扫码上报，返回结果（成功或失败）
            ScanReportResult result = await _scanRecordService.ReportAsync(request, sourceIp, cancellationToken);

            if (!result.Success)
            {
                /// 扫码上报是扫码端与 WCS 的接口交互，记入系统日志（WMS 交互尚未对接，不能记 WMS 分类）
                if (!result.IsSystemError)
                {
                    _logService.WriteSystem(LogRiskLevel.Medium, "扫码上报", "POST /api/scan/report",
                        "扫码上报参数错误：" + result.Message, ipAddress: sourceIp, success: false);
                }

                ScanReportResponse errorResponse = new ScanReportResponse();
                errorResponse.Success = false;
                errorResponse.Code = result.Code;
                errorResponse.Message = result.Message;

                if (result.IsSystemError)
                {
                    return StatusCode(500, errorResponse);
                }
                return BadRequest(errorResponse);
            }

            // 成功：记低风险系统日志（detail 保留报文关键字段，便于追溯）
            string detail = "productionDate=" + request.ProductionDate
                + "; productBatchNo=" + request.ProductBatchNo
                + "; expiryDate=" + request.ExpiryDate
                + "; traceCode=" + request.TraceCode
                + "; internalCode=" + request.InternalCode;
            _logService.WriteSystem(LogRiskLevel.Low, "扫码上报", "POST /api/scan/report",
                "接收扫码信息，count=" + result.Count + "，重复=" + result.Duplicate,
                detail, target: request.InternalCode, ipAddress: sourceIp, success: true);

            ScanReportData data = new ScanReportData();
            data.Count = result.Count;
            /// 接收时间按 yyyy-MM-dd HH:mm:ss 返回（对端便于直接展示，不带时区和小数秒）
            data.Date = result.ReceivedAt.ToString("yyyy-MM-dd HH:mm:ss");
            data.Duplicate = result.Duplicate;

            ScanReportResponse response = new ScanReportResponse();
            response.Success = true;
            response.Code = result.Code;
            response.Message = "ok";
            response.Data = data;

            return Ok(response);
        }
    }
}
