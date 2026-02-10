using MemoryPack;

namespace GameShared.Messages;

/// <summary>
/// 客户端玩家操作请求
/// </summary>
[MemoryPackable]
public partial class C2S_PlayerAction
{
    public int ActionType { get; set; }
    public int ActionData { get; set; }
}
