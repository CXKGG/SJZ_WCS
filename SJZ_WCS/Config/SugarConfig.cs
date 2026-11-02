using Microsoft.Extensions.DependencyInjection;
using Model.Entities;
using Model.PLC;
using SqlSugar;

namespace SJZ_WCS.Config
{
    /// <summary>
    /// SqlSugar 客户端工厂：从 appsettings.json 的 ConnectionStrings:Default 读取 MySQL 连接串
    /// </summary>
    public class SugarConfig
    {
        public static SqlSugarScope GetInstance(IConfiguration configuration)
        {
            string? connectionString = configuration.GetConnectionString("Default");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("未配置数据库连接串 ConnectionStrings:Default");
            }

            // SqlSugarScope 内部按线程/请求维护自己的连接，是官方推荐的线程安全单例用法
            SqlSugarScope db = new SqlSugarScope(
                new ConnectionConfig()
                {
                    ConnectionString = connectionString,
                    DbType = DbType.MySql,          // 数据库类型：MySQL
                    IsAutoCloseConnection = true,   // 自动释放连接
                    InitKeyType = InitKeyType.Attribute // 读取实体上的 [SugarColumn] 特性
                });

            // 开启控制台打印 SQL，方便排查问题
            db.Aop.OnLogExecuting = (sql, pars) =>
            {
                Console.WriteLine("执行 SQL: " + sql);
            };

            return db;
        }

        /// <summary>
        /// 初始化数据库：建库、按实体建表，并把 appsettings.json 里的拆垛 PLC 配置写成 PLCinfo 记录。
        /// 应用启动时调用一次；数据库暂不可用时由调用方记录错误，不影响 PLC 连接与接口调试。
        /// </summary>
        public static void InitDatabase(IServiceProvider services)
        {
            using IServiceScope scope = services.CreateScope();
            ISqlSugarClient db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            IConfiguration configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

            // MySQL 下 CreateDatabase 只在库不存在时创建（本项目库名 sjz_wcs）
            db.DbMaintenance.CreateDatabase();

            // 实体建表：设备信息（拆垛 PLC / RGV）+ 系统日志 + PLC 发送缓存 + 拆垛任务 + 扫码上报记录 + 系统用户
            db.CodeFirst.InitTables(typeof(PLCinfo), typeof(RGVinfo), typeof(SysLog), typeof(PlcSendMsg),
                typeof(TaskInfo), typeof(ScanRecord), typeof(SysUser));

            // 设备信息初始化：以 appsettings.json 为数据源，重复启动不会产生重复记录
            SeedPlcInfo(db, configuration);
            SeedRgvInfo(db, configuration);

            // 系统用户初始化：表里没有账号时建一个默认管理员（admin / 123456），方便先登录调试
            SeedSysUser(db);
        }

