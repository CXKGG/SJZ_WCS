using SqlSugar;

namespace Model.Entities
{
    /// <summary>
    /// PLC 发送缓存表：WCS 要下发给 PLC 的 DB 块先落库，再由后台服务按顺序发送。
    /// 网络抖动或 PLC 掉线时不丢命令，支持重试、查询、手动补发。
    /// </summary>
    public class PlcSendMsg
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public long id { get; set; }

        public string plccode { get; set; } = "";       // 目标设备编号，如 PLC1
        public int dbnumber { get; set; }               // 目标 DB 号，如 500
        public int startbyte { get; set; }              // 起始字节
        public string payload { get; set; } = "";       // 要写入的字节内容（hex 字符串，与接口保持一致便于排查）
        public string businesstype { get; set; } = "";  // 业务类型，如"拆垛任务下发"（可空）
        public string businessid { get; set; } = "";    // 关联业务号/任务号（可空）
        public int status { get; set; }                 // 发送状态：0待发送 1已发送 2发送失败待重试 3已放弃
        public int retrycount { get; set; }             // 已重试次数
        public int maxretry { get; set; } = 3;          // 最大重试次数
        public string lasterror { get; set; } = "";     // 最近一次失败原因
        public DateTime createtime { get; set; }        // 创建时间

        // SqlSugar 建表时默认所有列 NOT NULL；要允许为空必须显式 IsNullable = true
        [SugarColumn(IsNullable = true)]
        public DateTime? sendtime { get; set; }         // 发送成功时间（未发送为空）

        public DateTime updatetime { get; set; }        // 最后更新时间
    }
}
