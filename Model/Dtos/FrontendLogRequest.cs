namespace Model.Dtos
{
    /// <summary>
    /// 前端操作日志上报请求。
    /// <para>
    /// 浏览器端在页面切换、按钮点击、前端异常时调用 <c>POST /api/logs/frontend</c> 上报，
    /// 后端统一写入 SysLog（分类 = 用户操作），便于在「系统日志」页面按模块/关键字追溯全部前端操作。
    /// </para>
    /// </summary>
    public class FrontendLogRequest
    {
        /// <summary>功能模块，如 "前端/设备监控"。</summary>
        public string Module { get; set; } = "";

        /// <summary>动作名称，如 "页面访问"、"点击 连接"、"前端异常"。</summary>
        public string Action { get; set; } = "";

        /// <summary>日志摘要（列表页展示的短文本）。</summary>
        public string Content { get; set; } = "";

        /// <summary>详细信息（如页面路径、按钮文本、异常堆栈），可为空。</summary>
        public string? Detail { get; set; }

        /// <summary>操作对象，可为空（如 "PLC1"、"任务 T20261030001"）。</summary>
        public string? Target { get; set; }

        /// <summary>风险等级：1 低、2 中、3 高（默认低）。</summary>
        public int RiskLevel { get; set; } = 1;

        /// <summary>是否成功（默认成功）。</summary>
        public bool Success { get; set; } = true;

        /// <summary>耗时（毫秒），可为空。</summary>
        public long ElapsedMs { get; set; }
    }
}
