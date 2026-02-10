using GameGrainInterfaces;
using GameShared;
using GameShared.Messages;
using Microsoft.Extensions.Logging;
using Orleans;

namespace GameGrains;

/// <summary>
/// 支付 Grain 实现（基础骨架）
/// Key = "payment"（单例或按玩家分片）
/// 实际生产环境需要对接第三方支付平台
/// </summary>
public class PaymentGrain : Grain, IPaymentGrain
{
    private readonly ILogger<PaymentGrain> _logger;

    /// <summary>内存中的订单存储（生产环境应使用数据库）</summary>
    private readonly Dictionary<string, OrderInfo> _orders = new();

    public PaymentGrain(ILogger<PaymentGrain> logger)
    {
        _logger = logger;
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("PaymentGrain activated: {Key}", this.GetPrimaryKeyString());
        return base.OnActivateAsync(cancellationToken);
    }

    /// <summary>
    /// 创建支付订单
    /// </summary>
    public Task<S2C_OrderResult> CreateOrder(long playerId, string productId)
    {
        var orderId = $"ORD-{playerId}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

        var order = new OrderInfo
        {
            OrderId = orderId,
            PlayerId = playerId,
            ProductId = productId,
            Status = 0, // pending
            CreatedAt = DateTime.UtcNow
        };

        _orders[orderId] = order;

        _logger.LogInformation("Order created: {OrderId}, Player: {PlayerId}, Product: {ProductId}",
            orderId, playerId, productId);

        return Task.FromResult(new S2C_OrderResult
        {
            ErrorCode = ErrorCodes.Success,
            OrderId = orderId,
            ProductId = productId,
            Status = 0
        });
    }

    /// <summary>
    /// 确认支付完成（第三方回调）
    /// </summary>
    public Task<bool> ConfirmPayment(string orderId, bool success)
    {
        if (!_orders.TryGetValue(orderId, out var order))
        {
            _logger.LogWarning("Order not found: {OrderId}", orderId);
            return Task.FromResult(false);
        }

        order.Status = success ? 1 : 2;
        order.CompletedAt = DateTime.UtcNow;

        _logger.LogInformation("Payment confirmed: {OrderId}, Success: {Success}", orderId, success);

        // TODO: 支付成功后发放道具
        // if (success) { 调用 PlayerGrain.AddItem(...) }

        return Task.FromResult(true);
    }

    /// <summary>
    /// 查询订单状态
    /// </summary>
    public Task<S2C_OrderResult> QueryOrder(string orderId)
    {
        if (!_orders.TryGetValue(orderId, out var order))
        {
            return Task.FromResult(new S2C_OrderResult
            {
                ErrorCode = ErrorCodes.CommonError,
                OrderId = orderId,
                ProductId = string.Empty,
                Status = -1
            });
        }

        return Task.FromResult(new S2C_OrderResult
        {
            ErrorCode = ErrorCodes.Success,
            OrderId = order.OrderId,
            ProductId = order.ProductId,
            Status = order.Status
        });
    }

    /// <summary>内部订单信息</summary>
    private class OrderInfo
    {
        public string OrderId { get; set; } = string.Empty;
        public long PlayerId { get; set; }
        public string ProductId { get; set; } = string.Empty;
        public int Status { get; set; } // 0=pending, 1=success, 2=failed
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }
}
