using MessagePack;

namespace boottorrent_lib.communication.codec;

public class MessagePackCodec : IMessageCodec
{
    public T Decode<T>(ReadOnlyMemory<byte> payload) =>
        MessagePackSerializer.Deserialize<T>(payload);

    public byte[] Encode<T>(T message) =>
        MessagePackSerializer.Serialize(message);
}