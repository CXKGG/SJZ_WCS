using SqlSugar;

namespace Model.Entities
{
    /// <summary>
    /// 扫码上报记录表：上位机（扫码端）上报的产品信息落这张表，字段与上报报文一一对应。
    /// 只接收 5 个业务字段，另外由服务器补充来源 IP 和接收时间，不改对端报文。
    /// </summary>
    /// 索引：按追溯码、内部管理码查记录最常用，建索引避免全表扫。
    [SugarIndex("idx_scanrecord_tracecode", nameof(tracecode), OrderByType.Asc)]
    [SugarIndex("idx_scanrecord_internalcode", nameof(internalcode), OrderByType.Asc)]
    public class ScanRecord
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public long id { get; set; }

        // 生产日期（统一存 YYYY-MM-DD）
        [SugarColumn(Length = 10)]
        public string productiondate { get; set; } = "";

        // 产品批号
        [SugarColumn(Length = 32)]
        public string productbatchno { get; set; } = "";

        // 有效日期（统一存 YYYY-MM-DD）
        [SugarColumn(Length = 10)]
        public string expirydate { get; set; } = "";

        // 产品追溯码
        [SugarColumn(Length = 64)]
        public string tracecode { get; set; } = "";

        // 内部管理码
        [SugarColumn(Length = 64)]
        public string internalcode { get; set; } = "";

        // 上报方 IP（服务器从请求连接里取）
        [SugarColumn(Length = 64)]
        public string sourceip { get; set; } = "";

        // 服务器接收时间
        public DateTime receivedat { get; set; } = DateTime.Now;
    }
}
