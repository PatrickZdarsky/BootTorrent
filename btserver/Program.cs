using boottorrent_lib.communication;
using boottorrent_lib.communication.codec;
using btserver;
using btserver.Config;
using btserver.Data;
using btserver.handler;
using btserver.Swarm;
using btserver.torrent;
using btserver.torrent.impl;
using btserver.torrent.monotorrent;
using btserver.torrent.tracker;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Serilog;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSerilog(config => config
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext:l}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/log.txt", rollingInterval: RollingInterval.Day));

//Config
builder.Services.AddDbContext<BtDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.Configure<MqttSettings>(builder.Configuration.GetSection("Mqtt"));
builder.Services.Configure<TorrentConfig>(builder.Configuration.GetSection("Torrent"));
builder.Services.Configure<MachineRegistryConfig>(builder.Configuration.GetSection("MachineRegistry"));

// builder.Configuration
//     .AddJsonFile("swarm.json", optional: false, reloadOnChange: true);

// builder.Services.AddOptions<SwarmConfig>()
//     .Configure<IConfiguration>((options, config) =>
//     {
//         options.Zones = config
//             .GetSection("Zones")
//             .GetChildren()
//             .Select(SwarmConfigBinder.BindZone)
//             .ToList();
//     })
//     .Validate(c => c.Zones.Count > 0, "At least one zone is required.")
//     .ValidateOnStart();



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


builder.Services.AddSingleton<MachineRegistry>();
builder.Services.AddSingleton<MachineConfigurationService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MachineRegistry>());
//Torrent / Artifact Management
builder.Services.AddSingleton<ITorrentCreator, MonoTorrentCreator>();
builder.Services.AddSingleton<TorrentArtifactRegistry>();
builder.Services.AddSingleton<ITorrentArtifactRegistry>(sp => sp.GetRequiredService<TorrentArtifactRegistry>());
builder.Services.AddSingleton<ISeederRegistry, SeederRegistry>();
builder.Services.AddSingleton<SubnetZoneTorrentAccessPolicy>();
builder.Services.AddSingleton<RandomPeerTorrentAccessPolicy>();
builder.Services.AddSingleton<ITorrentAccessPolicy>(sp => sp.GetRequiredService<SubnetZoneTorrentAccessPolicy>());
builder.Services.AddSingleton<ITorrentAccessPolicy>(sp => sp.GetRequiredService<RandomPeerTorrentAccessPolicy>());
builder.Services.AddSingleton<ITorrentAccessPolicyRegistry, TorrentAccessPolicyRegistry>();
builder.Services.AddSingleton<TrackerServer>();

builder.Services.AddSingleton<MonoTorrentSeederService>();
builder.Services.AddSingleton<ITorrentSeeder>(sp => sp.GetRequiredService<MonoTorrentSeederService>());
builder.Services.AddSingleton<ITorrentSeederService>(sp => sp.GetRequiredService<MonoTorrentSeederService>());

builder.Services.AddScoped<ArtifactAssigner>();
builder.Services.AddScoped<ZoneArtifactAssignmentService>();

builder.Services.AddHostedService<Worker>();

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.AddContext<AppJsonSerializerContext>();
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.EnableAnnotations();
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Version = "v1",
        Title = "BootTorrent API",
        Description = "An API for managing the BootTorrent swarm",
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthorization();

app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();
