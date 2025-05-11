using boottorrent_lib.communication.codec;
using boottorrent_lib.communication.message;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace boottorrent_lib.communication;

public class MessageDispatcher
{
    private readonly ILogger<MessageDispatcher> _logger;
    private readonly Dictionary<string, (Type messageType, Func<MqttTopicContext, ReadOnlyMemory<byte>, Task> handlerFunc)> _routes;

    public IMessageCodec Codec { get; }

    public MessageDispatcher(IMessageCodec codec, IServiceProvider provider, ILogger<MessageDispatcher> logger)
    {
        Codec = codec;
        _logger = logger;
        _routes = new Dictionary<string, (Type messageType, Func<MqttTopicContext, ReadOnlyMemory<byte>, Task> handlerFunc)>();

        var handlerInterfaceType = typeof(IMessageHandler<>);

        // Find all registered types implementing IMessageHandler<T>
        var allHandlerTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(x => x.GetTypes())
            .Where(t =>
                !t.IsAbstract &&
                t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == handlerInterfaceType)
            );

        foreach (var handlerType in allHandlerTypes)
        {
            var iface = handlerType.GetInterfaces().First(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == handlerInterfaceType);

            var messageType = iface.GetGenericArguments()[0];
            var handlerInstance = provider.GetRequiredService(handlerType);

            var messageTypeProp = handlerInterfaceType.MakeGenericType(messageType)
                .GetProperty(nameof(IMessageHandler<IMqttMessage>.MessageType))!;

            var eventTypeKey = messageTypeProp.GetValue(handlerInstance)?.ToString()!.ToLowerInvariant();
            if (eventTypeKey is null) continue;

            _routes[eventTypeKey] = (messageType, async (machineId, payload) =>
            {
                var method = typeof(IMessageCodec).GetMethod(nameof(IMessageCodec.Decode))!.MakeGenericMethod(messageType);
                var msg = method.Invoke(Codec, new object[] { payload })!;

                var handleMethod = iface.GetMethod("HandleAsync")!;
                await (Task)handleMethod.Invoke(handlerInstance, new[] { machineId, msg })!;
            });
        }
    }

    public Task DispatchAsync(string topic, ReadOnlyMemory<byte> payload)
    {
        var context = MqttTopicContext.Parse(topic);
        
        if (_routes.TryGetValue(context.MessageType, out var route))
        {
            return route.handlerFunc(context, payload);
        }

        _logger.LogWarning("Unknown message type: {messageType}, Context: {context}", context.MessageType, context);
        return Task.CompletedTask;
    }
}