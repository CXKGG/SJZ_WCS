using Interface.Logs;
using Interface.PLC;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Model.Logs;

namespace Service.PLC
{
    /// <summary>
    /// PLC 业务后台服务（全项目只有这一个 PLC 后台服务）：
    /// 按固定间隔做三件事——重连断线设备、采集 DB600/DB500 刷新内存快照、处理发送缓存。
    /// <para>
    /// 间隔取 <c>PlcSettings:PollIntervalMs</c>（默认 500ms）；
    /// 重连按 <c>PlcSettings:ReconnectSeconds</c>（默认 5 秒）折算成循环次数；
    /// 发送队列按 <c>SendMsgSettings:PollSeconds</c>（默认 1 秒）折算成循环次数。
    /// </para>
    /// <para>
    /// 单个后台服务内串行执行，避免原来 3 个后台服务各自开循环互相抢同一批 PLC 连接。
    /// </para>
    /// </summary>
    public class PlcBackgroundService : BackgroundService
    {
        private readonly IPlcService _plcService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<PlcBackgroundService> _logger;
        private readonly ILogService _logService;

        /// <summary>
        /// 构造 PLC 后台服务。
        /// </summary>
        /// <param name="plcService">PLC 服务（连接、读写、快照、发送缓存都走它）</param>
        /// <param name="configuration">应用配置</param>
        /// <param name="logger">进程日志</param>
        /// <param name="logService">业务日志服务（启动/停止等系统日志）</param>
        public PlcBackgroundService(IPlcService plcService, IConfiguration configuration,
            ILogger<PlcBackgroundService> logger, ILogService logService)
        {
            _plcService = plcService;
            _configuration = configuration;
            _logger = logger;
            _logService = logService;
        }

        /// <summary>主循环：重连 → 采集 → 发送队列，直到应用停止。</summary>
        /// <param name="stoppingToken">应用停止令牌</param>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // 主循环间隔：PLC 采集节奏，最低 100ms，防止配置成 0/负数时占满 CPU
            int intervalMs = _configuration.GetValue("PlcSettings:PollIntervalMs", 500);
            if (intervalMs < 100)
            {
                intervalMs = 100;
            }

            // 采集开关：现场调试时可以只连设备不采集
            bool pollEnabled = _configuration.GetValue("PlcSettings:PollEnabled", true);

            // 自动重连开关与间隔（秒）
            bool autoReconnect = _configuration.GetValue("PlcSettings:AutoReconnect", true);
            int reconnectSeconds = _configuration.GetValue("PlcSettings:ReconnectSeconds", 5);
            if (reconnectSeconds < 1)
            {
                reconnectSeconds = 1;
            }

            // 发送缓存：轮询间隔（秒）与每批条数
            int sendSeconds = _configuration.GetValue("SendMsgSettings:PollSeconds", 1);
            if (sendSeconds < 1)
            {
                sendSeconds = 1;
            }

            int batchSize = _configuration.GetValue("SendMsgSettings:BatchSize", 50);
            if (batchSize < 1)
            {
                batchSize = 1;
            }

            // 把"秒"折算成主循环次数：例如 500ms 主循环 + 5 秒重连 = 每 10 圈重连一次
            int loopsPerReconnect = reconnectSeconds * 1000 / intervalMs;
            if (loopsPerReconnect < 1)
            {
                loopsPerReconnect = 1;
            }

            int loopsPerSend = sendSeconds * 1000 / intervalMs;
            if (loopsPerSend < 1)
            {
                loopsPerSend = 1;
            }

            _logger.LogInformation(
                "PLC 后台服务启动：主循环 {Interval}ms，采集={PollEnabled}，自动重连={AutoReconnect}（每 {Reconnect} 秒），发送队列每 {Send} 秒最多 {Batch} 条",
                intervalMs, pollEnabled, autoReconnect, reconnectSeconds, sendSeconds, batchSize);
            _logService.WriteSystem(LogRiskLevel.Low, "PLC", "Startup",
                "PLC 后台服务启动：采集=" + pollEnabled + "，自动重连=" + autoReconnect);

            int loop = 0;
            while (stoppingToken.IsCancellationRequested == false)
            {
                try
                {
                    // 1) 重连：按 ReconnectSeconds 折算的圈数执行
                    if (autoReconnect && loop % loopsPerReconnect == 0)
                    {
                        await _plcService.ReconnectDisconnectedAsync(stoppingToken);
                    }

                    // 2) 采集：每圈都读 DB600/DB500，刷新任务调度用的内存快照
                    if (pollEnabled)
                    {
                        await _plcService.PollOnceAsync(stoppingToken);
                    }

                    // 3) 发送缓存：按 PollSeconds 折算的圈数执行
                    if (loop % loopsPerSend == 0)
                    {
                        await _plcService.ProcessSendQueueAsync(batchSize, stoppingToken);
                    }

                    loop = loop + 1;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // 后台服务不能因为单轮异常退出（宿主默认会因此停掉整个应用）
                    _logger.LogError(ex, "PLC 后台服务单轮执行异常，下一轮继续");
                    _logService.WriteException(LogCategory.System, LogRiskLevel.Medium,
                        "PLC", "BackgroundLoop", "PLC 后台服务单轮执行异常", ex);
                }

                try
                {
                    await Task.Delay(intervalMs, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _logger.LogInformation("PLC 后台服务已停止");
        }
    }
}
