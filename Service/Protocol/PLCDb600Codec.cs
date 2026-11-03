using Model.Protocol;

namespace Service.Protocol
{
    /// <summary>
    /// 拆垛段 DB600 编解码（PLC → WCS，PLC「发送上位机数据」）。
    /// 全部逐字段手写，不依赖反射；偏移均从 0 开始，与注释一一对应。
    /// </summary>
    public static class PLCDb600Codec
    {
        // 协议长度（字节）。
        public const int Length = 408;

        #region 解析（字节数组 → 模型）

        /// <summary>解析 DB600 字节数组为模型。</summary>
        public static DestackerDb600 Parse(byte[] data)
        {
            if (data is null) { throw new ArgumentNullException(nameof(data)); }
            if (data.Length < Length) { throw new ArgumentException("db600 数据长度不足：需要 " + Length + " 字节，实际 " + data.Length + " 字节。", nameof(data)); }
            DestackerDb600 model = new DestackerDb600();
            model.Feed0 = ParseFeedPut(data, 0);  // 块起始字节 0：北起段托盘输送线信息【0】（6101 / 6102 / 6103）
            model.Feed1 = ParseFeedPut(data, 32);  // 块起始字节 32：北起段托盘输送线信息【1】（6201 / 6202 / 6203）
            model.Outbound0 = ParseOutboundPut(data, 64);  // 块起始字节 64：出库至拆垛位【0】（6105 / 6106）
            model.Outbound1 = ParseOutboundPut(data, 84);  // 块起始字节 84：出库至拆垛位【1】（6205 / 6206）
            model.UnstackDone0 = ParseUnstackDonePut(data, 104);  // 块起始字节 104：拆垛完成托盘出【0】（6105 / 6106）
            model.UnstackDone1 = ParseUnstackDonePut(data, 132);  // 块起始字节 132：拆垛完成托盘出【1】（6205 / 6206）
            model.Stack0 = ParseStackPut(data, 160);  // 块起始字节 160：垒盘托盘【0】（6107）
            model.Stack1 = ParseStackPut(data, 284);  // 块起始字节 284：垒盘托盘【1】（6108）        
            return model;
        }

        /// <summary>解析北起段托盘输送线信息块（32 字节）。</summary>
        private static DestackerFeedPutInfo ParseFeedPut(byte[] data, int start)
        {
            DestackerFeedPutInfo info = new DestackerFeedPutInfo();
            info.PickingRequestOut = S7Codec.ReadBool(data, start + 0, 0);  // 拣选口请求出
            info.PickingPalletCount = S7Codec.ReadInt16(data, start + 2);  // 拣选口托盘箱数
            info.PickingPalletNo = S7Codec.ReadString(data, start + 4, 6);  // 拣选口托盘号
            info.LeftInAllowed = S7Codec.ReadBool(data, start + 12, 0);  // 左出库口允许进盘
            info.LeftInDone = S7Codec.ReadBool(data, start + 12, 1);  // 左出库口进盘完成
            info.LeftPalletNo = S7Codec.ReadString(data, start + 14, 6);  // 左出库口托盘号
            info.RightInAllowed = S7Codec.ReadBool(data, start + 22, 0);  // 右出库口允许进盘
            info.RightInDone = S7Codec.ReadBool(data, start + 22, 1);  // 右出库口进盘完成
            info.RightPalletNo = S7Codec.ReadString(data, start + 24, 6);  // 右出库口托盘号
            return info;
        }

        /// <summary>解析出库至拆垛位块（20 字节）。</summary>
        private static DestackerOutboundPutInfo ParseOutboundPut(byte[] data, int start)
        {
            DestackerOutboundPutInfo info = new DestackerOutboundPutInfo();
            info.LeftRequestOutToRgv = S7Codec.ReadBool(data, start + 0, 0);  // 左侧请求出盘至RGV
            info.LeftInAllowed = S7Codec.ReadBool(data, start + 0, 1);  // 左侧拆垛位允许进盘
            info.LeftInDone = S7Codec.ReadBool(data, start + 0, 2);  // 左侧拆垛位允许进盘完成
            info.LeftPalletNo = S7Codec.ReadString(data, start + 2, 6);  // 左侧拆垛位托盘号
            info.RightRequestOutToRgv = S7Codec.ReadBool(data, start + 10, 0);  // 右侧请求出盘至RGV
            info.RightInAllowed = S7Codec.ReadBool(data, start + 10, 1);  // 右侧拆垛位允许进盘
            info.RightInDone = S7Codec.ReadBool(data, start + 10, 2);  // 右侧拆垛位允许进盘完成
            info.RightPalletNo = S7Codec.ReadString(data, start + 12, 6);  // 右侧拆垛位托盘号
            return info;
        }

