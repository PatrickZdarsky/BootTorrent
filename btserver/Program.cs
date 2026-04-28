using boottorrent_lib.communication;
using boottorrent_lib.communication.codec;
using btserver;
using btserver.Config;
using btserver.Config.Swarm;
using btserver.handler;
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

//Config
builder.Services.Configure<MqttSettings>(builder.Configuration.GetSection("Mqtt"));
builder.Services.Configure<TorrentConfig>(builder.Configuration.GetSection("Torrent"));

builder.Configuration
    .AddJsonFile("swarm.json", optional: false, reloadOnChange: true);

builder.Services.AddOptions<SwarmConfig>()
    .Configure<IConfiguration>((options, config) =>
    {
        options.Zones = config
            .GetSection("Zones")
            .GetChildren()
            .Select(SwarmConfigBinder.BindZone)
            .ToList();
    })
    .Validate(c => c.Zones.Count > 0, "At least one zone is required.")
    .ValidateOnStart();



//Todo: Fix dependency issues cause MQTT stuff needs other things but it gets loaded first
//Setup MQTT
builder.Services.AddSingleton<IMessageCodec, JsonMessageCodec>();
// builder.Services.Scan(scan => scan
//     .FromAssemblyOf<Program>()
//     .AddClasses(classes => classes.AssignableTo(typeof(IMessageHandler<>)))
//     .AsImplementedInterfaces()
//     .WithSingletonLifetime());
builder.Services.AddTransient<Lazy<ServerMqttService>>(provider => new Lazy<ServerMqttService>(provider.GetService<ServerMqttService>));
builder.Services.AddSingleton<MachineStartedHandler>();

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

