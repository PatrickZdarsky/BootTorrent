using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace boottorrent_lib.communication;

public abstract class MqttMessageService : MqttClientService
{
    private readonly ILogger _logger;
    private readonly MessageDispatcher _dispatcher;

    protected MqttMessageService(MqttClientOptions options, ILogger logger,
        MessageDispatcher dispatcher) : base(options, logger)
    {
        _logger = logger;
        _dispatcher = dispatcher;
    }

    protected async Task HandleMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs eventArgs)
    {
        try
        {
            await _dispatcher.DispatchAsync(eventArgs.ApplicationMessage.Topic,
                eventArgs.ApplicationMessage.PayloadSegment);
        } catch (Exception ex)
        {
            _logger.LogError(ex, "Error while handling MQTT message");
        }
    }

    public async Task PublishAsync(IMqttMessage mqttMessage, MqttTopicContext context)
    {
        var payload = _dispatcher.Codec.Encode(mqttMessage);
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(context.ToTopic())
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        await MqttClient.PublishAsync(message);
    }
}