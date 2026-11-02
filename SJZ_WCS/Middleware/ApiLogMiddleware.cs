using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Interface.Logs;
using Model.Logs;

namespace SJZ_WCS.Middleware
{
    /// <summary>
    /// 接口日志中间件：统一记录与前端（及外部系统）的接口交互。
    /// <para>
    /// 分类规则：GET/HEAD/OPTIONS 等读请求 → 系统日志（<see cref="LogCategory.System"/>）；
    /// POST/PUT/PATCH/DELETE 等写请求 → 用户操作日志（<see cref="LogCategory.UserOperation"/>）。
    /// </para>
    /// <para>
    /// 风险等级：2xx/3xx → 低；4xx → 中；5xx 或抛出未处理异常 → 高。
    /// 请求体只记录前 <c>LogSettings:MaxBodyLength</c> 个字符（默认 2000），避免大报文撑爆日志表；
    /// <c>LogSettings:SkipPaths</c> 里的路径前缀（默认 /openapi、/scalar、/api/logs）不记录，防止文档轮询和日志查询本身刷屏。
    /// </para>
    /// <para>
    /// 写日志走 <see cref="ILogService"/>（同步写 SysLog 表，写失败只记进程日志，不影响接口响应）；
    /// 即使日志落库失败也不会影响业务请求。
    /// </para>
    /// </summary>
    public sealed class ApiLogMiddleware
    {
        /// <summary>默认不记录的路径前缀：OpenAPI 文档、Scalar 页面、日志查询接口本身</summary>
        private static readonly string[] DefaultSkipPaths = new[] { "/openapi", "/scalar", "/api/logs" };

