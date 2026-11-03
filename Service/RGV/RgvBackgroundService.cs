using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Service.RGV
{
    /// <summary>
    /// RGV 业务后台服务：RGV 通讯协议（S7 地址表）现场还没给，业务逻辑无法先写，
    /// 这里先按配置空跑一个循环占位；协议确定后在本服务的循环里补 RGV 任务下发/状态采集即可。
    /// <para>
    /// 配置：<c>RgvSettings:Enabled</c>（默认 true）、<c>RgvSettings:IntervalMs</c>（默认 1000ms）。
    /// 不产生任何模拟数据。
    /// </para>
    /// </summary>
    public class RgvBackgroundService : BackgroundService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<RgvBackgroundService> _logger;

        /// <summary>
        /// 构造 RGV 业务后台服务。
        /// </summary>
        /// <param name="configuration">应用配置</param>
        /// <param name="logger">进程日志</param>
        public RgvBackgroundService(IConfiguration configuration, ILogger<RgvBackgroundService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>空循环：等 RGV 协议确定后在此写业务，当前只保持服务节奏。</summary>
        /// <param name="stoppingToken">应用停止令牌</param>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            bool enabled = _configuration.GetValue("RgvSettings:Enabled", true);
            if (enabled == false)
            {
                _logger.LogInformation("RGV 业务服务已关闭（RgvSettings:Enabled=false）");
                return;
            }

            int intervalMs = _configuration.GetValue("RgvSettings:IntervalMs", 1000);
            if (intervalMs < 100)
            {
                intervalMs = 100;
            }

            _logger.LogInformation("RGV 业务服务启动（协议未确定，暂为空循环）：间隔 {Interval}ms", intervalMs);

            while (stoppingToken.IsCancellationRequested == false)
            {
                try
                {
                    await Task.Delay(intervalMs, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                /// TODO RGV 协议确定后在这里补：读 RGV 状态、下发搬运任务、处理到位/完成信号。
            }

            _logger.LogInformation("RGV 业务服务已停止");
        }
    }
}
