namespace Model.Protocol
{
    /// <summary>
    /// 拆垛段 DB500 模型：PLC「接收上位机数据」，即 WCS 写入 PLC（WCS → PLC）。
    /// <para>
    /// 字段偏移来自《四药国龙拆垛PLC与上位机信息交互.docx》TIA 截图（PLC_1 [CPU 1511-1 PN] 程序块 DB500），
    /// 4 台拆垛 PLC（PLC1~PLC4，型号 1500）各一份同构 DB，DB 号均为 500。
    /// 数组数据在 TIA 里是 Array[0..1]，这里展开成 Xxx0 / Xxx1 两个属性。
    /// </para>
    /// <para>DB500 总长度 171 字节。</para>
    /// <para>本类只承载数据，字段偏移与类型在 Service/Protocol 下的编解码类中逐字段写明。</para>
    /// </summary>
    public class DestackerDb500
    {
        #region 拆垛位信息（字节 0、32，各 32 字节）

        // 拆垛位信息[0]：6105（左）/ 6106（右），字节 0 起，长 32 字节。
        public DestackerStationInfo Station0 { get; set; } = new();

        // 拆垛位信息[1]：6205（左）/ 6206（右），字节 32 起，长 32 字节。
        public DestackerStationInfo Station1 { get; set; } = new();

        #endregion

        #region 北起段托盘输送信息交互（字节 64、84，各 20 字节）

        // 北起段托盘输送信息交互[0]：61 段（6101、6102、6103 工位），字节 64 起，长 20 字节。
        public DestackerFeedInfo Feed0 { get; set; } = new();

        // 北起段托盘输送信息交互[1]：62 段（6201、6202、6203 工位），字节 84 起，长 20 字节。
        public DestackerFeedInfo Feed1 { get; set; } = new();

        #endregion

        #region 出库至拆垛位（字节 104、124，各 20 字节）

        // 出库至拆垛位[0]：6105 / 6106 拆垛位，字节 104 起，长 20 字节。
        public DestackerOutboundInfo Outbound0 { get; set; } = new();

        // 出库至拆垛位[1]：6205 / 6206 拆垛位，字节 124 起，长 20 字节。
        public DestackerOutboundInfo Outbound1 { get; set; } = new();

        #endregion

        #region 拆盘完成托盘出（字节 144、146，各 6 个 Boolean）

        // 拆盘完成托盘出[0]：6105 / 6106 拆垛位，字节 144 起的 6 个 Boolean。
        public DestackerUnstackDoneInfo UnstackDone0 { get; set; } = new();

        // 拆盘完成托盘出[1]：6205 / 6206 拆垛位，字节 146 起的 6 个 Boolean。
        public DestackerUnstackDoneInfo UnstackDone1 { get; set; } = new();

        #endregion

        #region 垒托盘（字节 148、160，各 11 字节）

        // 垒托盘[0]：6107 叠盘机，字节 148 起，长 11 字节。
        public DestackerStackInfo Stack0 { get; set; } = new();

        // 垒托盘[1]：6108 叠盘机，字节 160 起，长 11 字节。
        public DestackerStackInfo Stack1 { get; set; } = new();

        #endregion
    }

    #region 拆垛位信息数据结构

    /// <summary>
    /// 拆垛位信息：左、右两个拆垛位的来料信息，每组 32 字节。
    /// 产品信息规则：葡萄糖注射液 500ml=1、250ml=2；氯化钠注射液 500ml=3、250ml=4；整盘拆=1、拆零盘=2。
    /// </summary>
    public class DestackerStationInfo
    {
        // 左侧产品信息（字节偏移 0，Int16；1=葡萄糖 500ml、2=葡萄糖 250ml、3=氯化钠 500ml、4=氯化钠 250ml）。
        public short LeftProductType { get; set; }

        // 左侧托盘数量（字节偏移 2，Int16）。
        public short LeftPalletCount { get; set; }

        // 左侧是否整盘拆（字节偏移 4，Int16；1=整盘拆、2=拆零盘）。
        public short LeftWholePallet { get; set; }

        // 左侧拆箱数量（字节偏移 6，Int16）。
        public short LeftBoxCount { get; set; }

        // 左侧托盘号（字节偏移 8，S7 String[6]，共 8 字节）。
        public string LeftPalletNo { get; set; } = "";

        // 右侧产品信息（字节偏移 16，Int16，含义同左侧）。
        public short RightProductType { get; set; }

        // 右侧托盘数量（字节偏移 18，Int16）。
        public short RightPalletCount { get; set; }

        // 右侧是否整盘拆（字节偏移 20，Int16）。
        public short RightWholePallet { get; set; }

        // 右侧拆箱数量（字节偏移 22，Int16）。
        public short RightBoxCount { get; set; }

        // 右侧托盘号（字节偏移 24，S7 String[6]，共 8 字节）。
        public string RightPalletNo { get; set; } = "";
    }

