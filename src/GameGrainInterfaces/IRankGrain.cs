namespace GameGrainInterfaces;

using GameShared.Messages;
using Orleans;

/// <summary>
/// 排行榜 Grain 接口
/// 使用字符串 Key（排行榜类型）作为 Grain 标识
/// 如 "level"、"score"
/// </summary>
public interface IRankGrain : IGrainWithStringKey
{
    /// <summary>
    /// 更新玩家分数
    /// </summary>
    /// <param name="playerId">玩家ID</param>
    /// <param name="nickname">玩家昵称</param>
    /// <param name="score">分数</param>
    Task UpdateScore(long playerId, string nickname, long score);

    /// <summary>
    /// 获取排行榜（分页）
    /// </summary>
    /// <param name="startIndex">起始索引（0-based）</param>
    /// <param name="count">获取数量</param>
    /// <returns>排行榜数据</returns>
    Task<S2C_RankList> GetRankList(int startIndex = 0, int count = 10);

    /// <summary>
    /// 获取玩家排名
    /// </summary>
    /// <param name="playerId">玩家ID</param>
    /// <returns>排名（1-based），不在榜上返回 -1</returns>
    Task<int> GetPlayerRank(long playerId);
}
