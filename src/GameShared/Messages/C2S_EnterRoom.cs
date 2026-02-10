using MemoryPack;

namespace GameShared.Messages;

/// <summary>
/// 客户端进入房间请求
/// </summary>
[MemoryPackable]
public partial class C2S_EnterRoom
{
    public long RoomId { get; set; }
}
