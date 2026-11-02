using Interface.Logs;
using Interface.PLC;
using Interface.Scheduling;
using Microsoft.Extensions.Logging;
using Model.Entities;
using Model.Logs;
using Model.PLC;
using Model.Protocol;
using Model.Tasks;
using Service.Protocol;
using SqlSugar;
using TaskStatus = Model.Tasks.TaskStatus;

namespace Service.Tasks
{
    /// <summary>
    /// 拆垛任务调度实现。流程按《石家庄流程图20260929》拆成一个个 Step，每个 Step 对应流程图的判断节点，
    /// 任务当前走到哪一步存在 TaskInfo.status（见 <see cref="TaskStatus"/>）。
    /// 后台服务每 500ms 调一次 TickAsync，每次只推进一小步，所以 PLC 断线/上游慢都不会卡死线程。
    /// 已接入：任务来源 = PLC 到位信息（DB600 拆垛位托盘号）、拆垛工位 6105/6106 编号、
    /// 机器人拆垛完成 = PLC「拆盘完成托盘出」信号、叠盘机满 = DB600 垒托盘块「空盘满请求出盘」、
    /// 回叠盘机 = 写 DB500 叠盘机垒托盘块、去目标工位前判断相邻车公共行程冲突（有冲突就让行等待）。
    /// 拣选口完成 = RGV 小车 PLC 汇报（RGV 协议未提供，先留空）→ 再写回拆垛 PLC DB500「拣选口完成」。
    /// 任务按 4 个任务组分开推进（见 <see cref="TaskGroup"/>：组1~组3 每组 1 台 PLC + 2 台车，组4 = PLC4 + 车7 独享叠盘机），
    /// 组与组之间互不影响。
    /// 尚未确定：WMS 托盘类型（川/田）、RGV 的 S7 协议（小车空闲判断、拣选口完成汇报、输送拣选口动作、出口等待）。
    /// </summary>
    public class TaskScheduling : ITaskSchedulingService
    {
        private readonly ISqlSugarClient _db;
        private readonly IPlcService _plcService;
        private readonly ILogService _logService;
        private readonly ILogger<TaskScheduling> _logger;

        /// <summary>已提示过的空方法：同一空方法只提示一次，避免每 500ms 刷屏</summary>
        private readonly HashSet<string> _stubLogged = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>保护 _stubLogged（调度在后台线程，人工接口可能并发触发）</summary>
        private readonly object _stubLock = new object();

        /// <summary>已记录过「等待相邻车」日志的任务 id：同一任务只写一条，让行后清掉</summary>
        private readonly HashSet<long> _conflictLogged = new HashSet<long>();

        /// <summary>保护 _conflictLogged（后台调度线程与人工接口可能并发）</summary>
        private readonly object _conflictLock = new object();

        public TaskScheduling(ISqlSugarClient db, IPlcService plcService,
            ILogService logService, ILogger<TaskScheduling> logger)
        {
            _db = db;
            _plcService = plcService;
            _logService = logService;
            _logger = logger;
        }

        /// <summary>执行一轮调度：先取新任务落库，再逐个推进未结束的任务</summary>
        public async Task<int> TickAsync(CancellationToken cancellationToken = default)
        {
            // 1) 新任务来源：流程图「PLC托盘到位信息」→「输送线记录托盘时间、保存任务信息」
            List<TaskInfo> newTasks = await FetchNewTasksAsync(cancellationToken);
            foreach (TaskInfo task in newTasks)
            {
                // 任务调度后台服务停止时，直接退出
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (task.createtime == default(DateTime))
                {
                    task.createtime = DateTime.Now;
                }

                task.updatetime = DateTime.Now;
                task.status = (int)TaskStatus.New;
                await _db.Insertable(task).ExecuteReturnBigIdentityAsync();
                WriteLog(LogRiskLevel.Low, "任务产生", "新任务 " + task.taskno + "，托盘 " + task.palletno);
            }

            // 2) 推进未结束的任务：按 4 个任务组分开执行（每个组 = 1 台 PLC + 1~2 台 RGV），
            //    组与组互不影响：某一组的 PLC 断线或该组车忙，不会拖住其他组。
            TaskGroup[] groups = new TaskGroup[] { TaskGroup.Group1, TaskGroup.Group2, TaskGroup.Group3, TaskGroup.Group4 };
            foreach (TaskGroup group in groups)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                switch (group)
                {
                    case TaskGroup.Group1:
                        await TickGroupAsync(TaskGroup.Group1, cancellationToken);
                        break;
                    case TaskGroup.Group2:
                        await TickGroupAsync(TaskGroup.Group2, cancellationToken);
                        break;
                    case TaskGroup.Group3:
                        await TickGroupAsync(TaskGroup.Group3, cancellationToken);
                        break;
                    case TaskGroup.Group4:
                        await TickGroupAsync(TaskGroup.Group4, cancellationToken);
                        break;
                    default:
                        break;
                }
            }

