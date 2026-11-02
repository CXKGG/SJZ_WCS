using Model.Entities;

namespace Interface.Scheduling
{
    /// <summary>
    /// 拆垛任务调度：按《石家庄流程图20260929》推进 TaskInfo 的状态。
    /// 由后台服务 <c>TaskSchedulingBackgroundService</c> 定时调用 TickAsync，接口/前端只做查询与人工干预。
    /// 协议、机器人、WMS 等尚未确定的外部交互，方法先留空（TODO），后续补充。
    /// </summary>
    public interface ITaskSchedulingService
    {
        /// <summary>执行一轮调度（处理新任务、推进处理中任务），返回本轮新增任务数</summary>
        /// <param name="cancellationToken">取消令牌</param>
        Task<int> TickAsync(CancellationToken cancellationToken = default);

        /// <summary>查询未结束的任务（New/WaitStation/WaitRgv/RobotDestacking/ReturnToStacker/WaitPickStation/WaitExit）</summary>
        Task<List<TaskInfo>> GetActiveTasksAsync();

        /// <summary>人工暂停一个任务</summary>
        /// <param name="taskId">TaskInfo.id</param>
        Task<bool> PauseAsync(long taskId);

        /// <summary>人工恢复一个暂停的任务</summary>
        /// <param name="taskId">TaskInfo.id</param>
        Task<bool> ResumeAsync(long taskId);
    }
}
