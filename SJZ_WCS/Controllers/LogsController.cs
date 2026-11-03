using Interface.Logs;
using Microsoft.AspNetCore.Mvc;
using Model.Dtos;
using Model.Entities;
using Model.Logs;
using SqlSugar;

namespace SJZ_WCS.Controllers
{
    /// <summary>
    /// 日志接口：分页查询 SysLog，以及接收前端操作日志上报。
    /// <para>
    /// 日志由日志服务同步落库；查询接口本身不写接口日志（见 ApiLogMiddleware 的 SkipPaths），避免自己刷自己。
    /// </para>
    /// </summary>
    [ApiController]
    [Route("api/logs")]
    public class LogsController : ControllerBase
    {
        private readonly ISqlSugarClient _db;
        private readonly ILogService _logService;

        /// <summary>构造日志控制器。</summary>
        /// <param name="db">SqlSugar 客户端（单例）</param>
        /// <param name="logService">日志服务（写前端操作日志用）</param>
        public LogsController(ISqlSugarClient db, ILogService logService)
        {
            _db = db;
            _logService = logService;
        }

        /// <summary>
        /// 分页查询日志（所有过滤条件都可空，不传即不过滤）。
        /// </summary>
        /// <param name="category">日志分类：1系统 2WMS 3RGV 4PLC 5用户操作</param>
        /// <param name="riskLevel">风险等级：1低 2中 3高</param>
        /// <param name="module">模块（精确匹配），如 "API"、"PLC/PLC1"</param>
        /// <param name="keyword">关键字（在摘要和明细里模糊匹配）</param>
        /// <param name="from">起始时间（含）</param>
        /// <param name="to">结束时间（含）</param>
        /// <param name="page">页码，从 1 开始</param>
        /// <param name="size">每页条数，1~100，默认 20</param>
        [HttpGet]
        public ActionResult GetPage(
            [FromQuery] LogCategory? category = null,
            [FromQuery] LogRiskLevel? riskLevel = null,
            [FromQuery] string? module = null,
            [FromQuery] string? keyword = null,
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null,
            [FromQuery] int page = 1,
            [FromQuery] int size = 20)
        {
            if (page < 1) return BadRequest("page 必须大于等于 1");
            if (size < 1 || size > 100) return BadRequest("size 取值范围 1~100");

            ISugarQueryable<SysLog> query = _db.Queryable<SysLog>();

            if (category.HasValue) query = query.Where(x => x.logcategory == category.Value);
            if (riskLevel.HasValue) query = query.Where(x => x.risklevel == riskLevel.Value);
            if (!string.IsNullOrWhiteSpace(module)) query = query.Where(x => x.module == module);
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                query = query.Where(x => x.content.Contains(keyword!) || x.detail.Contains(keyword!));
            }

            if (from.HasValue) query = query.Where(x => x.createdtime >= from.Value);
            if (to.HasValue) query = query.Where(x => x.createdtime <= to.Value);

            int total = query.Count();
            ISugarQueryable<SysLog> pageQuery = query.OrderByDescending(x => x.createdtime);
            pageQuery = pageQuery.Skip((page - 1) * size);
            pageQuery = pageQuery.Take(size);
            List<SysLog> items = pageQuery.ToList();

            return Ok(new
            {
                success = true,
                total,
                page,
                size,
                items
            });
        }

        /// <summary>按主键查询单条日志。</summary>
        /// <param name="id">日志主键</param>
        [HttpGet("{id:long}")]
        public ActionResult GetById(long id)
        {
            SysLog? log = _db.Queryable<SysLog>().First(x => x.id == id);
            if (log == null) return NotFound($"未找到日志：{id}");
            return Ok(log);
        }

        /// <summary>
        /// 前端操作日志上报：页面访问、按钮点击、前端异常等由浏览器端上报，统一写入用户操作日志。
        /// </summary>
        /// <param name="request">前端上报内容（module/action/content 至少要有 action 或 content）</param>
        [HttpPost("frontend")]
        public ActionResult WriteFrontend([FromBody] FrontendLogRequest? request)
        {
            if (request == null)
            {
                return BadRequest("请求体不能为空");
            }

            bool hasAction = !string.IsNullOrWhiteSpace(request.Action);
            bool hasContent = !string.IsNullOrWhiteSpace(request.Content);
            if (!hasAction && !hasContent)
            {
                return BadRequest("action 与 content 不能同时为空");
            }

            LogRiskLevel riskLevel = LogRiskLevel.Low;
            if (request.RiskLevel == 2) riskLevel = LogRiskLevel.Medium;
            if (request.RiskLevel >= 3) riskLevel = LogRiskLevel.High;

            string module = string.IsNullOrWhiteSpace(request.Module) ? "前端" : request.Module.Trim();
            string action = hasAction ? request.Action.Trim() : "操作";
            string content = hasContent ? request.Content.Trim() : action;

            string? userId = Request.Headers["X-User-Id"].FirstOrDefault();
            string? userName = Request.Headers["X-User-Name"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(userName))
            {
                /// 前端用 encodeURIComponent 编码后放进请求头，这里还原成中文用户名
                try
                {
                    userName = Uri.UnescapeDataString(userName);
                }
                catch
                {
                    /// 编码异常时保留原值，不阻断日志写入
                }
            }

            _logService.WriteUserOperation(riskLevel, module, action, content,
                request.Detail, request.Target, userId, userName,
                HttpContext.Connection.RemoteIpAddress?.ToString(), request.Success, request.ElapsedMs);

            return Ok(new { success = true });
        }
    }
}
