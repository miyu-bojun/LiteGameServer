namespace GameGrainInterfaces;

using GameShared.Messages;
using Orleans;

/// <summary>
/// 聊天频道 Grain 接口
/// 使用字符串 Key（ChannelId）作为 Grain 标识
/// 如 "world"、"room_123"、"guild_456"
/// </summary>
public interface IChatGrain : IGrainWithStringKey
{
    /// <summary>
    /// 发送聊天消息到频道
    /// </summary>
    /// <param name="senderId">发送者玩家ID</param>
    /// <param name="senderNickname">发送者昵称</param>
    /// <param name="content">聊天内容</param>
    Task SendMessage(long senderId, string senderNickname, string content);

    /// <summary>
    /// 玩家加入频道（订阅消息推送）
    /// </summary>
    /// <param name="playerId">玩家ID</param>
    Task JoinChannel(long playerId);

    /// <summary>
    /// 玩家离开频道
    /// </summary>
    /// <param name="playerId">玩家ID</param>
    Task LeaveChannel(long playerId);

    /// <summary>
    /// 获取最近的聊天记录
    /// </summary>
    /// <param name="count">获取数量</param>
    /// <returns>最近的聊天消息列表</returns>
    Task<List<S2C_ChatMessage>> GetRecentMessages(int count = 20);
}
