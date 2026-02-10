using GameGateway;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;

var host = Host.CreateDefaultBuilder(args)
    .UseOrleansClient((context, clientBuilder) =>
    {
        var pg = context.Configuration.GetConnectionString("PostgreSQL");

        clientBuilder
            .UseAdoNetClustering(options =>
            {
                options.Invariant = "Npgsql";
                options.ConnectionString = pg;
            })
            .Configure<ClusterOptions>(options =>
            {
                options.ClusterId = context.Configuration.GetValue<string>("Orleans:ClusterId") ?? "GameCluster";
                options.ServiceId = context.Configuration.GetValue<string>("Orleans:ServiceId") ?? "GameService";
            });
    })
    .ConfigureServices((context, services) =>
    {
        // 注册 GatewayService 为后台服务
        services.AddHostedService<GatewayService>();
    })
    .ConfigureLogging(logging =>
    {
        logging.AddConsole();
        logging.SetMinimumLevel(LogLevel.Information);
    })
    .Build();

await host.RunAsync();
