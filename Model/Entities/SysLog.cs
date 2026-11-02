using Model.Logs;
using SqlSugar;

namespace Model.Entities
{
    /// <summary>
    /// 系统日志表：5 类业务日志（系统/WMS/RGV/PLC/用户操作）统一落这一张表，
    /// 通过 <see cref="logcategory"/> 区分分类、<see cref="risklevel"/> 区分风险等级，
    /// 这样新增日志分类只需要扩展枚举，不用加表；单表查询也便于跨分类追溯同一时间点发生的事。
    /// </summary>
    // 索引：日志列表按时间倒序翻页 + 按分类筛选，这两个查询最频繁，建索引避免全表扫。
    [SugarIndex("idx_syslog_createdtime", nameof(createdtime), OrderByType.Desc)]
    [SugarIndex("idx_syslog_logcategory", nameof(logcategory), OrderByType.Asc)]
    public class SysLog
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public long id { get; set; }

        /// <summary>日志分类（LogCategory 枚举，落库为 int：1系统 2WMS 3RGV 4PLC 5用户操作）</summary>
        public LogCategory logcategory { get; set; } = LogCategory.System;

        /// <summary>风险等级（LogRiskLevel 枚举，落库为 int：1低 2中 3高）</summary>
        public LogRiskLevel risklevel { get; set; } = LogRiskLevel.Low;

        /// <summary>功能模块，如 API、PLC/PLC1、RGV/RGV01、任务调度</summary>
        [SugarColumn(Length = 100)]
        public string module { get; set; } = "";

        /// <summary>动作名称，如 Connect、ReadDb、POST /api/plc/PLC1/db/500</summary>
        [SugarColumn(Length = 200)]
        public string action { get; set; } = "";

        /// <summary>日志摘要</summary>
        [SugarColumn(Length = 500)]
        public string content { get; set; } = "";

        /// <summary>详细信息（报文/堆栈/参数），用 text 类型避免截断</summary>
        // ColumnDataType = "text"：显式指定 MySQL 列类型，避免长异常堆栈被 varchar(255) 截断
        [SugarColumn(ColumnDataType = "text")]
        public string detail { get; set; } = "";

        /// <summary>操作对象（设备名、任务号等）</summary>
        [SugarColumn(Length = 200)]
        public string target { get; set; } = "";

        /// <summary>用户编号</summary>
        [SugarColumn(Length = 64)]
        public string userid { get; set; } = "";

        /// <summary>用户名</summary>
        [SugarColumn(Length = 64)]
        public string username { get; set; } = "";

        /// <summary>客户端 IP</summary>
        [SugarColumn(Length = 64)]
        public string ipaddress { get; set; } = "";

        /// <summary>是否成功</summary>
        public bool success { get; set; } = true;

        /// <summary>耗时（毫秒）</summary>
        public int elapsedms { get; set; }

        /// <summary>日志时间（落库时由写入服务补当前时间）</summary>
        public DateTime createdtime { get; set; } = DateTime.Now;
    }
}
