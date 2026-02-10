namespace GameGrainInterfaces;

using GameShared.Messages;
using Orleans;
using GameGrainInterfaces.States;

/// <summary>
/// 房间服务 Grain 接口
/// 使用整数 Key（RoomId）作为 Grain 标识
/// </summary>
public interface IRoomGrain : IGrainWithIntegerKey
{
    /// <summary>
    /// 玩家加入房间
    /// </summary>
    /// <param name="playerId">玩家 ID</param>
    /// <returns>是否加入成功</returns>
    Task<bool> JoinRoom(long playerId);

    /// <summary>
    /// 玩家离开房间
    /// </summary>
    /// <param name="playerId">玩家 ID</param>
    /// <returns></returns>
    Task LeaveRoom(long playerId);

    /// <summary>
    /// 处理玩家在房间内的操作
    /// </summary>
    /// <param name="playerId">玩家 ID</param>
    /// <param name="action">玩家操作</param>
    /// <returns></returns>
    Task PlayerAction(long playerId, C2S_PlayerAction action);

    /// <summary>
    /// 获取房间信息
    /// </summary>
    /// <returns>房间状态</returns>
    Task<RoomState> GetRoomInfo();

    /// <summary>
    /// 设置房间游戏状态
    /// </summary>
    /// <param name="gameState">游戏状态</param>
    /// <returns></returns>
    Task SetGameState(int gameState);

    /// <summary>
    /// 启动帧同步（开始游戏）
    /// </summary>
    /// <param name="frameRate">帧率（如 15 或 30）</param>
    Task StartFrameSync(int frameRate = 15);

    /// <summary>
    /// 停止帧同步
    /// </summary>
    Task StopFrameSync();
}
