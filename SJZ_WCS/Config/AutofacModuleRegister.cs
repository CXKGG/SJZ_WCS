using Autofac;
using Interface.Logs;
using Interface.PLC;
using Interface.Scan;
using Interface.Scheduling;
using Interface.Users;
using Microsoft.Extensions.Hosting;
using Model.PLC;
using Service.Logging;
using Service.PLC;
using Service.RGV;
using Service.Scan;
using Service.Tasks;
using Service.Users;
using SqlSugar;

namespace SJZ_WCS.Config
{
    /// <summary>
    /// Autofac 模块注册类：数据库、PLC 配置、业务服务、后台服务统一在这里注册（全部单例）。
    /// <para>
    /// 精简后只有 3 个后台服务：PLC 业务（<see cref="PlcBackgroundService"/>）、
    /// RGV 业务（<see cref="RgvBackgroundService"/>）、任务调度（<see cref="TaskSchedulingBackgroundService"/>）。
    /// </para>
    /// </summary>
    public class AutofacModuleRegister : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            // 1. SqlSugar：SqlSugarScope 是线程安全的单例客户端，Controller / 后台服务共用
            builder.Register(c =>
            {
                IConfiguration configuration = c.Resolve<IConfiguration>();
                return SugarConfig.GetInstance(configuration);
            }).As<ISqlSugarClient>().SingleInstance();

            // 2. PLC 设备配置（appsettings.json 的 Plcs 节）：只读配置，解析一次后单例注入
            builder.Register(c =>
            {
                IConfiguration configuration = c.Resolve<IConfiguration>();
                return configuration.GetSection("Plcs").Get<List<PlcConfig>>() ?? new List<PlcConfig>();
            }).As<List<PlcConfig>>().SingleInstance();

            // 3. 日志服务：直接写 SysLog 表，不再有队列/落库后台服务
            builder.RegisterType<LogService>().As<ILogService>().SingleInstance();

            // 4. PLC 服务：连接 + 读写 + 快照 + 发送缓存 + 重连，原来 4 个接口合成这一个
            builder.RegisterType<PlcService>().As<IPlcService>().SingleInstance();

            // 5. 任务调度服务：任务状态机推进
            builder.RegisterType<TaskScheduling>().As<ITaskSchedulingService>().SingleInstance();

            // 6. 扫码上报服务：接收上位机上报的产品信息，校验后写 scanrecord 表
            builder.RegisterType<ScanRecordService>().As<IScanRecordService>().SingleInstance();

            // 7. 系统用户服务：登录校验（库内明文密码比对），日志里的操作人来自这里
            builder.RegisterType<UserService>().As<IUserService>().SingleInstance();

            // 8. 后台服务（3 个）：PLC 业务、RGV 业务、任务调度
            //    .As<IHostedService>()：注册成宿主服务，应用启动时会自动开始执行
            builder.RegisterType<PlcBackgroundService>().As<IHostedService>().SingleInstance();
            builder.RegisterType<RgvBackgroundService>().As<IHostedService>().SingleInstance();
            builder.RegisterType<TaskSchedulingBackgroundService>().As<IHostedService>().SingleInstance();
        }
    }
}
