using GameGrainInterfaces;
using GameShared;
using GameShared.Messages;
using MemoryPack;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Concurrency;

namespace GameGrains;

/// <summary>
/// 聊天频道 Grain 实现
/// Key = ChannelId（如 "world", "room_123"）
/// 内存中维护频道成员和最近消息，不持久化
/// </summary>
[Reentrant]
public class ChatGrain : Grain, IChatGrain
{
    private readonly ILogger<ChatGrain> _logger;
    private readonly IGrainFactory _grainFactory;

    /// <summary>频道内的玩家ID列表</summary>
    private readonly HashSet<long> _members = new();

    /// <summary>最近消息缓存（环形缓冲，最多 100 条）</summary>
    private readonly LinkedList<S2C_ChatMessage> _recentMessages = new();
    private const int MaxRecentMessages = 100;

    public ChatGrain(ILogger<ChatGrain> logger, IGrainFactory grainFactory)
    {
        _logger = logger;
        _grainFactory = grainFactory;
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        var channelId = this.GetPrimaryKeyString();
        _logger.LogInformation("ChatGrain activated for channel {ChannelId}", channelId);
        return base.OnActivateAsync(cancellationToken);
    }

    /// <summary>
    /// 发送聊天消息到频道，广播给所有成员
    /// </summary>
    public async Task SendMessage(long senderId, string senderNickname, string content)
    {
        var channelId = this.GetPrimaryKeyString();

        var chatMessage = new S2C_ChatMessage
        {
            ChannelId = channelId,
            SenderId = senderId,
            SenderNickname = senderNickname,
            Content = content,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        // 缓存到最近消息
        _recentMessages.AddLast(chatMessage);
        if (_recentMessages.Count > MaxRecentMessages)
        {
            _recentMessages.RemoveFirst();
        }

        _logger.LogDebug("Chat message in channel {ChannelId} from {SenderId}: {Content}",
            channelId, senderId, content);

        // 广播给所有频道成员
        await BroadcastToMembers(chatMessage);
    }

    /// <summary>
    /// 玩家加入频道
    /// </summary>
    public Task JoinChannel(long playerId)
    {
        _members.Add(playerId);
        _logger.LogInformation("Player {PlayerId} joined channel {ChannelId}, members: {Count}",
            playerId, this.GetPrimaryKeyString(), _members.Count);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 玩家离开频道
    /// </summary>
    public Task LeaveChannel(long playerId)
    {
        _members.Remove(playerId);
        _logger.LogInformation("Player {PlayerId} left channel {ChannelId}, members: {Count}",
            playerId, this.GetPrimaryKeyString(), _members.Count);

        // 如果频道为空，延迟停用
        if (_members.Count == 0)
        {
            DeactivateOnIdle();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取最近的聊天记录
    /// </summary>
    public Task<List<S2C_ChatMessage>> GetRecentMessages(int count = 20)
    {
        var messages = _recentMessages
            .Reverse()
            .Take(count)
            .Reverse()
            .ToList();
        return Task.FromResult(messages);
    }

    /// <summary>
    /// 广播消息给频道内所有成员（通过 PlayerGrain 的 Observer 推送）
    /// </summary>
    private Task BroadcastToMembers(S2C_ChatMessage message)
    {
        ushort messageId = MessageRegistry.GetId<S2C_ChatMessage>();
        byte[] payload = MemoryPackSerializer.Serialize(message);

        var tasks = new List<Task>();
        foreach (var playerId in _members)
        {
            var playerGrain = _grainFactory.GetGrain<IPlayerGrain>(playerId);
            // 使用 fire-and-forget 方式推送，避免单个玩家离线阻塞整个广播
            tasks.Add(PushToPlayerSafe(playerGrain, messageId, payload));
        }

        return Task.WhenAll(tasks);
    }

    private async Task PushToPlayerSafe(IPlayerGrain playerGrain, ushort messageId, byte[] payload)
    {
        try
        {
            await playerGrain.PushMessage(messageId, payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to push chat message to player");
        }
    }
}
