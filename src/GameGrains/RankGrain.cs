using GameGrainInterfaces;
using GameShared.Messages;
using Microsoft.Extensions.Logging;
using Orleans;

namespace GameGrains;

/// <summary>
/// 排行榜 Grain 实现
/// Key = 排行榜类型（如 "level", "score"）
/// 内存中使用 SortedSet 维护排行，定期快照
/// </summary>
public class RankGrain : Grain, IRankGrain
{
    private readonly ILogger<RankGrain> _logger;

    /// <summary>排行数据：PlayerId → (Nickname, Score)</summary>
    private readonly Dictionary<long, (string Nickname, long Score)> _playerData = new();

    /// <summary>排序后的排行榜（按分数降序）</summary>
    private readonly SortedSet<(long Score, long PlayerId)> _sortedRank = new(
        Comparer<(long Score, long PlayerId)>.Create((a, b) =>
        {
            int cmp = b.Score.CompareTo(a.Score); // 降序
            return cmp != 0 ? cmp : a.PlayerId.CompareTo(b.PlayerId); // 相同分数按 PlayerId 升序
        }));

    public RankGrain(ILogger<RankGrain> logger)
    {
        _logger = logger;
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        var rankType = this.GetPrimaryKeyString();
        _logger.LogInformation("RankGrain activated for type {RankType}", rankType);
        return base.OnActivateAsync(cancellationToken);
    }

    /// <summary>
    /// 更新玩家分数
    /// </summary>
    public Task UpdateScore(long playerId, string nickname, long score)
    {
        // 移除旧数据
        if (_playerData.TryGetValue(playerId, out var oldData))
        {
            _sortedRank.Remove((oldData.Score, playerId));
        }

        // 添加新数据
        _playerData[playerId] = (nickname, score);
        _sortedRank.Add((score, playerId));

        _logger.LogDebug("Rank {RankType} updated: Player {PlayerId} ({Nickname}) score={Score}",
            this.GetPrimaryKeyString(), playerId, nickname, score);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取排行榜（分页）
    /// </summary>
    public Task<S2C_RankList> GetRankList(int startIndex = 0, int count = 10)
    {
        var rankType = this.GetPrimaryKeyString();

        var entries = _sortedRank
            .Skip(startIndex)
            .Take(count)
            .Select((item, index) =>
            {
                var data = _playerData[item.PlayerId];
                return new RankEntry
                {
                    Rank = startIndex + index + 1,
                    PlayerId = item.PlayerId,
                    Nickname = data.Nickname,
                    Score = item.Score
                };
            })
            .ToList();

        var result = new S2C_RankList
        {
            RankType = rankType,
            Entries = entries
        };

        return Task.FromResult(result);
    }

    /// <summary>
    /// 获取玩家排名
    /// </summary>
    public Task<int> GetPlayerRank(long playerId)
    {
        if (!_playerData.TryGetValue(playerId, out var data))
        {
            return Task.FromResult(-1);
        }

        int rank = 0;
        foreach (var item in _sortedRank)
        {
            rank++;
            if (item.PlayerId == playerId)
            {
                return Task.FromResult(rank);
            }
        }

        return Task.FromResult(-1);
    }
}
