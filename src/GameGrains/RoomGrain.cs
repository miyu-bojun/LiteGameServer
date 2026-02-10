using GameGrainInterfaces;
using GameGrainInterfaces.States;
using GameShared;
using GameShared.Messages;
using MemoryPack;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Concurrency;

namespace GameGrains;

/// <summary>
/// 房间服务 Grain 实现
/// 使用 RoomId 作为 Grain Key
/// </summary>
[Reentrant]
public class RoomGrain : Grain, IRoomGrain
{
    private readonly ILogger<RoomGrain> _logger;
    private readonly IPersistentState<RoomState> _state;
    private readonly IGrainFactory _grainFactory;

    /// <summary>帧同步相关</summary>
    private IDisposable? _frameSyncTimer;
    private int _currentFrameId;
    private readonly List<FrameInput> _pendingInputs = new();

    public RoomGrain(
        ILogger<RoomGrain> logger,
        [PersistentState("RoomState", "PostgreSQL")] IPersistentState<RoomState> state,
        IGrainFactory grainFactory)
    {
        _logger = logger;
        _state = state;
        _grainFactory = grainFactory;
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        long roomId = this.GetPrimaryKeyLong();
        _logger.LogInformation("RoomGrain activated for room_id {RoomId}", roomId);

        // 如果是首次激活，初始化状态
        if (_state.State.RoomId == 0)
        {
            _state.State.RoomId = roomId;
            _state.State.RoomName = $"Room{roomId}";
            _state.State.MaxPlayers = 10;
            _state.State.Players = new List<long>();
            _state.State.CreateTime = DateTime.UtcNow;
            _state.State.GameState = 0; // 等待中
            _state.State.RoomType = 0; // 普通
            await _state.WriteStateAsync();
        }
    }

    /// <summary>
    /// 玩家加入房间
    /// </summary>
    public async Task<bool> JoinRoom(long playerId)
    {
        long roomId = this.GetPrimaryKeyLong();
        _logger.LogInformation("Player {PlayerId} attempting to join room {RoomId}", playerId, roomId);

        // 检查房间是否已满
        if (_state.State.Players.Count >= _state.State.MaxPlayers)
        {
            _logger.LogWarning("Room {RoomId} is full, player {PlayerId} cannot join", roomId, playerId);
            return false;
        }

        // 检查玩家是否已在房间中
        if (_state.State.Players.Contains(playerId))
        {
            _logger.LogWarning("Player {PlayerId} is already in room {RoomId}", playerId, roomId);
            return false;
        }

        // 添加玩家到房间
        _state.State.Players.Add(playerId);
        await _state.WriteStateAsync();

        _logger.LogInformation("Player {PlayerId} joined room {RoomId}, current player count: {Count}",
            playerId, roomId, _state.State.Players.Count);

        // 广播玩家加入消息给房间内其他玩家
        await BroadcastPlayerJoin(playerId);

        return true;
    }

    /// <summary>
    /// 玩家离开房间
    /// </summary>
    public async Task LeaveRoom(long playerId)
    {
        long roomId = this.GetPrimaryKeyLong();
        _logger.LogInformation("Player {PlayerId} leaving room {RoomId}", playerId, roomId);

        // 从房间中移除玩家
        bool removed = _state.State.Players.Remove(playerId);
        if (removed)
        {
            await _state.WriteStateAsync();

            _logger.LogInformation("Player {PlayerId} left room {RoomId}, remaining players: {Count}",
                playerId, roomId, _state.State.Players.Count);

            // 广播玩家离开消息给房间内其他玩家
            await BroadcastPlayerLeave(playerId);

            // 如果房间为空，停用 Grain
            if (_state.State.Players.Count == 0)
            {
                _logger.LogInformation("Room {RoomId} is now empty, deactivating", roomId);
                this.DeactivateOnIdle();
            }
        }
        else
        {
            _logger.LogWarning("Player {PlayerId} was not in room {RoomId}", playerId, roomId);
        }
    }

    /// <summary>
    /// 处理玩家在房间内的操作
    /// </summary>
    public async Task PlayerAction(long playerId, C2S_PlayerAction action)
    {
        long roomId = this.GetPrimaryKeyLong();
        _logger.LogDebug("Player {PlayerId} performed action {ActionType} in room {RoomId}",
            playerId, action.ActionType, roomId);

        // 检查玩家是否在房间中
        if (!_state.State.Players.Contains(playerId))
        {
            _logger.LogWarning("Player {PlayerId} is not in room {RoomId}, ignoring action", playerId, roomId);
            return;
        }

        // 广播玩家操作给房间内其他玩家
        await BroadcastPlayerAction(playerId, action);
    }

    /// <summary>
    /// 广播玩家加入消息
    /// </summary>
    private async Task BroadcastPlayerJoin(long playerId)
    {
        // 获取玩家信息
        var playerGrain = _grainFactory.GetGrain<IPlayerGrain>(playerId);
        var playerInfo = await playerGrain.GetPlayerInfo();

        // 创建加入消息（使用 PlayerAction 消息类型，ActionType 表示加入）
        var joinMessage = new S2C_PlayerAction
        {
            PlayerId = playerId,
            ActionType = 1, // 1 = 玩家加入
            ActionData = playerInfo.Level // 简化：使用等级作为额外数据
        };

        await BroadcastToAllPlayers(joinMessage);
    }

