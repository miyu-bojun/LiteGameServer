using MemoryPack;

namespace GameShared.Messages;

/// <summary>
/// 服务端帧同步数据包
/// </summary>
[MemoryPackable]
public partial class S2C_FrameData
{
    /// <summary>帧编号</summary>
    public int FrameId { get; set; }

    /// <summary>帧内所有玩家的输入</summary>
    public List<FrameInput> Inputs { get; set; } = new();
}

/// <summary>
/// 单个玩家的帧输入
/// </summary>
[MemoryPackable]
public partial class FrameInput
{
    /// <summary>玩家ID</summary>
    public long PlayerId { get; set; }

    /// <summary>操作类型</summary>
    public int ActionType { get; set; }

    /// <summary>操作数据</summary>
    public int ActionData { get; set; }
}
