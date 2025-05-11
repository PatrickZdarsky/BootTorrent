using boottorrent_lib.communication;
using boottorrent_lib.communication.codec;
using btserver;
using btserver.transport;
using Serilog;

Log.Logger = new LoggerConfiguration()
 .WriteTo.Console()
 .WriteTo.File("logs/log.txt", rollingInterval: RollingInterval.Day)
 .Enrich.FromLogContext()
 .CreateLogger();

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSerilog(); 

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
builder.Services.Configure<MqttSettings>(builder.Configuration.GetSection("Mqtt"));
builder.Services.AddSingleton<ServerMqttService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ServerMqttService>());

//builder.Services.AddHostedService<Worker>();


var host = builder.Build();
host.Run();