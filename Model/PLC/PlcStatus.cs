namespace Model.PLC
{
    /// <summary>一台 PLC 的实时状态快照，供接口/界面展示</summary>
    public class PlcStatus
    {
        public string Name { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string DeviceType { get; set; } = "";
        public int DeviceNo { get; set; }
        public string Ip { get; set; } = "";

        /// <summary>通讯协议</summary>
        public string Protocol { get; set; } = "";

        public string CpuType { get; set; } = "";
        public int Port { get; set; }
        public int Rack { get; set; }
        public int Slot { get; set; }

        /// <summary>上位机读取 PLC 的 DB 块号（PLC → WCS）</summary>
        public int ReadDbNumber { get; set; }

        /// <summary>上位机写入 PLC 的 DB 块号（WCS → PLC）</summary>
        public int WriteDbNumber { get; set; }

        /// <summary>当前是否已连接</summary>
        public bool IsConnected { get; set; }

        /// <summary>最近一次连接失败/读写失败的报错</summary>
        public string LastError { get; set; } = "";

        /// <summary>最近一次连接成功的时间</summary>
        public DateTime? LastConnectTime { get; set; }
    }
}
