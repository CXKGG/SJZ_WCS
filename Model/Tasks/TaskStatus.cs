namespace Model.Tasks
{
    /// <summary>
    /// 拆垛任务状态（对应《石家庄流程图20260929》节点的执行阶段）。
    /// 数据库 TaskInfo.status 存这个枚举的整数值。
    /// </summary>
    public enum TaskStatus
    {
        /// <summary>新建：任务刚产生，尚未开始处理</summary>
        New = 0,

        /// <summary>等待拆垛工位：流程图「判断1、2号拆垛工位是否空闲」</summary>
        WaitStation = 1,

        /// <summary>等待 RGV：流程图「判断小车是否空闲」（否 → 等待小车任务结束）</summary>
        WaitRgv = 2,

        /// <summary>机器人拆垛中：流程图「机器人拆垛工位1/2」</summary>
        RobotDestacking = 3,

        /// <summary>托盘回叠盘机：流程图「回川字型/田字型叠盘机」</summary>
        ReturnToStacker = 4,

        /// <summary>等待拣选口：流程图「执行托盘回叠盘机、输送拣选口」中等待拣选口完成</summary>
        WaitPickStation = 5,

        /// <summary>任务暂停：流程图「任务暂停」（相邻拣选接口任务未完成）</summary>
        Paused = 6,

        /// <summary>回出口等待：流程图「回出口等待」</summary>
        WaitExit = 7,

        /// <summary>已完成</summary>
        Completed = 8,

        /// <summary>异常：执行出错，需人工处理</summary>
        Failed = 9
    }
}
