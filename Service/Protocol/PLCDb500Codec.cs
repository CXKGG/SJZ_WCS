using Model.Protocol;

namespace Service.Protocol
{
    /// <summary>
    /// 拆垛段 DB500 编解码（WCS → PLC，PLC「接收上位机数据」）。
    /// 全部逐字段手写，不依赖反射；偏移均从 0 开始，与注释一一对应。
    /// </summary>
    public static class PLCDb500Codec
    {
        // 协议长度（字节）。
        public const int Length = 171;

        #region 解析（字节数组 → 模型）

        public static DestackerDb500 Parse(byte[] data)
        {
            if (data is null) { throw new ArgumentNullException(nameof(data)); }
            if (data.Length < Length) { throw new ArgumentException("db500 数据长度不足：需要 " + Length + " 字节，实际 " + data.Length + " 字节。", nameof(data)); }
            DestackerDb500 model = new DestackerDb500();
            model.Station0 = ParseStation(data, 0);  // 块起始字节 0：拆垛位信息【0】（6105 左 / 6106 右）
            model.Station1 = ParseStation(data, 32);  // 块起始字节 32：拆垛位信息【1】（6205 左 / 6206 右）
            model.Feed0 = ParseFeed(data, 64);  // 块起始字节 64：北起段托盘输送线交互信息【0】（6101 / 6102 / 6103）
            model.Feed1 = ParseFeed(data, 84);  // 块起始字节 84：北起段托盘输送线交互信息【1】（6201 / 6202 / 6203）
            model.Outbound0 = ParseOutbound(data, 104);  // 块起始字节 104：出库至拆垛位信息【0】（6105 / 6106）
            model.Outbound1 = ParseOutbound(data, 124);  // 块起始字节 124：出库至拆垛位信息【1】（6205 / 6206）
            model.UnstackDone0 = ParseUnstackDone(data, 144);  // 块起始字节 144：拆盘完成托盘出信息【0】（6105 / 6106）
            model.UnstackDone1 = ParseUnstackDone(data, 146);  // 块起始字节 146：拆盘完成托盘出信息【1】（6205 / 6206）
            model.Stack0 = ParseStack(data, 148);  // 块起始字节 148：叠盘机垒托盘信号【0】（6107）
            model.Stack1 = ParseStack(data, 160);  // 块起始字节 160：叠盘机垒托盘信号【1】（6108）
            return model;
        }
        /// <summary>把模型编码成 db500 字节；嵌套模型为 null 时该块按全 0 处理。</summary>

        private static DestackerFeedInfo ParseFeed(byte[] data, int start)
        {
            DestackerFeedInfo info = new DestackerFeedInfo();
            info.PickingDone = S7Codec.ReadBool(data, start + 0, 0);  // 拣选口出库完成
            info.LeftOutboundRequest = S7Codec.ReadBool(data, start + 0, 1);  // 左出库口请求出盘
            info.LeftPalletNo = S7Codec.ReadString(data, start + 2, 6);  // 左出库口托盘号
            info.RightOutboundRequest = S7Codec.ReadBool(data, start + 10, 0);  // 右出库口请求出盘
            info.RightPalletNo = S7Codec.ReadString(data, start + 12, 6);  // 右出库口托盘号
            return info;
        }
        /// <summary>序列化北起段托盘输送信息交互块（20 字节）。</summary>

        private static DestackerOutboundInfo ParseOutbound(byte[] data, int start)
        {
            DestackerOutboundInfo info = new DestackerOutboundInfo();
            info.RgvArrivedLeft = S7Codec.ReadBool(data, start + 0, 0);  // RGV到达左侧出盘位
            info.LeftOutboundDone = S7Codec.ReadBool(data, start + 0, 1);  // 左侧出盘至RGV完成
            info.LeftRequestOutbound = S7Codec.ReadBool(data, start + 0, 2);  // 左侧拆垛位RGV请求出盘
            info.LeftPalletNo = S7Codec.ReadString(data, start + 2, 6);  // 左侧拆垛位托盘号
            info.RgvArrivedRight = S7Codec.ReadBool(data, start + 10, 0);  // RGV到达右侧出盘位
            info.RightOutboundDone = S7Codec.ReadBool(data, start + 10, 1);  // 右侧出盘至RGV完成
            info.RightRequestOutbound = S7Codec.ReadBool(data, start + 10, 2);  // 右侧拆垛位RGV请求出盘
            info.RightPalletNo = S7Codec.ReadString(data, start + 12, 6);  // 右侧拆垛位托盘号
            return info;
        }
        /// <summary>序列化出库至拆垛位块（20 字节）。</summary>