    #endregion

    #region 北起段托盘输送信息交互数据结构

    /// <summary>
    /// 北起段托盘输送信息交互：拣选口完成信号与两个出库口请求出盘信号，每段 20 字节。
    /// </summary>
    public class DestackerFeedInfo
    {
        // 拣选口完成（字节偏移 0 第 0 位，Boolean；xx01 工位）。
        public bool PickingDone { get; set; }

        // 左出库口请求出盘（字节偏移 0 第 1 位，Boolean；xx02 工位）。
        public bool LeftOutboundRequest { get; set; }

        // 左出库口托盘号（字节偏移 2，S7 String[6]，共 8 字节）。
        public string LeftPalletNo { get; set; } = "";

        // 右出库口请求出盘（字节偏移 10 第 0 位，Boolean；xx03 工位）。
        public bool RightOutboundRequest { get; set; }

        // 右出库口托盘号（字节偏移 12，S7 String[6]，共 8 字节）。
        public string RightPalletNo { get; set; } = "";
    }

    #endregion

    #region 出库至拆垛位数据结构

    /// <summary>
    /// 出库至拆垛位：RGV 到位、出盘完成、请求出盘信号，每段 20 字节。
    /// </summary>
    public class DestackerOutboundInfo
    {
        // RGV 到达左侧出盘位（字节偏移 0 第 0 位，Boolean）。
        public bool RgvArrivedLeft { get; set; }

        // 左侧出盘至 RGV 完成（字节偏移 0 第 1 位，Boolean）。
        public bool LeftOutboundDone { get; set; }

        // 左侧拆垛位 RGV 请求出盘（字节偏移 0 第 2 位，Boolean）。
        public bool LeftRequestOutbound { get; set; }

        // 左侧拆垛位托盘号（字节偏移 2，S7 String[6]，共 8 字节）。
        public string LeftPalletNo { get; set; } = "";

        // RGV 到达右侧出盘位（字节偏移 10 第 0 位，Boolean）。
        public bool RgvArrivedRight { get; set; }

        // 右侧出盘至 RGV 完成（字节偏移 10 第 1 位，Boolean）。
        public bool RightOutboundDone { get; set; }

        // 右侧拆垛位 RGV 请求出盘（字节偏移 10 第 2 位，Boolean）。
        public bool RightRequestOutbound { get; set; }

        // 右侧拆垛位托盘号（字节偏移 12，S7 String[6]，共 8 字节）。
        public string RightPalletNo { get; set; } = "";
    }

    #endregion

    #region 拆盘完成托盘出数据结构

    /// <summary>
    /// 拆盘完成托盘出：拆垛完成后的 6 个握手位（一组 6 个 Boolean）。
    /// </summary>
    public class DestackerUnstackDoneInfo
    {
        // RGV 到达左侧拆垛位（字节偏移 0 第 0 位，Boolean）。
        public bool RgvArrivedLeft { get; set; }

        // 左侧拆垛完成，RGV 允许进盘（字节偏移 0 第 1 位，Boolean）。
        public bool LeftAllowIn { get; set; }

        // 左侧拆垛完成，RGV 进盘完成（字节偏移 0 第 2 位，Boolean）。
        public bool LeftInDone { get; set; }

        // RGV 到达右侧拆垛位（字节偏移 0 第 3 位，Boolean）。
        public bool RgvArrivedRight { get; set; }

        // 右侧拆垛完成，RGV 允许进盘（字节偏移 0 第 4 位，Boolean）。
        public bool RightAllowIn { get; set; }

        // 右侧拆垛完成，RGV 进盘完成（字节偏移 0 第 5 位，Boolean）。
        public bool RightInDone { get; set; }
    }

    #endregion

    #region 垒托盘数据结构

    /// <summary>
    /// 垒托盘：叠盘机的空盘/满盘换货握手与空盘托盘号，长 11 字节。
    /// </summary>
    public class DestackerStackInfo
    {
        // RGV 送货到达叠盘位目的地（字节偏移 0 第 0 位，Boolean）。
        public bool RgvArrivedDelivery { get; set; }

        // RGV 送货请求空盘出（字节偏移 0 第 1 位，Boolean）。
        public bool RgvRequestEmptyPalletOut { get; set; }

        // RGV 送货空盘托盘号（字节偏移 2，S7 String[6]，共 8 字节）。
        public string RgvEmptyPalletNo { get; set; } = "";

        // RGV 换货到达叠盘位起始地（字节偏移 10 第 0 位，Boolean）。
        public bool RgvSwapArrivedStart { get; set; }

        // RGV 换货允许空盘出（字节偏移 10 第 1 位，Boolean）。
        public bool RgvSwapAllowEmptyOut { get; set; }

        // RGV 换货满盘出完成（字节偏移 10 第 2 位，Boolean）。
        public bool RgvSwapFullOutDone { get; set; }
    }

    #endregion
}
