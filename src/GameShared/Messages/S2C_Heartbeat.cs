using MemoryPack;

namespace GameShared.Messages;

/// <summary>
/// 服务端心跳响应
/// </summary>
[MemoryPackable]
public partial class S2C_Heartbeat
{
    /// <summary>服务端时间戳（毫秒）</summary>
    public long ServerTimestamp { get; set; }
}
