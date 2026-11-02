using Interface.Scheduling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Service.Tasks
{
    /// <summary>
    /// 任务调度后台服务：按 TaskSettings:IntervalMs（默认 500ms）调用 <see cref="ITaskSchedulingService.TickAsync"/>，
    /// 和 PLC 采集使用同一个节奏，保证调度看到的是最新快照。
    /// </summary>
    public class TaskSchedulingBackgroundService : BackgroundService
    {
        private readonly ITaskSchedulingService _taskSchedulingService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<TaskSchedulingBackgroundService> _logger;

        /// <summary>
        /// 构造任务调度后台服务。
        /// </summary>
        /// <param name="taskSchedulingService">任务调度服务</param>
        /// <param name="configuration">应用配置</param>
        /// <param name="logger">进程日志</param>
        public TaskSchedulingBackgroundService(ITaskSchedulingService taskSchedulingService, IConfiguration configuration,
            ILogger<TaskSchedulingBackgroundService> logger)
        {
            _taskSchedulingService = taskSchedulingService;
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>按固定间隔执行调度，直到应用停止。</summary>
        /// <param name="stoppingToken">应用停止令牌</param>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // 任务调度服务可通过配置关闭（调试或维护时），默认启用
            bool enabled = _configuration.GetValue("TaskSettings:Enabled", true);
            if (enabled == false)
            {
                _logger.LogInformation("任务调度服务已关闭（TaskSettings:Enabled=false）");
                return;
            }
            int intervalMs = _configuration.GetValue("TaskSettings:IntervalMs", 500);
            if (intervalMs < 100)
            {
                intervalMs = 100;
            }

            _logger.LogInformation("任务调度服务启动：间隔 {Interval}ms", intervalMs);
            // 任务调度服务停止时，退出循环
            while (stoppingToken.IsCancellationRequested == false)
            {
                try
                {
                    // 调度服务内部已捕获异常并记录日志，不会抛出到这里
                    await _taskSchedulingService.TickAsync(stoppingToken);
                }
                // 任务调度服务停止时，退出循环
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // 单轮异常不影响下一轮；任务本身的状态异常在服务内已捕获并置为 Failed
                    _logger.LogError(ex, "任务调度轮次异常");
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
        }
    }
}