        /// <summary>请求体里 password 字段的掩码正则：日志里绝不落明文密码</summary>
        private static readonly Regex PasswordPattern = new Regex(
            "\"password\"\\s*:\\s*\"[^\"]*\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly RequestDelegate _next;
        private readonly ILogService _logService;
        private readonly ILogger<ApiLogMiddleware> _logger;
        private readonly bool _enabled;
        private readonly int _maxBodyLength;
        private readonly string[] _skipPaths;

        /// <summary>
        /// 构造中间件（由 UseMiddleware&lt;ApiLogMiddleware&gt;() 通过 DI 创建）。
        /// </summary>
        /// <param name="next">下一个中间件</param>
        /// <param name="logService">日志服务</param>
        /// <param name="logger">进程日志（仅用于记录"日志记录本身失败"等内部错误）</param>
        /// <param name="configuration">配置：LogSettings:Enabled / MaxBodyLength / SkipPaths</param>
        public ApiLogMiddleware(RequestDelegate next, ILogService logService,
            ILogger<ApiLogMiddleware> logger, IConfiguration configuration)
        {
            _next = next;
            _logService = logService;
            _logger = logger;
            _enabled = configuration.GetValue("LogSettings:Enabled", true);
            _maxBodyLength = Math.Max(0, configuration.GetValue("LogSettings:MaxBodyLength", 2000));
            _skipPaths = configuration.GetSection("LogSettings:SkipPaths").Get<string[]>() ?? DefaultSkipPaths;
        }

        /// <summary>
        /// 中间件入口：记录请求开始/结束、状态码、耗时、请求体与异常，然后按分类写日志。
        /// </summary>
        /// <param name="context">HTTP 上下文</param>
        public async Task InvokeAsync(HttpContext context)
        {
            // 未启用或路径在 SkipPaths 里，直接跳过
            if (!_enabled || ShouldSkip(context.Request.Path))
            {
                await _next(context);
                return;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            bool isWrite = IsWriteMethod(context.Request.Method);

            // 先读出请求体（随后把流复位，保证 Controller 还能正常绑定）
            string body = await ReadRequestBodyAsync(context.Request);
            Exception? caught = null;

            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                caught = ex;
                throw;
            }
            finally
            {
                stopwatch.Stop();
                try
                {
                    WriteLog(context, isWrite, body, caught, stopwatch.ElapsedMilliseconds);
                }
                catch (Exception logEx)
                {
                    // 日志记录失败绝不影响业务请求
                    _logger.LogError(logEx, "接口日志记录失败：{Method} {Path}", context.Request.Method, context.Request.Path);
                }
            }
        }

        /// <summary>按配置的路径前缀跳过记录（前缀不区分大小写）。</summary>
        private bool ShouldSkip(PathString path)
        {
            if (!path.HasValue)
            {
                return false;
            }

            string value = path.Value!;
            foreach (string prefix in _skipPaths)
            {
                if (!string.IsNullOrWhiteSpace(prefix)
                    && value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>是否为写操作（有副作用的请求记录为用户操作日志）。</summary>
        private static bool IsWriteMethod(string method)
        {
            return HttpMethods.IsPost(method)
                || HttpMethods.IsPut(method)
                || HttpMethods.IsPatch(method)
                || HttpMethods.IsDelete(method);
        }

        /// <summary>
        /// 读取请求体文本（最多 MaxBodyLength 字符）；读之前 EnableBuffering 并读完复位，
        /// 让后续模型绑定仍能读取同一份请求体。
        /// </summary>
        private async Task<string> ReadRequestBodyAsync(HttpRequest request)
        {
            if (!request.Body.CanRead || request.ContentLength == null || request.ContentLength == 0)
            {
                return "";
            }

            request.EnableBuffering();
            request.Body.Position = 0;

            using StreamReader reader = new StreamReader(
                request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            string text = await reader.ReadToEndAsync();
            request.Body.Position = 0;

            if (_maxBodyLength > 0 && text.Length > _maxBodyLength)
            {
                text = text.Substring(0, _maxBodyLength) + "…（已截断）";
            }

            return text;
        }

        /// <summary>按请求分类、风险等级构造并写入日志。</summary>
        private void WriteLog(HttpContext context, bool isWrite, string body, Exception? caught, long elapsedMs)
        {
            HttpRequest request = context.Request;
            int statusCode = context.Response.StatusCode;
            if (caught != null)
            {
                statusCode = StatusCodes.Status500InternalServerError;
            }

            bool success = caught == null && statusCode < 400;

            LogRiskLevel riskLevel;
            if (caught != null || statusCode >= 500)
            {
                riskLevel = LogRiskLevel.High;
            }
            else if (statusCode >= 400)
            {
                riskLevel = LogRiskLevel.Medium;
            }
            else
            {
                riskLevel = LogRiskLevel.Low;
            }

            string action = $"{request.Method} {request.Path}{request.QueryString}";
            string content;
            if (success)
            {
                content = "接口调用成功";
            }
            else
            {
                content = "接口调用失败";
            }

            content = content + $"（HTTP {statusCode}）";

            string ip = context.Connection.RemoteIpAddress?.ToString() ?? "";

            // 操作人：前端登录后把用户信息放在请求头里带过来（登录接口本身没有这个头，就留空）
            string userId = request.Headers["X-User-Id"].ToString();
            string userName = request.Headers["X-User-Name"].ToString();
            if (userName.Length == 0)
            {
                userName = context.User?.Identity?.Name ?? "";
            }

            string detail;
            if (caught == null)
            {
                // 请求体里的密码做掩码处理，避免把明文密码写进日志表
                detail = $"请求体：{MaskSensitive(body)}";
            }
            else
            {
                detail = $"异常：{caught.GetType().Name}: {caught.Message}{Environment.NewLine}{caught.StackTrace}";
            }

            if (isWrite)
            {
                _logService.WriteUserOperation(riskLevel, "API", action, content, detail,
                    target: request.Path.ToString(), userId: userId, userName: userName,
                    ipAddress: ip, success: success, elapsedMs: elapsedMs);
            }
            else
            {
                _logService.WriteSystem(riskLevel, "API", action, content, detail,
                    target: request.Path.ToString(), success: success, elapsedMs: elapsedMs, ipAddress: ip);
            }
        }

        /// <summary>把请求体里的 password 值替换为 ***（防明文密码落日志）。</summary>
        /// <param name="text">原始请求体</param>
        /// <returns>掩码后的文本</returns>
        private static string MaskSensitive(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            return PasswordPattern.Replace(text, "\"password\":\"***\"");
        }
    }
}
