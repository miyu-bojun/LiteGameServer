using MemoryPack;

namespace GameShared.Messages;

/// <summary>
/// 客户端登录请求
/// </summary>
[MemoryPackable]
public partial class C2S_Login
{
    public string Account { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public int Platform { get; set; }
}
