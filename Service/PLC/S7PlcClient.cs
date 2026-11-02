using System.Diagnostics;
using Interface.Logs;
using Interface.PLC;
using Microsoft.Extensions.Logging;
using Model.Logs;
using Model.PLC;
using S7.Net;

namespace Service.PLC
{
    /// <summary>
    /// 真实 S7 PLC 客户端：持有一台 PLC 的一条物理连接，负责连接维护与 DB 块字节读写。
    /// S7.Net 的 Plc 对象不是线程安全的，所有访问都用信号量串行化；
    /// 连接、读写都有超时上限，避免现场某台 PLC 掉线时把接口请求或后台线程卡死。
    /// S7.Net 的 Plc 在构造时要求合法 IP，所以按需创建：配置里 IP 为空时只保留占位，
    /// 等现场补充地址后再真正建连接。不使用任何模拟数据。
    /// <para>
    /// 设备交互日志：连接/断开/读写失败会写设备日志（PLC 设备写 <see cref="LogCategory.Plc"/>，
    /// RGV 设备写 <see cref="LogCategory.Rgv"/>，按 <c>DeviceType</c> 自动判定），
    /// 由 <see cref="ILogService"/> 直接写日志表，不阻塞通讯。
    /// </para>
    /// </summary>
    public class S7PlcClient
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly ILogger<S7PlcClient> _logger;
        private readonly ILogService _logService;
        private readonly TimeSpan _timeout;
        private readonly CpuType _cpuType;

        /// <summary>该设备的日志分类（按 DeviceType 区分 RGV / 输送线 PLC）</summary>
        private readonly LogCategory _logCategory;

        /// <summary>连接失败日志的抑制间隔：后台重连服务每 5 秒重试一次，失败日志按此间隔去重，避免刷屏</summary>
        private static readonly TimeSpan ConnectFailureLogInterval = TimeSpan.FromSeconds(30);

        /// <summary>最近一次写"连接失败"设备日志的时间（配合 <see cref="ConnectFailureLogInterval"/> 限流）</summary>
        private DateTime _lastConnectFailureLogAt = DateTime.MinValue;

        /// <summary>真实 S7 连接对象，首次连接成功前为 null</summary>
        private Plc? _plc;

        /// <summary>这台 PLC 的配置</summary>
        public PlcConfig Config { get; }

        public string Name => Config.Name;

        public bool IsConnected => _plc?.IsConnected == true;

        public string LastError { get; private set; } = "";

        /// <summary>最近一次连接成功的时间</summary>
        public DateTime? LastConnectTime { get; private set; }

        public S7PlcClient(PlcConfig config, ILoggerFactory loggerFactory, ILogService logService)
        {
            Config = config;
            _logService = logService;
            _logCategory = ResolveLogCategory(config);
            _logger = loggerFactory.CreateLogger<S7PlcClient>();
            _timeout = TimeSpan.FromMilliseconds(Math.Max(500, config.Timeout));

            if (!Enum.TryParse(config.CpuType, ignoreCase: true, out _cpuType))
            {
                throw new ArgumentException(
                    $"PLC[{config.Name}] 的 CpuType 配置无效：{config.CpuType}（可选 S71200/S71500/S7300/S7400/S7200/S7200Smart）");
            }

            if (!string.Equals(config.Protocol, "S7", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"PLC[{config.Name}] 的通讯协议配置无效：{config.Protocol}（当前仅支持 S7）");
            }

            if (config.Port <= 0 || config.Port > 65535)
            {
                throw new ArgumentException(
                    $"PLC[{config.Name}] 的端口配置无效：{config.Port}（S7 默认 102）");
            }
        }