        private static DestackerStackInfo ParseStack(byte[] data, int start)
        {
            DestackerStackInfo info = new DestackerStackInfo();
            info.RgvArrivedDelivery = S7Codec.ReadBool(data, start + 0, 0);  // RGV送货到达垒盘位目的地
            info.RgvRequestEmptyPalletOut = S7Codec.ReadBool(data, start + 0, 1);  // RGV送货请求空盘出
            info.RgvEmptyPalletNo = S7Codec.ReadString(data, start + 2, 6);  // RGV送货请求空盘托盘号
            info.RgvSwapArrivedStart = S7Codec.ReadBool(data, start + 10, 0);  // RGV接货到达垒盘位起始地
            info.RgvSwapAllowEmptyOut = S7Codec.ReadBool(data, start + 10, 1);  // RGV接货允许满盘出
            info.RgvSwapFullOutDone = S7Codec.ReadBool(data, start + 10, 2);  // RGV接货满盘出完成
            return info;
        }
        /// <summary>序列化垒托盘块（11 字节）。</summary>

        private static DestackerStationInfo ParseStation(byte[] data, int start)
        {
            DestackerStationInfo info = new DestackerStationInfo();
            info.LeftProductType = S7Codec.ReadInt16(data, start + 0);  // 左侧产品信息
            info.LeftPalletCount = S7Codec.ReadInt16(data, start + 2);  // 左侧托盘箱数
            info.LeftWholePallet = S7Codec.ReadInt16(data, start + 4);  // 左侧是否整盘拆
            info.LeftBoxCount = S7Codec.ReadInt16(data, start + 6);  // 左侧拆垛箱数
            info.LeftPalletNo = S7Codec.ReadString(data, start + 8, 6);  // 左侧托盘号
            info.RightProductType = S7Codec.ReadInt16(data, start + 16);  // 右侧产品信息
            info.RightPalletCount = S7Codec.ReadInt16(data, start + 18);  // 右侧托盘箱数
            info.RightWholePallet = S7Codec.ReadInt16(data, start + 20);  // 右侧是否整盘拆
            info.RightBoxCount = S7Codec.ReadInt16(data, start + 22);  // 右侧拆垛箱数
            info.RightPalletNo = S7Codec.ReadString(data, start + 24, 6);  // 右侧托盘号
            return info;
        }
        /// <summary>序列化拆垛位信息块（32 字节）。</summary>

        private static DestackerUnstackDoneInfo ParseUnstackDone(byte[] data, int start)
        {
            DestackerUnstackDoneInfo info = new DestackerUnstackDoneInfo();
            info.RgvArrivedLeft = S7Codec.ReadBool(data, start + 0, 0);  // RGV到达左侧拆垛位
            info.LeftAllowIn = S7Codec.ReadBool(data, start + 0, 1);  // 左侧拆垛完成RGV允许进盘
            info.LeftInDone = S7Codec.ReadBool(data, start + 0, 2);  // 左侧拆垛完成RGV进盘完成
            info.RgvArrivedRight = S7Codec.ReadBool(data, start + 0, 3);  // RGV到达右侧拆垛位
            info.RightAllowIn = S7Codec.ReadBool(data, start + 0, 4);  // 右侧拆垛完成RGV允许进盘
            info.RightInDone = S7Codec.ReadBool(data, start + 0, 5);  // 右侧拆垛完成RGV进盘完成
            return info;
        }
        /// <summary>序列化拆盘完成托盘出块（6 个 Boolean）。</summary>

        #endregion

        #region 序列化（模型 → 字节数组）

        public static byte[] ToBytes(DestackerDb500 model)
        {
            if (model is null) { throw new ArgumentNullException(nameof(model)); }
            byte[] data = new byte[Length];
            if (model.Station0 is not null) { WriteStation(data, 0, model.Station0); }
            if (model.Station1 is not null) { WriteStation(data, 32, model.Station1); }
            if (model.Feed0 is not null) { WriteFeed(data, 64, model.Feed0); }
            if (model.Feed1 is not null) { WriteFeed(data, 84, model.Feed1); }
            if (model.Outbound0 is not null) { WriteOutbound(data, 104, model.Outbound0); }
            if (model.Outbound1 is not null) { WriteOutbound(data, 124, model.Outbound1); }
            if (model.UnstackDone0 is not null) { WriteUnstackDone(data, 144, model.UnstackDone0); }
            if (model.UnstackDone1 is not null) { WriteUnstackDone(data, 146, model.UnstackDone1); }
            if (model.Stack0 is not null) { WriteStack(data, 148, model.Stack0); }
            if (model.Stack1 is not null) { WriteStack(data, 160, model.Stack1); }
            return data;
        }
        /// <summary>列出 db500 的字段布局（路径、起始字节、位号、类型、字符串最大长度）。</summary>

