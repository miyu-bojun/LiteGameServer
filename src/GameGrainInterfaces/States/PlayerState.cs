namespace GameGrainInterfaces.States;

using GameShared.Messages;
using Orleans;

/// <summary>
/// 玩家状态类
/// 用于持久化存储玩家数据
/// </summary>
[GenerateSerializer]
public class PlayerState
{
    /// <summary>
    /// 玩家 ID
    /// </summary>
    [Id(0)]
    public long PlayerId { get; set; }

    /// <summary>
    /// 玩家昵称
    /// </summary>
    [Id(1)]
    public string Nickname { get; set; } = string.Empty;

    /// <summary>
    /// 玩家等级
    /// </summary>
    [Id(2)]
    public int Level { get; set; } = 1;

    /// <summary>
    /// 玩家经验值
    /// </summary>
    [Id(3)]
    public long Exp { get; set; } = 0;

    /// <summary>
    /// 玩家道具列表
    /// </summary>
    [Id(4)]
    public List<ItemInfo> Items { get; set; } = new();

    /// <summary>
    /// 账号创建时间
    /// </summary>
    [Id(5)]
    public DateTime CreateTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 最后登录时间
    /// </summary>
    [Id(6)]
    public DateTime LastLoginTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 当前所在房间 ID（0 表示不在房间）
    /// </summary>
    [Id(7)]
    public long CurrentRoomId { get; set; } = 0;

    /// <summary>
    /// 玩家评分/段位（用于匹配）
    /// </summary>
    [Id(8)]
    public int Rating { get; set; } = 1000;
}
