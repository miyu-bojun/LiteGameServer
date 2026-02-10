namespace GameGrainInterfaces;

using GameShared.Messages;
using Orleans;

/// <summary>
/// 登录服务 Grain 接口
/// 使用字符串 Key（账号名）作为 Grain 标识
/// </summary>
public interface ILoginGrain : IGrainWithStringKey
{
    /// <summary>
    /// 处理登录请求
    /// </summary>
    /// <param name="request">登录请求消息</param>
    /// <returns>登录响应消息</returns>
    Task<S2C_Login> Login(C2S_Login request);
}
