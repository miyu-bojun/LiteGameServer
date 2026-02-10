using System.Net.Sockets;
using GameShared;

namespace GameGateway;

/// <summary>
/// 客户端会话，维护一个TCP连接的完整上下文
/// </summary>
public class ClientSession : IDisposable
{
    public string SessionId { get; } = Guid.NewGuid().ToString("N");
    public long PlayerId { get; set; }
    public Socket Socket { get; }
    public bool IsAuthenticated { get; set; }
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;
    public int GatewayId { get; set; }

    private readonly PacketFrameDecoder _decoder = new();

    public ClientSession(Socket socket)
    {
        Socket = socket;
    }

    /// <summary>
    /// 获取包帧解码器
    /// </summary>
    public PacketFrameDecoder Decoder => _decoder;

    public void Dispose()
    {
        Socket?.Dispose();
    }
}
