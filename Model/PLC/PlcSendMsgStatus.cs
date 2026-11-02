namespace Model.PLC
{
    /// <summary>
    /// PLC 发送缓存状态：描述一条下发报文当前处于哪个阶段。
    /// 表里按 int 存储，枚举值确定后不要改动，避免历史数据含义变化。
    /// </summary>
    public enum PlcSendMsgStatus
    {
        /// <summary>待发送：刚写入缓存，尚未尝试发送</summary>
        Pending = 0,

        /// <summary>已发送：PLC 写入成功</summary>
        Sent = 1,

        /// <summary>发送失败待重试：本次发送失败，后台服务会继续重试</summary>
        Failed = 2,

        /// <summary>已放弃：重试次数达到上限，需要人工处理（可在查询接口里手动重发）</summary>
        Abandoned = 3
    }
}
