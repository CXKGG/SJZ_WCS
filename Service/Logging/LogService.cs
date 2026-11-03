using Interface.Logs;
using Microsoft.Extensions.Logging;
using Model.Entities;
using Model.Logs;
using SqlSugar;

namespace Service.Logging
{
    /// <summary>
    /// 日志服务实现：同步直接写 SysLog 表。
    /// <para>
    /// 之前是"内存队列 + 后台服务批量落库"，为了少一个后台服务、少两层抽象，现在改成写库即落盘；
    /// 写库失败只记录进程日志，不向调用方抛异常（日志失败不应该影响业务主流程）。
    /// </para>
    /// </summary>
    public class LogService : ILogService
    {
        // 各列长度上限，和 SysLog 实体的 SugarColumn(Length=...) 保持一致，超长先截断再落库
        private const int ModuleMaxLength = 100;
        private const int ActionMaxLength = 200;
        private const int ContentMaxLength = 500;
        private const int DetailMaxLength = 8000;
        private const int TargetMaxLength = 200;
        private const int UserMaxLength = 64;
        private const int IpMaxLength = 64;

        private readonly ISqlSugarClient _db;
        private readonly ILogger<LogService> _logger;

        /// <summary>
        /// 构造日志服务。
        /// </summary>
        /// <param name="db">SqlSugar 客户端</param>
        /// <param name="logger">进程日志（写库失败时记录，避免业务被日志拖垮）</param>
        public LogService(ISqlSugarClient db, ILogger<LogService> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>写一条日志（同步落库）。</summary>
        /// <param name="entry">日志内容</param>
        public void Write(LogEntry entry)
        {
            SysLog row = ToEntity(entry);
            try
            {
                _db.Insertable(row).ExecuteCommand();
            }
            catch (Exception ex)
            {
                /// 日志表写失败不能让业务流程跟着失败：这里只记录到进程日志，便于本机排查
                _logger.LogError(ex, "写日志失败：{Category}/{Action}", entry.Category, entry.Action);
            }
        }

        /// <summary>写系统日志。</summary>
        public void WriteSystem(LogRiskLevel riskLevel, string module, string action, string content,
            string? detail = null, string? target = null, bool success = true, long elapsedMs = 0,
            string? ipAddress = null)
        {
            LogEntry entry = CreateEntry(LogCategory.System, riskLevel, module, action, content);
            entry.Detail = detail ?? "";
            entry.Target = target ?? "";
            entry.IpAddress = ipAddress ?? "";
            entry.Success = success;
            entry.ElapsedMs = elapsedMs;
            Write(entry);
        }

        /// <summary>写 WMS 交互日志。</summary>
        public void WriteWms(LogRiskLevel riskLevel, string module, string action, string content,
            string? detail = null, string? target = null, bool success = true, long elapsedMs = 0,
            string? ipAddress = null)
        {
            LogEntry entry = CreateEntry(LogCategory.Wms, riskLevel, module, action, content);
            entry.Detail = detail ?? "";
            entry.Target = target ?? "";
            entry.IpAddress = ipAddress ?? "";
            entry.Success = success;
            entry.ElapsedMs = elapsedMs;
            Write(entry);
        }

        /// <summary>写 RGV 设备交互日志。</summary>
        public void WriteRgv(LogRiskLevel riskLevel, string module, string action, string content,
            string? detail = null, string? target = null, bool success = true, long elapsedMs = 0,
            string? ipAddress = null)
        {
            LogEntry entry = CreateEntry(LogCategory.Rgv, riskLevel, module, action, content);
            entry.Detail = detail ?? "";
            entry.Target = target ?? "";
            entry.IpAddress = ipAddress ?? "";
            entry.Success = success;
            entry.ElapsedMs = elapsedMs;
            Write(entry);
        }

        /// <summary>写 PLC 设备交互日志。</summary>
        public void WritePlc(LogRiskLevel riskLevel, string module, string action, string content,
            string? detail = null, string? target = null, bool success = true, long elapsedMs = 0,
            string? ipAddress = null)
        {
            LogEntry entry = CreateEntry(LogCategory.Plc, riskLevel, module, action, content);
            entry.Detail = detail ?? "";
            entry.Target = target ?? "";
            entry.IpAddress = ipAddress ?? "";
            entry.Success = success;
            entry.ElapsedMs = elapsedMs;
            Write(entry);
        }

        /// <summary>写用户操作日志。</summary>
        public void WriteUserOperation(LogRiskLevel riskLevel, string module, string action, string content,
            string? detail = null, string? target = null, string? userId = null, string? userName = null,
            string? ipAddress = null, bool success = true, long elapsedMs = 0)
        {
            LogEntry entry = CreateEntry(LogCategory.UserOperation, riskLevel, module, action, content);
            entry.Detail = detail ?? "";
            entry.Target = target ?? "";
            entry.UserId = userId ?? "";
            entry.UserName = userName ?? "";
            entry.IpAddress = ipAddress ?? "";
            entry.Success = success;
            entry.ElapsedMs = elapsedMs;
            Write(entry);
        }

        /// <summary>写异常日志。</summary>
        public void WriteException(LogCategory category, LogRiskLevel riskLevel, string module, string action,
            string content, Exception exception, string? target = null)
        {
            LogEntry entry = CreateEntry(category, riskLevel, module, action, content);
            entry.Detail = exception.ToString();
            entry.Target = target ?? "";
            entry.Success = false;
            Write(entry);
        }

        /// <summary>按分类/等级/模块等固定三项创建日志对象，分类方法只补上下文。</summary>
        private static LogEntry CreateEntry(LogCategory category, LogRiskLevel riskLevel,
            string module, string action, string content)
        {
            LogEntry entry = LogEntry.Create(category, riskLevel, module, action, content);
            return entry;
        }

        /// <summary>内存日志对象转数据库实体（长度超限截断，时间取当前）。</summary>
        private static SysLog ToEntity(LogEntry entry)
        {
            SysLog row = new SysLog();
            row.logcategory = entry.Category;
            row.risklevel = entry.RiskLevel;
            row.module = Truncate(entry.Module, ModuleMaxLength);
            row.action = Truncate(entry.Action, ActionMaxLength);
            row.content = Truncate(entry.Content, ContentMaxLength);
            row.detail = Truncate(entry.Detail, DetailMaxLength);
            row.target = Truncate(entry.Target, TargetMaxLength);
            row.userid = Truncate(entry.UserId, UserMaxLength);
            row.username = Truncate(entry.UserName, UserMaxLength);
            row.ipaddress = Truncate(entry.IpAddress, IpMaxLength);
            row.success = entry.Success;
            row.elapsedms = (int)entry.ElapsedMs;
            row.createdtime = entry.CreatedTime ?? DateTime.Now;
            return row;
        }

        /// <summary>按最大长度截断字符串，null 统一变成空串。</summary>
        private static string Truncate(string? value, int maxLength)
        {
            if (value == null)
            {
                return "";
            }

            if (value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(0, maxLength);
        }
    }
}
