using boottorrent_lib.communication;
using boottorrent_lib.communication.codec;
using btclient;
using btclient.artifact;
using btclient.handler;
using btclient.torrent;
using btclient.torrent.monotorrent;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/log.txt", rollingInterval: RollingInterval.Day)
    .Enrich.FromLogContext()
    .CreateLogger();

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSerilog(); 
builder.Services
    .AddOptions<BTClientSettings>()
    .BindConfiguration("Client")
    .ValidateOnStart();

//MQTT Setup
builder.Services.Configure<MqttSettings>(builder.Configuration.GetSection("Mqtt"));
builder.Services.AddSingleton<IMessageCodec, JsonMessageCodec>();
builder.Services.AddSingleton<MessageDispatcher>();
builder.Services.AddSingleton<ClientMqttService>();
builder.Services.AddSingleton<ArtifactRegistry>();
builder.Services.AddSingleton<TorrentAssignmentHandler>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ClientMqttService>());

builder.Services.AddSingleton<ITorrentClient, MonoTorrentClient>();

builder.Services.AddHostedService<ClientStatusWorker>();

var host = builder.Build();
host.Run();