        /// <summary>解析垒盘托盘块（124 字节，含托盘号 1~15）。</summary>
        private static DestackerStackPutInfo ParseStackPut(byte[] data, int start)
        {
            DestackerStackPutInfo info = new DestackerStackPutInfo();
            info.EmptyPalletInAllowed = S7Codec.ReadBool(data, start + 0, 0);  // 空盘允许进盘
            info.EmptyPalletInDone = S7Codec.ReadBool(data, start + 0, 1);  // 空盘进盘完成
            info.EmptyPalletFullRequestOut = S7Codec.ReadBool(data, start + 0, 2);  // 空盘满请求出盘
            info.PalletCount = S7Codec.ReadInt16(data, start + 2);  // 托盘数量
            info.PalletNo1 = S7Codec.ReadString(data, start + 4, 6);  // 托盘号1
            info.PalletNo2 = S7Codec.ReadString(data, start + 12, 6);  // 托盘号2
            info.PalletNo3 = S7Codec.ReadString(data, start + 20, 6);  // 托盘号3
            info.PalletNo4 = S7Codec.ReadString(data, start + 28, 6);  // 托盘号4
            info.PalletNo5 = S7Codec.ReadString(data, start + 36, 6);  // 托盘号5
            info.PalletNo6 = S7Codec.ReadString(data, start + 44, 6);  // 托盘号6
            info.PalletNo7 = S7Codec.ReadString(data, start + 52, 6);  // 托盘号7
            info.PalletNo8 = S7Codec.ReadString(data, start + 60, 6);  // 托盘号8
            info.PalletNo9 = S7Codec.ReadString(data, start + 68, 6);  // 托盘号9
            info.PalletNo10 = S7Codec.ReadString(data, start + 76, 6);  // 托盘号10
            info.PalletNo11 = S7Codec.ReadString(data, start + 84, 6);  // 托盘号11
            info.PalletNo12 = S7Codec.ReadString(data, start + 92, 6);  // 托盘号12
            info.PalletNo13 = S7Codec.ReadString(data, start + 100, 6);  // 托盘号13
            info.PalletNo14 = S7Codec.ReadString(data, start + 108, 6);  // 托盘号14
            info.PalletNo15 = S7Codec.ReadString(data, start + 116, 6);  // 托盘号15
            return info;
        }

        /// <summary>解析拆垛完成托盘出块（28 字节）。</summary>
        private static DestackerUnstackDonePutInfo ParseUnstackDonePut(byte[] data, int start)
        {
            DestackerUnstackDonePutInfo info = new DestackerUnstackDonePutInfo();
            info.LeftRequestOut = S7Codec.ReadBool(data, start + 0, 0);  // 左侧拆垛完成托盘请求出
            info.LeftPalletType = S7Codec.ReadInt16(data, start + 2);  // 左侧托盘类型
            info.LeftBoxCount = S7Codec.ReadInt16(data, start + 4);  // 左侧托盘箱数
            info.LeftPalletNo = S7Codec.ReadString(data, start + 6, 6);  // 左侧托盘号
            info.RightRequestOut = S7Codec.ReadInt16(data, start + 14);  // 右侧拆垛完成托盘请求出（Int16）
            info.RightPalletType = S7Codec.ReadInt16(data, start + 16);  // 右侧托盘类型
            info.RightBoxCount = S7Codec.ReadInt16(data, start + 18);  // 右侧托盘箱数
            info.RightPalletNo = S7Codec.ReadString(data, start + 20, 6);  // 右侧托盘号
            return info;
        }