        private static void WriteFeed(byte[] data, int start, DestackerFeedInfo info)
        {
            S7Codec.WriteBool(data, start + 0, 0, info.PickingDone);
            S7Codec.WriteBool(data, start + 0, 1, info.LeftOutboundRequest);
            S7Codec.WriteString(data, start + 2, info.LeftPalletNo, 6);
            S7Codec.WriteBool(data, start + 10, 0, info.RightOutboundRequest);
            S7Codec.WriteString(data, start + 12, info.RightPalletNo, 6);
        }
        /// <summary>把北起段托盘输送信息交互块的字段偏移加入布局列表。</summary>

        private static void WriteOutbound(byte[] data, int start, DestackerOutboundInfo info)
        {
            S7Codec.WriteBool(data, start + 0, 0, info.RgvArrivedLeft);
            S7Codec.WriteBool(data, start + 0, 1, info.LeftOutboundDone);
            S7Codec.WriteBool(data, start + 0, 2, info.LeftRequestOutbound);
            S7Codec.WriteString(data, start + 2, info.LeftPalletNo, 6);
            S7Codec.WriteBool(data, start + 10, 0, info.RgvArrivedRight);
            S7Codec.WriteBool(data, start + 10, 1, info.RightOutboundDone);
            S7Codec.WriteBool(data, start + 10, 2, info.RightRequestOutbound);
            S7Codec.WriteString(data, start + 12, info.RightPalletNo, 6);
        }
        /// <summary>把出库至拆垛位块的字段偏移加入布局列表。</summary>

        private static void WriteStack(byte[] data, int start, DestackerStackInfo info)
        {
            S7Codec.WriteBool(data, start + 0, 0, info.RgvArrivedDelivery);
            S7Codec.WriteBool(data, start + 0, 1, info.RgvRequestEmptyPalletOut);
            S7Codec.WriteString(data, start + 2, info.RgvEmptyPalletNo, 6);
            S7Codec.WriteBool(data, start + 10, 0, info.RgvSwapArrivedStart);
            S7Codec.WriteBool(data, start + 10, 1, info.RgvSwapAllowEmptyOut);
            S7Codec.WriteBool(data, start + 10, 2, info.RgvSwapFullOutDone);
        }
        /// <summary>把垒托盘块的字段偏移加入布局列表。</summary>

        private static void WriteStation(byte[] data, int start, DestackerStationInfo info)
        {
            S7Codec.WriteInt16(data, start + 0, info.LeftProductType);
            S7Codec.WriteInt16(data, start + 2, info.LeftPalletCount);
            S7Codec.WriteInt16(data, start + 4, info.LeftWholePallet);
            S7Codec.WriteInt16(data, start + 6, info.LeftBoxCount);
            S7Codec.WriteString(data, start + 8, info.LeftPalletNo, 6);
            S7Codec.WriteInt16(data, start + 16, info.RightProductType);
            S7Codec.WriteInt16(data, start + 18, info.RightPalletCount);
            S7Codec.WriteInt16(data, start + 20, info.RightWholePallet);
            S7Codec.WriteInt16(data, start + 22, info.RightBoxCount);
            S7Codec.WriteString(data, start + 24, info.RightPalletNo, 6);
        }
        /// <summary>把拆垛位信息块的字段偏移加入布局列表。</summary>

        private static void WriteUnstackDone(byte[] data, int start, DestackerUnstackDoneInfo info)
        {
            S7Codec.WriteBool(data, start + 0, 0, info.RgvArrivedLeft);
            S7Codec.WriteBool(data, start + 0, 1, info.LeftAllowIn);
            S7Codec.WriteBool(data, start + 0, 2, info.LeftInDone);
            S7Codec.WriteBool(data, start + 0, 3, info.RgvArrivedRight);
            S7Codec.WriteBool(data, start + 0, 4, info.RightAllowIn);
            S7Codec.WriteBool(data, start + 0, 5, info.RightInDone);
        }
        /// <summary>把拆盘完成托盘出块的字段偏移加入布局列表。</summary>

        #endregion

        #region 布局（字段路径与偏移列表）

        public static List<DbFieldInfo> Layout()
        {
            List<DbFieldInfo> fields = new List<DbFieldInfo>();
            AddStationLayout(fields, 0, "Station0");
            AddStationLayout(fields, 32, "Station1");
            AddFeedLayout(fields, 64, "Feed0");
            AddFeedLayout(fields, 84, "Feed1");
            AddOutboundLayout(fields, 104, "Outbound0");
            AddOutboundLayout(fields, 124, "Outbound1");
            AddUnstackDoneLayout(fields, 144, "UnstackDone0");
            AddUnstackDoneLayout(fields, 146, "UnstackDone1");
            AddStackLayout(fields, 148, "Stack0");
            AddStackLayout(fields, 160, "Stack1");
            return fields;
        }
        /// <summary>解析拆垛位信息块（32 字节；块起始字节由调用方传入）。</summary>