            return newTasks.Count;
        }

        /// <summary>查询未结束的任务</summary>
        public async Task<List<TaskInfo>> GetActiveTasksAsync()
        {
            List<int> activeStatuses = new List<int>
            {
                (int)TaskStatus.New,
                (int)TaskStatus.WaitStation,
                (int)TaskStatus.WaitRgv,
                (int)TaskStatus.RobotDestacking,
                (int)TaskStatus.ReturnToStacker,
                (int)TaskStatus.WaitPickStation,
                (int)TaskStatus.WaitExit
            };

            return await _db.Queryable<TaskInfo>()
                .Where(x => activeStatuses.Contains(x.status))
                .OrderBy(x => x.id)
                .ToListAsync();
        }

        /// <summary>查询指定任务组内未结束的任务（只推进本组任务，组与组之间互不影响）</summary>
        /// <param name="group">任务组</param>
        private async Task<List<TaskInfo>> GetActiveTasksByGroupAsync(TaskGroup group)
        {
            int groupNo = (int)group;

            // SqlSugar 解析表达式时只认局部变量里的 List，所以这里把静态状态表复制到局部变量再查询
            List<int> activeStatuses = new List<int>(ActiveStatusValues);
            return await _db.Queryable<TaskInfo>()
                .Where(x => x.groupno == groupNo && activeStatuses.Contains(x.status))
                .OrderBy(x => x.id)
                .ToListAsync();
        }

