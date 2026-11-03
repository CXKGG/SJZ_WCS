namespace Model.Dtos
{
    /// <summary>登录请求体</summary>
    public class LoginRequest
    {
        // 登录账号
        public string usercode { get; set; } = "";

        // 登录密码（明文，与库里 sysuser.password 比对）
        public string password { get; set; } = "";
    }

    /// <summary>登录响应体</summary>
    public class LoginResponse
    {
        // 是否登录成功
        public bool success { get; set; }

        // 结果说明
        public string message { get; set; } = "";

        // 登录成功时的用户信息；失败为 null
        public LoginUser? data { get; set; }
    }

    /// <summary>
    /// 登录返回的用户信息：前端拿到后，后续写接口请求头带上
    /// <c>X-User-Id</c> 与 <c>X-User-Name</c>，日志中间件就会把操作人记进 SysLog。
    /// </summary>
    public class LoginUser
    {
        // 用户主键（对应 X-User-Id 请求头）
        public long id { get; set; }

        // 登录账号
        public string usercode { get; set; } = "";

        // 姓名（对应 X-User-Name 请求头）
        public string username { get; set; } = "";

        // 角色/岗位
        public string role { get; set; } = "";
    }
}
