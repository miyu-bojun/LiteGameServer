using MemoryPack;

namespace GameShared.Messages;

/// <summary>
/// 服务端支付订单结果
/// </summary>
[MemoryPackable]
public partial class S2C_OrderResult
{
    /// <summary>错误码（0 = 成功）</summary>
    public int ErrorCode { get; set; }

    /// <summary>订单号</summary>
    public string OrderId { get; set; } = string.Empty;

    /// <summary>商品ID</summary>
    public string ProductId { get; set; } = string.Empty;

    /// <summary>订单状态：0=pending, 1=success, 2=failed</summary>
    public int Status { get; set; }
}
