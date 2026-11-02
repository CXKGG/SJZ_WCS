using Autofac;
using Autofac.Extensions.DependencyInjection;
using Interface.Logs;
using Microsoft.Extensions.Hosting;
using Model.Logs;
using Scalar.AspNetCore;
using SJZ_WCS.Config;
using SJZ_WCS.Middleware;

var builder = WebApplication.CreateBuilder(args);

// 日志：只输出控制台。Windows 上默认还会注册 EventLog 提供程序，
// 非管理员运行时写事件日志会抛"拒绝访问"，进而把后台服务带崩，这里统一清掉。
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "HH:mm:ss ";
    options.SingleLine = false;
});
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));

// 控制器 + OpenAPI 文档
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// 跨域：前端 dist 独立部署（如 5173 / 其他机器）时允许跨域调用。
// 默认允许全部来源；如要收紧，在 appsettings 里配置 Cors:AllowedOrigins: ["http://ip:port"]。
string[]? corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (corsOrigins is { Length: > 0 })
        {
            policy.WithOrigins(corsOrigins);
        }
        else
        {
            policy.AllowAnyOrigin();
        }

        policy.AllowAnyHeader().AllowAnyMethod();
    });
});

// 后台服务（PLC 连接监视）异常时只记日志，不让整个应用退出
builder.Services.Configure<HostOptions>(options =>
{
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
});

// 用 Autofac 接管依赖注入：单例服务、SqlSugar、PLC 配置都在 AutofacModuleRegister 里注册
builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());
builder.Host.ConfigureContainer<ContainerBuilder>(containerBuilder =>
{
    containerBuilder.RegisterModule(new AutofacModuleRegister());
});

var app = builder.Build();

// 跨域必须放在接口日志中间件之前
app.UseCors();

// 接口日志：GET 请求记系统日志、写请求记用户操作日志（同步落库，失败不影响接口）
app.UseMiddleware<ApiLogMiddleware>();

// 日志服务（单例）。启动阶段的日志也走它，直接写入 SysLog 表。
ILogService logService = app.Services.GetRequiredService<ILogService>();

// 数据库初始化：建库（sjz_wcs）、按实体建表，并把 4 台拆垛 PLC 的设备信息写入 PLCinfo 表。
// 数据库暂未启动时只记错误日志，PLC 连接与接口调试不受影响。
try
{
    SugarConfig.InitDatabase(app.Services);
    logService.WriteSystem(LogRiskLevel.Low, "启动", "InitDatabase",
        "数据库初始化完成（建库/建表/设备信息同步）", target: "sjz_wcs");
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "数据库初始化失败（不影响 PLC 连接与接口调试）");
    logService.WriteException(LogCategory.System, LogRiskLevel.High, "启动", "InitDatabase",
        "数据库初始化失败（不影响 PLC 连接与接口调试）", ex, "sjz_wcs");
}

logService.WriteSystem(LogRiskLevel.Low, "启动", "Startup",
    $"应用启动成功，环境 {app.Environment.EnvironmentName}", target: string.Join(";", app.Urls));

if (app.Environment.IsDevelopment())
{
    // OpenAPI 文档：/openapi/v1.json
    app.MapOpenApi();

    // Scalar 调试界面：/scalar/v1
    app.MapScalarApiReference(options =>
    {
        options.WithLocalization(new ScalarLocalizationOptions { Locale = "zh-CN" });

        options.WithTitle("SJZ WCS 接口文档");
        options.WithProxy(null!);
    });
}

// 内网 WCS 接口走 HTTP 直连，不做 HTTPS 跳转
app.MapControllers();

app.Run();
