using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Interface.Logs;
using Interface.Scan;
using Model.Dtos;
using Model.Entities;
using Model.Logs;
using SqlSugar;

namespace Service.Scan
{
    /// <summary>
    /// 扫码上报服务实现：参数校验 → 判重 → 入库。
    /// 日志由 Controller 统一记（WMS 交互日志），这里只负责业务。
    /// </summary>
    public class ScanRecordService : IScanRecordService
    {
        /// <summary>业务码：成功（与 HTTP 200 一致）</summary>
        private const int CodeSuccess = 200;

        /// <summary>业务码：请求参数错误（与 HTTP 400 一致）</summary>
        private const int CodeBadRequest = 400;

        /// <summary>业务码：服务内部异常（与 HTTP 500 一致）</summary>
        private const int CodeSystemError = 500;

        /// <summary>日期允许的几种输入写法（对端可能给 2026-10-28 或 20261028）</summary>
        private static readonly string[] DateFormats = { "yyyy-MM-dd", "yyyyMMdd", "yyyy/MM/dd", "yyyy.MM.dd" };

        private readonly ISqlSugarClient _db;
        private readonly ILogService _logService;

        public ScanRecordService(ISqlSugarClient db, ILogService logService)
        {
            _db = db;
            _logService = logService;
        }

        /// <summary>
        /// 处理扫码上报请求
        /// </summary>
        /// <param name="request"></param>
        /// <param name="sourceIp"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<ScanReportResult> ReportAsync(ScanReportRequest request, string sourceIp,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            // 1. 必填校验
            string? requiredError = CheckRequired(request);
            if (requiredError != null)
            {
                return Fail(CodeBadRequest, requiredError);
            }

            // 2. 去空格（对端可能带空格，先统一清掉再校验格式和长度）
            string productionDate = request.ProductionDate.Trim();
            string productBatchNo = request.ProductBatchNo.Trim();
            string expiryDate = request.ExpiryDate.Trim();
            string traceCode = request.TraceCode.Trim();
            string internalCode = request.InternalCode.Trim();

            // 3. 格式/长度校验
            string? formatError = CheckFormat(productionDate, productBatchNo, expiryDate, traceCode, internalCode);
            if (formatError != null)
            {
                return Fail(CodeBadRequest, formatError);
            }

            // 4. 日期统一成 YYYY-MM-DD 再入库，避免同一日期出现多种写法
            //    （前面 CheckFormat 已校验过格式，此处正常不会为 null）
            string? normalizedProductionDate = NormalizeDate(productionDate);
            string? normalizedExpiryDate = NormalizeDate(expiryDate);
            if (normalizedProductionDate == null || normalizedExpiryDate == null)
            {
                // 兜底：格式校验通过但仍无法转换，返回参数错误，避免把 null 写进库
                return Fail(CodeBadRequest, "日期格式应为 YYYY-MM-DD");
            }

            try
            {
                // 5. 判重：同一追溯码 + 内部管理码视为同一条记录（对端重试时不重复入库）
                ScanRecord existing = await _db.Queryable<ScanRecord>()
                    .FirstAsync(x => x.tracecode == traceCode && x.internalcode == internalCode);

                if (existing != null)
                {
                    // 重复上报：count=0、回原记录时间，方便对端判断本次没有新增
                    return new ScanReportResult
                    {
                        Success = true,
                        Code = CodeSuccess,
                        Message = "ok",
                        Count = 0,
                        ReceivedAt = existing.receivedat,
                        Duplicate = true
                    };
                }

                // 6. 入库
                ScanRecord record = new ScanRecord();
                record.productiondate = normalizedProductionDate;
                record.productbatchno = productBatchNo;
                record.expirydate = normalizedExpiryDate;
                record.tracecode = traceCode;
                record.internalcode = internalCode;
                record.sourceip = sourceIp;
                record.receivedat = DateTime.Now;

                // ExecuteReturnBigIdentityAsync：插入并返回自增主键
                await _db.Insertable(record).ExecuteReturnBigIdentityAsync();

                return new ScanReportResult
                {
                    Success = true,
                    Code = CodeSuccess,
                    Message = "ok",
                    Count = 1,
                    ReceivedAt = record.receivedat,
                    Duplicate = false
                };
            }
            catch (Exception exception)
            {
                // 数据库/未知异常：记高风险系统日志，返回 500（Controller 转成 HTTP 500）
                _logService.WriteException(LogCategory.System, LogRiskLevel.High, "扫码上报",
                    "POST /api/scan/report", "扫码上报入库失败", exception, target: internalCode);

                return new ScanReportResult
                {
                    Success = false,
                    Code = CodeSystemError,
                    Message = "服务内部异常，请稍后重试",
                    IsSystemError = true
                };
            }
        }

        /// <summary>
        /// 检查 5 个字段是否都有值；返回第一条错误说明，全部通过返回 null。
        /// </summary>
        private static string? CheckRequired(ScanReportRequest request)
        {
            if (request == null)
            {
                return "请求体不能为空";
            }
            if (string.IsNullOrWhiteSpace(request.ProductionDate))
            {
                return "生产日期 productionDate 不能为空";
            }
            if (string.IsNullOrWhiteSpace(request.ProductBatchNo))
            {
                return "产品批号 productBatchNo 不能为空";
            }
            if (string.IsNullOrWhiteSpace(request.ExpiryDate))
            {
                return "有效日期 expiryDate 不能为空";
            }
            if (string.IsNullOrWhiteSpace(request.TraceCode))
            {
                return "产品追溯码 traceCode 不能为空";
            }
            if (string.IsNullOrWhiteSpace(request.InternalCode))
            {
                return "内部管理码 internalCode 不能为空";
            }
            return null;
        }

        /// <summary>
        /// 检查日期格式与字段长度；返回错误说明，全部通过返回 null。
        /// </summary>
        private static string? CheckFormat(string productionDate, string productBatchNo, string expiryDate,
            string traceCode, string internalCode)
        {
            if (NormalizeDate(productionDate) == null)
            {
                return "生产日期格式应为 YYYY-MM-DD";
            }
            if (NormalizeDate(expiryDate) == null)
            {
                return "有效日期格式应为 YYYY-MM-DD";
            }
            if (productBatchNo.Length > 32)
            {
                return "产品批号长度不能超过 32";
            }
            if (traceCode.Length > 64)
            {
                return "产品追溯码长度不能超过 64";
            }
            if (internalCode.Length > 64)
            {
                return "内部管理码长度不能超过 64";
            }
            return null;
        }

        /// <summary>
        /// 把日期统一成 YYYY-MM-DD；无法识别时返回 null。
        /// </summary>
        private static string? NormalizeDate(string value)
        {
            DateTime parsed;
            // 允许的几种输入写法（对端可能给 2026-10-28 或 20261028）
            bool ok = DateTime.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out parsed);
            if (!ok)
            {
                return null;
            }
            // yyyy-MM-dd：固定输出格式，保证入库写法一致
            return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 生成失败结果（参数类错误，HTTP 400）。
        /// </summary>
        private static ScanReportResult Fail(int code, string message)
        {
            return new ScanReportResult
            {
                Success = false,
                Code = code,
                Message = message
            };
        }
    }
}