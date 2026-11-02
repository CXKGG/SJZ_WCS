using Interface.Scheduling;
using Microsoft.AspNetCore.Mvc;
using Model.Entities;
using SqlSugar;

namespace SJZ_WCS.Controllers
{
    /// <summary>
    /// 拆垛任务查询/干预接口（前端任务调度页面用）。
    /// <para>任务由后台服务 TaskSchedulingBackgroundService 自动推进，这里只做查询和人工暂停/恢复。</para>
    /// </summary>
    [ApiController]
    [Route("api/task")]
    public class TaskController : ControllerBase
    {
        private readonly ISqlSugarClient _db;
        private readonly ITaskSchedulingService _taskSchedulingService;

        /// <summary>
        /// 构造任务控制器。
        /// </summary>
        /// <param name="db">SqlSugar 客户端（分页查询任务）</param>
        /// <param name="taskSchedulingService">任务调度服务（人工暂停/恢复）</param>
        public TaskController(ISqlSugarClient db, ITaskSchedulingService taskSchedulingService)
        {
            _db = db;
            _taskSchedulingService = taskSchedulingService;
        }

        /// <summary>
        /// 分页查询任务（过滤条件都可空）。
        /// </summary>
        /// <param name="status">任务状态：0新建 1等工位 2等RGV 3拆垛中 4回叠盘机 5等拣选口 6暂停 7回出口 8完成 9异常</param>
        /// <param name="groupno">任务组：1~4</param>
        /// <param name="keyword">任务号或托盘号（模糊匹配）</param>
        /// <param name="page">页码，从 1 开始</param>
        /// <param name="size">每页条数，1~100，默认 20</param>
        [HttpGet]
        public ActionResult GetPage([FromQuery] int? status = null, [FromQuery] int? groupno = null,
            [FromQuery] string? keyword = null, [FromQuery] int page = 1, [FromQuery] int size = 20)
        {
            if (page < 1)
            {
                return BadRequest("page 必须大于等于 1");
            }

            if (size < 1 || size > 100)
            {
                return BadRequest("size 取值范围 1~100");
            }

            ISugarQueryable<TaskInfo> query = _db.Queryable<TaskInfo>();
            if (status.HasValue)
            {
                query = query.Where(x => x.status == status.Value);
            }

            if (groupno.HasValue)
            {
                query = query.Where(x => x.groupno == groupno.Value);
            }

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                string key = keyword.Trim();
                query = query.Where(x => x.taskno.Contains(key) || x.palletno.Contains(key));
            }

            int total = query.Count();
            ISugarQueryable<TaskInfo> pageQuery = query.OrderByDescending(x => x.createtime);
            pageQuery = pageQuery.Skip((page - 1) * size);
            pageQuery = pageQuery.Take(size);
            List<TaskInfo> items = pageQuery.ToList();

            return Ok(new
            {
                success = true,
                total,
                page,
                size,
                items
            });
        }

        /// <summary>按主键查询单条任务。</summary>
        /// <param name="id">任务主键</param>
        [HttpGet("{id:long}")]
        public ActionResult GetById(long id)
        {
            TaskInfo? task = _db.Queryable<TaskInfo>().First(x => x.id == id);
            if (task == null)
            {
                return NotFound("未找到任务：" + id);
            }

            return Ok(task);
        }

        /// <summary>人工暂停任务（暂停后调度不再推进，可用恢复继续）。</summary>
        /// <param name="id">任务主键</param>
        [HttpPost("{id:long}/pause")]
        public async Task<ActionResult> Pause(long id)
        {
            bool result = await _taskSchedulingService.PauseAsync(id);
            if (!result)
            {
                return NotFound("未找到可暂停的任务：" + id);
            }

            return Ok(new { success = true, id });
        }

        /// <summary>人工恢复任务。</summary>
        /// <param name="id">任务主键</param>
        [HttpPost("{id:long}/resume")]
        public async Task<ActionResult> Resume(long id)
        {
            bool result = await _taskSchedulingService.ResumeAsync(id);
            if (!result)
            {
                return NotFound("未找到可恢复的任务：" + id);
            }

            return Ok(new { success = true, id });
        }
    }
}
