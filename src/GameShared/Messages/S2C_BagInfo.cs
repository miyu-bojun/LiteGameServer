using MemoryPack;

namespace GameShared.Messages;

/// <summary>
/// 服务端背包信息响应
/// </summary>
[MemoryPackable]
public partial class S2C_BagInfo
{
    public List<ItemInfo> Items { get; set; } = new();
}