        #endregion

        #region 序列化（模型 → 字节数组）

        /// <summary>把模型编码成 db600 字节；嵌套模型为 null 时该块按全 0 处理。</summary>
        public static byte[] ToBytes(DestackerDb600 model)
        {
            if (model is null) { throw new ArgumentNullException(nameof(model)); }
            byte[] data = new byte[Length];
            if (model.Feed0 is not null) { WriteFeedPut(data, 0, model.Feed0); }
            if (model.Feed1 is not null) { WriteFeedPut(data, 32, model.Feed1); }
            if (model.Outbound0 is not null) { WriteOutboundPut(data, 64, model.Outbound0); }
            if (model.Outbound1 is not null) { WriteOutboundPut(data, 84, model.Outbound1); }
            if (model.UnstackDone0 is not null) { WriteUnstackDonePut(data, 104, model.UnstackDone0); }
            if (model.UnstackDone1 is not null) { WriteUnstackDonePut(data, 132, model.UnstackDone1); }
            if (model.Stack0 is not null) { WriteStackPut(data, 160, model.Stack0); }
            if (model.Stack1 is not null) { WriteStackPut(data, 284, model.Stack1); }
            return data;
        }

        /// <summary>序列化北起段托盘输送线信息块（32 字节）。</summary>
        private static void WriteFeedPut(byte[] data, int start, DestackerFeedPutInfo info)
        {
            S7Codec.WriteBool(data, start + 0, 0, info.PickingRequestOut);
            S7Codec.WriteInt16(data, start + 2, info.PickingPalletCount);
            S7Codec.WriteString(data, start + 4, info.PickingPalletNo, 6);
            S7Codec.WriteBool(data, start + 12, 0, info.LeftInAllowed);
            S7Codec.WriteBool(data, start + 12, 1, info.LeftInDone);
            S7Codec.WriteString(data, start + 14, info.LeftPalletNo, 6);
            S7Codec.WriteBool(data, start + 22, 0, info.RightInAllowed);
            S7Codec.WriteBool(data, start + 22, 1, info.RightInDone);
            S7Codec.WriteString(data, start + 24, info.RightPalletNo, 6);
        }

        /// <summary>序列化出库至拆垛位块（20 字节）。</summary>
        private static void WriteOutboundPut(byte[] data, int start, DestackerOutboundPutInfo info)
        {
            S7Codec.WriteBool(data, start + 0, 0, info.LeftRequestOutToRgv);
            S7Codec.WriteBool(data, start + 0, 1, info.LeftInAllowed);
            S7Codec.WriteBool(data, start + 0, 2, info.LeftInDone);
            S7Codec.WriteString(data, start + 2, info.LeftPalletNo, 6);
            S7Codec.WriteBool(data, start + 10, 0, info.RightRequestOutToRgv);
            S7Codec.WriteBool(data, start + 10, 1, info.RightInAllowed);
            S7Codec.WriteBool(data, start + 10, 2, info.RightInDone);
            S7Codec.WriteString(data, start + 12, info.RightPalletNo, 6);
        }

        /// <summary>序列化垒盘托盘块（124 字节，含托盘号 1~15）。</summary>
        private static void WriteStackPut(byte[] data, int start, DestackerStackPutInfo info)
        {
            S7Codec.WriteBool(data, start + 0, 0, info.EmptyPalletInAllowed);
            S7Codec.WriteBool(data, start + 0, 1, info.EmptyPalletInDone);
            S7Codec.WriteBool(data, start + 0, 2, info.EmptyPalletFullRequestOut);
            S7Codec.WriteInt16(data, start + 2, info.PalletCount);
            S7Codec.WriteString(data, start + 4, info.PalletNo1, 6);
            S7Codec.WriteString(data, start + 12, info.PalletNo2, 6);
            S7Codec.WriteString(data, start + 20, info.PalletNo3, 6);
            S7Codec.WriteString(data, start + 28, info.PalletNo4, 6);
            S7Codec.WriteString(data, start + 36, info.PalletNo5, 6);
            S7Codec.WriteString(data, start + 44, info.PalletNo6, 6);
            S7Codec.WriteString(data, start + 52, info.PalletNo7, 6);
            S7Codec.WriteString(data, start + 60, info.PalletNo8, 6);
            S7Codec.WriteString(data, start + 68, info.PalletNo9, 6);
            S7Codec.WriteString(data, start + 76, info.PalletNo10, 6);
            S7Codec.WriteString(data, start + 84, info.PalletNo11, 6);
            S7Codec.WriteString(data, start + 92, info.PalletNo12, 6);
            S7Codec.WriteString(data, start + 100, info.PalletNo13, 6);
            S7Codec.WriteString(data, start + 108, info.PalletNo14, 6);
            S7Codec.WriteString(data, start + 116, info.PalletNo15, 6);
        }

