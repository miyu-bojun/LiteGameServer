using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using GameGrains.Services;

Console.WriteLine("Starting GameSilo...");

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        // 注册 GameDbRepository
        services.AddSingleton<GameDbRepository>();
    })
    .UseOrleans((context, siloBuilder) =>
    {
        // 从 HostBuilderContext 获取连接字符串
        var connectionString = context.Configuration.GetConnectionString("PostgreSQL")
            ?? throw new InvalidOperationException("PostgreSQL connection string is required");

        var siloPort = context.Configuration.GetValue<int>("Silo:SiloPort", 11111);
        var gatewayPort = context.Configuration.GetValue<int>("Silo:GatewayPort", 30000);
        var serviceId = context.Configuration.GetValue<string>("Silo:ServiceId") ?? "GameServer";
        var clusterId = context.Configuration.GetValue<string>("Silo:ClusterId") ?? "GameServerCluster";

        // 配置集群信息
        siloBuilder.Configure<ClusterOptions>(options =>
        {
            options.ServiceId = serviceId;
            options.ClusterId = clusterId;
        });

        // 配置 Silo 端点
        siloBuilder.ConfigureEndpoints(
            siloPort: siloPort,
            gatewayPort: gatewayPort);

        // 配置 ADO.NET 集群成员资格（PostgreSQL）
        siloBuilder.UseAdoNetClustering(options =>
        {
            options.ConnectionString = connectionString;
            options.Invariant = "Npgsql";
        });

        // 配置 ADO.NET Grain 存储（PostgreSQL）
        // 注：Orleans 8 默认使用 Orleans 内置序列化，不再支持 UseJsonFormat
        //     如需 JSON 格式，可自定义 IGrainStorageSerializer
        siloBuilder.AddAdoNetGrainStorage("PostgreSQL", options =>
        {
            options.ConnectionString = connectionString;
            options.Invariant = "Npgsql";
        });

        // 配置 ADO.NET 提醒服务
        siloBuilder.UseAdoNetReminderService(options =>
        {
            options.ConnectionString = connectionString;
            options.Invariant = "Npgsql";
        });
    })
    .ConfigureLogging(logging =>
    {
        logging.AddConsole();
        logging.SetMinimumLevel(LogLevel.Information);
    })
    .Build();

Console.WriteLine("Starting Silo host...");
await host.StartAsync();
Console.WriteLine("Silo started successfully!");
Console.WriteLine("Press Ctrl+C to stop...");

// 等待终止信号
await host.WaitForShutdownAsync();

Console.WriteLine("Shutting down Silo...");
await host.StopAsync();
Console.WriteLine("Silo stopped.");
