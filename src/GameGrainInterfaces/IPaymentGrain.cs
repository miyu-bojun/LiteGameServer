namespace GameGrainInterfaces;

using GameShared.Messages;
using Orleans;

/// <summary>
/// 支付 Grain 接口
/// 使用字符串 Key（PlayerId 字符串）作为 Grain 标识
/// </summary>
public interface IPaymentGrain : IGrainWithStringKey
{
    /// <summary>
    /// 创建支付订单
    /// </summary>
    /// <param name="playerId">玩家ID</param>
    /// <param name="productId">商品ID</param>
    /// <returns>订单结果</returns>
    Task<S2C_OrderResult> CreateOrder(long playerId, string productId);

    /// <summary>
    /// 确认支付完成（第三方回调）
    /// </summary>
    /// <param name="orderId">订单号</param>
    /// <param name="success">是否支付成功</param>
    Task<bool> ConfirmPayment(string orderId, bool success);

    /// <summary>
    /// 查询订单状态
    /// </summary>
    /// <param name="orderId">订单号</param>
    /// <returns>订单结果</returns>
    Task<S2C_OrderResult> QueryOrder(string orderId);
}
