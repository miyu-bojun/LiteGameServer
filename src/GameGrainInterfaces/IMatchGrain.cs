namespace GameGrainInterfaces;

using GameShared.Messages;
using Orleans;

/// <summary>
/// 匹配服务 Grain 接口
/// 使用字符串 Key（匹配类型）作为 Grain 标识
/// 例如: "ranked", "casual", "practice"
/// </summary>
public interface IMatchGrain : IGrainWithStringKey
{
    /// <summary>
    /// 请求匹配
    /// </summary>
    /// <param name="playerId">玩家 ID</param>
    /// <param name="rating">玩家评分/段位</param>
    /// <returns>匹配结果</returns>
    Task<S2C_MatchResult> RequestMatch(long playerId, int rating);

    /// <summary>
    /// 取消匹配
    /// </summary>
    /// <param name="playerId">玩家 ID</param>
    /// <returns></returns>
    Task CancelMatch(long playerId);

    /// <summary>
    /// 获取匹配队列大小
    /// </summary>
    /// <returns>队列中的玩家数量</returns>
    Task<int> GetQueueSize();
}
