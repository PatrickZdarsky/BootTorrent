using boottorrent_lib.communication.codec;
using boottorrent_lib.communication.message;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace boottorrent_lib.communication;

public class MessageDispatcher
{
    private readonly ILogger<MessageDispatcher> _logger;
    private readonly Dictionary<string, List<Func<MqttTopicContext, ReadOnlyMemory<byte>, Task>>> _routes;

    public IMessageCodec Codec { get; }

    public MessageDispatcher(IMessageCodec codec, IServiceProvider provider, ILogger<MessageDispatcher> logger)
    {
        Codec = codec;
        _logger = logger;
        _routes = new Dictionary<string, List<Func<MqttTopicContext, ReadOnlyMemory<byte>, Task>>>();

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
                .GetProperty(nameof(IMessageHandler<>.MessageType))!;

            var eventTypeKey = messageTypeProp.GetValue(handlerInstance)?.ToString()!.ToLowerInvariant();
            if (eventTypeKey is null) continue;

            var handlers = GetOrCreateHandlersEntry(eventTypeKey);
            handlers.Add(async (machineId, payload) =>
            {
                var method = typeof(IMessageCodec).GetMethod(nameof(IMessageCodec.Decode))!.MakeGenericMethod(messageType);
                var msg = method.Invoke(Codec, [payload])!;

                var handleMethod = iface.GetMethod("HandleAsync")!;
                await (Task)handleMethod.Invoke(handlerInstance, [machineId, msg])!;
            });
        }
    }

    public void AddHandler<TMessage>(string messageTypeKey, Func<MqttTopicContext, TMessage, Task> handler)
        where TMessage : IMqttMessage
    {
        var key = messageTypeKey.ToLowerInvariant();
        
        var handlers = GetOrCreateHandlersEntry(key);
        handlers.Add(async (context, payload) =>
        {
            var messageType = typeof(TMessage);
            var method = typeof(IMessageCodec).GetMethod(nameof(IMessageCodec.Decode))!.MakeGenericMethod(messageType);
            var msg = (TMessage)method.Invoke(Codec, [payload])!;

            await handler(context, msg);
        });
    }
    
    private List<Func<MqttTopicContext, ReadOnlyMemory<byte>, Task>> GetOrCreateHandlersEntry(string eventTypeKey)
    {
        if (_routes.TryGetValue(eventTypeKey, out var value)) return value;
        
        value = [];
        _routes[eventTypeKey] = value;

        return value;
    }

    public async Task DispatchAsync(string topic, ReadOnlyMemory<byte> payload)
    {
        var context = MqttTopicContext.Parse(topic);
        
        if (_routes.TryGetValue(context.MessageType, out var handlers))
        {
            var tasks = handlers.Select(handler => handler(context, payload));
            await Task.WhenAll(tasks);
            return;
        }

        _logger.LogWarning("Unknown message type: {messageType}, Context: {context}", context.MessageType, context);
    }
}