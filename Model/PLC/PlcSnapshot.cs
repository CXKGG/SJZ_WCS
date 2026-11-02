using Model.Protocol;

namespace Model.PLC
{
    /// <summary>
    /// 一台 PLC 的实时数据快照（由后台轮询服务写入，任务调度只读内存，不再直接读 S7）。
    /// DB600 是 PLC → WCS 的状态块（PLC 主动推送，WCS 只读）；
    /// DB500 是 WCS → PLC 的指令块（本快照读回的内容用于核对下发结果）。
    /// </summary>
    public class PlcSnapshot
    {
        /// <summary>设备编码（PlcConfig.Name）</summary>
        public string Plccode { get; set; } = "";

        /// <summary>采集时连接是否正常</summary>
        public bool Connected { get; set; }

        /// <summary>每次更新自增，用于判断快照是否已刷新</summary>
        public long Version { get; set; }

        /// <summary>最近一次更新时间（本机时间）</summary>
        public DateTime UpdatedAt { get; set; }

        /// <summary>DB600 解析结果；读取失败时为 null</summary>
        public DestackerDb600? Db600 { get; set; }

        /// <summary>DB600 最近一次读取成功的时间</summary>
        public DateTime? Db600Time { get; set; }

        /// <summary>DB600 是否读取成功</summary>
        public bool Db600Ok { get; set; }

        /// <summary>DB600 最近一次读取失败的说明</summary>
        public string Db600Error { get; set; } = "";

        /// <summary>DB500 解析结果；读取失败时为 null</summary>
        public DestackerDb500? Db500 { get; set; }

        /// <summary>DB500 最近一次读取成功的时间</summary>
        public DateTime? Db500Time { get; set; }

        /// <summary>DB500 是否读取成功</summary>
        public bool Db500Ok { get; set; }

        /// <summary>DB500 最近一次读取失败的说明</summary>
        public string Db500Error { get; set; } = "";
    }
}
