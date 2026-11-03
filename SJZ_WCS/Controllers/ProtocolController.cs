using Microsoft.AspNetCore.Mvc;
using Model.Protocol;
using Service.Protocol;

namespace SJZ_WCS.Controllers
{
    /// <summary>
    /// 协议解析接口：无需连接 PLC，直接用十六进制报文验证 DB500/DB600 的编解码结果，
    /// 方便现场拿到博途监控数据后离线核对字段解析是否正确。
    /// 编解码由手写 codec 完成（<see cref="PLCDb500Codec"/> / <see cref="PLCDb600Codec"/>），不使用反射。
    /// </summary>
    [ApiController]
    [Route("api/protocol")]
    public class ProtocolController : ControllerBase
    {
        /// <summary>获取拆垛段 DB500、DB600 的字段布局（偏移、类型、长度、位号）与 DB 总长度。</summary>
        [HttpGet("destacker/layout")]
        public ActionResult GetDestackerLayout()
        {
            List<DbFieldInfo> db500Fields = PLCDb500Codec.Layout();
            List<DbFieldInfo> db600Fields = PLCDb600Codec.Layout();

            return Ok(new
            {
                db500 = new
                {
                    length = PLCDb500Codec.Length,
                    fields = db500Fields
                },
                db600 = new
                {
                    length = PLCDb600Codec.Length,
                    fields = db600Fields
                }
            });
        }

        /// <summary>解码 DB500 报文（WCS → PLC 方向）。</summary>
        /// <param name="request">十六进制报文，可带空格</param>
        [HttpPost("destacker/db500/decode")]
        public ActionResult DecodeDb500([FromBody] DbHexRequest request)
        {
            try
            {
                byte[] data = HexCodec.FromHex(request.Hex);
                DestackerDb500 model = PLCDb500Codec.Parse(data);
                return Ok(new { success = true, length = data.Length, model });
            }
            catch (Exception ex)
            {
                return BadRequest("解码失败：" + ex.Message);
            }
        }

        /// <summary>解码 DB600 报文（PLC → WCS 方向）。</summary>
        /// <param name="request">十六进制报文，可带空格</param>
        [HttpPost("destacker/db600/decode")]
        public ActionResult DecodeDb600([FromBody] DbHexRequest request)
        {
            try
            {
                byte[] data = HexCodec.FromHex(request.Hex);
                DestackerDb600 model = PLCDb600Codec.Parse(data);
                return Ok(new { success = true, length = data.Length, model });
            }
            catch (Exception ex)
            {
                return BadRequest("解码失败：" + ex.Message);
            }
        }

        /// <summary>把 DB500 模型编码成十六进制报文（WCS → PLC 方向），用于离线构造报文。</summary>
        /// <param name="model">DB500 模型，未赋值字段按 0/空串处理</param>
        [HttpPost("destacker/db500/encode")]
        public ActionResult EncodeDb500([FromBody] DestackerDb500 model)
        {
            try
            {
                byte[] data = PLCDb500Codec.ToBytes(model);
                return Ok(new { success = true, length = data.Length, hex = HexCodec.ToHex(data) });
            }
            catch (Exception ex)
            {
                return BadRequest("编码失败：" + ex.Message);
            }
        }

        /// <summary>把 DB600 模型编码成十六进制报文（PLC → WCS 方向），一般用于造测试数据。</summary>
        /// <param name="model">DB600 模型，未赋值字段按 0/空串处理</param>
        [HttpPost("destacker/db600/encode")]
        public ActionResult EncodeDb600([FromBody] DestackerDb600 model)
        {
            try
            {
                byte[] data = PLCDb600Codec.ToBytes(model);
                return Ok(new { success = true, length = data.Length, hex = HexCodec.ToHex(data) });
            }
            catch (Exception ex)
            {
                return BadRequest("编码失败：" + ex.Message);
            }
        }
    }

    /// <summary>十六进制报文请求体。</summary>
    public class DbHexRequest
    {
        // 十六进制字符串（可带空格、短横线、0x 前缀）
        public string Hex { get; set; } = "";
    }
}
