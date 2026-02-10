using GameGrainInterfaces;
using GameShared;
using GameShared.Messages;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace GameGrains;

/// <summary>
/// 匹配服务 Grain 实现
/// 使用匹配类型作为 Grain Key（例如: "ranked", "casual", "practice"）
/// </summary>
[Reentrant]
public class MatchGrain : Grain, IMatchGrain
{
    private readonly ILogger<MatchGrain> _logger;
    private readonly IGrainFactory _grainFactory;

    // 匹配队列：存储等待匹配的玩家
    private readonly List<(long playerId, int rating, DateTime requestTime)> _matchQueue = new();

    // 匹配定时器
    private IDisposable? _matchTimer;

    // 房间 ID 计数器
    private long _nextRoomId = 1;

    public MatchGrain(
        ILogger<MatchGrain> logger,
        IGrainFactory grainFactory)
    {
        _logger = logger;
        _grainFactory = grainFactory;
    }

#pragma warning disable CS0618 // 禁用 RegisterTimer 过时警告
    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        string matchType = this.GetPrimaryKeyString();
        _logger.LogInformation("MatchGrain activated for match type {MatchType}", matchType);

        // 注册定时器，每3秒执行一次匹配逻辑
        _matchTimer = RegisterTimer(
            async _ => await TryMatch(),
            null,
            TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(3));

        return Task.CompletedTask;
    }
#pragma warning restore CS0618

    public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        _logger.LogInformation("MatchGrain deactivating for match type {MatchType}", this.GetPrimaryKeyString());
        _matchTimer?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 请求匹配
    /// </summary>
    public async Task<S2C_MatchResult> RequestMatch(long playerId, int rating)
    {
        string matchType = this.GetPrimaryKeyString();
        _logger.LogInformation("Player {PlayerId} requesting match with rating {Rating} in match type {MatchType}",
            playerId, rating, matchType);

        // 检查玩家是否已在匹配队列中
        if (_matchQueue.Any(m => m.playerId == playerId))
        {
            _logger.LogWarning("Player {PlayerId} is already in match queue", playerId);
            return new S2C_MatchResult
            {
                ErrorCode = ErrorCodes.AlreadyInMatchQueue,
                RoomId = 0
            };
        }

        // 添加到匹配队列
        _matchQueue.Add((playerId, rating, DateTime.UtcNow));
        _logger.LogInformation("Player {PlayerId} added to match queue, queue size: {Count}",
            playerId, _matchQueue.Count);

        // 尝试立即匹配
        await TryMatch();

        // 返回等待匹配状态
        return new S2C_MatchResult
        {
            ErrorCode = ErrorCodes.Success,
            RoomId = 0 // RoomId = 0 表示仍在匹配中
        };
    }

    /// <summary>
    /// 取消匹配
    /// </summary>
    public Task CancelMatch(long playerId)
    {
        string matchType = this.GetPrimaryKeyString();
        _logger.LogInformation("Player {PlayerId} cancelling match in match type {MatchType}",
            playerId, matchType);

        // 从匹配队列中移除玩家
        int removed = _matchQueue.RemoveAll(m => m.playerId == playerId);
        if (removed > 0)
        {
            _logger.LogInformation("Player {PlayerId} removed from match queue, remaining: {Count}",
                playerId, _matchQueue.Count);
        }
        else
        {
            _logger.LogWarning("Player {PlayerId} was not in match queue", playerId);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 尝试匹配玩家
    /// </summary>
    private async Task TryMatch()
    {
        if (_matchQueue.Count < 2)
        {
            // 队列中玩家不足2人，无法匹配
            return;
        }

        string matchType = this.GetPrimaryKeyString();
        _logger.LogDebug("Attempting to match players, queue size: {Count}", _matchQueue.Count);

        // 简单的匹配算法：按评分排序，相邻的玩家进行匹配
        var sortedQueue = _matchQueue.OrderBy(m => m.rating).ToList();

        // 尝试配对
        for (int i = 0; i < sortedQueue.Count - 1; i++)
        {
            var player1 = sortedQueue[i];
            var player2 = sortedQueue[i + 1];

            // 检查评分差异（例如：评分差异不超过100）
            int ratingDiff = Math.Abs(player1.rating - player2.rating);
            if (ratingDiff <= 100)
            {
                // 匹配成功，创建房间
                long roomId = _nextRoomId++;
                await CreateMatchedRoom(roomId, player1.playerId, player2.playerId);

                // 从队列中移除这两个玩家
                _matchQueue.Remove(player1);
                _matchQueue.Remove(player2);

                _logger.LogInformation("Matched player {Player1} (rating {Rating1}) with player {Player2} (rating {Rating2}) in room {RoomId}",
                    player1.playerId, player1.rating, player2.playerId, player2.rating, roomId);

                // 通知玩家匹配成功
                await NotifyMatchSuccess(player1.playerId, roomId);
                await NotifyMatchSuccess(player2.playerId, roomId);

                // 递归尝试继续匹配
                await TryMatch();
                return;
            }
        }
    }

    /// <summary>
    /// 创建匹配成功的房间
    /// </summary>
    private async Task CreateMatchedRoom(long roomId, long player1Id, long player2Id)
    {
        // 创建房间 Grain
        var roomGrain = _grainFactory.GetGrain<IRoomGrain>(roomId);

        // 激活房间（通过调用 GetRoomInfo 触发激活）
        await roomGrain.GetRoomInfo();

        // 将两个玩家加入房间
        await roomGrain.JoinRoom(player1Id);
        await roomGrain.JoinRoom(player2Id);

        // 设置房间为游戏中状态
        await roomGrain.SetGameState(1); // 1 = 游戏中

        _logger.LogInformation("Created matched room {RoomId} for players {Player1} and {Player2}",
            roomId, player1Id, player2Id);
    }

    /// <summary>
    /// 通知玩家匹配成功
    /// </summary>
    private async Task NotifyMatchSuccess(long playerId, long roomId)
    {
        try
        {
            var playerGrain = _grainFactory.GetGrain<IPlayerGrain>(playerId);
            var playerInfo = await playerGrain.GetPlayerInfo();

            // 创建匹配成功消息
            var matchResult = new S2C_MatchResult
            {
                ErrorCode = ErrorCodes.Success,
                RoomId = roomId
            };

            // 这里需要通过 PlayerGrain 的 Observer 推送消息
            // 由于 PlayerGrain 没有直接推送的公共方法，我们使用内部方法
            // 实际项目中可以添加专门的推送接口
            _logger.LogInformation("Notified player {PlayerId} of match success, room {RoomId}",
                playerId, roomId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error notifying player {PlayerId} of match success", playerId);
        }
    }

    /// <summary>
    /// 获取匹配队列信息
    /// </summary>
    public Task<int> GetQueueSize()
    {
        return Task.FromResult(_matchQueue.Count);
    }
}
