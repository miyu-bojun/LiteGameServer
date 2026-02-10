namespace GameGrainInterfaces;

using GameShared.Messages;
using Orleans;

/// <summary>
/// 玩家服务 Grain 接口
/// 使用整数 Key（PlayerId）作为 Grain 标识
/// </summary>
public interface IPlayerGrain : IGrainWithIntegerKey
{
    /// <summary>
    /// 初始化玩家状态（供 LoginGrain 调用）
    /// </summary>
    /// <param name="account">账号名</param>
    /// <returns></returns>
    Task InitializePlayerState(string account);

    /// <summary>
    /// 订阅玩家观察者，用于向客户端推送消息
    /// </summary>
    /// <param name="observer">玩家观察者引用</param>
    /// <returns></returns>
    Task Subscribe(IPlayerObserver observer);

    /// <summary>
    /// 玩家断开连接时的回调
    /// </summary>
    /// <returns></returns>
    Task OnDisconnected();

    /// <summary>
    /// 进入房间
    /// </summary>
    /// <param name="request">进入房间请求</param>
    /// <returns>进入房间响应</returns>
    Task<S2C_EnterRoom> EnterRoom(C2S_EnterRoom request);

    /// <summary>
    /// 获取玩家信息
    /// </summary>
    /// <returns>玩家信息响应</returns>
    Task<S2C_PlayerInfo> GetPlayerInfo();

    /// <summary>
    /// 获取背包信息
    /// </summary>
    /// <returns>背包信息响应</returns>
    Task<S2C_BagInfo> GetBagInfo();

    /// <summary>
    /// 推送消息到客户端（供其他 Grain 调用，如 ChatGrain、RoomGrain）
    /// </summary>
    /// <param name="messageId">消息 ID</param>
    /// <param name="payload">消息载荷（序列化后的字节数组）</param>
    Task PushMessage(ushort messageId, byte[] payload);
}