        /// <summary>序列化拆垛完成托盘出块（28 字节）。</summary>
        private static void WriteUnstackDonePut(byte[] data, int start, DestackerUnstackDonePutInfo info)
        {
            S7Codec.WriteBool(data, start + 0, 0, info.LeftRequestOut);
            S7Codec.WriteInt16(data, start + 2, info.LeftPalletType);
            S7Codec.WriteInt16(data, start + 4, info.LeftBoxCount);
            S7Codec.WriteString(data, start + 6, info.LeftPalletNo, 6);
            S7Codec.WriteInt16(data, start + 14, info.RightRequestOut);
            S7Codec.WriteInt16(data, start + 16, info.RightPalletType);
            S7Codec.WriteInt16(data, start + 18, info.RightBoxCount);
            S7Codec.WriteString(data, start + 20, info.RightPalletNo, 6);
        }

        #endregion

        #region 布局（字段路径与偏移列表）

        /// <summary>列出 db600 的字段布局（路径、起始字节、位号、类型、字符串最大长度）。</summary>
        public static List<DbFieldInfo> Layout()
        {
            List<DbFieldInfo> fields = new List<DbFieldInfo>();
            AddFeedPutLayout(fields, 0, "Feed0");
            AddFeedPutLayout(fields, 32, "Feed1");
            AddOutboundPutLayout(fields, 64, "Outbound0");
            AddOutboundPutLayout(fields, 84, "Outbound1");
            AddUnstackDonePutLayout(fields, 104, "UnstackDone0");
            AddUnstackDonePutLayout(fields, 132, "UnstackDone1");
            AddStackPutLayout(fields, 160, "Stack0");
            AddStackPutLayout(fields, 284, "Stack1");
            return fields;
        }

        /// <summary>把北起段托盘输送线信息块的字段偏移加入布局列表。</summary>
        private static void AddFeedPutLayout(List<DbFieldInfo> fields, int start, string prefix)
        {
            fields.Add(new DbFieldInfo(prefix + "." + "PickingRequestOut", start + 0, 0, DbFieldKind.Bool, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "PickingPalletCount", start + 2, null, DbFieldKind.Int16, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "PickingPalletNo", start + 4, null, DbFieldKind.S7String, 6));
            fields.Add(new DbFieldInfo(prefix + "." + "LeftInAllowed", start + 12, 0, DbFieldKind.Bool, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "LeftInDone", start + 12, 1, DbFieldKind.Bool, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "LeftPalletNo", start + 14, null, DbFieldKind.S7String, 6));
            fields.Add(new DbFieldInfo(prefix + "." + "RightInAllowed", start + 22, 0, DbFieldKind.Bool, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "RightInDone", start + 22, 1, DbFieldKind.Bool, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "RightPalletNo", start + 24, null, DbFieldKind.S7String, 6));
        }

        /// <summary>把出库至拆垛位块的字段偏移加入布局列表。</summary>
        private static void AddOutboundPutLayout(List<DbFieldInfo> fields, int start, string prefix)
        {
            fields.Add(new DbFieldInfo(prefix + "." + "LeftRequestOutToRgv", start + 0, 0, DbFieldKind.Bool, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "LeftInAllowed", start + 0, 1, DbFieldKind.Bool, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "LeftInDone", start + 0, 2, DbFieldKind.Bool, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "LeftPalletNo", start + 2, null, DbFieldKind.S7String, 6));
            fields.Add(new DbFieldInfo(prefix + "." + "RightRequestOutToRgv", start + 10, 0, DbFieldKind.Bool, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "RightInAllowed", start + 10, 1, DbFieldKind.Bool, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "RightInDone", start + 10, 2, DbFieldKind.Bool, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "RightPalletNo", start + 12, null, DbFieldKind.S7String, 6));
        }