        /// <summary>
        /// 把拆垛段 PLC 的配置写入 PLCinfo 表：存在则按配置更新，不存在则新增。
        /// </summary>
        private static void SeedPlcInfo(ISqlSugarClient db, IConfiguration configuration)
        {
            List<PlcConfig> plcs = configuration.GetSection("Plcs").Get<List<PlcConfig>>() ?? new List<PlcConfig>();

            // 只把拆垛段 PLC 写进 PLCinfo 表（RGV 是小车设备，不进这张表）
            List<PlcConfig> destackers = new List<PlcConfig>();
            foreach (PlcConfig plc in plcs)
            {
                if (string.Equals(plc.DeviceType, "Destacker", StringComparison.OrdinalIgnoreCase))
                {
                    destackers.Add(plc);
                }
            }

            foreach (PlcConfig plc in destackers)
            {
                PLCinfo? existing = db.Queryable<PLCinfo>().First(x => x.plccode == plc.Name);

                if (existing == null)
                {
                    db.Insertable(new PLCinfo
                    {
                        plccode = plc.Name,
                        plcname = plc.DisplayName,
                        ipaddress = plc.Ip,
                        port = plc.Port.ToString(),
                        model = plc.CpuType,
                        rack = plc.Rack.ToString(),
                        slot = plc.Slot.ToString(),
                        protocol = plc.Protocol,
                        readdb = plc.ReadDbNumber.ToString(),
                        writedb = plc.WriteDbNumber.ToString(),
                        isactive = "True",
                        status = "True",
                        taskstatus = "空闲",
                        lastheartbeat = ""
                    }).ExecuteCommand();
                }
                else
                {
                    // 配置是设备信息的数据源：名称、IP、型号等以 appsettings.json 为准
                    existing.plcname = plc.DisplayName;
                    existing.ipaddress = plc.Ip;
                    existing.port = plc.Port.ToString();
                    existing.model = plc.CpuType;
                    existing.rack = plc.Rack.ToString();
                    existing.slot = plc.Slot.ToString();
                    existing.protocol = plc.Protocol;
                    existing.readdb = plc.ReadDbNumber.ToString();
                    existing.writedb = plc.WriteDbNumber.ToString();
                    db.Updateable(existing).ExecuteCommand();
                }
            }

            if (destackers.Count > 0)
            {
                Console.WriteLine($"PLCinfo 设备信息初始化完成，共 {destackers.Count} 台拆垛 PLC");
            }
        }

        /// <summary>
        /// 把 RGV 小车配置写入 RGVinfo 表：存在则按配置更新，不存在则新增。
        /// </summary>
        private static void SeedRgvInfo(ISqlSugarClient db, IConfiguration configuration)
        {
            List<PlcConfig> plcs = configuration.GetSection("Plcs").Get<List<PlcConfig>>() ?? new List<PlcConfig>();

            int count = 0;
            foreach (PlcConfig plc in plcs)
            {
                if (!string.Equals(plc.DeviceType, "RGV", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                RGVinfo? existing = db.Queryable<RGVinfo>().First(x => x.plccode == plc.Name);

                if (existing == null)
                {
                    db.Insertable(new RGVinfo
                    {
                        plccode = plc.Name,
                        plcname = plc.DisplayName,
                        ipaddress = plc.Ip,
                        port = plc.Port.ToString(),
                        model = plc.CpuType,
                        rack = plc.Rack.ToString(),
                        slot = plc.Slot.ToString(),
                        isactive = "True",
                        status = "在线",
                        taskstatus = "空闲",
                        lastheartbeat = ""
                    }).ExecuteCommand();
                }
                else
                {
                    // 配置是设备信息的数据源：名称、IP、型号等以 appsettings.json 为准
                    existing.plcname = plc.DisplayName;
                    existing.ipaddress = plc.Ip;
                    existing.port = plc.Port.ToString();
                    existing.model = plc.CpuType;
                    existing.rack = plc.Rack.ToString();
                    existing.slot = plc.Slot.ToString();
                    db.Updateable(existing).ExecuteCommand();
                }

                count++;
            }

            if (count > 0)
            {
                Console.WriteLine($"RGVinfo 设备信息初始化完成，共 {count} 台 RGV");
            }
        }

        /// <summary>
        /// 系统用户初始化：sysuser 表里一个账号都没有时，建一个默认管理员。
        /// 账号 admin / 密码 123456（明文，按需求），供前端先登录调试；正式账号后续直接往表里加。
        /// </summary>
        private static void SeedSysUser(ISqlSugarClient db)
        {
            SysUser? first = db.Queryable<SysUser>().First(x => x.id > 0);
            if (first != null)
            {
                return;
            }

            DateTime now = DateTime.Now;
            db.Insertable(new SysUser
            {
                usercode = "admin",
                username = "管理员",
                password = "123456",
                role = "管理员",
                isactive = true,
                createtime = now,
                updatetime = now
            }).ExecuteCommand();

            Console.WriteLine("系统用户初始化完成：默认管理员 admin / 123456");
        }
    }
}
