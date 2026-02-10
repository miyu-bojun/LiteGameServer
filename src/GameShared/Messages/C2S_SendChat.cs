using MemoryPack;

namespace GameShared.Messages;

/// <summary>
/// 客户端发送聊天消息
/// </summary>
[MemoryPackable]
public partial class C2S_SendChat
{
    /// <summary>频道ID（"world", "room_123", "private_456"）</summary>
    public string ChannelId { get; set; } = string.Empty;

    /// <summary>聊天内容</summary>
    public string Content { get; set; } = string.Empty;
}
