using MemoryPack;

namespace GameShared.Messages;

/// <summary>
/// 服务端玩家信息响应
/// </summary>
[MemoryPackable]
public partial class S2C_PlayerInfo
{
    public long PlayerId { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public int Level { get; set; }
    public long Exp { get; set; }
}
