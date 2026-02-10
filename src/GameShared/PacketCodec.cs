using MemoryPack;
using System.Buffers.Binary;
using GameShared.Messages;

namespace GameShared;

/// <summary>
/// 数据包编解码器
/// 协议格式：Length(4 bytes, Big-Endian) + MessageId(2 bytes, Big-Endian) + Body(N bytes)
/// </summary>
public static class PacketCodec
{
    /// <summary>包头大小：4字节Length + 2字节MessageId</summary>
    public const int HeaderSize = 6;

    /// <summary>
    /// 编码：将强类型消息对象编码为二进制包
    /// </summary>
    public static byte[] Encode<T>(T message) where T : class
    {
        ushort messageId = MessageRegistry.GetId<T>();
        byte[] body = MemoryPackSerializer.Serialize(message);

        byte[] packet = new byte[HeaderSize + body.Length];

        // 写入包体长度（大端序）
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(0, 4), (uint)body.Length);
        // 写入消息ID（大端序）
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(4, 2), messageId);
        // 写入包体
        body.CopyTo(packet.AsSpan(HeaderSize));

        return packet;
    }

    /// <summary>
    /// 解码：从二进制数据解析出消息ID和强类型对象
    /// </summary>
    public static (ushort messageId, object message) Decode(ReadOnlySpan<byte> packet)
    {
        uint bodyLength = BinaryPrimitives.ReadUInt32BigEndian(packet[..4]);
        ushort messageId = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(4, 2));
        ReadOnlySpan<byte> body = packet.Slice(HeaderSize, (int)bodyLength);

        Type? messageType = MessageRegistry.GetType(messageId);
        if (messageType == null)
            throw new InvalidOperationException($"Unknown message ID: {messageId}");

        object message = MemoryPackSerializer.Deserialize(messageType, body)!;
        return (messageId, message);
    }
}