        private static void AddFeedLayout(List<DbFieldInfo> fields, int start, string prefix)
        {
            fields.Add(new DbFieldInfo(prefix + "." + "PickingDone", start + 0, 0, DbFieldKind.Bool, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "LeftOutboundRequest", start + 0, 1, DbFieldKind.Bool, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "LeftPalletNo", start + 2, null, DbFieldKind.S7String, 6));
            fields.Add(new DbFieldInfo(prefix + "." + "RightOutboundRequest", start + 10, 0, DbFieldKind.Bool, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "RightPalletNo", start + 12, null, DbFieldKind.S7String, 6));
        }
        /// <summary>解析出库至拆垛位块（20 字节）。</summary>

        private static void AddOutboundLayout(List<DbFieldInfo> fields, int start, string prefix)
        {
            fields.Add(new DbFieldInfo(prefix + "." + "RgvArrivedLeft", start + 0, 0, DbFieldKind.Bool, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "LeftOutboundDone", start + 0, 1, DbFieldKind.Bool, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "LeftRequestOutbound", start + 0, 2, DbFieldKind.Bool, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "LeftPalletNo", start + 2, null, DbFieldKind.S7String, 6));
            fields.Add(new DbFieldInfo(prefix + "." + "RgvArrivedRight", start + 10, 0, DbFieldKind.Bool, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "RightOutboundDone", start + 10, 1, DbFieldKind.Bool, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "RightRequestOutbound", start + 10, 2, DbFieldKind.Bool, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "RightPalletNo", start + 12, null, DbFieldKind.S7String, 6));
        }
        /// <summary>解析拆盘完成托盘出块（6 个 Boolean，共 1 字节）。</summary>

        private static void AddStackLayout(List<DbFieldInfo> fields, int start, string prefix)
        {
            fields.Add(new DbFieldInfo(prefix + "." + "RgvArrivedDelivery", start + 0, 0, DbFieldKind.Bool, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "RgvRequestEmptyPalletOut", start + 0, 1, DbFieldKind.Bool, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "RgvEmptyPalletNo", start + 2, null, DbFieldKind.S7String, 6));
            fields.Add(new DbFieldInfo(prefix + "." + "RgvSwapArrivedStart", start + 10, 0, DbFieldKind.Bool, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "RgvSwapAllowEmptyOut", start + 10, 1, DbFieldKind.Bool, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "RgvSwapFullOutDone", start + 10, 2, DbFieldKind.Bool, 0));
        }

        private static void AddStationLayout(List<DbFieldInfo> fields, int start, string prefix)
        {
            fields.Add(new DbFieldInfo(prefix + "." + "LeftProductType", start + 0, null, DbFieldKind.Int16, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "LeftPalletCount", start + 2, null, DbFieldKind.Int16, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "LeftWholePallet", start + 4, null, DbFieldKind.Int16, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "LeftBoxCount", start + 6, null, DbFieldKind.Int16, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "LeftPalletNo", start + 8, null, DbFieldKind.S7String, 6));
            fields.Add(new DbFieldInfo(prefix + "." + "RightProductType", start + 16, null, DbFieldKind.Int16, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "RightPalletCount", start + 18, null, DbFieldKind.Int16, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "RightWholePallet", start + 20, null, DbFieldKind.Int16, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "RightBoxCount", start + 22, null, DbFieldKind.Int16, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "RightPalletNo", start + 24, null, DbFieldKind.S7String, 6));
        }
        /// <summary>解析北起段托盘输送信息交互块（20 字节）。</summary>

        private static void AddUnstackDoneLayout(List<DbFieldInfo> fields, int start, string prefix)
        {
            fields.Add(new DbFieldInfo(prefix + "." + "RgvArrivedLeft", start + 0, 0, DbFieldKind.Bool, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "LeftAllowIn", start + 0, 1, DbFieldKind.Bool, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "LeftInDone", start + 0, 2, DbFieldKind.Bool, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "RgvArrivedRight", start + 0, 3, DbFieldKind.Bool, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "RightAllowIn", start + 0, 4, DbFieldKind.Bool, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "RightInDone", start + 0, 5, DbFieldKind.Bool, 0));
        }
        /// <summary>解析垒托盘块（11 字节）。</summary>

        #endregion
    }
}
