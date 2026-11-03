using Interface.PLC;
using Microsoft.AspNetCore.Mvc;
using Model.PLC;
using Model.Protocol;
using Service.Protocol;

namespace SJZ_WCS.Controllers
{
    /// <summary>
    /// PLC 连接与 DB 读写调试接口：对接现场真实 PLC，用于联调与排障。
    /// 业务协议（任务下发/状态采集）后续在 Service 层封装后，按协议接口再对外提供。
    /// </summary>
    [ApiController]
    [Route("api/plc")]
    public class PlcController : ControllerBase
    {
        private readonly IPlcService _plcService;

        /// <summary>
        /// 构造 PLC 控制器。
        /// </summary>
        /// <param name="plcService">PLC 服务（连接、读写、快照都在里面）</param>
        public PlcController(IPlcService plcService)
        {
            _plcService = plcService;
        }

        /// <summary>获取全部 PLC 的配置与实时连接状态</summary>
        [HttpGet]
        public ActionResult<IReadOnlyList<PlcStatus>> GetAllStatus()
        {
            return Ok(_plcService.GetAllStatus());
        }

        /// <summary>获取单台 PLC 的状态</summary>
        [HttpGet("{name}/status")]
        public ActionResult<PlcStatus> GetStatus(string name)
        {
            PlcStatus? status = _plcService.GetStatus(name);
            if (status == null)
            {
                return NotFound($"未找到 PLC：{name}");
            }

            return Ok(status);
        }

        /// <summary>主动连接一台 PLC（后台服务也会自动重连，这个接口用于现场即时触发）</summary>
        [HttpPost("{name}/connect")]
        public async Task<ActionResult> Connect(string name, CancellationToken cancellationToken)
        {
            PlcConfig? config = _plcService.GetConfig(name);
            if (config == null)
            {
                return NotFound($"未找到 PLC：{name}");
            }

            bool connected = await _plcService.ConnectAsync(name, cancellationToken);
            if (connected == false)
            {
                PlcStatus? status = _plcService.GetStatus(name);
                return StatusCode(StatusCodes.Status502BadGateway, new
                {
                    success = false,
                    name,
                    error = status?.LastError ?? "连接失败"
                });
            }

            return Ok(new
            {
                success = true,
                name,
                connected = true
            });
        }

        /// <summary>断开一台 PLC 的连接</summary>
        [HttpPost("{name}/disconnect")]
        public async Task<ActionResult> Disconnect(string name, CancellationToken cancellationToken)
        {
            PlcConfig? config = _plcService.GetConfig(name);
            if (config == null)
            {
                return NotFound($"未找到 PLC：{name}");
            }

            await _plcService.DisconnectAsync(name, cancellationToken);
            return Ok(new
            {
                success = true,
                name,
                connected = _plcService.IsConnected(name)
            });
        }

        /// <summary>读取 DB 块原始字节，返回十六进制与 Base64，便于和博途监控对照</summary>
        [HttpGet("{name}/db/{dbNumber:int}")]
        public async Task<ActionResult> ReadDb(string name, int dbNumber,
            [FromQuery] int start = 0, [FromQuery] int length = 1,
            CancellationToken cancellationToken = default)
        {
            if (length <= 0 || length > 2048)
            {
                return BadRequest("length 取值范围 1~2048");
            }

            if (start < 0)
            {
                return BadRequest("start 不能小于 0");
            }

            PlcConfig? config = _plcService.GetConfig(name);
            if (config == null)
            {
                return NotFound($"未找到 PLC：{name}");
            }

            PlcReadResult result = await _plcService.ReadDbAsync(name, dbNumber, start, length, cancellationToken);
            if (result.Success == false)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new
                {
                    success = false,
                    error = result.ErrorMessage
                });
            }

