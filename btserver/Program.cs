using boottorrent_lib.communication;
using boottorrent_lib.communication.codec;
using btserver;
using btserver.transport;

var builder = Host.CreateApplicationBuilder(args);

//Setup MQTT
builder.Services.AddSingleton<IMessageCodec, JsonMessageCodec>();
// builder.Services.Scan(scan => scan
//     .FromAssemblyOf<Program>()
//     .AddClasses(classes => classes.AssignableTo(typeof(IMessageHandler<>)))
//     .AsImplementedInterfaces()
//     .WithSingletonLifetime());
 builder.Services.AddSingleton<MachineStartedHandler>();

builder.Services.AddSingleton<MessageDispatcher>();
builder.Services.Configure<MqttSettings>(builder.Configuration.GetSection("Mqtt"));
builder.Services.AddHostedService<ServerMqttService>();

//builder.Services.AddHostedService<Worker>();


var host = builder.Build();
host.Run();