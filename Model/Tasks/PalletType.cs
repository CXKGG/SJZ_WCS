namespace Model.Tasks
{
    /// <summary>
    /// 托盘类型（流程图「判断托盘类型 川/田」）。
    /// 数值与 PLC 协议里的「托盘类型」字段保持一致，具体 1/2 的对应关系以现场为准（TODO 待确认）。
    /// </summary>
    public enum PalletType
    {
        /// <summary>未知</summary>
        Unknown = 0,

        /// <summary>川字型（对应「回川字型叠盘机」）</summary>
        Chuan = 1,

        /// <summary>田字型（对应「回田字型叠盘机」）</summary>
        Tian = 2
    }
}
