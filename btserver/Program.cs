using boottorrent_lib.communication;
using boottorrent_lib.communication.codec;
using btserver;
using btserver.settings;
using btserver.torrent;
using btserver.torrent.monotorrent;
using btserver.transport;
using Serilog;

Log.Logger = new LoggerConfiguration()
 .WriteTo.Console()
 .WriteTo.File("logs/log.txt", rollingInterval: RollingInterval.Day)
 .Enrich.FromLogContext()
 .CreateLogger();

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSerilog(); 

//Config
builder.Services.Configure<MqttSettings>(builder.Configuration.GetSection("Mqtt"));
builder.Services.Configure<ArtifactSettings>(builder.Configuration.GetSection("artifacts"));

//Setup MQTT
builder.Services.AddSingleton<IMessageCodec, JsonMessageCodec>();
// builder.Services.Scan(scan => scan
//     .FromAssemblyOf<Program>()
//     .AddClasses(classes => classes.AssignableTo(typeof(IMessageHandler<>)))
//     .AsImplementedInterfaces()
//     .WithSingletonLifetime());
builder.Services.AddSingleton<MachineStartedHandler>();
builder.Services.AddSingleton<MachineStoppedHandler>();

builder.Services.AddSingleton<MessageDispatcher>();
builder.Services.AddSingleton<ServerMqttService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ServerMqttService>());



//Torrent / Artifact Management
builder.Services.AddSingleton<ITorrentCreator, MonoTorrentCreator>();


builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();