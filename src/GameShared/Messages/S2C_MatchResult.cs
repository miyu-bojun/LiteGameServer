using MemoryPack;

namespace GameShared.Messages;

/// <summary>
/// 服务端匹配结果响应
/// </summary>
[MemoryPackable]
public partial class S2C_MatchResult
{
    public int ErrorCode { get; set; }
    public long RoomId { get; set; }
}
