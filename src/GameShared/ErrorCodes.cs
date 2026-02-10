namespace GameShared;

/// <summary>
/// 错误码常量
/// </summary>
public static class ErrorCodes
{
    /// <summary>成功</summary>
    public const int Success = 0;

    /// <summary>通用错误</summary>
    public const int CommonError = 1000;

    /// <summary>参数错误</summary>
    public const int InvalidParam = 1001;

    // 登录相关错误 (1001-1999)
    /// <summary>账号不存在</summary>
    public const int AccountNotFound = 1002;

    /// <summary>密码错误</summary>
    public const int PasswordError = 1003;

    /// <summary>账号已登录</summary>
    public const int AccountAlreadyOnline = 1004;

    // 玩家相关错误 (2001-2999)
    /// <summary>玩家不存在</summary>
    public const int PlayerNotFound = 2001;

    /// <summary>玩家未登录</summary>
    public const int PlayerNotLoggedIn = 2002;

    // 房间相关错误 (3001-3999)
    /// <summary>房间不存在</summary>
    public const int RoomNotFound = 3001;

    /// <summary>房间已满</summary>
    public const int RoomFull = 3002;

    /// <summary>玩家已在房间中</summary>
    public const int PlayerAlreadyInRoom = 3003;

    /// <summary>玩家不在房间中</summary>
    public const int PlayerNotInRoom = 3004;

    // 匹配相关错误 (4001-4999)
    /// <summary>匹配失败</summary>
    public const int MatchFailed = 4001;

    /// <summary>已在匹配队列中</summary>
    public const int AlreadyInMatchQueue = 4002;

    /// <summary>不在匹配队列中</summary>
    public const int NotInMatchQueue = 4003;
}