        public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(Config.Ip))
            {
                LastError = "未配置 IP 地址";
                return false;
            }

            if (IsConnected) return true;

            Stopwatch stopwatch = Stopwatch.StartNew();
            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (IsConnected) return true;

                // 用 CancellationTokenSource 限制 S7.Net 建立 TCP + ISO-on-TCP 握手的时间
                using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(_timeout);

                try
                {
                    if (_plc == null)
                    {
                        _plc = CreatePlc();
                    }

                    Task openTask = _plc.OpenAsync(cts.Token);
                    await openTask.WaitAsync(_timeout, cancellationToken);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    ResetPlcAfterFailure();
                    LastError = $"连接超时（{_timeout.TotalMilliseconds:0} ms 内无响应）";
                    _logger.LogWarning("PLC[{Name}] {Ip} {Error}", Name, Config.Ip, LastError);
                    WriteConnectFailureLog(LastError, stopwatch.ElapsedMilliseconds);
                    return false;
                }
                catch (TimeoutException)
                {
                    ResetPlcAfterFailure();
                    LastError = $"连接超时（{_timeout.TotalMilliseconds:0} ms 内无响应）";
                    _logger.LogWarning("PLC[{Name}] {Ip} {Error}", Name, Config.Ip, LastError);
                    WriteConnectFailureLog(LastError, stopwatch.ElapsedMilliseconds);
                    return false;
                }
                catch (Exception ex)
                {
                    ResetPlcAfterFailure();
                    LastError = ex.Message;
                    _logger.LogWarning("PLC[{Name}] {Ip} 连接失败：{Error}", Name, Config.Ip, ex.Message);
                    WriteConnectFailureLog($"连接失败：{ex.Message}", stopwatch.ElapsedMilliseconds);
                    return false;
                }

                if (!_plc.IsConnected)
                {
                    ResetPlcAfterFailure();
                    LastError = "Open 之后连接状态仍为断开";
                    _logger.LogWarning("PLC[{Name}] {Ip} {Error}", Name, Config.Ip, LastError);
                    WriteConnectFailureLog(LastError, stopwatch.ElapsedMilliseconds);
                    return false;
                }

                LastError = "";
                LastConnectTime = DateTime.Now;
                _lastConnectFailureLogAt = DateTime.MinValue;
                _logger.LogInformation("PLC[{Name}] {Ip} 连接成功", Name, Config.Ip);
                WriteDeviceLog(LogRiskLevel.Low, "Connect", "连接成功",
                    $"IP={Config.Ip}; 端口={Config.Port}; 型号={Config.CpuType}; 机架/插槽={Config.Rack}/{Config.Slot}",
                    success: true, elapsedMs: stopwatch.ElapsedMilliseconds);
                return true;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                bool wasConnected = _plc?.IsConnected == true;
                if (wasConnected) _plc!.Close();
                LastError = "";
                _logger.LogInformation("PLC[{Name}] 已断开连接", Name);
                if (wasConnected)
                {
                    WriteDeviceLog(LogRiskLevel.Medium, "Disconnect", "已主动断开连接");
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _logger.LogWarning("PLC[{Name}] 断开连接失败：{Error}", Name, ex.Message);
                WriteDeviceLog(LogRiskLevel.Medium, "Disconnect", $"断开连接失败：{ex.Message}", success: false);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<PlcReadResult> ReadDbAsync(int dbNumber, int startByte, int length,
            CancellationToken cancellationToken = default, bool writeLog = true)
        {
            PlcReadResult result = new();
            Stopwatch stopwatch = Stopwatch.StartNew();

            await _gate.WaitAsync(cancellationToken);
            try
            {
                Plc? plc = _plc;
                if (plc == null || !plc.IsConnected)
                {
                    return FailRead(result, $"PLC[{Name}] 未连接", writeLog);
                }

                // S7.Net 的读写是同步阻塞的，丢到线程池执行；再用 WaitAsync 兜底限制总耗时
                try
                {
                    TimeSpan totalTimeout = _timeout + TimeSpan.FromMilliseconds(1000);
                    Task<byte[]> readTask = Task.Run(
                        () => plc.ReadBytes(DataType.DataBlock, dbNumber, startByte, length), cancellationToken);
                    result.Data = await readTask.WaitAsync(totalTimeout, cancellationToken);
                    result.Success = true;
                }
                catch (TimeoutException)
                {
                    result = FailRead(result, $"PLC[{Name}] 读取 DB{dbNumber} 超时", writeLog);
                }
                catch (Exception ex)
                {
                    result = FailRead(result, ex.Message, writeLog);
                }

                if (writeLog && result.Success && result.Data is not null)
                {
                    WriteDeviceLog(LogRiskLevel.Low, "ReadDb",
                        $"读取 DB{dbNumber} 成功，{result.Data.Length} 字节",
                        $"DB{dbNumber}; 起始={startByte}; 长度={length}; 数据={Convert.ToHexString(result.Data)}",
                        success: true, elapsedMs: stopwatch.ElapsedMilliseconds);
                }

                return result;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<PlcWriteResult> WriteDbAsync(int dbNumber, int startByte, byte[] data,
            CancellationToken cancellationToken = default)
        {
            PlcWriteResult result = new();
            Stopwatch stopwatch = Stopwatch.StartNew();

            if (data == null || data.Length == 0)
            {
                result.ErrorMessage = "写入数据不能为空";
                return result;
            }

            await _gate.WaitAsync(cancellationToken);
            try
            {
                Plc? plc = _plc;
                if (plc == null || !plc.IsConnected)
                {
                    return FailWrite(result, $"PLC[{Name}] 未连接");
                }

                try
                {
                    // S7.Net 的读写是同步阻塞的，丢到线程池执行；再用 WaitAsync 兜底限制总耗时
                    TimeSpan totalTimeout = _timeout + TimeSpan.FromMilliseconds(1000);
                    Task writeTask = Task.Run(
                        () => plc.WriteBytes(DataType.DataBlock, dbNumber, startByte, data), cancellationToken);
                    
                    await writeTask.WaitAsync(totalTimeout, cancellationToken);// 兜底总耗时，避免线程池被卡死
                    result.Success = true;
                }
                catch (TimeoutException)
                {
                    result = FailWrite(result, $"PLC[{Name}] 写入 DB{dbNumber} 超时");
                }
                catch (Exception ex)
                {
                    result = FailWrite(result, ex.Message);
                }

                if (result.Success)
                {
                    WriteDeviceLog(LogRiskLevel.Low, "WriteDb",
                        $"写入 DB{dbNumber} 成功，{data.Length} 字节",
                        $"DB{dbNumber}; 起始={startByte}; 数据={Convert.ToHexString(data)}",
                        success: true, elapsedMs: stopwatch.ElapsedMilliseconds);
                }

                return result;
            }
            finally
            {
                _gate.Release();
            }
        }

        private Plc CreatePlc()
        {
            return new Plc(_cpuType, Config.Ip, Config.Port, (short)Config.Rack, (short)Config.Slot)
            {
                ReadTimeout = Config.Timeout,
                WriteTimeout = Config.Timeout
            };
        }

        /// <summary>连接失败后丢弃当前连接对象，下一次重连时重新创建（调用方需持有 _gate）</summary>
        private void ResetPlcAfterFailure()
        {
            if (_plc == null) return;

            try
            {
                _plc.Close();
            }
            catch
            {
                // 连接本就失败，关闭异常忽略即可
            }

            _plc = null;
        }

        /// <summary>
        /// 按设备类型决定设备日志分类（封装，避免每处调用都判断）：
        /// DeviceType=RGV → <see cref="LogCategory.Rgv"/>；其余（输送线/拆垛段 PLC）→ <see cref="LogCategory.Plc"/>。
        /// </summary>
        /// <param name="config">PLC 配置</param>
        private static LogCategory ResolveLogCategory(PlcConfig config)
        {
            return string.Equals(config.DeviceType, "RGV", StringComparison.OrdinalIgnoreCase)
                ? LogCategory.Rgv
                : LogCategory.Plc;
        }

        /// <summary>
        /// 写一条设备交互日志（便捷封装）：自动补 DeviceType 对应分类、模块名 "PLC/{Name}"、目标设备名与设备 IP，
        /// 失败时按风险等级标记；由 ILogService 直接写日志表，不影响通讯耗时。
        /// </summary>
        /// <param name="riskLevel">风险等级：正常交互 Low，失败 Medium</param>
        /// <param name="action">动作，如 Connect / Disconnect / ReadDb / WriteDb</param>
        /// <param name="content">摘要</param>
        /// <param name="detail">明细（IP、DB 号、报文 hex 等）</param>
        /// <param name="success">是否成功</param>
        /// <param name="elapsedMs">耗时（毫秒）</param>
        private void WriteDeviceLog(LogRiskLevel riskLevel, string action, string content,
            string? detail = null, bool success = true, long elapsedMs = 0)
        {
            LogEntry entry = LogEntry.Create(_logCategory, riskLevel, $"PLC/{Name}", action, content);
            entry.Detail = detail ?? "";
            entry.Target = Name;
            entry.IpAddress = Config.Ip;
            entry.Success = success;
            entry.ElapsedMs = elapsedMs;
            _logService.Write(entry);
        }

        /// <summary>
        /// 连接失败日志（带限流）：后台重连每 5 秒重试时同一台设备最多每 30 秒记一条，
        /// 避免现场断线期间把日志表刷满；连接成功后重置限流状态。
        /// </summary>
        /// <param name="message">失败原因</param>
        /// <param name="elapsedMs">本次尝试耗时（毫秒）</param>
        private void WriteConnectFailureLog(string message, long elapsedMs)
        {
            DateTime now = DateTime.Now;
            if (now - _lastConnectFailureLogAt < ConnectFailureLogInterval)
            {
                return;
            }

            _lastConnectFailureLogAt = now;
            WriteDeviceLog(LogRiskLevel.Medium, "Connect", message, success: false, elapsedMs: elapsedMs);
        }

        /// <summary>读取失败时设置 LastError 并返回失败结果（writeLog=false 只记控制台日志，不写设备交互日志，供后台轮询使用）</summary>
        private PlcReadResult FailRead(PlcReadResult result, string error, bool writeLog = true)
        {
            LastError = error;
            result.Success = false;
            result.ErrorMessage = error;
            if (writeLog)
            {
                _logger.LogWarning("PLC[{Name}] 读取失败：{Error}", Name, error);
                WriteDeviceLog(LogRiskLevel.Medium, "ReadDb", error, success: false);
            }
            else
            {
                // 后台轮询每 500ms 一次，失败时只在控制台打 Debug，避免刷屏；
                // 数据库日志由 PLC 业务服务按"失败状态变化"记一条（轮询时不逐次写）。
                _logger.LogDebug("PLC[{Name}] 轮询读取失败：{Error}", Name, error);
            }

            return result;
        }

        /// <summary>写入失败时设置 LastError 并返回失败结果（内部已记录日志）</summary>
        private PlcWriteResult FailWrite(PlcWriteResult result, string error)
        {
            LastError = error;
            result.Success = false;
            result.ErrorMessage = error;
            _logger.LogWarning("PLC[{Name}] 写入失败：{Error}", Name, error);
            WriteDeviceLog(LogRiskLevel.Medium, "WriteDb", error, success: false);
            return result;
        }
    }
}
