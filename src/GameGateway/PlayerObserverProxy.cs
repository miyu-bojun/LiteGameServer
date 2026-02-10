using System.Net.Sockets;
using GameGrainInterfaces;
using GameShared;

namespace GameGateway;

/// <summary>
/// PlayerObserver 实现，用于 Orleans Grain 向客户端推送消息
/// </summary>
public class PlayerObserverProxy : IPlayerObserver
{
    private readonly ClientSession _session;
    private readonly GatewayService _gatewayService;

    public PlayerObserverProxy(ClientSession session, GatewayService gatewayService)
    {
        _session = session;
        _gatewayService = gatewayService;
    }

    /// <summary>
    /// 接收来自 Orleans Grain 的消息推送
    /// </summary>
    public void OnMessagePush(ushort messageId, byte[] payload)
    {
        // 构建完整的包（Header + Body）
        var packet = new byte[PacketCodec.HeaderSize + payload.Length];
        
        // 写入包体长度（大端序）
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
            packet.AsSpan(0, 4), (uint)payload.Length);
        
        // 写入消息ID（大端序）
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(
            packet.AsSpan(4, 2), messageId);
        
        // 写入包体
        payload.CopyTo(packet.AsSpan(PacketCodec.HeaderSize));

        // 发送数据
        try
        {
            _session.Socket.Send(packet, SocketFlags.None);
        }
        catch
        {
            // 发送失败，可能是连接已断开
            _gatewayService.OnSessionDisconnected(_session);
        }
    }
}
