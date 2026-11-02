namespace Model.Protocol
{
    /// <summary>
    /// 拆垛段 DB600 模型：PLC「发送上位机数据」，即 WCS 读取 PLC（PLC → WCS）。
    /// <para>
    /// 字段偏移来自《四药国龙拆垛PLC与上位机信息交互.docx》TIA 截图（PLC_1 [CPU 1511-1 PN] 程序块 DB600），
    /// 4 台拆垛 PLC（PLC1~PLC4，型号 1500）各一份同构 DB，DB 号均为 600。
    /// 数组数据在 TIA 里是 Array[0..1]，这里展开成 Xxx0 / Xxx1 两个属性。
    /// </para>
    /// <para>DB600 总长度 408 字节。</para>
    /// <para>本类只承载数据，字段偏移与类型在 Service/Protocol 下的编解码类中逐字段写明。</para>
    /// </summary>
    public class DestackerDb600
    {
        #region 北起段托盘输送交互信息（字节 0、32，各 32 字节）

        /// <summary>北起段托盘输送交互信息[0]：61 段（6101、6102、6103 工位），字节 0 起，长 32 字节。</summary>
        public DestackerFeedPutInfo Feed0 { get; set; } = new();

        /// <summary>北起段托盘输送交互信息[1]：62 段（6201、6202、6203 工位），字节 32 起，长 32 字节。</summary>
        public DestackerFeedPutInfo Feed1 { get; set; } = new();

        #endregion

        #region 出库至拆垛位（字节 64、84，各 20 字节）

        /// <summary>出库至拆垛位[0]：6105 / 6106 拆垛位，字节 64 起，长 20 字节。</summary>
        public DestackerOutboundPutInfo Outbound0 { get; set; } = new();

        /// <summary>出库至拆垛位[1]：6205 / 6206 拆垛位，字节 84 起，长 20 字节。</summary>
        public DestackerOutboundPutInfo Outbound1 { get; set; } = new();

        #endregion

        #region 拆垛完成托盘出（字节 104、132，各 28 字节）

        /// <summary>拆垛完成托盘出[0]：6105 / 6106 拆垛位，字节 104 起，长 28 字节。</summary>
        public DestackerUnstackDonePutInfo UnstackDone0 { get; set; } = new();

        /// <summary>拆垛完成托盘出[1]：6205 / 6206 拆垛位，字节 132 起，长 28 字节。</summary>
        public DestackerUnstackDonePutInfo UnstackDone1 { get; set; } = new();

        #endregion

        #region 垒托盘（字节 160、284，各 124 字节）

        /// <summary>垒托盘[0]：6107 叠盘机，字节 160 起，长 124 字节（含托盘号 1~15）。</summary>
        public DestackerStackPutInfo Stack0 { get; set; } = new();

        /// <summary>垒托盘[1]：6108 叠盘机，字节 284 起，长 124 字节（含托盘号 1~15）。</summary>
        public DestackerStackPutInfo Stack1 { get; set; } = new();

        #endregion
    }

    #region 北起段托盘输送交互信息数据结构

    /// <summary>
    /// 北起段托盘输送交互信息：拣选口请求出、出库口允许进盘 / 进盘完成，每段 32 字节。
    /// </summary>
    public class DestackerFeedPutInfo
    {
        /// <summary>拣选口请求出（字节偏移 0 第 0 位，Boolean；xx01 工位）。</summary>
        public bool PickingRequestOut { get; set; }

        /// <summary>拣选口托盘数量（字节偏移 2，Int16）。</summary>
        public short PickingPalletCount { get; set; }

        /// <summary>拣选口托盘号（字节偏移 4，S7 String[6]，共 8 字节）。</summary>
        public string PickingPalletNo { get; set; } = "";

        /// <summary>左出库口允许进盘（字节偏移 12 第 0 位，Boolean；xx02 工位）。</summary>
        public bool LeftInAllowed { get; set; }

        /// <summary>左出库口进盘完成（字节偏移 12 第 1 位，Boolean）。</summary>
        public bool LeftInDone { get; set; }

        /// <summary>左出库口托盘号（字节偏移 14，S7 String[6]，共 8 字节）。</summary>
        public string LeftPalletNo { get; set; } = "";

        /// <summary>右出库口允许进盘（字节偏移 22 第 0 位，Boolean；xx03 工位）。</summary>
        public bool RightInAllowed { get; set; }

        /// <summary>右出库口进盘完成（字节偏移 22 第 1 位，Boolean）。</summary>
        public bool RightInDone { get; set; }

        /// <summary>右出库口托盘号（字节偏移 24，S7 String[6]，共 8 字节）。</summary>
        public string RightPalletNo { get; set; } = "";
    }

    #endregion

    #region 出库至拆垛位数据结构

    /// <summary>
    /// 出库至拆垛位：请求出盘至 RGV、允许进盘、进盘完成，每段 20 字节。
    /// </summary>
    public class DestackerOutboundPutInfo
    {
        /// <summary>左侧请求出盘至 RGV（字节偏移 0 第 0 位，Boolean）。</summary>
        public bool LeftRequestOutToRgv { get; set; }

        /// <summary>左侧拆垛位允许进盘（字节偏移 0 第 1 位，Boolean）。</summary>
        public bool LeftInAllowed { get; set; }

        /// <summary>左侧拆垛位允许进盘完成（字节偏移 0 第 2 位，Boolean）。</summary>
        public bool LeftInDone { get; set; }

        /// <summary>左侧拆垛位托盘号（字节偏移 2，S7 String[6]，共 8 字节）。</summary>
        public string LeftPalletNo { get; set; } = "";

        /// <summary>右侧请求出盘至 RGV（字节偏移 10 第 0 位，Boolean）。</summary>
        public bool RightRequestOutToRgv { get; set; }

        /// <summary>右侧拆垛位允许进盘（字节偏移 10 第 1 位，Boolean）。</summary>
        public bool RightInAllowed { get; set; }

        /// <summary>右侧拆垛位允许进盘完成（字节偏移 10 第 2 位，Boolean）。</summary>
        public bool RightInDone { get; set; }

        /// <summary>右侧拆垛位托盘号（字节偏移 12，S7 String[6]，共 8 字节）。</summary>
        public string RightPalletNo { get; set; } = "";
    }

    #endregion

    #region 拆垛完成托盘出数据结构

    /// <summary>
    /// 拆垛完成托盘出：拆垛完成请求出 + 托盘类型 / 箱数 / 托盘号，每段 28 字节。
    /// 注意：右侧「请求出」在协议里是 Int（16 位），不是 Boolean，按 Int16 解析。
    /// </summary>
    public class DestackerUnstackDonePutInfo
    {
        /// <summary>左侧拆垛完成托盘请求出（字节偏移 0 第 0 位，Boolean）。</summary>
        public bool LeftRequestOut { get; set; }

        /// <summary>左侧托盘类型（字节偏移 2，Int16）。</summary>
        public short LeftPalletType { get; set; }

        /// <summary>左侧托盘箱数（字节偏移 4，Int16）。</summary>
        public short LeftBoxCount { get; set; }

        /// <summary>左侧托盘号（字节偏移 6，S7 String[6]，共 8 字节）。</summary>
        public string LeftPalletNo { get; set; } = "";

        /// <summary>右侧拆垛完成托盘请求出（字节偏移 14，Int16，不是 Boolean）。</summary>
        public short RightRequestOut { get; set; }

        /// <summary>右侧托盘类型（字节偏移 16，Int16）。</summary>
        public short RightPalletType { get; set; }

        /// <summary>右侧托盘箱数（字节偏移 18，Int16）。</summary>
        public short RightBoxCount { get; set; }

        /// <summary>右侧托盘号（字节偏移 20，S7 String[6]，共 8 字节）。</summary>
        public string RightPalletNo { get; set; } = "";
    }

    #endregion

    #region 垒托盘数据结构

    /// <summary>
    /// 垒托盘：空盘进盘握手 + 托盘数量 + 15 个托盘号，长 124 字节。
    /// </summary>
    public class DestackerStackPutInfo
    {
        /// <summary>空盘允许进盘（字节偏移 0 第 0 位，Boolean）。</summary>
        public bool EmptyPalletInAllowed { get; set; }

        /// <summary>空盘进盘完成（字节偏移 0 第 1 位，Boolean）。</summary>
        public bool EmptyPalletInDone { get; set; }

        /// <summary>空盘满请求出盘（字节偏移 0 第 2 位，Boolean）。</summary>
        public bool EmptyPalletFullRequestOut { get; set; }

        /// <summary>托盘数量（字节偏移 2，Int16）。</summary>
        public short PalletCount { get; set; }

        /// <summary>托盘号 1（字节偏移 4，S7 String[6]，共 8 字节）。</summary>
        public string PalletNo1 { get; set; } = "";

        /// <summary>托盘号 2（字节偏移 12，S7 String[6]，共 8 字节）。</summary>
        public string PalletNo2 { get; set; } = "";

        /// <summary>托盘号 3（字节偏移 20，S7 String[6]，共 8 字节）。</summary>
        public string PalletNo3 { get; set; } = "";

        /// <summary>托盘号 4（字节偏移 28，S7 String[6]，共 8 字节）。</summary>
        public string PalletNo4 { get; set; } = "";

        /// <summary>托盘号 5（字节偏移 36，S7 String[6]，共 8 字节）。</summary>
        public string PalletNo5 { get; set; } = "";

        /// <summary>托盘号 6（字节偏移 44，S7 String[6]，共 8 字节）。</summary>
        public string PalletNo6 { get; set; } = "";

        /// <summary>托盘号 7（字节偏移 52，S7 String[6]，共 8 字节）。</summary>
        public string PalletNo7 { get; set; } = "";

        /// <summary>托盘号 8（字节偏移 60，S7 String[6]，共 8 字节）。</summary>
        public string PalletNo8 { get; set; } = "";

        /// <summary>托盘号 9（字节偏移 68，S7 String[6]，共 8 字节）。</summary>
        public string PalletNo9 { get; set; } = "";

        /// <summary>托盘号 10（字节偏移 76，S7 String[6]，共 8 字节）。</summary>
        public string PalletNo10 { get; set; } = "";

        /// <summary>托盘号 11（字节偏移 84，S7 String[6]，共 8 字节）。</summary>
        public string PalletNo11 { get; set; } = "";

        /// <summary>托盘号 12（字节偏移 92，S7 String[6]，共 8 字节）。</summary>
        public string PalletNo12 { get; set; } = "";

        /// <summary>托盘号 13（字节偏移 100，S7 String[6]，共 8 字节）。</summary>
        public string PalletNo13 { get; set; } = "";

        /// <summary>托盘号 14（字节偏移 108，S7 String[6]，共 8 字节）。</summary>
        public string PalletNo14 { get; set; } = "";

        /// <summary>托盘号 15（字节偏移 116，S7 String[6]，共 8 字节）。</summary>
        public string PalletNo15 { get; set; } = "";
    }

    #endregion
}
