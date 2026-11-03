using System.Collections.Concurrent;
using Interface.Logs;
using Interface.PLC;
using Microsoft.Extensions.Logging;
using Model.Entities;
using Model.Logs;
using Model.PLC;
using Model.Protocol;
using Service.Protocol;
using SqlSugar;

namespace Service.PLC
{
    /// <summary>
    /// PLC 服务（单例）：一个类管全部 PLC/RGV 的连接、DB 读写、后台采集快照、发送缓存与重连。
    /// <para>
    /// 原来拆成 PlcManager + S7PlcClient + PlcSnapshotService + PlcSendMsgService + 4 个接口，
    /// 小项目里太绕；现在对外只有 <see cref="IPlcService"/> 一个入口，
    /// 只有 S7 的连接/读写细节放在 <see cref="S7PlcClient"/> 里，其余都合并到本类。
    /// </para>
    /// <para>
    /// 三个后台服务（PLC 业务、RGV 业务、任务调度）按固定间隔调用这里的 tick 方法：
    /// <see cref="ReconnectDisconnectedAsync"/>、<see cref="PollOnceAsync"/>、<see cref="ProcessSendQueueAsync"/>。
    /// </para>
    /// </summary>
    public class PlcService : IPlcService
    {
        private readonly ISqlSugarClient _db;
        private readonly ILogService _logService;
        private readonly ILogger<PlcService> _logger;

        // 配置顺序（接口输出、快照顺序都按它来，和 appsettings.json 一致）
        private readonly List<string> _names = new List<string>();

        // 设备编码 → S7 客户端（大小写不敏感）
        private readonly Dictionary<string, S7PlcClient> _clients =
            new Dictionary<string, S7PlcClient>(StringComparer.OrdinalIgnoreCase);

        // PLC 实时快照：更新时整体替换引用，读方拿到的总是完整对象
        private readonly ConcurrentDictionary<string, PlcSnapshot> _snapshots =
            new ConcurrentDictionary<string, PlcSnapshot>(StringComparer.OrdinalIgnoreCase);

        // 保护轮询失败去重字典
        private readonly object _pollLogLock = new object();

        // 每台 PLC 每块数据最近一次的失败信息：失败原因不变就不重复写日志
        private readonly Dictionary<string, string> _lastPollErrors =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 构造 PLC 服务：按 appsettings.json 的 Plcs 配置创建每台设备的 S7 客户端。
        /// 配置非法（缺 Name、重复、协议/型号/端口不对）的条目跳过并记日志，不影响其余设备启动。
        /// </summary>
        /// <param name="db">SqlSugar 客户端（发送缓存落库用）</param>
        /// <param name="configs">PLC 配置列表（来自 appsettings.json 的 Plcs 节）</param>
        /// <param name="logService">日志服务</param>
        /// <param name="loggerFactory">进程日志工厂（每台设备客户端各要一个类别日志）</param>
        public PlcService(ISqlSugarClient db, List<PlcConfig> configs, ILogService logService, ILoggerFactory loggerFactory)
        {
            _db = db;
            _logService = logService;
            _logger = loggerFactory.CreateLogger<PlcService>();

            foreach (PlcConfig config in configs)
            {
                if (string.IsNullOrWhiteSpace(config.Name))
                {
                    _logger.LogWarning("Plcs 配置里有一条缺少 Name，已跳过");
                    continue;
                }

                if (_clients.ContainsKey(config.Name))
                {
                    _logger.LogWarning("PLC 配置 Name 重复：{Name}，已跳过", config.Name);
                    continue;
                }

                try
                {
                    _clients[config.Name] = new S7PlcClient(config, loggerFactory, logService);
                    _names.Add(config.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "PLC[{Name}] 配置初始化失败，已跳过：{Error}", config.Name, ex.Message);
                }
            }

            _logger.LogInformation("PLC 配置加载完成，共 {Count} 台：{Names}", _clients.Count, string.Join("、", _names));
        }

        /// <summary>全部 PLC 配置（按配置顺序）</summary>
        public IReadOnlyList<PlcConfig> GetConfigs()
        {
            List<PlcConfig> list = new List<PlcConfig>(_names.Count);
            foreach (string name in _names)
            {
                S7PlcClient client = _clients[name];
                list.Add(client.Config);
            }

            return list;
        }

