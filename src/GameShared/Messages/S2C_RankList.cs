using MemoryPack;

namespace GameShared.Messages;

/// <summary>
/// 服务端返回排行榜数据
/// </summary>
[MemoryPackable]
public partial class S2C_RankList
{
    /// <summary>排行榜类型</summary>
    public string RankType { get; set; } = string.Empty;

    /// <summary>排行榜条目列表</summary>
    public List<RankEntry> Entries { get; set; } = new();
}

/// <summary>
/// 排行榜条目
/// </summary>
[MemoryPackable]
public partial class RankEntry
{
    /// <summary>排名（1-based）</summary>
    public int Rank { get; set; }

    /// <summary>玩家ID</summary>
    public long PlayerId { get; set; }

    /// <summary>玩家昵称</summary>
    public string Nickname { get; set; } = string.Empty;

    /// <summary>分数/数值</summary>
    public long Score { get; set; }
}
