using Interface.Logs;
using Interface.Users;
using Model.Entities;
using Model.Logs;
using SqlSugar;

namespace Service.Users
{
    /// <summary>
    /// 系统用户服务实现：直接查 sysuser 表比对明文密码（按需求不做哈希）。
    /// <para>
    /// 登录成功写一条低风险用户操作日志，失败写中风险日志（都带账号 IP），
    /// 这样即使中间件日志没记到，登录记录也能在 SysLog 里查到。
    /// </para>
    /// </summary>
    public class UserService : IUserService
    {
        private readonly ISqlSugarClient _db;
        private readonly ILogService _logService;

        /// <summary>构造：注入数据库与日志服务</summary>
        /// <param name="db">SqlSugar 客户端</param>
        /// <param name="logService">日志服务</param>
        public UserService(ISqlSugarClient db, ILogService logService)
        {
            _db = db;
            _logService = logService;
        }

        /// <summary>
        /// 登录：查启用账号后做明文密码比对。
        /// </summary>
        /// <param name="usercode">登录账号</param>
        /// <param name="password">登录密码（明文）</param>
        public SysUser? Login(string usercode, string password)
        {
            string code = (usercode ?? "").Trim();
            if (code.Length == 0)
            {
                return null;
            }

            string pwd = password ?? "";
            SysUser? user = _db.Queryable<SysUser>().First(x => x.usercode == code && x.isactive == true);
            if (user == null || user.password != pwd)
            {
                _logService.WriteUserOperation(LogRiskLevel.Medium, "登录", "登录失败",
                    "账号或密码错误：" + code, target: code, userId: "", userName: code, success: false);
                return null;
            }

            _logService.WriteUserOperation(LogRiskLevel.Low, "登录", "登录成功",
                "用户 " + user.username + "（" + user.usercode + "）登录成功",
                target: user.usercode, userId: user.id.ToString(), userName: user.username, success: true);
            return user;
        }

        /// <summary>按主键取用户</summary>
        /// <param name="id">用户主键</param>
        public SysUser? GetById(long id)
        {
            return _db.Queryable<SysUser>().First(x => x.id == id);
        }
    }
}
