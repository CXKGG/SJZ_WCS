using Interface.Users;
using Microsoft.AspNetCore.Mvc;
using Model.Dtos;
using Model.Entities;

namespace SJZ_WCS.Controllers
{
    /// <summary>
    /// 登录接口：账号密码直接在数据库 sysuser 表里比对，不签发 Token/Session。
    /// <para>
    /// 前端登录成功后保存返回的 <see cref="LoginUser"/>，之后调写接口时在请求头带上
    /// <c>X-User-Id</c>、<c>X-User-Name</c>，ApiLogMiddleware 会把操作人写进日志（SysLog.userid / username）。
    /// </para>
    /// </summary>
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly IUserService _userService;

        /// <summary>构造：注入用户服务</summary>
        /// <param name="userService">用户服务</param>
        public AuthController(IUserService userService)
        {
            _userService = userService;
        }

        /// <summary>
        /// 登录：POST /api/auth/login。
        /// 成功 200 + 用户信息；账号或密码错误 401；参数缺失 400。
        /// </summary>
        /// <param name="request">登录请求（账号 + 明文密码）</param>
        [HttpPost("login")]
        public ActionResult<LoginResponse> Login([FromBody] LoginRequest? request)
        {
            if (request == null
                || string.IsNullOrWhiteSpace(request.usercode)
                || string.IsNullOrEmpty(request.password))
            {
                return BadRequest(new LoginResponse
                {
                    success = false,
                    message = "账号和密码不能为空"
                });
            }

            SysUser? user = _userService.Login(request.usercode, request.password);
            if (user == null)
            {
                return Unauthorized(new LoginResponse
                {
                    success = false,
                    message = "账号或密码错误"
                });
            }

            return Ok(new LoginResponse
            {
                success = true,
                message = "登录成功",
                data = ToLoginUser(user)
            });
        }

        /// <summary>
        /// 按用户主键查用户：GET /api/auth/users/{id}，供前端刷新页面后恢复登录人信息。
        /// </summary>
        /// <param name="id">用户主键</param>
        [HttpGet("users/{id:long}")]
        public ActionResult<LoginUser> GetUser(long id)
        {
            SysUser? user = _userService.GetById(id);
            if (user == null)
            {
                return NotFound();
            }

            return Ok(ToLoginUser(user));
        }

        /// <summary>实体转登录返回对象（不返回密码）</summary>
        private static LoginUser ToLoginUser(SysUser user)
        {
            return new LoginUser
            {
                id = user.id,
                usercode = user.usercode,
                username = user.username,
                role = user.role
            };
        }
    }
}
