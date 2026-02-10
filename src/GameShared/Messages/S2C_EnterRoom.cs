using MemoryPack;

namespace GameShared.Messages;

/// <summary>
/// 服务端进入房间响应
/// </summary>
[MemoryPackable]
public partial class S2C_EnterRoom
{
    public int ErrorCode { get; set; }
    public long RoomId { get; set; }
}
