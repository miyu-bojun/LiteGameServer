using MemoryPack;

namespace GameShared.Messages;

/// <summary>
/// 道具信息
/// </summary>
[MemoryPackable]
public partial class ItemInfo
{
    public int ItemId { get; set; }
    public int Count { get; set; }
}
