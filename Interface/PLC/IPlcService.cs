using Model.PLC;

namespace Interface.PLC
{
    /// <summary>
    /// PLC 服务（业务层唯一入口）：统一管理所有 PLC/RGV 的 S7 连接、DB 读写、后台采集快照、发送缓存。
    /// <para>
    /// 原来拆成了 IPlcClient / IPlcManager / IPlcSnapshotService / IPlcSendMsgService 四个接口，
    /// 对这个规模的项目过度设计了；现在合并成一个服务，Controller、任务调度、后台服务都只依赖它。
    /// </para>
    /// <para>
    /// 约定：读 DB 用 <see cref="ReadDbAsync"/>，写 DB 用 <see cref="WriteDbAsync"/>；
    /// 后台采集的数据通过 <see cref="GetSnapshot"/> / <see cref="GetSnapshots"/> 直接从内存取，不再访问 S7。
    /// </para>
    /// </summary>
    public interface IPlcService
    {
        /// <summary>全部 PLC 配置（appsettings.json 的 Plcs 节，顺序不变）</summary>
        IReadOnlyList<PlcConfig> GetConfigs();

        /// <summary>按设备编码取配置，找不到返回 null</summary>
        /// <param name="name">设备编码，如 PLC1、RGV01</param>
        PlcConfig? GetConfig(string name);

        /// <summary>全部设备的配置 + 实时连接状态</summary>
        IReadOnlyList<PlcStatus> GetAllStatus();

        /// <summary>单台设备的配置 + 实时连接状态，找不到返回 null</summary>
        /// <param name="name">设备编码</param>
        PlcStatus? GetStatus(string name);

        /// <summary>设备当前是否已连接</summary>
        /// <param name="name">设备编码</param>
        bool IsConnected(string name);

        /// <summary>连接一台设备；IP 为空或连接失败返回 false，失败原因见 <see cref="GetStatus"/> 的 LastError</summary>
        /// <param name="name">设备编码</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task<bool> ConnectAsync(string name, CancellationToken cancellationToken = default);

        /// <summary>断开一台设备（等待后台自动重连的请先关闭 PlcSettings:AutoReconnect）</summary>
        /// <param name="name">设备编码</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task DisconnectAsync(string name, CancellationToken cancellationToken = default);

        /// <summary>读取 DB 块原始字节</summary>
        /// <param name="name">设备编码</param>
        /// <param name="dbNumber">DB 块号</param>
        /// <param name="startByte">起始字节</param>
        /// <param name="length">读取长度</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <param name="writeLog">是否写设备交互日志；后台轮询传 false，避免刷日志</param>
        Task<PlcReadResult> ReadDbAsync(string name, int dbNumber, int startByte, int length,
            CancellationToken cancellationToken = default, bool writeLog = true);

        /// <summary>向 DB 块写入原始字节</summary>
        /// <param name="name">设备编码</param>
        /// <param name="dbNumber">DB 块号</param>
        /// <param name="startByte">起始字节</param>
        /// <param name="data">要写入的字节</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task<PlcWriteResult> WriteDbAsync(string name, int dbNumber, int startByte, byte[] data,
            CancellationToken cancellationToken = default);

        /// <summary>取一台 PLC 的实时快照（后台采集，内存读取，不访问 S7）</summary>
        /// <param name="plccode">设备编码</param>
        PlcSnapshot? GetSnapshot(string plccode);

        /// <summary>取全部 PLC 的实时快照</summary>
        IReadOnlyList<PlcSnapshot> GetSnapshots();

        /// <summary>采集一轮：读所有拆垛 PLC 的 DB600/DB500 并更新内存快照，返回本轮采集的设备数（后台服务调用）</summary>
        /// <param name="cancellationToken">取消令牌</param>
        Task<int> PollOnceAsync(CancellationToken cancellationToken = default);

        /// <summary>重连所有未连接的设备（后台服务定时调用）</summary>
        /// <param name="cancellationToken">取消令牌</param>
        Task ReconnectDisconnectedAsync(CancellationToken cancellationToken = default);

        /// <summary>处理一批发送缓存：从 PlcSendMsg 表取待发送记录逐条写入，返回本轮处理条数（后台服务调用）</summary>
        /// <param name="batchSize">每轮最多处理条数</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task<int> ProcessSendQueueAsync(int batchSize, CancellationToken cancellationToken = default);

        /// <summary>写入一条发送缓存（先落库再发送），返回主键</summary>
        /// <param name="plccode">目标设备编码</param>
        /// <param name="dbnumber">目标 DB 号</param>
        /// <param name="startbyte">起始字节</param>
        /// <param name="payload">要写入的字节</param>
        /// <param name="businesstype">业务类型（可空）</param>
        /// <param name="businessid">关联业务号（可空）</param>
        long Enqueue(string plccode, int dbnumber, int startbyte, byte[] payload,
            string? businesstype = null, string? businessid = null);

        /// <summary>手动重发：把记录重置为待发送，后台服务下一轮会重新发出</summary>
        /// <param name="id">缓存记录主键</param>
        bool RetrySendMsg(long id);
    }
}
