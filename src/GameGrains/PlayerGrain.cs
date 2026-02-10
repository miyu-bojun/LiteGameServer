using GameGrainInterfaces;
using GameGrainInterfaces.States;
using GameShared;
using GameShared.Messages;
using Microsoft.Extensions.Logging;
using MemoryPack;
using Orleans;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace GameGrains;

/// <summary>
/// 玩家服务 Grain 实现
/// 使用 PlayerId 作为 Grain Key
/// </summary>
[Reentrant]
public class PlayerGrain : Grain, IPlayerGrain
{
    private readonly ILogger<PlayerGrain> _logger;
    private readonly IPersistentState<PlayerState> _state;
    private readonly IGrainFactory _grainFactory;
    private IPlayerObserver? _observer;

    public PlayerGrain(
        ILogger<PlayerGrain> logger,
        [PersistentState("PlayerState", "PostgreSQL")] IPersistentState<PlayerState> state,
        IGrainFactory grainFactory)
    {
        _logger = logger;
        _state = state;
        _grainFactory = grainFactory;
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        long playerId = this.GetPrimaryKeyLong();
        _logger.LogInformation("PlayerGrain activated for player_id {PlayerId}", playerId);

        // 如果是首次激活，初始化状态
        if (_state.State.PlayerId == 0)
        {
            _state.State.PlayerId = playerId;
            await _state.WriteStateAsync();
        }
    }

    /// <summary>
    /// 初始化玩家状态（供 LoginGrain 调用）
    /// </summary>
    public async Task InitializePlayerState(string account)
    {
        long playerId = this.GetPrimaryKeyLong();
        _logger.LogInformation("Initializing player state for player_id {PlayerId}, account {Account}", playerId, account);

        _state.State.PlayerId = playerId;
        _state.State.Nickname = $"Player{playerId}";
        _state.State.Level = 1;
        _state.State.Exp = 0;
        _state.State.Items = new List<ItemInfo>
        {
            new ItemInfo { ItemId = 1001, Count = 100 }, // 初始金币
            new ItemInfo { ItemId = 2001, Count = 1 }    // 初始道具
        };
        _state.State.CreateTime = DateTime.UtcNow;
        _state.State.LastLoginTime = DateTime.UtcNow;
        _state.State.CurrentRoomId = 0;
        _state.State.Rating = 1000;

        await _state.WriteStateAsync();
        _logger.LogInformation("Player state initialized for player_id {PlayerId}", playerId);
    }

