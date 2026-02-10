using MemoryPack;

namespace GameShared.Messages;

/// <summary>
/// 服务端玩家操作响应（广播）
/// </summary>
[MemoryPackable]
public partial class S2C_PlayerAction
{
    public long PlayerId { get; set; }
    public int ActionType { get; set; }
    public int ActionData { get; set; }
}
