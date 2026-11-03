namespace Model.PLC
{
    /// <summary>
    /// 一台 PLC 的连接配置，字段名与 appsettings.json 的 Plcs 节保持一致。
    /// 当前配置 7 台 RGV 小车（车1~车7）+ 4 台拆垛段 PLC（1~4 段，型号 S7-1500），
    /// 设备增减时在 Plcs 数组里追加/删除即可，接口与后台重连服务都会自动生效。
    /// </summary>
    public class PlcConfig
    {
        // 设备编码，如 RGV01、PLC1，接口和日志里用它区分设备
        public string Name { get; set; } = "";

        // 展示名称，如 RGV小车1、拆垛PLC1（61xx/62xx段）
        public string DisplayName { get; set; } = "";

        // 设备类型：RGV=小车，Destacker=拆垛段 PLC
        public string DeviceType { get; set; } = "";

        // 设备号（协议里的编号），从 1 开始
        public int DeviceNo { get; set; } = 0;

        // PLC 的 IP 地址。现场地址未确定时保持空字符串：
        // 客户端不会对空地址发起 TCP 连接，后台重连服务会直接跳过，等地址补充后自动生效。
        public string Ip { get; set; } = "";

        // 通讯协议，目前只支持 S7
        public string Protocol { get; set; } = "S7";

        // S7.Net 的 CpuType 枚举名：S71200 / S71500 / S7300 / S7400 / S7200 / S7200Smart
        public string CpuType { get; set; } = "S71200";

        // 端口，S7 协议默认 102
        public int Port { get; set; } = 102;

        // 机架号
        public int Rack { get; set; } = 0;

        // 槽号
        public int Slot { get; set; } = 1;

        // 上位机读取 PLC 数据的 DB 块号（PLC → WCS）。
        // 拆垛 PLC 约定为 DB600，0 表示尚未配置。
        public int ReadDbNumber { get; set; } = 0;

        // 上位机写入 PLC 数据的 DB 块号（WCS → PLC）。
        // 拆垛 PLC 约定为 DB500，0 表示尚未配置。
        public int WriteDbNumber { get; set; } = 0;

        // 读/写超时（毫秒），超过该时间无响应即视为断线
        public int Timeout { get; set; } = 3000;
    }
}