        /// <summary>按设备编码取配置，找不到返回 null</summary>
        /// <param name="name">设备编码，如 PLC1、RGV01</param>
        public PlcConfig? GetConfig(string name)
        {
            S7PlcClient? client;
            if (_clients.TryGetValue(name, out client) == false)
            {
                return null;
            }

            return client.Config;
        }

        /// <summary>全部设备的配置 + 实时连接状态</summary>
        public IReadOnlyList<PlcStatus> GetAllStatus()
        {
            List<PlcStatus> list = new List<PlcStatus>(_names.Count);
            foreach (string name in _names)
            {
                list.Add(ToStatus(_clients[name]));
            }

            return list;
        }

        /// <summary>单台设备的配置 + 实时连接状态，找不到返回 null</summary>
        /// <param name="name">设备编码</param>
        public PlcStatus? GetStatus(string name)
        {
            S7PlcClient? client;
            if (_clients.TryGetValue(name, out client) == false)
            {
                return null;
            }

            return ToStatus(client);
        }

        /// <summary>设备当前是否已连接</summary>
        /// <param name="name">设备编码</param>
        public bool IsConnected(string name)
        {
            S7PlcClient? client;
            if (_clients.TryGetValue(name, out client) == false)
            {
                return false;
            }

            return client.IsConnected;
        }

        /// <summary>连接一台设备；IP 为空或连接失败返回 false，失败原因见 <see cref="GetStatus"/> 的 LastError</summary>
        /// <param name="name">设备编码</param>
        /// <param name="cancellationToken">取消令牌</param>
        public async Task<bool> ConnectAsync(string name, CancellationToken cancellationToken = default)
        {
            S7PlcClient? client;
            if (_clients.TryGetValue(name, out client) == false)
            {
                return false;
            }

            bool connected = await client.ConnectAsync(cancellationToken);
            SetConnected(name, connected);
            return connected;
        }

        /// <summary>断开一台设备</summary>
        /// <param name="name">设备编码</param>
        /// <param name="cancellationToken">取消令牌</param>
        public async Task DisconnectAsync(string name, CancellationToken cancellationToken = default)
        {
            S7PlcClient? client;
            if (_clients.TryGetValue(name, out client) == false)
            {
                return;
            }

            await client.DisconnectAsync(cancellationToken);
            SetConnected(name, false);
        }

        /// <summary>读取 DB 块原始字节</summary>
        /// <param name="name">设备编码</param>
        /// <param name="dbNumber">DB 块号</param>
        /// <param name="startByte">起始字节</param>
        /// <param name="length">读取长度</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <param name="writeLog">是否写设备交互日志：后台轮询传 false，避免每 500ms 刷一条日志</param>
        public async Task<PlcReadResult> ReadDbAsync(string name, int dbNumber, int startByte, int length,
            CancellationToken cancellationToken = default, bool writeLog = true)
        {
            S7PlcClient? client;
            if (_clients.TryGetValue(name, out client) == false)
            {
                PlcReadResult missing = new PlcReadResult();
                missing.Success = false;
                missing.ErrorMessage = "未找到 PLC 配置：" + name;
                return missing;
            }

            return await client.ReadDbAsync(dbNumber, startByte, length, cancellationToken, writeLog);
        }

        /// <summary>向 DB 块写入原始字节</summary>
        /// <param name="name">设备编码</param>
        /// <param name="dbNumber">DB 块号</param>
        /// <param name="startByte">起始字节</param>
        /// <param name="data">要写入的字节</param>
        /// <param name="cancellationToken">取消令牌</param>
        public async Task<PlcWriteResult> WriteDbAsync(string name, int dbNumber, int startByte, byte[] data,
            CancellationToken cancellationToken = default)
        {
            S7PlcClient? client;
            if (_clients.TryGetValue(name, out client) == false)
            {
                PlcWriteResult missing = new PlcWriteResult();
                missing.Success = false;
                missing.ErrorMessage = "未找到 PLC 配置：" + name;
                return missing;
            }

            return await client.WriteDbAsync(dbNumber, startByte, data, cancellationToken);
        }

        /// <summary>取一台 PLC 的实时快照（后台采集，内存读取，不访问 S7）</summary>
        /// <param name="plccode">设备编码</param>
        public PlcSnapshot? GetSnapshot(string plccode)
        {
            PlcSnapshot? snapshot;
            bool found = _snapshots.TryGetValue(plccode, out snapshot);
            if (found == false)
            {
                return null;
            }

            return snapshot;
        }

