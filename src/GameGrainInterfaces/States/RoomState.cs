namespace GameGrainInterfaces.States;

using Orleans;

/// <summary>
/// 房间状态类
/// 用于持久化存储房间数据
/// </summary>
[GenerateSerializer]
public class RoomState
{
    /// <summary>
    /// 房间 ID
    /// </summary>
    [Id(0)]
    public long RoomId { get; set; }

    /// <summary>
    /// 房间名称
    /// </summary>
    [Id(1)]
    public string RoomName { get; set; } = string.Empty;

    /// <summary>
    /// 房间内玩家 ID 列表
    /// </summary>
    [Id(2)]
    public List<long> Players { get; set; } = new();

    /// <summary>
    /// 房间最大容量
    /// </summary>
    [Id(3)]
    public int MaxPlayers { get; set; } = 10;

    /// <summary>
    /// 房间创建时间
    /// </summary>
    [Id(4)]
    public DateTime CreateTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 游戏状态：0=等待中, 1=游戏中, 2=已结束
    /// </summary>
    [Id(5)]
    public int GameState { get; set; } = 0;

    /// <summary>
    /// 房间类型：0=普通, 1=排位, 2=练习
    /// </summary>
    [Id(6)]
    public int RoomType { get; set; } = 0;
}
