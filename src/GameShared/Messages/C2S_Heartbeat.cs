using MemoryPack;

namespace GameShared.Messages;

/// <summary>
/// 客户端心跳请求
/// </summary>
[MemoryPackable]
public partial class C2S_Heartbeat
{
    /// <summary>客户端时间戳（毫秒）</summary>
    public long ClientTimestamp { get; set; }
}
