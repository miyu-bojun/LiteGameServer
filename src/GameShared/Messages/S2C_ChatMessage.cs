using MemoryPack;

namespace GameShared.Messages;

/// <summary>
/// 服务端广播聊天消息
/// </summary>
[MemoryPackable]
public partial class S2C_ChatMessage
{
    /// <summary>频道ID</summary>
    public string ChannelId { get; set; } = string.Empty;

    /// <summary>发送者玩家ID</summary>
    public long SenderId { get; set; }

    /// <summary>发送者昵称</summary>
    public string SenderNickname { get; set; } = string.Empty;

    /// <summary>聊天内容</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>发送时间戳（毫秒）</summary>
    public long Timestamp { get; set; }
}