        /// <summary>取全部 PLC 的实时快照（按配置顺序）</summary>
        public IReadOnlyList<PlcSnapshot> GetSnapshots()
        {
            List<PlcSnapshot> list = new List<PlcSnapshot>(_names.Count);
            foreach (string name in _names)
            {
                PlcSnapshot? snapshot = GetSnapshot(name);
                if (snapshot != null)
                {
                    list.Add(snapshot);
                }
            }

            return list;
        }

        /// <summary>
        /// 采集一轮：对每台配置了读写 DB 号的 PLC，读 DB600（PLC → WCS）和 DB500（WCS → PLC）整块并解析，
        /// 更新内存快照；没有配置 DB 号的设备（当前 RGV）跳过。返回本轮采集成功的设备数。
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        public async Task<int> PollOnceAsync(CancellationToken cancellationToken = default)
        {
            int count = 0;
            List<Task> tasks = new List<Task>();
            foreach (string name in _names)
            {
                S7PlcClient client = _clients[name];
                if (client.Config.ReadDbNumber <= 0 || client.Config.WriteDbNumber <= 0)
                {
                    continue;
                }

                tasks.Add(PollOneAsync(client, cancellationToken));
            }

            if (tasks.Count == 0)
            {
                return 0;
            }

            await Task.WhenAll(tasks);
            count = tasks.Count;
            return count;
        }

        /// <summary>重连所有未连接的设备（已连接的跳过；未填 IP 的设备内部会直接返回 false，不会发起 TCP）</summary>
        /// <param name="cancellationToken">取消令牌</param>
        public async Task ReconnectDisconnectedAsync(CancellationToken cancellationToken = default)
        {
            List<Task> tasks = new List<Task>();
            foreach (string name in _names)
            {
                S7PlcClient client = _clients[name];
                if (client.IsConnected)
                {
                    continue;
                }

                tasks.Add(CheckOneAsync(client, cancellationToken));
            }

            if (tasks.Count == 0)
            {
                return;
            }

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// 处理一批发送缓存：从 PlcSendMsg 表取待发送/待重试记录，按创建时间顺序逐条写入 PLC。
        /// 成功置已发送；失败重试次数 +1，达到上限置已放弃并记高风险日志。
        /// </summary>
        /// <param name="batchSize">每轮最多处理条数</param>
        /// <param name="cancellationToken">取消令牌</param>
        public async Task<int> ProcessSendQueueAsync(int batchSize, CancellationToken cancellationToken = default)
        {
            if (batchSize < 1)
            {
                batchSize = 1;
            }

            int pending = (int)PlcSendMsgStatus.Pending;
            int failed = (int)PlcSendMsgStatus.Failed;

            List<PlcSendMsg> messages = _db.Queryable<PlcSendMsg>()
                .Where(x => x.status == pending || x.status == failed)
                .OrderBy(x => x.createtime)
                .Take(batchSize)
                .ToList();

            int count = 0;
            foreach (PlcSendMsg msg in messages)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                await SendOneAsync(msg, cancellationToken);
                count = count + 1;
            }

            return count;
        }

        /// <summary>写入一条发送缓存（先落库再发送），返回主键</summary>
        /// <param name="plccode">目标设备编码</param>
        /// <param name="dbnumber">目标 DB 号</param>
        /// <param name="startbyte">起始字节</param>
        /// <param name="payload">要写入的字节</param>
        /// <param name="businesstype">业务类型（可空）</param>
        /// <param name="businessid">关联业务号（可空）</param>
        public long Enqueue(string plccode, int dbnumber, int startbyte, byte[] payload,
            string? businesstype = null, string? businessid = null)
        {
            if (string.IsNullOrWhiteSpace(plccode))
            {
                throw new ArgumentException("plccode 不能为空", nameof(plccode));
            }

            if (dbnumber <= 0)
            {
                throw new ArgumentException("dbnumber 必须大于 0", nameof(dbnumber));
            }

            if (startbyte < 0)
            {
                throw new ArgumentException("startbyte 不能小于 0", nameof(startbyte));
            }

            if (payload == null || payload.Length == 0)
            {
                throw new ArgumentException("payload 不能为空", nameof(payload));
            }

            DateTime now = DateTime.Now;
            PlcSendMsg msg = new PlcSendMsg();
            msg.plccode = plccode;
            msg.dbnumber = dbnumber;
            msg.startbyte = startbyte;
            msg.payload = HexCodec.ToHex(payload);
            msg.businesstype = businesstype ?? "";
            msg.businessid = businessid ?? "";
            msg.status = (int)PlcSendMsgStatus.Pending;
            msg.retrycount = 0;
            msg.maxretry = 3;
            msg.lasterror = "";
            msg.createtime = now;
            msg.sendtime = null;
            msg.updatetime = now;

            return _db.Insertable(msg).ExecuteReturnBigIdentity();
        }

