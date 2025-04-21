namespace boottorrent_lib.communication.codec;

public interface IMessageCodec
{
    T Decode<T>(ReadOnlyMemory<byte> payload);
    byte[] Encode<T>(T message);
}