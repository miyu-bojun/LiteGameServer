namespace GameGrainInterfaces;

using Orleans;

/// <summary>
/// 玩家观察者接口
/// 用于 Grain 向客户端推送消息
/// Gateway 侧实现此接口，并作为 Observer 引用传递给 PlayerGrain
/// </summary>
public interface IPlayerObserver : IGrainObserver
{
    /// <summary>
    /// 推送消息到客户端
    /// </summary>
    /// <param name="messageId">消息 ID</param>
    /// <param name="payload">消息载荷（序列化后的字节数组）</param>
    void OnMessagePush(ushort messageId, byte[] payload);
}
