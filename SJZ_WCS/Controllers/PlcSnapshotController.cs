using Interface.PLC;
using Microsoft.AspNetCore.Mvc;
using Model.PLC;

namespace SJZ_WCS.Controllers
{
    /// <summary>
    /// PLC 实时快照接口：数据来自 PLC 后台服务的采集（内存快照），
    /// 接口本身不直接读 S7，所以任务调度/前端查询不会被 PLC 通讯阻塞。
    /// </summary>
    [ApiController]
    [Route("api/plc")]
    public class PlcSnapshotController : ControllerBase
    {
        private readonly IPlcService _plcService;

        /// <summary>
        /// 构造快照控制器。
        /// </summary>
        /// <param name="plcService">PLC 服务（快照由它的后台采集更新）</param>
        public PlcSnapshotController(IPlcService plcService)
        {
            _plcService = plcService;
        }

        /// <summary>所有 PLC 的最新快照（含 DB600/DB500 解析后的对象）</summary>
        [HttpGet("snapshot")]
        public ActionResult GetAll()
        {
            IReadOnlyList<PlcSnapshot> list = _plcService.GetSnapshots();
            return Ok(new { success = true, count = list.Count, items = list });
        }

        /// <summary>单台 PLC 的最新快照，名称同 appsettings.json 里 Plcs 的 Name（如 PLC1）</summary>
        /// <param name="name">设备编码</param>
        [HttpGet("{name}/snapshot")]
        public ActionResult Get(string name)
        {
            PlcSnapshot? snapshot = _plcService.GetSnapshot(name);
            if (snapshot == null)
            {
                return NotFound(new { success = false, message = "PLC " + name + " 还没有采集数据，请确认已配置且 PLC 后台服务已启动" });
            }

            return Ok(new { success = true, item = snapshot });
        }
    }
}
