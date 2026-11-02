using Model.Entities;

namespace Interface.Users
{
    /// <summary>
    /// 系统用户服务：登录校验、按主键取用户。
    /// <para>登录只做数据库明文比对（按需求），不涉及 Token/Session；用户信息由前端在后续请求头里回传。</para>
    /// </summary>
    public interface IUserService
    {
        /// <summary>
        /// 登录：按账号查启用用户，并与库里的明文密码比对。
        /// </summary>
        /// <param name="usercode">登录账号</param>
        /// <param name="password">登录密码（明文）</param>
        /// <returns>成功返回用户；账号不存在、已停用或密码不匹配返回 null</returns>
        SysUser? Login(string usercode, string password);

        /// <summary>
        /// 按主键取用户（接口回显、日志查操作人等场景用）。
        /// </summary>
        /// <param name="id">用户主键</param>
        /// <returns>不存在返回 null</returns>
        SysUser? GetById(long id);
    }
}
