using SqlSugar;

namespace Model.Entities
{
    /// <summary>
    /// 系统用户表：登录账号，同时作为日志里的「操作人」（SysLog.userid / username）。
    /// <para>
    /// 按需求直接保存明文密码，登录时做字符串比对（不做哈希、不加盐）。
    /// 后续若要改成哈希，只需在 UserService.Login 里换成哈希比对，其他调用方不用动。
    /// </para>
    /// </summary>
    public class SysUser
    {
        /// <summary>主键，自增</summary>
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public long id { get; set; }

        /// <summary>登录账号（唯一）</summary>
        public string usercode { get; set; } = "";

        /// <summary>姓名（写日志时作为操作人显示）</summary>
        public string username { get; set; } = "";

        /// <summary>登录密码（按需求明文保存，仅内网使用）</summary>
        public string password { get; set; } = "";

        /// <summary>角色/岗位（可选，如 管理员、操作员）</summary>
        public string role { get; set; } = "";

        /// <summary>是否启用（停用的账号不能登录）</summary>
        public bool isactive { get; set; } = true;

        /// <summary>创建时间</summary>
        public DateTime createtime { get; set; }

        /// <summary>更新时间</summary>
        public DateTime updatetime { get; set; }
    }
}
