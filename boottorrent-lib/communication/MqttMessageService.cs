using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace boottorrent_lib.communication;

public abstract class MqttMessageService(
    MqttClientOptions options,
    ILogger logger,
    MessageDispatcher dispatcher) : MqttClientService(options, logger)
{
    private readonly ILogger _logger = logger;

    protected async Task HandleMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs eventArgs)
    {
        try
        {
            await dispatcher.DispatchAsync(eventArgs.ApplicationMessage.Topic,
                eventArgs.ApplicationMessage.PayloadSegment);
        } catch (Exception ex)
        {
            _logger.LogError(ex, "Error while handling MQTT message");
        }
    }

    public async Task PublishAsync(IMqttMessage mqttMessage, MqttTopicContext context)
    {
        var payload = dispatcher.Codec.Encode(mqttMessage);
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(context.ToTopic())
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        await MqttClient.PublishAsync(message);
    }

    public void AddHandler<TMessage>(string messageTypeKey, Func<MqttTopicContext, TMessage, Task> handler)
        where TMessage : IMqttMessage
    {
        dispatcher.AddHandler(messageTypeKey, handler);
    }
}