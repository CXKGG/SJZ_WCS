namespace Model.Dtos
{
    /// <summary>登录请求体</summary>
    public class LoginRequest
    {
        /// <summary>登录账号</summary>
        public string usercode { get; set; } = "";

        /// <summary>登录密码（明文，与库里 sysuser.password 比对）</summary>
        public string password { get; set; } = "";
    }

    /// <summary>登录响应体</summary>
    public class LoginResponse
    {
        /// <summary>是否登录成功</summary>
        public bool success { get; set; }

        /// <summary>结果说明</summary>
        public string message { get; set; } = "";

        /// <summary>登录成功时的用户信息；失败为 null</summary>
        public LoginUser? data { get; set; }
    }

    /// <summary>
    /// 登录返回的用户信息：前端拿到后，后续写接口请求头带上
    /// <c>X-User-Id</c> 与 <c>X-User-Name</c>，日志中间件就会把操作人记进 SysLog。
    /// </summary>
    public class LoginUser
    {
        /// <summary>用户主键（对应 X-User-Id 请求头）</summary>
        public long id { get; set; }

        /// <summary>登录账号</summary>
        public string usercode { get; set; } = "";

        /// <summary>姓名（对应 X-User-Name 请求头）</summary>
        public string username { get; set; } = "";

        /// <summary>角色/岗位</summary>
        public string role { get; set; } = "";
    }
}
