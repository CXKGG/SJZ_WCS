using SqlSugar;

namespace Model.Entities
{
    /// 拆垛任务表：保存流程图里「输送线记录托盘时间、保存任务信息」的任务。
    /// 任务来源目前未定（WMS / PLC 到位信息），字段按流程图需要定义，后续按实际接口补充。
    public class TaskInfo
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public long id { get; set; }

        // 任务号：WCS 自编（WMS 接入后由 WMS 下发，需同样保证唯一）。
        // 生成见 TaskScheduling.BuildTaskNo（"WCS" + 雪花 ID）；唯一性另由 SugarConfig.EnsureTaskNoUniqueIndex 建唯一索引兜底。
        public string taskno { get; set; } = "";         // 任务号 = "WCS" + 雪花 ID（WMS 任务号或 WCS 自编）
        public string palletno { get; set; } = "";       // 托盘号
        public int productinfo { get; set; }             // 产品信息（PLC 协议定义：1/2/3/4…，见拆垛协议说明）
        public int pallettype { get; set; }              // 托盘类型：见 Model.Tasks.PalletType（1 川 / 2 田，待确认）
        public int boxcount { get; set; }                // 箱数（PLC 拆垛完成报文里的托盘箱数）
        public int stationno { get; set; }               // 拆垛工位号（如 6105/6106/6205/6206）
        public int groupno { get; set; }                 // 任务组：1~4，见 Model.Tasks.TaskGroup（一台 PLC + 1~2 台车为一组）
        public string rgvno { get; set; } = "";          // 执行搬运的 RGV 名称（如 RGV01）
        public int status { get; set; }                  // 任务状态：见 Model.Tasks.TaskStatus
        public string source { get; set; } = "";         // 任务来源：WMS / PLC / 手工（待定）
        public string lasterror { get; set; } = "";      // 最近一次失败原因
        public DateTime createtime { get; set; }         // 创建时间（即托盘到位/任务产生时间）
        public DateTime updatetime { get; set; }         // 最后更新时间
    }
}