        /// <summary>推进一个任务组：只处理本组任务，单个任务异常只标记它自己失败，不影响其他任务</summary>
        /// <param name="group">任务组</param>
        /// <param name="cancellationToken">取消令牌</param>
        private async Task TickGroupAsync(TaskGroup group, CancellationToken cancellationToken)
        {
            List<TaskInfo> tasks = await GetActiveTasksByGroupAsync(group);
            foreach (TaskInfo task in tasks)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    await ProcessTaskAsync(task, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "任务 {TaskNo}（组 {Group}）调度异常", task.taskno, group);
                    await SetStatusAsync(task, TaskStatus.Failed, "调度异常：" + ex.Message);
                    WriteLog(LogRiskLevel.High, "任务异常", "任务 " + task.taskno + " 调度异常：" + ex.Message);
                }
            }
        }

        /// <summary>
        /// 人工暂停任务：只允许暂停未结束的任务
        /// </summary>
        public async Task<bool> PauseAsync(long taskId)
        {
            TaskInfo? task = await _db.Queryable<TaskInfo>().FirstAsync(x => x.id == taskId);
            if (task == null)
            {
                return false;
            }

            TaskStatus current = (TaskStatus)task.status;
            if (current == TaskStatus.Completed || current == TaskStatus.Failed || current == TaskStatus.Paused)
            {
                return false;
            }

            await SetStatusAsync(task, TaskStatus.Paused, "人工暂停");
            WriteLog(LogRiskLevel.Medium, "任务暂停", "任务 " + task.taskno + " 人工暂停");
            return true;
        }

        /// <summary>
        /// 恢复暂停的任务：从 WaitStation 重新走一遍流程（工位/小车状态会重新判断）
        /// </summary>
        public async Task<bool> ResumeAsync(long taskId)
        {
            TaskInfo? task = await _db.Queryable<TaskInfo>().FirstAsync(x => x.id == taskId);
            if (task == null)
            {
                return false;
            }

            if ((TaskStatus)task.status != TaskStatus.Paused)
            {
                return false;
            }

            await SetStatusAsync(task, TaskStatus.WaitStation, "人工恢复");
            WriteLog(LogRiskLevel.Low, "任务恢复", "任务 " + task.taskno + " 人工恢复");
            return true;
        }

        /// <summary>
        /// 按当前状态推进一步，对应流程图的各个判断节点
        /// </summary>
        private async Task ProcessTaskAsync(TaskInfo task, CancellationToken cancellationToken)
        {
            TaskStatus status = (TaskStatus)task.status;
            switch (status)
            {
                case TaskStatus.New:
                    // 流程图：PLC 托盘到位信息
                    await StepWaitPalletArrivedAsync(task, cancellationToken);
                    break;
                case TaskStatus.WaitStation:
                    // 流程图：判断 1、2 号拆垛工位是否空闲
                    await StepWaitStationAsync(task, cancellationToken);
                    break;
                case TaskStatus.WaitRgv:
                    // 流程图：判断小车是否空闲（否 → 等待小车任务结束）
                    await StepWaitRgvAsync(task, cancellationToken);
                    break;
                case TaskStatus.RobotDestacking:
                    // 流程图：机器人拆垛工位 1/2 → 判断叠盘机是否满 → 判断托盘类型
                    await StepRobotDestackAsync(task, cancellationToken);
                    break;
                case TaskStatus.ReturnToStacker:
                    // 流程图：回川/田字型叠盘机 → 去目标工位前判断公共行程范围内相邻车任务（有冲突就等待）
                    await StepReturnToStackerAsync(task, cancellationToken);
                    break;
                case TaskStatus.WaitPickStation:
                    // 流程图：执行托盘回叠盘机、输送拣选口 → 判断是否有新任务
                    await StepWaitPickStationAsync(task, cancellationToken);
                    break;
                case TaskStatus.WaitExit:
                    // 流程图：回出口等待
                    await StepWaitExitAsync(task, cancellationToken);
                    break;
                default:
                    // Paused / Completed / Failed：不在自动推进范围内
                    break;
            }
        }

        /// <summary>Step：PLC 托盘到位 → 记录到位时间 → 等拆垛工位</summary>
        private async Task StepWaitPalletArrivedAsync(TaskInfo task, CancellationToken cancellationToken)
        {
            bool arrived = await HasPalletArrivedAsync(task.stationno, cancellationToken);
            if (!arrived)
            {
                return;
            }

            await SetStatusAsync(task, TaskStatus.WaitStation, "");
        }

        /// <summary>Step：判断 1、2 号拆垛工位是否空闲 → 判断小车是否空闲 → 机器人拆垛</summary>
        private async Task StepWaitStationAsync(TaskInfo task, CancellationToken cancellationToken)
        {
            int stationNo = await FindIdleDestackStationAsync(task, cancellationToken);
            if (stationNo <= 0)
            {
                return;
            }

            task.stationno = stationNo;
            bool rgvIdle = await IsRgvIdleAsync(task, cancellationToken);
            if (!rgvIdle)
            {
                await SetStatusAsync(task, TaskStatus.WaitRgv, "");
                return;
            }

            await SetStatusAsync(task, TaskStatus.RobotDestacking, "");
        }

        /// <summary>Step：等待小车任务结束；空闲后进入机器人拆垛</summary>
        private async Task StepWaitRgvAsync(TaskInfo task, CancellationToken cancellationToken)
        {
            bool rgvIdle = await IsRgvIdleAsync(task, cancellationToken);
            if (!rgvIdle)
            {
                return;
            }

            await SetStatusAsync(task, TaskStatus.RobotDestacking, "");
        }

        /// <summary>Step：机器人拆垛 → 叠盘机是否满 → 托盘类型（川/田）→ 回叠盘机</summary>
        private async Task StepRobotDestackAsync(TaskInfo task, CancellationToken cancellationToken)
        {
            bool destackDone = await RunRobotDestackAsync(task, cancellationToken);
            if (!destackDone)
            {
                return;
            }

            // 流程图：判断叠盘机是否满 → 是：跳过回叠盘机，直接进入「去拣选口」阶段
            // （仍然要过公共行程冲突检查，所以状态同样切到 ReturnToStacker，由 StepReturnToStackerAsync 统一判断）
            bool stackerFull = await IsStackerFullAsync(task.stationno, cancellationToken);
            if (stackerFull)
            {
                await SetStatusAsync(task, TaskStatus.ReturnToStacker, "");
                return;
            }

            PalletType palletType = await GetPalletTypeAsync(task, cancellationToken);
            task.pallettype = (int)palletType;
            bool returned = await ReturnToStackerAsync(task, palletType, cancellationToken);
            if (!returned)
            {
                return;
            }

            await SetStatusAsync(task, TaskStatus.ReturnToStacker, "");
        }

        /// <summary>
        /// Step：去目标工位（叠盘机/拣选口）前，判断相邻车在公共行程内有没有未完成的任务：
        /// 有就让行等待（停在当前状态，下一轮 500ms 再判断，不写库、不重复刷日志），没有就执行回叠盘机、输送拣选口。
        /// 两车同时都在公共行程内时按任务时间先后让行（先到的先走、后到的等），避免互相等待死锁。
        /// </summary>
        private async Task StepReturnToStackerAsync(TaskInfo task, CancellationToken cancellationToken)
        {
            bool adjacentBusy = await HasAdjacentTaskInPublicRangeAsync(task, cancellationToken);
            if (adjacentBusy)
            {
                // 有冲突：停在 ReturnToStacker 等待，下一轮再判断
                return;
            }

            ClearConflictLog(task.id);
            await ExecuteReturnTrayAndPickAsync(task, cancellationToken);
            await SetStatusAsync(task, TaskStatus.WaitPickStation, "");
        }

        /// <summary>Step：拣选口完成（RGV 汇报）→ 把完成写回拆垛 PLC → 判断是否有新任务 → 有新任务回到起点，无新任务回出口等待</summary>
        private async Task StepWaitPickStationAsync(TaskInfo task, CancellationToken cancellationToken)
        {
            bool finished = await IsPickStationTaskFinishedAsync(task, cancellationToken);
            if (!finished)
            {
                return;
            }

            // RGV 汇报完成后，WCS 再给拆垛 PLC 写入「拣选口完成」（写不进去就下一轮再试）
            bool reported = await WritePickStationDoneAsync(task, cancellationToken);
            if (!reported)
            {
                return;
            }

            bool hasNew = await HasNewTaskAsync(cancellationToken);
            if (hasNew)
            {
                await SetStatusAsync(task, TaskStatus.New, "");
                return;
            }

            await SetStatusAsync(task, TaskStatus.WaitExit, "");
        }

        /// <summary>Step：回出口等待 → 完成</summary>
        private async Task StepWaitExitAsync(TaskInfo task, CancellationToken cancellationToken)
        {
            bool exited = await WaitExitAsync(task, cancellationToken);
            if (!exited)
            {
                return;
            }

            await SetStatusAsync(task, TaskStatus.Completed, "");
        }

        // ===========================================================================================
        // 以下为外部交互方法：除「查快照」外当前都未确定，先留空，业务确认后逐个补充。
        // ===========================================================================================

        /// <summary>
        /// 取新任务：来源 = PLC 到位信息（用户 m01855 确认）。
        /// 遍历 4 台拆垛 PLC 的 DB600 快照，检查各拆垛工位（xx05 / xx06）的托盘号：
        /// 托盘号非空、且库里没有同工位同托盘号的任务，就产生一条新任务。
        /// </summary>
        private async Task<List<TaskInfo>> FetchNewTasksAsync(CancellationToken cancellationToken)
        {
            List<TaskInfo> tasks = new List<TaskInfo>();
            IReadOnlyList<PlcSnapshot> snapshots = _plcService.GetSnapshots();
            foreach (PlcSnapshot snapshot in snapshots)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (snapshot.Db600 == null || !snapshot.Db600Ok)
                {
                    continue;
                }

                List<int> stations = GetDestackStations(snapshot.Plccode);
                foreach (int stationNo in stations)
                {
                    string palletNo = GetPalletNoAtStation(snapshot.Db600, stationNo);
                    if (string.IsNullOrWhiteSpace(palletNo))
                    {
                        continue;
                    }

                    // 同一托盘只建一次任务（失败的任务允许重建）
                    bool exists = await _db.Queryable<TaskInfo>()
                        .Where(x => x.stationno == stationNo && x.palletno == palletNo
                            && x.status != (int)TaskStatus.Failed)
                        .AnyAsync();
                    if (exists)
                    {
                        continue;
                    }

                    TaskInfo task = new TaskInfo();
                    task.taskno = BuildTaskNo();
                    task.palletno = palletNo;
                    task.stationno = stationNo;
                    task.groupno = (int)TaskGroupHelper.GetGroupByStation(stationNo);  // 工位号 → 任务组
                    task.rgvno = TaskGroupHelper.GetRgvNoByStation(stationNo);          // 工位号 → 该站群固定的车
                    task.source = "PLC";
                    task.createtime = DateTime.Now;
                    task.updatetime = DateTime.Now;
                    tasks.Add(task);
                }
            }

            if (tasks.Count > 0)
            {
                _logger.LogInformation("PLC 到位信息产生 {Count} 条新任务", tasks.Count);
            }

            return tasks;
        }

        /// <summary>PLC 托盘到位信息（用户 m01855 确认）：读该工位所属 PLC 的 DB600，拆垛位托盘号非空即视为到位</summary>
        private Task<bool> HasPalletArrivedAsync(int stationNo, CancellationToken cancellationToken)
        {
            PlcSnapshot? snapshot = GetSnapshotByStation(stationNo);
            if (snapshot == null || snapshot.Db600 == null || !snapshot.Db600Ok)
            {
                return Task.FromResult(false);
            }

            string palletNo = GetPalletNoAtStation(snapshot.Db600, stationNo);
            return Task.FromResult(!string.IsNullOrWhiteSpace(palletNo));
        }

        /// <summary>
        /// 判断 1、2 号拆垛工位是否空闲：在任务所属 PLC 的拆垛工位（xx05 / xx06）里，
        /// 跳过被其他未结束任务占用的工位；优先返回任务自己的工位，其次返回当前没有托盘的工位。
        /// 都忙返回 0。
        /// </summary>
        private async Task<int> FindIdleDestackStationAsync(TaskInfo task, CancellationToken cancellationToken)
        {
            string? plccode = TaskGroupHelper.TryGetPlccodeByStation(task.stationno);
            if (plccode == null)
            {
                return 0;
            }

            List<int> stations = GetDestackStations(plccode);
            foreach (int stationNo in stations)
            {
                bool busy = await _db.Queryable<TaskInfo>()
                    .Where(x => x.stationno == stationNo && x.id != task.id
                        && ActiveStatusValues.Contains(x.status))
                    .AnyAsync();
                if (busy)
                {
                    continue;
                }

                if (stationNo == task.stationno)
                {
                    return stationNo;
                }

                PlcSnapshot? snapshot = _plcService.GetSnapshot(plccode);
                if (snapshot == null || snapshot.Db600 == null || !snapshot.Db600Ok)
                {
                    continue;
                }

                string palletNo = GetPalletNoAtStation(snapshot.Db600, stationNo);
                if (string.IsNullOrWhiteSpace(palletNo))
                {
                    return stationNo;
                }
            }

            return 0;
        }

        /// <summary>[TODO] 判断小车是否空闲：RGV S7 协议未定，暂返回 false</summary>
        private Task<bool> IsRgvIdleAsync(TaskInfo task, CancellationToken cancellationToken)
        {
            LogStub("IsRgvIdleAsync", "RGV 空闲判断未实现（RGV 协议未定），暂返回 false");
            return Task.FromResult(false);
        }

        /// <summary>
        /// 机器人拆垛：等 PLC 的「拆盘完成托盘出」信号（用户 m01855 确认）。
        /// 读该工位 DB600 的拆垛完成托盘出块对应侧「请求出」，为真表示拆垛完成；
        /// 同时把 PLC 报的托盘类型 / 箱数 / 托盘号回填到任务。
        /// </summary>
        private Task<bool> RunRobotDestackAsync(TaskInfo task, CancellationToken cancellationToken)
        {
            PlcSnapshot? snapshot = GetSnapshotByStation(task.stationno);
            if (snapshot == null || snapshot.Db600 == null || !snapshot.Db600Ok)
            {
                return Task.FromResult(false);
            }

            int index = GetSegmentIndex(task.stationno);
            if (index < 0)
            {
                return Task.FromResult(false);
            }

            DestackerUnstackDonePutInfo block;
            if (index == 0)
            {
                block = snapshot.Db600.UnstackDone0;
            }
            else
            {
                block = snapshot.Db600.UnstackDone1;
            }

            int side = task.stationno % 10;
            bool requestOut;
            short palletType;
            short boxCount;
            string palletNo;
            if (side == 5)
            {
                requestOut = block.LeftRequestOut;
                palletType = block.LeftPalletType;
                boxCount = block.LeftBoxCount;
                palletNo = block.LeftPalletNo;
            }
            else
            {
                // 图纸上右侧「请求出」是 Int（不是 Bool），非 0 视为完成
                requestOut = block.RightRequestOut != 0;
                palletType = block.RightPalletType;
                boxCount = block.RightBoxCount;
                palletNo = block.RightPalletNo;
            }

            if (!requestOut)
            {
                return Task.FromResult(false);
            }

            if (palletType > 0)
            {
                task.pallettype = palletType;
            }

            task.boxcount = boxCount;
            if (!string.IsNullOrWhiteSpace(palletNo))
            {
                task.palletno = palletNo;
            }

            return Task.FromResult(true);
        }

        /// <summary>
        /// 判断叠盘机是否满：6107 / 6108 是叠盘机的工位号（用户 m01855 确认），
        /// 读 DB600 垒托盘块的「空盘满请求出盘」位（PLC 主动推送，WCS 只读）。
        /// 拆垛位 xx05 对应叠盘机 xx07（块 [0]），xx06 对应 xx08（块 [1]）。
        /// </summary>
        private Task<bool> IsStackerFullAsync(int stationNo, CancellationToken cancellationToken)
        {
            PlcSnapshot? snapshot = GetSnapshotByStation(stationNo);
            if (snapshot == null || snapshot.Db600 == null || !snapshot.Db600Ok)
            {
                return Task.FromResult(false);
            }

            // 拆垛位 xx05 → 叠盘机 xx07（块 [0]）；xx06 → xx08（块 [1]）
            int side = stationNo % 10;
            DestackerStackPutInfo block;
            if (side == 5)
            {
                block = snapshot.Db600.Stack0;
            }
            else
            {
                block = snapshot.Db600.Stack1;
            }

            return Task.FromResult(block.EmptyPalletFullRequestOut);
        }

        /// <summary>
        /// [TODO] 取托盘类型（川/田）：用户 m01855 确认「托盘类型由 WMS 下发的托盘号区分」，
        /// WMS 接口与托盘号编码规则未定，先返回 Unknown；等 WMS 定了在这里加规则即可。
        /// （PLC 拆垛完成报文里也有托盘类型，已在 RunRobotDestackAsync 回填到 task.pallettype，可作兜底）
        /// </summary>
        private Task<PalletType> GetPalletTypeAsync(TaskInfo task, CancellationToken cancellationToken)
        {
            LogStub("GetPalletTypeAsync", "WMS 托盘号编码规则未定，暂返回 Unknown");
            return Task.FromResult(PalletType.Unknown);
        }

        /// <summary>
        /// 回叠盘机：写 PLC 的 DB500（WCS 下发的写入块）叠盘机垒托盘信号。
        /// 拆垛工位 xx05 → 叠盘机块【0】（xx07，基址 148），xx06 → 块【1】（xx08，基址 160）；
        /// 字段：RVG送货请求空盘出（基址+1 位）、RVG送货请求托盘号（基址+2，S7 String[6]）。
        /// 指令按「起始字节 + 11 字节块」进 PlcSendMsg 队列，由 PLC 后台服务按顺序写 DB500；
        /// 写之前先按当前 DB500 快照编码整块再改本块字段，避免覆盖其他工位的字段。
        /// </summary>
        private Task<bool> ReturnToStackerAsync(TaskInfo task, PalletType palletType, CancellationToken cancellationToken)
        {
            string? plccode = TaskGroupHelper.TryGetPlccodeByStation(task.stationno);
            if (plccode == null)
            {
                return Task.FromResult(false);
            }

            PlcSnapshot? snapshot = GetSnapshotByStation(task.stationno);
            if (snapshot == null || snapshot.Db500 == null || !snapshot.Db500Ok)
            {
                // 还没采到 DB500 快照时不发指令，下一轮再试
                return Task.FromResult(false);
            }

            int stackerIndex = TaskGroupHelper.GetStackerIndexByDestackStation(task.stationno);
            int startByte;
            if (stackerIndex == 0)
            {
                startByte = 148;
            }
            else
            {
                startByte = 160;
            }

            byte[] db500Bytes = PLCDb500Codec.ToBytes(snapshot.Db500);
            S7Codec.WriteBool(db500Bytes, startByte, 1, true);                 // RVG送货请求空盘出
            S7Codec.WriteString(db500Bytes, startByte + 2, task.palletno, 6);  // RVG送货请求托盘号

            byte[] payload = new byte[11];
            Array.Copy(db500Bytes, startByte, payload, 0, payload.Length);

            int writeDbNumber = 500;
            PlcConfig? config = _plcService.GetConfig(plccode);
            if (config != null && config.WriteDbNumber > 0)
            {
                writeDbNumber = config.WriteDbNumber;
            }

            long msgId = _plcService.Enqueue(plccode, writeDbNumber, startByte, payload, "回叠盘机", task.taskno);
            WriteLog(LogRiskLevel.Low, "回叠盘机指令", "任务 " + task.taskno + " 下发叠盘机块 " + startByte + "（指令 " + msgId + "）");
            return Task.FromResult(true);
        }

        /// <summary>
        /// 判断相邻车在公共行程范围内是否还有未完成的任务（《小车分组》表的「相邻车冲突点」）：
        /// 组1 车1↔车2、组2 车3↔车4、组3 车5↔车6、组4 车6↔车7（跨组）。
        /// 「在公共行程内」= 车已推进到回叠盘机 / 等待拣选口 / 出口等待这几个阶段（这时车会驶入公共行程）；
        /// 目标点表见 TaskGroupHelper.GetPublicRangeStations，等 RGV 协议（实时位置）定了再细化到具体点位。
        /// 两车都在公共行程内时，谁的任务更早（createtime，相同再按 id）谁先走，保证不会互相死锁。
        /// </summary>
        private async Task<bool> HasAdjacentTaskInPublicRangeAsync(TaskInfo task, CancellationToken cancellationToken)
        {
            TaskGroup group = TaskGroupHelper.GetGroupByStation(task.stationno);
            string[] adjacentRgvNos = TaskGroupHelper.GetAdjacentRgvNos(group, task.rgvno);
            if (adjacentRgvNos.Length == 0)
            {
                return false;
            }

            // SqlSugar 解析表达式时只认局部变量，所以先放进局部 List
            List<string> rgvList = new List<string>(adjacentRgvNos);
            List<int> publicStatuses = new List<int>
            {
                (int)TaskStatus.ReturnToStacker,
                (int)TaskStatus.WaitPickStation,
                (int)TaskStatus.WaitExit
            };

            List<TaskInfo> adjacentTasks = await _db.Queryable<TaskInfo>()
                .Where(x => x.id != task.id && rgvList.Contains(x.rgvno) && publicStatuses.Contains(x.status))
                .OrderBy(x => x.id)
                .ToListAsync(cancellationToken);

            foreach (TaskInfo adjacentTask in adjacentTasks)
            {
                // 相邻车任务更早 → 本任务让行；本任务更早 → 先走，让对方等
                bool adjacentEarlier = adjacentTask.createtime < task.createtime
                    || (adjacentTask.createtime == task.createtime && adjacentTask.id < task.id);
                if (adjacentEarlier)
                {
                    LogConflictOnce(task, adjacentTask);
                    return true;
                }
            }

            return false;
        }

        /// <summary>记录一次「等待相邻车」日志（同一任务只写一条，让行后清掉）</summary>
        private void LogConflictOnce(TaskInfo task, TaskInfo adjacentTask)
        {
            lock (_conflictLock)
            {
                if (!_conflictLogged.Add(task.id))
                {
                    return;
                }
            }

            WriteLog(LogRiskLevel.Low, "任务等待",
                "任务 " + task.taskno + "（" + task.rgvno + "）让行相邻车任务 " + adjacentTask.taskno + "（" + adjacentTask.rgvno + "）");
        }

        /// <summary>让行结束：清掉等待标记，下次再阻塞时重新记日志</summary>
        private void ClearConflictLog(long taskId)
        {
            lock (_conflictLock)
            {
                _conflictLogged.Remove(taskId);
            }
        }

        /// <summary>
        /// 执行「托盘回叠盘机、输送拣选口」：回叠盘机的 DB500 指令已在 ReturnToStackerAsync 入队；
        /// 「输送拣选口」是 RGV 动作，RGV 的 S7 协议未定，这里先留空（不阻塞后续「等待拣选口完成」）。
        /// </summary>
        private Task ExecuteReturnTrayAndPickAsync(TaskInfo task, CancellationToken cancellationToken)
        {
            LogStub("ExecuteReturnTrayAndPickAsync", "输送拣选口为 RGV 动作，RGV 协议未定，暂不下发");
            return Task.CompletedTask;
        }

        /// <summary>
        /// 判断拣选口任务是否完成：按用户 m03044 的口径，任务下发后由 RGV 去执行，
        /// 完成后由 RGV 自己的 PLC 通过 DB 块汇报（读「小车 PLC」的 DB 块，不是拆垛 PLC 的 DB600）；
        /// RGV 的 S7 协议未提供，这里先返回 false，协议到了在这里读对应 DB 块即可。
        /// </summary>
        private Task<bool> IsPickStationTaskFinishedAsync(TaskInfo task, CancellationToken cancellationToken)
        {
            LogStub("IsPickStationTaskFinishedAsync", "拣选口完成需读 RGV 小车 PLC 的 DB 块，RGV 协议未定，暂返回 false");
            return Task.FromResult(false);
        }

        /// <summary>
        /// RGV 汇报拣选口完成后，把「拣选口完成」写回拆垛 PLC 的 DB500（WCS→PLC 写入块）：
        /// 北起段托盘输送块【0】基址 64（61xx 段）、【1】基址 84（62xx 段），拣选口完成 = 基址字节的第 0 位。
        /// 单字节指令进 PlcSendMsg 队列；编码时先按当前 DB500 快照编码整块，只改该位，保留同字节其他信号。
        /// </summary>
        private Task<bool> WritePickStationDoneAsync(TaskInfo task, CancellationToken cancellationToken)
        {
            string? plccode = TaskGroupHelper.TryGetPlccodeByStation(task.stationno);
            if (plccode == null)
            {
                return Task.FromResult(false);
            }

            PlcSnapshot? snapshot = GetSnapshotByStation(task.stationno);
            if (snapshot == null || snapshot.Db500 == null || !snapshot.Db500Ok)
            {
                return Task.FromResult(false);
            }

            int index = GetSegmentIndex(task.stationno);
            if (index < 0)
            {
                return Task.FromResult(false);
            }

            int startByte;
            if (index == 0)
            {
                startByte = 64;
            }
            else
            {
                startByte = 84;
            }

            byte[] db500Bytes = PLCDb500Codec.ToBytes(snapshot.Db500);
            S7Codec.WriteBool(db500Bytes, startByte, 0, true); // 拣选口完成
            byte[] payload = new byte[1] { db500Bytes[startByte] };

            int writeDbNumber = 500;
            PlcConfig? config = _plcService.GetConfig(plccode);
            if (config != null && config.WriteDbNumber > 0)
            {
                writeDbNumber = config.WriteDbNumber;
            }

            long msgId = _plcService.Enqueue(plccode, writeDbNumber, startByte, payload, "拣选口完成", task.taskno);
            WriteLog(LogRiskLevel.Low, "拣选口完成回报", "任务 " + task.taskno + " 写回拣选口完成（指令 " + msgId + "）");
            return Task.FromResult(true);
        }

        /// <summary>[TODO] 判断是否有新任务：暂返回 false</summary>
        private Task<bool> HasNewTaskAsync(CancellationToken cancellationToken)
        {
            LogStub("HasNewTaskAsync", "是否有新任务判断未实现，暂返回 false");
            return Task.FromResult(false);
        }

        /// <summary>[TODO] 回出口等待：出口状态未定，暂返回 false</summary>
        private Task<bool> WaitExitAsync(TaskInfo task, CancellationToken cancellationToken)
        {
            LogStub("WaitExitAsync", "回出口等待判断未实现，暂返回 false");
            return Task.FromResult(false);
        }

        // ===========================================================================================
        // 工位号 / 快照 辅助方法
        // ===========================================================================================

        /// <summary>排除使用的旧表说明：工位号/站群/组/PLC/RGV 的换算统一在 Model.Tasks.TaskGroupHelper（组4 只有 67xx，没有 68xx）</summary>

        /// <summary>未结束的任务状态（判断工位是否被占用时用）</summary>
        private static readonly List<int> ActiveStatusValues = new List<int>
        {
            (int)TaskStatus.New,
            (int)TaskStatus.WaitStation,
            (int)TaskStatus.WaitRgv,
            (int)TaskStatus.RobotDestacking,
            (int)TaskStatus.ReturnToStacker,
            (int)TaskStatus.WaitPickStation,
            (int)TaskStatus.WaitExit,
            (int)TaskStatus.Paused
        };

        /// <summary>任务号自增序号（WMS 未接入前由 WCS 自编）</summary>
        private static int _taskSeq;

        /// <summary>生成 WCS 自编任务号：T + 时间 + 3 位序号</summary>
        private static string BuildTaskNo()
        {
            int seq = Interlocked.Increment(ref _taskSeq);
            return "T" + DateTime.Now.ToString("yyyyMMddHHmmss") + seq.ToString("D3");
        }

        /// <summary>取工位所属 PLC 的快照，工位号非法或未采集时返回 null</summary>
        private PlcSnapshot? GetSnapshotByStation(int stationNo)
        {
            string? plccode = TaskGroupHelper.TryGetPlccodeByStation(stationNo);
            if (plccode == null)
            {
                return null;
            }

            return _plcService.GetSnapshot(plccode);
        }

        /// <summary>工位号 → 该 PLC 内 DB600 的段下标（61xx/63xx/65xx/67xx → 0，62xx/64xx/66xx → 1，规则见 TaskGroupHelper）</summary>
        private static int GetSegmentIndex(int stationNo)
        {
            return TaskGroupHelper.GetSegmentIndexByStation(stationNo);
        }

        /// <summary>取某台 PLC 负责的拆垛工位号（组1~组3 每台 PLC 两个站群，组4 只有 6705 / 6706）</summary>
        private static List<int> GetDestackStations(string plccode)
        {
            return TaskGroupHelper.GetDestackStations(plccode);
        }

        /// <summary>
        /// 取某拆垛工位（xx05 / xx06）对应的托盘号：读 DB600「出库至拆垛位」块，
        /// 段下标 0 用 Outbound0（61xx），1 用 Outbound1（62xx）；尾号 5 取左、6 取右。
        /// </summary>
        private static string GetPalletNoAtStation(DestackerDb600 db600, int stationNo)
        {
            int index = GetSegmentIndex(stationNo);
            if (index < 0)
            {
                return "";
            }

            DestackerOutboundPutInfo block;
            if (index == 0)
            {
                block = db600.Outbound0;
            }
            else
            {
                block = db600.Outbound1;
            }

            int side = stationNo % 10;
            if (side == 5)
            {
                return block.LeftPalletNo;
            }

            if (side == 6)
            {
                return block.RightPalletNo;
            }

            return "";
        }

        // ===========================================================================================
        // 内部工具
        // ===========================================================================================

        /// <summary>更新任务状态并落库</summary>
        private async Task SetStatusAsync(TaskInfo task, TaskStatus status, string error)
        {
            task.status = (int)status;
            task.lasterror = error;
            task.updatetime = DateTime.Now;
            await _db.Updateable(task).ExecuteCommandAsync();
        }

        /// <summary>写一条系统日志（走 ILogService，直接写日志表）</summary>
        private void WriteLog(LogRiskLevel riskLevel, string action, string content)
        {
            LogEntry entry = LogEntry.Create(LogCategory.System, riskLevel, "TaskScheduling", action, content);
            entry.Success = riskLevel != LogRiskLevel.High;
            _logService.Write(entry);
        }

        /// <summary>空方法提示：同一个空方法只提示一次</summary>
        private void LogStub(string methodName, string description)
        {
            lock (_stubLock)
            {
                if (!_stubLogged.Add(methodName))
                {
                    return;
                }
            }

            _logger.LogWarning("[TODO 未实现] {Method}：{Description}", methodName, description);
        }
    }
}