            return Ok(new
            {
                success = true,
                db = dbNumber,
                start,
                length,
                hex = Convert.ToHexString(result.Data),
                base64 = Convert.ToBase64String(result.Data)
            });
        }

        /// <summary>向 DB 块写入原始字节（hex 字符串，可带空格，如 "01 02 00 FF"）</summary>
        [HttpPost("{name}/db/{dbNumber:int}")]
        public async Task<ActionResult> WriteDb(string name, int dbNumber,
            [FromBody] PlcDbWriteRequest request,
            CancellationToken cancellationToken = default)
        {
            PlcConfig? config = _plcService.GetConfig(name);
            if (config == null)
            {
                return NotFound($"未找到 PLC：{name}");
            }

            byte[] data;
            try
            {
                data = ParseHex(request.Hex);
            }
            catch (Exception ex)
            {
                return BadRequest("Hex 数据格式不正确：" + ex.Message);
            }

            if (data.Length == 0)
            {
                return BadRequest("写入数据不能为空");
            }

            if (data.Length > 2048)
            {
                return BadRequest("单次写入不能超过 2048 字节");
            }

            PlcWriteResult result = await _plcService.WriteDbAsync(name, dbNumber, request.StartByte, data, cancellationToken);
            if (result.Success == false)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new
                {
                    success = false,
                    error = result.ErrorMessage
                });
            }

            return Ok(new
            {
                success = true,
                db = dbNumber,
                start = request.StartByte,
                length = data.Length
            });
        }

        /// <summary>
        /// 读取拆垛 PLC 的 DB600（PLC → WCS）并按协议模型解析，返回原始报文 + 结构化字段。
        /// DB 号取配置的 ReadDbNumber（当前拆垛 PLC 为 600），长度取 <see cref="PLCDb600Codec.Length"/>。
        /// </summary>
        [HttpGet("{name}/destacker/db")]
        public async Task<ActionResult> ReadDestackerDb(string name, CancellationToken cancellationToken = default)
        {
            PlcConfig? config = _plcService.GetConfig(name);
            if (config == null)
            {
                return NotFound($"未找到 PLC：{name}");
            }

            if (IsDestacker(config) == false)
            {
                return BadRequest($"PLC[{name}] 不是拆垛 PLC（DeviceType 不是 Destacker）");
            }

            int dbNumber = config.ReadDbNumber;
            int length = PLCDb600Codec.Length;

            PlcReadResult result = await _plcService.ReadDbAsync(name, dbNumber, 0, length, cancellationToken);
            if (result.Success == false)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new
                {
                    success = false,
                    name,
                    error = result.ErrorMessage
                });
            }

            byte[] raw = result.Data;
            if (raw == null)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new
                {
                    success = false,
                    name,
                    error = "PLC 返回数据为空"
                });
            }

            DestackerDb600 model = PLCDb600Codec.Parse(raw);
            return Ok(new
            {
                success = true,
                name,
                db = dbNumber,
                length,
                hex = HexCodec.ToHex(raw),
                model
            });
        }

        /// <summary>
        /// 把 DB500（WCS → PLC）模型编码后写入拆垛 PLC，DB 号取配置的 WriteDbNumber（当前拆垛 PLC 为 500）。
        /// 请求体为 DB500 模型 JSON，未赋值字段按 0/空串处理。
        /// </summary>
        [HttpPost("{name}/destacker/db")]
        public async Task<ActionResult> WriteDestackerDb(string name, [FromBody] DestackerDb500 model,
            CancellationToken cancellationToken = default)
        {
            PlcConfig? config = _plcService.GetConfig(name);
            if (config == null)
            {
                return NotFound($"未找到 PLC：{name}");
            }

            if (IsDestacker(config) == false)
            {
                return BadRequest($"PLC[{name}] 不是拆垛 PLC（DeviceType 不是 Destacker）");
            }

            int dbNumber = config.WriteDbNumber;
            byte[] data = PLCDb500Codec.ToBytes(model);

            PlcWriteResult result = await _plcService.WriteDbAsync(name, dbNumber, 0, data, cancellationToken);
            if (result.Success == false)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new
                {
                    success = false,
                    name,
                    error = result.ErrorMessage
                });
            }

            return Ok(new
            {
                success = true,
                name,
                db = dbNumber,
                length = data.Length,
                hex = HexCodec.ToHex(data)
            });
        }

        /// <summary>判断是否为拆垛段 PLC（按配置的 DeviceType）。</summary>
        /// <param name="config">PLC 配置</param>
        private static bool IsDestacker(PlcConfig config)
        {
            return string.Equals(config.DeviceType, "Destacker", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>把 hex 字符串解析成字节数组（兼容 0x 前缀、空格、短横、逗号分隔）。</summary>
        /// <param name="hex">十六进制字符串</param>
        private static byte[] ParseHex(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
            {
                throw new ArgumentException("缺少 Hex 数据");
            }

            string clean = hex.Trim();
            if (clean.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                clean = clean.Substring(2);
            }

            clean = clean.Replace(" ", "", StringComparison.Ordinal);
            clean = clean.Replace("-", "", StringComparison.Ordinal);
            clean = clean.Replace(",", "", StringComparison.Ordinal);

            return Convert.FromHexString(clean);
        }
    }

    /// <summary>DB 块写入请求体</summary>
    public class PlcDbWriteRequest
    {
        // 从 DB 的第几个字节开始写
        public int StartByte { get; set; } = 0;

        // 要写入的字节内容（十六进制字符串）
        public string Hex { get; set; } = "";
    }
}
