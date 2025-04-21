namespace boottorrent_lib.communication;

public interface IMessageHandler<TMessage> where TMessage : IMqttMessage
{
    string MessageType { get; }
    Task HandleAsync(MqttTopicContext context, TMessage message);
}