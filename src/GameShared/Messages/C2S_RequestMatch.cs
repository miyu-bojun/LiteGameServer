using MemoryPack;

namespace GameShared.Messages;

/// <summary>
/// 客户端请求匹配
/// </summary>
[MemoryPackable]
public partial class C2S_RequestMatch
{
    public int Rating { get; set; }
}
