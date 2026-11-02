using Microsoft.AspNetCore.Mvc;
using Model.Entities;
using SqlSugar;

namespace SJZ_WCS.Controllers
{
    /// <summary>
    /// 扫码上报记录查询接口（前端扫码记录页面用）。
    /// 上报入口见 <see cref="ScanController"/>，本控制器只读，便于现场按追溯码/内部管理码追溯。
    /// </summary>
    [ApiController]
    [Route("api/scan/records")]
    public class ScanRecordController : ControllerBase
    {
        private readonly ISqlSugarClient _db;

        /// <summary>构造扫码记录查询控制器。</summary>
        /// <param name="db">SqlSugar 客户端</param>
        public ScanRecordController(ISqlSugarClient db)
        {
            _db = db;
        }

        /// <summary>
        /// 分页查询扫码记录（过滤条件都可空）。
        /// </summary>
        /// <param name="keyword">关键字：产品追溯码或内部管理码（模糊匹配）</param>
        /// <param name="from">接收时间起（含）</param>
        /// <param name="to">接收时间止（含）</param>
        /// <param name="page">页码，从 1 开始</param>
        /// <param name="size">每页条数，1~100，默认 20</param>
        [HttpGet]
        public ActionResult GetPage([FromQuery] string? keyword = null,
            [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null,
            [FromQuery] int page = 1, [FromQuery] int size = 20)
        {
            if (page < 1)
            {
                return BadRequest("page 必须大于等于 1");
            }

            if (size < 1 || size > 100)
            {
                return BadRequest("size 取值范围 1~100");
            }

            ISugarQueryable<ScanRecord> query = _db.Queryable<ScanRecord>();
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                string key = keyword.Trim();
                query = query.Where(x => x.tracecode.Contains(key) || x.internalcode.Contains(key));
            }

            if (from.HasValue)
            {
                query = query.Where(x => x.receivedat >= from.Value);
            }

            if (to.HasValue)
            {
                query = query.Where(x => x.receivedat <= to.Value);
            }

            int total = query.Count();
            ISugarQueryable<ScanRecord> pageQuery = query.OrderByDescending(x => x.receivedat);
            pageQuery = pageQuery.Skip((page - 1) * size);
            pageQuery = pageQuery.Take(size);
            List<ScanRecord> items = pageQuery.ToList();

            return Ok(new
            {
                success = true,
                total,
                page,
                size,
                items
            });
        }

        /// <summary>按主键查询单条扫码记录。</summary>
        /// <param name="id">记录主键</param>
        [HttpGet("{id:long}")]
        public ActionResult GetById(long id)
        {
            ScanRecord? record = _db.Queryable<ScanRecord>().First(x => x.id == id);
            if (record == null)
            {
                return NotFound("未找到扫码记录：" + id);
            }

            return Ok(record);
        }
    }
}
