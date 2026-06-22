using boottorrent_lib.communication;
using boottorrent_lib.communication.codec;
using btclient;
using btclient.artifact;
using btclient.handler;
using btclient.torrent;
using btclient.torrent.monotorrent;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSerilog(config => config
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext:l}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/log.txt", rollingInterval: RollingInterval.Day));
builder.Services
    .AddOptions<BTClientSettings>()
    .BindConfiguration("Client")
    .ValidateOnStart();

builder.Services.AddHttpClient();

//MQTT Setup
builder.Services.Configure<MqttSettings>(builder.Configuration.GetSection("Mqtt"));
builder.Services.AddSingleton<IMessageCodec, JsonMessageCodec>();
builder.Services.AddSingleton<MessageDispatcher>();
builder.Services.AddSingleton<ClientMqttService>();
builder.Services.AddSingleton<ClientMachineConfigurationService>();
builder.Services.AddSingleton<ArtifactRegistry>();
builder.Services.AddSingleton<ArtifactUnassignmentHandler>();
builder.Services.AddSingleton<ArtifactAssignmentHandler>();
builder.Services.AddSingleton<MachineConfigurationHandler>();
builder.Services.AddSingleton<MachineReRegisterHandler>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ClientMqttService>());

builder.Services.AddSingleton<ITorrentClient, MonoTorrentClient>();

builder.Services.AddHostedService<ClientStatusWorker>();

var host = builder.Build();
host.Run();