        /// <summary>把垒盘托盘块的字段偏移加入布局列表。</summary>
        private static void AddStackPutLayout(List<DbFieldInfo> fields, int start, string prefix)
        {
            fields.Add(new DbFieldInfo(prefix + "." + "EmptyPalletInAllowed", start + 0, 0, DbFieldKind.Bool, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "EmptyPalletInDone", start + 0, 1, DbFieldKind.Bool, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "EmptyPalletFullRequestOut", start + 0, 2, DbFieldKind.Bool, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "PalletCount", start + 2, null, DbFieldKind.Int16, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "PalletNo1", start + 4, null, DbFieldKind.S7String, 6));
            fields.Add(new DbFieldInfo(prefix + "." + "PalletNo2", start + 12, null, DbFieldKind.S7String, 6));
            fields.Add(new DbFieldInfo(prefix + "." + "PalletNo3", start + 20, null, DbFieldKind.S7String, 6));
            fields.Add(new DbFieldInfo(prefix + "." + "PalletNo4", start + 28, null, DbFieldKind.S7String, 6));
            fields.Add(new DbFieldInfo(prefix + "." + "PalletNo5", start + 36, null, DbFieldKind.S7String, 6));
            fields.Add(new DbFieldInfo(prefix + "." + "PalletNo6", start + 44, null, DbFieldKind.S7String, 6));
            fields.Add(new DbFieldInfo(prefix + "." + "PalletNo7", start + 52, null, DbFieldKind.S7String, 6));
            fields.Add(new DbFieldInfo(prefix + "." + "PalletNo8", start + 60, null, DbFieldKind.S7String, 6));
            fields.Add(new DbFieldInfo(prefix + "." + "PalletNo9", start + 68, null, DbFieldKind.S7String, 6));
            fields.Add(new DbFieldInfo(prefix + "." + "PalletNo10", start + 76, null, DbFieldKind.S7String, 6));
            fields.Add(new DbFieldInfo(prefix + "." + "PalletNo11", start + 84, null, DbFieldKind.S7String, 6));
            fields.Add(new DbFieldInfo(prefix + "." + "PalletNo12", start + 92, null, DbFieldKind.S7String, 6));
            fields.Add(new DbFieldInfo(prefix + "." + "PalletNo13", start + 100, null, DbFieldKind.S7String, 6));
            fields.Add(new DbFieldInfo(prefix + "." + "PalletNo14", start + 108, null, DbFieldKind.S7String, 6));
            fields.Add(new DbFieldInfo(prefix + "." + "PalletNo15", start + 116, null, DbFieldKind.S7String, 6));
        }

        /// <summary>把拆垛完成托盘出块的字段偏移加入布局列表。</summary>
        private static void AddUnstackDonePutLayout(List<DbFieldInfo> fields, int start, string prefix)
        {
            fields.Add(new DbFieldInfo(prefix + "." + "LeftRequestOut", start + 0, 0, DbFieldKind.Bool, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "LeftPalletType", start + 2, null, DbFieldKind.Int16, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "LeftBoxCount", start + 4, null, DbFieldKind.Int16, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "LeftPalletNo", start + 6, null, DbFieldKind.S7String, 6));
            fields.Add(new DbFieldInfo(prefix + "." + "RightRequestOut", start + 14, null, DbFieldKind.Int16, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "RightPalletType", start + 16, null, DbFieldKind.Int16, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "RightBoxCount", start + 18, null, DbFieldKind.Int16, 0));
            fields.Add(new DbFieldInfo(prefix + "." + "RightPalletNo", start + 20, null, DbFieldKind.S7String, 6));
        }

        #endregion
    }
}