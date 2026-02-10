using MemoryPack;

namespace GameShared.Messages;

/// <summary>
/// 客户端创建支付订单
/// </summary>
[MemoryPackable]
public partial class C2S_CreateOrder
{
    /// <summary>商品ID</summary>
    public string ProductId { get; set; } = string.Empty;
}
