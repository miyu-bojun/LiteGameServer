using MemoryPack;

namespace GameShared.Messages;

/// <summary>
/// 客户端请求排行榜数据
/// </summary>
[MemoryPackable]
public partial class C2S_GetRank
{
    /// <summary>排行榜类型（"level", "score"）</summary>
    public string RankType { get; set; } = string.Empty;

    /// <summary>请求起始排名（0-based）</summary>
    public int StartIndex { get; set; }

    /// <summary>请求数量</summary>
    public int Count { get; set; } = 10;
}