        /// <summary>手动重发：把记录重置为待发送，后台服务下一轮会重新发出</summary>
        /// <param name="id">缓存记录主键</param>
        public bool RetrySendMsg(long id)
        {
            PlcSendMsg? msg = _db.Queryable<PlcSendMsg>().First(x => x.id == id);
            if (msg == null)
            {
                return false;
            }

            if (msg.status == (int)PlcSendMsgStatus.Sent)
            {
                return false;
            }

            msg.status = (int)PlcSendMsgStatus.Pending;
            msg.retrycount = 0;
            msg.lasterror = "";
            msg.updatetime = DateTime.Now;
            _db.Updateable(msg).ExecuteCommand();
            return true;
        }

        /// <summary>采集单台 PLC 一轮：先更新连接状态，再读 DB600/DB500。</summary>
        /// <param name="client">设备客户端</param>
        /// <param name="cancellationToken">取消令牌</param>
        private async Task PollOneAsync(S7PlcClient client, CancellationToken cancellationToken)
        {
            string name = client.Config.Name;
            SetConnected(name, client.IsConnected);

            int readDbNumber = client.Config.ReadDbNumber;
            PlcReadResult db600 = await client.ReadDbAsync(readDbNumber, 0, PLCDb600Codec.Length, cancellationToken, false);
            if (db600.Success && db600.Data != null)
            {
                DestackerDb600 model = PLCDb600Codec.Parse(db600.Data);
                SetDb600(name, model, true, "");
                LogPollRecovered(name, "DB600");
            }
            else
            {
                string error = db600.ErrorMessage ?? "读取失败";
                SetDb600(name, null, false, error);
                LogPollError(name, "DB600", error);
            }

            int writeDbNumber = client.Config.WriteDbNumber;
            PlcReadResult db500 = await client.ReadDbAsync(writeDbNumber, 0, PLCDb500Codec.Length, cancellationToken, false);
            if (db500.Success && db500.Data != null)
            {
                DestackerDb500 model = PLCDb500Codec.Parse(db500.Data);
                SetDb500(name, model, true, "");
                LogPollRecovered(name, "DB500");
            }
            else
            {
                string error = db500.ErrorMessage ?? "读取失败";
                SetDb500(name, null, false, error);
                LogPollError(name, "DB500", error);
            }
        }

        /// <summary>检查单台设备连接（用于重连）；连接失败只打进程日志，避免每 5 秒写一条数据库日志。</summary>
        /// <param name="client">设备客户端</param>
        /// <param name="cancellationToken">取消令牌</param>
        private async Task CheckOneAsync(S7PlcClient client, CancellationToken cancellationToken)
        {
            try
            {
                bool connected = await client.ConnectAsync(cancellationToken);
                SetConnected(client.Config.Name, connected);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.LogWarning("PLC[{Name}] 重连异常：{Error}", client.Config.Name, ex.Message);
            }
        }

