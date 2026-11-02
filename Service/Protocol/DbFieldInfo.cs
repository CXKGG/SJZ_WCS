namespace Service.Protocol
{
    /// <summary>
    /// DB 字段描述：用于 GET /api/protocol/destacker/layout 展示协议布局（前端或调试用）。
    /// 仅作文档/展示数据，解析与序列化不再依赖这些元数据。
    /// </summary>
    /// <param name="Path">字段路径，如 "Station0.LeftProductType"</param>
    /// <param name="ByteOffset">在 DB 中的起始字节偏移（从 0 开始）</param>
    /// <param name="Bit">位号（Bool 为 0~7，其余类型为 null）</param>
    /// <param name="Kind">数据类型</param>
    /// <param name="Length">S7 String 的最大字符数（非字符串为 0）</param>
    public sealed record DbFieldInfo(string Path, int ByteOffset, int? Bit, DbFieldKind Kind, int Length);

    /// <summary>DB 字段数据类型（与 TIA 类型对应；数值与旧接口返回的 kind 保持一致）。</summary>
    public enum DbFieldKind
    {
        /// <summary>位，占 1 个位（TIA Bool）。</summary>
        Bool = 0,

        /// <summary>2 字节大端有符号整数（TIA Int）。</summary>
        Int16 = 1,

        /// <summary>2 字节大端无符号整数（TIA Word / UInt）。</summary>
        UInt16 = 2,

        /// <summary>4 字节大端有符号整数（TIA DInt）。</summary>
        Int32 = 3,

        /// <summary>4 字节大端无符号整数（TIA DWord / UDInt）。</summary>
        UInt32 = 4,

        /// <summary>S7 String：2 字节头 + ASCII（TIA String[n]）。</summary>
        S7String = 5,

        /// <summary>嵌套结构体块（TIA Struct / UDT），只用于布局展示，不单独占位。</summary>
        Block = 6,
    }
}
