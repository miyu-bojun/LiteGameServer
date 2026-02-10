using MemoryPack;

namespace GameShared.Messages;

/// <summary>
/// 服务端登录响应
/// </summary>
[MemoryPackable]
public partial class S2C_Login
{
    public int ErrorCode { get; set; }
    public long PlayerId { get; set; }
    public string Nickname { get; set; } = string.Empty;
}