    /// <summary>
    /// 广播玩家离开消息
    /// </summary>
    private async Task BroadcastPlayerLeave(long playerId)
    {
        // 创建离开消息（使用 PlayerAction 消息类型，ActionType 表示离开）
        var leaveMessage = new S2C_PlayerAction
        {
            PlayerId = playerId,
            ActionType = 2, // 2 = 玩家离开
            ActionData = 0
        };

        await BroadcastToAllPlayers(leaveMessage);
    }

    /// <summary>
    /// 广播玩家操作消息
    /// </summary>
    private async Task BroadcastPlayerAction(long playerId, C2S_PlayerAction action)
    {
        // 创建操作消息
        var actionMessage = new S2C_PlayerAction
        {
            PlayerId = playerId,
            ActionType = action.ActionType,
            ActionData = action.ActionData
        };

        await BroadcastToAllPlayers(actionMessage);
    }

    /// <summary>
    /// 向房间内所有玩家广播消息（通过 PlayerGrain.PushMessage）
    /// </summary>
    private async Task BroadcastToAllPlayers(S2C_PlayerAction message)
    {
        ushort messageId = MessageRegistry.GetId<S2C_PlayerAction>();
        byte[] payload = MemoryPackSerializer.Serialize(message);

        await BroadcastRawToAllPlayers(messageId, payload);
    }

    /// <summary>
    /// 向房间内所有玩家广播原始消息
    /// </summary>
    private async Task BroadcastRawToAllPlayers(ushort messageId, byte[] payload)
    {
        var tasks = new List<Task>();
        foreach (long playerId in _state.State.Players)
        {
            var playerGrain = _grainFactory.GetGrain<IPlayerGrain>(playerId);
            tasks.Add(PushToPlayerSafe(playerGrain, messageId, payload, playerId));
        }
        await Task.WhenAll(tasks);
    }

    private async Task PushToPlayerSafe(IPlayerGrain playerGrain, ushort messageId, byte[] payload, long playerId)
    {
        try
        {
            await playerGrain.PushMessage(messageId, payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error broadcasting message to player {PlayerId}", playerId);
        }
    }

    /// <summary>
    /// 获取房间信息
    /// </summary>
    public Task<RoomState> GetRoomInfo()
    {
        long roomId = this.GetPrimaryKeyLong();
        _logger.LogDebug("Getting room info for room_id {RoomId}", roomId);
        return Task.FromResult(_state.State);
    }

    /// <summary>
    /// 设置房间游戏状态
    /// </summary>
    public async Task SetGameState(int gameState)
    {
        long roomId = this.GetPrimaryKeyLong();
        _logger.LogInformation("Setting game state for room {RoomId} to {GameState}", roomId, gameState);

        _state.State.GameState = gameState;
        await _state.WriteStateAsync();
    }

    /// <summary>
    /// 启动帧同步
    /// </summary>
    public async Task StartFrameSync(int frameRate = 15)
    {
        long roomId = this.GetPrimaryKeyLong();

        if (_frameSyncTimer != null)
        {
            _logger.LogWarning("Frame sync already running for room {RoomId}", roomId);
            return;
        }

        _currentFrameId = 0;
        _pendingInputs.Clear();

        // 设置游戏状态为进行中
        _state.State.GameState = 1;
        await _state.WriteStateAsync();

        // 注册定频 Timer
        var interval = TimeSpan.FromMilliseconds(1000.0 / frameRate);
#pragma warning disable CS0618 // RegisterTimer is deprecated but RegisterGrainTimer API differs across 8.x versions
        _frameSyncTimer = this.RegisterTimer(OnFrameTick, null, interval, interval);
#pragma warning restore CS0618

        _logger.LogInformation("Frame sync started for room {RoomId} at {FrameRate}Hz", roomId, frameRate);
    }

    /// <summary>
    /// 停止帧同步
    /// </summary>
    public async Task StopFrameSync()
    {
        long roomId = this.GetPrimaryKeyLong();

        _frameSyncTimer?.Dispose();
        _frameSyncTimer = null;
        _pendingInputs.Clear();

        // 设置游戏状态为已结束
        _state.State.GameState = 2;
        await _state.WriteStateAsync();

        _logger.LogInformation("Frame sync stopped for room {RoomId}", roomId);
    }

    /// <summary>
    /// 帧同步 Tick：收集输入，广播帧数据
    /// </summary>
    private async Task OnFrameTick(object? state)
    {
        _currentFrameId++;

        // 构建帧数据
        var frameData = new S2C_FrameData
        {
            FrameId = _currentFrameId,
            Inputs = new List<FrameInput>(_pendingInputs)
        };

        // 清空待处理输入
        _pendingInputs.Clear();

        // 广播帧数据给所有玩家
        ushort messageId = MessageRegistry.GetId<S2C_FrameData>();
        byte[] payload = MemoryPackSerializer.Serialize(frameData);
        await BroadcastRawToAllPlayers(messageId, payload);
    }
}