        /// <summary>发送单条缓存：成功置已发送，失败按次数重试或放弃。</summary>
        /// <param name="msg">缓存记录</param>
        /// <param name="cancellationToken">取消令牌</param>
        private async Task SendOneAsync(PlcSendMsg msg, CancellationToken cancellationToken)
        {
            S7PlcClient? client;
            if (_clients.TryGetValue(msg.plccode, out client) == false)
            {
                MarkSendFailed(msg, "未找到 PLC 配置：" + msg.plccode);
                return;
            }

            byte[] payload;
            try
            {
                payload = HexCodec.FromHex(msg.payload);
            }
            catch (FormatException ex)
            {
                MarkSendFailed(msg, "缓存报文 hex 非法：" + ex.Message);
                return;
            }

            DateTime startTime = DateTime.Now;
            bool success = false;
            string errorMessage = "";

            try
            {
                PlcWriteResult result = await client.WriteDbAsync(msg.dbnumber, msg.startbyte, payload, cancellationToken);
                success = result.Success;
                if (success == false)
                {
                    errorMessage = result.ErrorMessage;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
            }

            long elapsedMs = (long)(DateTime.Now - startTime).TotalMilliseconds;

            if (success)
            {
                msg.status = (int)PlcSendMsgStatus.Sent;
                msg.sendtime = DateTime.Now;
                msg.lasterror = "";
                msg.updatetime = DateTime.Now;
                _db.Updateable(msg).ExecuteCommand();

                _logService.WritePlc(LogRiskLevel.Low, "PLC/" + msg.plccode, "SendMsg",
                    "发送缓存下发成功",
                    detail: "DB" + msg.dbnumber + "@" + msg.startbyte + " " + msg.payload,
                    target: msg.plccode, success: true, elapsedMs: elapsedMs, ipAddress: client.Config.Ip);
                return;
            }

            MarkSendFailed(msg, errorMessage, elapsedMs, client.Config.Ip);
        }

        /// <summary>发送失败处理：重试次数 +1，未到上限置待重试，到上限置已放弃并记高风险日志。</summary>
        /// <param name="msg">缓存记录</param>
        /// <param name="errorMessage">失败原因</param>
        /// <param name="elapsedMs">本次耗时</param>
        /// <param name="ipAddress">设备 IP</param>
        private void MarkSendFailed(PlcSendMsg msg, string errorMessage, long elapsedMs = 0, string ipAddress = "")
        {
            msg.retrycount = msg.retrycount + 1;
            msg.lasterror = errorMessage;
            msg.updatetime = DateTime.Now;

            if (msg.retrycount >= msg.maxretry)
            {
                msg.status = (int)PlcSendMsgStatus.Abandoned;
            }
            else
            {
                msg.status = (int)PlcSendMsgStatus.Failed;
            }

            _db.Updateable(msg).ExecuteCommand();

            if (msg.status == (int)PlcSendMsgStatus.Abandoned)
            {
                _logService.WritePlc(LogRiskLevel.High, "PLC/" + msg.plccode, "SendMsg",
                    "发送缓存重试达到上限，已放弃（需人工处理）",
                    detail: "DB" + msg.dbnumber + "@" + msg.startbyte + " " + msg.payload + "；原因：" + errorMessage,
                    target: msg.plccode, success: false, elapsedMs: elapsedMs, ipAddress: ipAddress);
                return;
            }

            _logService.WritePlc(LogRiskLevel.Medium, "PLC/" + msg.plccode, "SendMsg",
                "发送缓存下发失败，等待重试（第 " + msg.retrycount + " 次）",
                detail: "DB" + msg.dbnumber + "@" + msg.startbyte + " " + msg.payload + "；原因：" + errorMessage,
                target: msg.plccode, success: false, elapsedMs: elapsedMs, ipAddress: ipAddress);
        }

        /// <summary>把设备客户端转成对外展示的状态对象。</summary>
        /// <param name="client">设备客户端</param>
        private static PlcStatus ToStatus(S7PlcClient client)
        {
            PlcConfig config = client.Config;
            PlcStatus status = new PlcStatus();
            status.Name = config.Name;
            status.DisplayName = config.DisplayName;
            status.DeviceType = config.DeviceType;
            status.DeviceNo = config.DeviceNo;
            status.Ip = config.Ip;
            status.Protocol = config.Protocol;
            status.Port = config.Port;
            status.CpuType = config.CpuType;
            status.Rack = config.Rack;
            status.Slot = config.Slot;
            status.ReadDbNumber = config.ReadDbNumber;
            status.WriteDbNumber = config.WriteDbNumber;
            status.IsConnected = client.IsConnected;
            status.LastError = client.LastError;
            status.LastConnectTime = client.LastConnectTime;
            return status;
        }

        /// <summary>更新快照的连接状态。</summary>
        /// <param name="plccode">设备编码</param>
        /// <param name="connected">是否已连接</param>
        private void SetConnected(string plccode, bool connected)
        {
            PlcSnapshot snapshot = GetOrCreateSnapshot(plccode);
            snapshot.Connected = connected;
        }

        /// <summary>更新 DB600 快照：复制出新的快照对象后整体替换。</summary>
        /// <param name="plccode">设备编码</param>
        /// <param name="data">解析后的对象，读取失败传 null</param>
        /// <param name="ok">是否读取成功</param>
        /// <param name="error">失败说明</param>
        private void SetDb600(string plccode, DestackerDb600? data, bool ok, string error)
        {
            PlcSnapshot current = GetOrCreateSnapshot(plccode);
            PlcSnapshot next = CopySnapshot(current);
            next.Version = current.Version + 1;
            next.UpdatedAt = DateTime.Now;
            next.Db600 = data;
            next.Db600Ok = ok;
            next.Db600Error = error;
            if (ok)
            {
                next.Db600Time = next.UpdatedAt;
            }
            else
            {
                next.Db600Time = current.Db600Time;
            }

            _snapshots[plccode] = next;
        }

        /// <summary>更新 DB500 快照：同样整体替换。</summary>
        /// <param name="plccode">设备编码</param>
        /// <param name="data">解析后的对象，读取失败传 null</param>
        /// <param name="ok">是否读取成功</param>
        /// <param name="error">失败说明</param>
        private void SetDb500(string plccode, DestackerDb500? data, bool ok, string error)
        {
            PlcSnapshot current = GetOrCreateSnapshot(plccode);
            PlcSnapshot next = CopySnapshot(current);
            next.Version = current.Version + 1;
            next.UpdatedAt = DateTime.Now;
            next.Db500 = data;
            next.Db500Ok = ok;
            next.Db500Error = error;
            if (ok)
            {
                next.Db500Time = next.UpdatedAt;
            }
            else
            {
                next.Db500Time = current.Db500Time;
            }

            _snapshots[plccode] = next;
        }

        /// <summary>取快照；首次访问时创建空快照。</summary>
        /// <param name="plccode">设备编码</param>
        private PlcSnapshot GetOrCreateSnapshot(string plccode)
        {
            PlcSnapshot? existing;
            bool found = _snapshots.TryGetValue(plccode, out existing);
            if (found && existing != null)
            {
                return existing;
            }

            PlcSnapshot created = new PlcSnapshot();
            created.Plccode = plccode;
            created.UpdatedAt = DateTime.Now;
            _snapshots[plccode] = created;
            return created;
        }

        /// <summary>复制一份快照（只复制引用字段，DB600/DB500 对象本身只读使用）。</summary>
        /// <param name="current">当前快照</param>
        private static PlcSnapshot CopySnapshot(PlcSnapshot current)
        {
            PlcSnapshot next = new PlcSnapshot();
            next.Plccode = current.Plccode;
            next.Connected = current.Connected;
            next.Version = current.Version;
            next.UpdatedAt = current.UpdatedAt;
            next.Db600 = current.Db600;
            next.Db600Time = current.Db600Time;
            next.Db600Ok = current.Db600Ok;
            next.Db600Error = current.Db600Error;
            next.Db500 = current.Db500;
            next.Db500Time = current.Db500Time;
            next.Db500Ok = current.Db500Ok;
            next.Db500Error = current.Db500Error;
            return next;
        }

        /// <summary>读取失败：同一块数据失败原因没变就不重复记，避免断线期间刷日志。</summary>
        /// <param name="plccode">设备编码</param>
        /// <param name="block">数据块名，如 DB600</param>
        /// <param name="error">失败原因</param>
        private void LogPollError(string plccode, string block, string error)
        {
            string key = plccode + "|" + block;
            lock (_pollLogLock)
            {
                string? lastError;
                bool exists = _lastPollErrors.TryGetValue(key, out lastError);
                if (exists && lastError != null && string.Equals(lastError, error, StringComparison.Ordinal))
                {
                    return;
                }

                _lastPollErrors[key] = error;
            }

            _logService.WritePlc(LogRiskLevel.Medium, "PLC/" + plccode, "PollError",
                block + " 读取失败：" + error, target: plccode, success: false);
        }

        /// <summary>读取恢复：之前有失败才记一条，正常期间不写日志。</summary>
        /// <param name="plccode">设备编码</param>
        /// <param name="block">数据块名，如 DB600</param>
        private void LogPollRecovered(string plccode, string block)
        {
            string key = plccode + "|" + block;
            bool hadError;
            lock (_pollLogLock)
            {
                hadError = _lastPollErrors.Remove(key);
            }

            if (hadError == false)
            {
                return;
            }

            _logService.WritePlc(LogRiskLevel.Low, "PLC/" + plccode, "PollRecovered",
                block + " 读取恢复正常", target: plccode, success: true);
        }
    }
}
