using System.Buffers.Binary;

namespace GameShared;

/// <summary>
/// TCP 粘包/拆包处理器
/// 维护一个缓冲区，将新收到的数据追加到缓冲区，尝试解析出完整的包
/// </summary>
public class PacketFrameDecoder
{
    private readonly byte[] _buffer = new byte[64 * 1024]; // 64KB 缓冲区
    private int _writePos = 0;

    /// <summary>
    /// 将新收到的数据追加到缓冲区，尝试解析出完整的包
    /// </summary>
    public List<(ushort messageId, object message)> OnDataReceived(ReadOnlySpan<byte> data)
    {
        var results = new List<(ushort, object)>();

        // 追加数据到缓冲区
        data.CopyTo(_buffer.AsSpan(_writePos));
        _writePos += data.Length;

        // 循环尝试解析完整包
        int readPos = 0;
        while (true)
        {
            int remaining = _writePos - readPos;
            if (remaining < PacketCodec.HeaderSize)
                break; // 不够包头

            uint bodyLength = BinaryPrimitives.ReadUInt32BigEndian(_buffer.AsSpan(readPos, 4));
            int totalPacketSize = PacketCodec.HeaderSize + (int)bodyLength;

            if (remaining < totalPacketSize)
                break; // 不够一个完整包

            var packet = _buffer.AsSpan(readPos, totalPacketSize);
            var decoded = PacketCodec.Decode(packet);
            results.Add(decoded);

            readPos += totalPacketSize;
        }

        // 将未解析的数据移动到缓冲区头部
        if (readPos > 0)
        {
            int leftover = _writePos - readPos;
            _buffer.AsSpan(readPos, leftover).CopyTo(_buffer);
            _writePos = leftover;
        }

        return results;
    }

    /// <summary>
    /// 重置解码器状态
    /// </summary>
    public void Reset()
    {
        _writePos = 0;
    }
}
