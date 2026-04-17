using boottorrent_lib.communication;
using boottorrent_lib.communication.codec;
using btserver;
using btserver.handler;
using btserver.settings;
using btserver.torrent;
using btserver.torrent.impl;
using btserver.torrent.monotorrent;
using btserver.torrent.tracker;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSerilog(config => config
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext:l}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/log.txt", rollingInterval: RollingInterval.Day));

//builder.Services.AddSerilog(); 

// Log.Logger = new LoggerConfiguration()
//  .ReadFrom.Configuration(builder.Configuration)
//  .Enrich.FromLogContext()
//  .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext:l}] {Message:lj}{NewLine}{Exception}")
//  .WriteTo.File("logs/log.txt", rollingInterval: RollingInterval.Day)
//  .CreateLogger();

//Config
builder.Services.Configure<MqttSettings>(builder.Configuration.GetSection("Mqtt"));
builder.Services.Configure<TorrentSettings>(builder.Configuration.GetSection("Torrent"));


//Todo: Fix dependecy issues cause MQTT stuff needs other things but it gets loaded first
//Setup MQTT
builder.Services.AddSingleton<IMessageCodec, JsonMessageCodec>();
// builder.Services.Scan(scan => scan
//     .FromAssemblyOf<Program>()
//     .AddClasses(classes => classes.AssignableTo(typeof(IMessageHandler<>)))
//     .AsImplementedInterfaces()
//     .WithSingletonLifetime());
builder.Services.AddTransient<Lazy<ServerMqttService>>(provider => new Lazy<ServerMqttService>(provider.GetService<ServerMqttService>));
builder.Services.AddSingleton<MachineStartedHandler>();
builder.Services.AddSingleton<MachineStoppedHandler>();

builder.Services.AddSingleton<MessageDispatcher>();
builder.Services.AddSingleton<ServerMqttService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ServerMqttService>());


//Torrent / Artifact Management
builder.Services.AddSingleton<ITorrentCreator, MonoTorrentCreator>();
builder.Services.AddSingleton<TorrentArtifactRegistry>();
builder.Services.AddSingleton<ITorrentArtifactRegistry>(sp => sp.GetRequiredService<TorrentArtifactRegistry>());
builder.Services.AddSingleton<ITorrentAccessPolicy, TorrentAccessPolicy>();
builder.Services.AddSingleton<ISeederRegistry, SeederRegistry>();
builder.Services.AddSingleton<TrackerServer>();

builder.Services.AddSingleton<MonoTorrentSeederService>();
builder.Services.AddSingleton<ITorrentSeeder>(sp => sp.GetRequiredService<MonoTorrentSeederService>());
builder.Services.AddSingleton<ITorrentSeederService>(sp => sp.GetRequiredService<MonoTorrentSeederService>());




builder.Services.AddHostedService<Worker>();


var app = builder.Build();

app.Run();

