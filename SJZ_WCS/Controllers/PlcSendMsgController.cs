using Interface.PLC;
using Microsoft.AspNetCore.Mvc;
using Model.Entities;
using Service.Protocol;
using SqlSugar;

namespace SJZ_WCS.Controllers
{
    /// <summary>
    /// PLC 发送缓存接口：写入队列、分页查询、手动重发。
    /// 业务下发统一走缓存（先落库再发送），调试用的直写接口见 /api/plc/{name}/db。
    /// </summary>
    [ApiController]
    [Route("api/plc/sendmsg")]
    public class PlcSendMsgController : ControllerBase
    {
        private readonly ISqlSugarClient _db;
        private readonly IPlcService _plcService;

        /// <summary>
        /// 构造发送缓存控制器。
        /// </summary>
        /// <param name="db">SqlSugar 客户端（分页查询发送缓存）</param>
        /// <param name="plcService">PLC 服务（入队与重发）</param>
        public PlcSendMsgController(ISqlSugarClient db, IPlcService plcService)
        {
            _db = db;
            _plcService = plcService;
        }

        /// <summary>分页查询发送缓存（status、plccode 都可空，不传即不过滤）</summary>
        /// <param name="status">状态：0待发送 1已发送 2失败待重试 3已放弃</param>
        /// <param name="plccode">设备编号，如 PLC1</param>
        /// <param name="page">页码，从 1 开始</param>
        /// <param name="size">每页条数，1~100，默认 20</param>
        [HttpGet]
        public ActionResult GetPage([FromQuery] int? status = null, [FromQuery] string? plccode = null,
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

            ISugarQueryable<PlcSendMsg> query = _db.Queryable<PlcSendMsg>();
            if (status.HasValue)
            {
                query = query.Where(x => x.status == status.Value);
            }

            if (!string.IsNullOrWhiteSpace(plccode))
            {
                query = query.Where(x => x.plccode == plccode);
            }

            int total = query.Count();
            ISugarQueryable<PlcSendMsg> pageQuery = query.OrderByDescending(x => x.createtime);
            pageQuery = pageQuery.Skip((page - 1) * size);
            pageQuery = pageQuery.Take(size);
            List<PlcSendMsg> items = pageQuery.ToList();

            return Ok(new
            {
                success = true,
                total,
                page,
                size,
                items
            });
        }

        /// <summary>写入一条发送缓存，由后台服务负责实际下发</summary>
        /// <param name="request">缓存请求：设备、DB、偏移、hex 报文、业务信息</param>
        [HttpPost]
        public ActionResult Enqueue([FromBody] PlcSendMsgEnqueueRequest request)
        {
            if (request == null)
            {
                return BadRequest("请求体不能为空");
            }

            byte[] payload;
            try
            {
                payload = HexCodec.FromHex(request.Hex);
            }
            catch (FormatException ex)
            {
                return BadRequest(ex.Message);
            }

            try
            {
                long id = _plcService.Enqueue(request.Plccode, request.DbNumber, request.StartByte, payload,
                    request.BusinessType, request.BusinessId);
                return Ok(new
                {
                    success = true,
                    id
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        /// <summary>手动重发：把指定记录重置为待发送，后台服务下一轮会重新发出</summary>
        /// <param name="id">缓存记录主键</param>
        [HttpPost("{id:long}/retry")]
        public ActionResult Retry(long id)
        {
            bool result = _plcService.RetrySendMsg(id);
            if (!result)
            {
                return NotFound("未找到可重发的发送缓存：" + id);
            }

            return Ok(new
            {
                success = true,
                id
            });
        }
    }

    /// <summary>写入发送缓存的请求体。</summary>
    public class PlcSendMsgEnqueueRequest
    {
        /// <summary>目标设备编号，如 PLC1</summary>
        public string Plccode { get; set; } = "";

        /// <summary>目标 DB 号，如 500</summary>
        public int DbNumber { get; set; }

        /// <summary>起始字节</summary>
        public int StartByte { get; set; }

        /// <summary>要写入的字节内容（hex 字符串，可带空格/短横线/0x）</summary>
        public string Hex { get; set; } = "";

        /// <summary>业务类型（可空）</summary>
        public string? BusinessType { get; set; }

        /// <summary>关联业务号/任务号（可空）</summary>
        public string? BusinessId { get; set; }
    }
}