    /// <summary>
    /// 订阅玩家观察者，用于向客户端推送消息
    /// </summary>
    public Task Subscribe(IPlayerObserver observer)
    {
        long playerId = this.GetPrimaryKeyLong();
        _logger.LogInformation("Player {PlayerId} subscribed to observer", playerId);
        _observer = observer;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 玩家断开连接时的回调
    /// </summary>
    public async Task OnDisconnected()
    {
        long playerId = this.GetPrimaryKeyLong();
        _logger.LogInformation("Player {PlayerId} disconnected", playerId);

        // 清除观察者
        _observer = null;

        // 如果玩家在房间中，离开房间
        if (_state.State.CurrentRoomId > 0)
        {
            var roomGrain = _grainFactory.GetGrain<IRoomGrain>(_state.State.CurrentRoomId);
            await roomGrain.LeaveRoom(playerId);
            _state.State.CurrentRoomId = 0;
            await _state.WriteStateAsync();
        }

        // 延迟5分钟停用 Grain
        this.DeactivateOnIdle();
    }

    /// <summary>
    /// 获取玩家信息
    /// </summary>
    public Task<S2C_PlayerInfo> GetPlayerInfo()
    {
        long playerId = this.GetPrimaryKeyLong();
        _logger.LogDebug("Getting player info for player_id {PlayerId}", playerId);

        return Task.FromResult(new S2C_PlayerInfo
        {
            PlayerId = _state.State.PlayerId,
            Nickname = _state.State.Nickname,
            Level = _state.State.Level,
            Exp = _state.State.Exp
        });
    }

    /// <summary>
    /// 获取背包信息
    /// </summary>
    public Task<S2C_BagInfo> GetBagInfo()
    {
        long playerId = this.GetPrimaryKeyLong();
        _logger.LogDebug("Getting bag info for player_id {PlayerId}", playerId);

        return Task.FromResult(new S2C_BagInfo
        {
            Items = _state.State.Items
        });
    }

    /// <summary>
    /// 进入房间
    /// </summary>
    public async Task<S2C_EnterRoom> EnterRoom(C2S_EnterRoom request)
    {
        long playerId = this.GetPrimaryKeyLong();
        _logger.LogInformation("Player {PlayerId} requesting to enter room {RoomId}", playerId, request.RoomId);

        // 检查玩家是否已在房间中
        if (_state.State.CurrentRoomId > 0)
        {
            _logger.LogWarning("Player {PlayerId} is already in room {RoomId}", playerId, _state.State.CurrentRoomId);
            return new S2C_EnterRoom
            {
                ErrorCode = ErrorCodes.PlayerAlreadyInRoom,
                RoomId = _state.State.CurrentRoomId
            };
        }

        try
        {
            // 获取房间 Grain 并加入
            var roomGrain = _grainFactory.GetGrain<IRoomGrain>(request.RoomId);
            bool success = await roomGrain.JoinRoom(playerId);

            if (success)
            {
                // 更新玩家状态
                _state.State.CurrentRoomId = request.RoomId;
                await _state.WriteStateAsync();

                _logger.LogInformation("Player {PlayerId} successfully entered room {RoomId}", playerId, request.RoomId);
                return new S2C_EnterRoom
                {
                    ErrorCode = ErrorCodes.Success,
                    RoomId = request.RoomId
                };
            }
            else
            {
                _logger.LogWarning("Player {PlayerId} failed to enter room {RoomId}", playerId, request.RoomId);
                return new S2C_EnterRoom
                {
                    ErrorCode = ErrorCodes.RoomFull,
                    RoomId = 0
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error when player {PlayerId} entering room {RoomId}", playerId, request.RoomId);
            return new S2C_EnterRoom
            {
                ErrorCode = ErrorCodes.RoomNotFound,
                RoomId = 0
            };
        }
    }

    /// <summary>
    /// 添加道具到背包
    /// </summary>
    public async Task AddItem(int itemId, int count)
    {
        long playerId = this.GetPrimaryKeyLong();
        _logger.LogDebug("Adding item {ItemId} x{Count} to player {PlayerId}", itemId, count, playerId);

        // 查找是否已存在该道具
        var existingItem = _state.State.Items.FirstOrDefault(i => i.ItemId == itemId);
        if (existingItem != null)
        {
            existingItem.Count += count;
        }
        else
        {
            _state.State.Items.Add(new ItemInfo { ItemId = itemId, Count = count });
        }

        await _state.WriteStateAsync();

        // 通知客户端背包更新
        await PushToClient(new S2C_BagInfo { Items = _state.State.Items });
    }

    /// <summary>
    /// 推送消息到客户端
    /// </summary>
    private Task PushToClient<T>(T message) where T : class
    {
        if (_observer == null)
        {
            _logger.LogWarning("Cannot push message to client: observer is null");
            return Task.CompletedTask;
        }

        try
        {
            ushort messageId = MessageRegistry.GetId<T>();
            byte[] payload = MemoryPackSerializer.Serialize(message);
            _observer.OnMessagePush(messageId, payload);
            _logger.LogDebug("Pushed message {MessageId} to client", messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pushing message to client");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 推送已编码的消息到客户端（供其他 Grain 调用）
    /// </summary>
    public Task PushMessage(ushort messageId, byte[] payload)
    {
        if (_observer == null)
        {
            _logger.LogWarning("Cannot push message {MessageId}: observer is null for player {PlayerId}",
                messageId, this.GetPrimaryKeyLong());
            return Task.CompletedTask;
        }

        try
        {
            _observer.OnMessagePush(messageId, payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pushing message {MessageId} to player {PlayerId}",
                messageId, this.GetPrimaryKeyLong());
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 增加玩家经验值
    /// </summary>
    public async Task AddExp(long exp)
    {
        long playerId = this.GetPrimaryKeyLong();
        _logger.LogDebug("Adding {Exp} exp to player {PlayerId}", exp, playerId);

        _state.State.Exp += exp;

        // 简单的升级逻辑：每1000经验升1级
        int newLevel = 1 + (int)(_state.State.Exp / 1000);
        if (newLevel > _state.State.Level)
        {
            _state.State.Level = newLevel;
            _logger.LogInformation("Player {PlayerId} leveled up to {Level}", playerId, newLevel);
        }

        await _state.WriteStateAsync();
    }
}
