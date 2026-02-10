using GameShared.Messages;

namespace GameShared;

/// <summary>
/// 消息 ID 注册表，维护消息 ID ↔ Type 的双向映射
/// </summary>
public static class MessageRegistry
{
    private static readonly Dictionary<ushort, Type> _idToType = new();
    private static readonly Dictionary<Type, ushort> _typeToId = new();

    static MessageRegistry()
    {
        // 登录相关 (1001-1999)
        Register<C2S_Login>(1001);
        Register<S2C_Login>(1002);

        // 房间相关 (2001-2999)
        Register<C2S_EnterRoom>(2001);
        Register<S2C_EnterRoom>(2002);
        Register<C2S_PlayerAction>(2003);
        Register<S2C_PlayerAction>(2004);

        // 玩家信息 (3001-3999)
        Register<S2C_PlayerInfo>(3001);
        Register<S2C_BagInfo>(3002);

        // 匹配相关 (4001-4999)
        Register<C2S_RequestMatch>(4001);
        Register<S2C_MatchResult>(4002);

        // 聊天相关 (4501-4599)
        Register<C2S_SendChat>(4501);
        Register<S2C_ChatMessage>(4502);

        // 排行榜相关 (5001-5999)
        Register<C2S_GetRank>(5001);
        Register<S2C_RankList>(5002);

        // 帧同步 (3501-3599)
        Register<S2C_FrameData>(3501);

        // 支付相关 (6001-6999)
        Register<C2S_CreateOrder>(6001);
        Register<S2C_OrderResult>(6002);

        // 系统/心跳 (9001-9999)
        Register<C2S_Heartbeat>(9001);
        Register<S2C_Heartbeat>(9002);
    }

    /// <summary>
    /// 注册消息类型与 ID 的映射
    /// </summary>
    public static void Register<T>(ushort id) where T : class
    {
        var type = typeof(T);
        _idToType[id] = type;
        _typeToId[type] = id;
    }

    /// <summary>
    /// 根据消息 ID 获取消息类型
    /// </summary>
    public static Type? GetType(ushort id)
        => _idToType.GetValueOrDefault(id);

    /// <summary>
    /// 根据消息类型获取消息 ID
    /// </summary>
    public static ushort GetId<T>() where T : class
        => _typeToId[typeof(T)];

    /// <summary>
    /// 根据消息类型获取消息 ID
    /// </summary>
    public static ushort GetId(Type type)
        => _typeToId[type];
}
