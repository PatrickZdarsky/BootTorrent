using System.Text.Json;

namespace boottorrent_lib.communication.codec;

public class JsonMessageCodec : IMessageCodec
{
    public T Decode<T>(ReadOnlyMemory<byte> payload) =>
        JsonSerializer.Deserialize(payload.Span, typeof(T), MqttJsonContext.Default)! is T typed ? typed : throw new InvalidCastException();

    public byte[] Encode<T>(T message)
    {
        if (message == null) throw new ArgumentNullException(nameof(message));
        return JsonSerializer.SerializeToUtf8Bytes(message, message.GetType(), MqttJsonContext.Default);
    }
}