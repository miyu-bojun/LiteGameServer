# 📖 GameServer 开发教程

本教程将帮助你理解项目架构、核心概念，并学会如何扩展开发新功能。

---

## 目录

1. [架构概览](#1-架构概览)
2. [核心概念](#2-核心概念)
3. [消息系统详解](#3-消息系统详解)
4. [Gateway 网关详解](#4-gateway-网关详解)
5. [Grain 开发指南](#5-grain-开发指南)
6. [数据持久化](#6-数据持久化)
7. [实战：添加新功能](#7-实战添加新功能)
8. [测试指南](#8-测试指南)
9. [部署运维](#9-部署运维)
10. [常见问题](#10-常见问题)

---

## 1. 架构概览

### 1.1 三层架构

```
客户端 ──TCP──→ Gateway（网络层）──Orleans RPC──→ Silo（逻辑层）──ADO.NET──→ PostgreSQL（数据层）
              编解码 + 路由                       Grain 业务逻辑              持久化存储
```

| 层级 | 项目 | 职责 |
|------|------|------|
| **网络层** | `GameGateway` | TCP 监听、二进制编解码、消息路由、Observer 推送、心跳检测 |
| **逻辑层** | `GameSilo` + `GameGrains` | 游戏业务逻辑、状态管理、Grain 生命周期 |
| **数据层** | PostgreSQL | Grain State 持久化、业务数据存储 |

### 1.2 数据流

```
              ┌──────── 请求路径 ────────┐
Client → TCP → Gateway → Decode → Dispatch → Grain.Method() → State → PG
              │                              │
              └──────── 响应路径 ────────────┘
              Gateway ← Encode ← 返回值 ←───┘

              ┌──────── 推送路径 ────────┐
Client ← TCP ← Gateway ← Observer ← Grain 主动推送
              PlayerObserverProxy      PushMessage(id, payload)
```

### 1.3 项目依赖关系

```
GameShared  ←── GameGrainInterfaces  ←── GameGrains  ←── GameSilo
     ↑                   ↑
     └───────────────────┴──── GameGateway
```

- **GameShared**：消息定义、编解码器、错误码（所有项目共用）
- **GameGrainInterfaces**：Grain 接口 + State 定义（Gateway 和 Grains 都需要引用）
- **GameGrains**：Grain 实现（只有 Silo 引用）
- **GameSilo**：Silo 宿主进程
- **GameGateway**：Gateway 独立进程（通过 `IClusterClient` 连接 Silo）

---

## 2. 核心概念

### 2.1 Orleans 虚拟 Actor（Grain）

每个 Grain 是一个**虚拟 Actor**：
- **单线程执行**：一个 Grain 同时只处理一个请求，无需加锁
- **自动激活/停用**：首次调用时自动激活，空闲后自动停用
- **位置透明**：调用者不需要知道 Grain 在哪个 Silo 上

```csharp
// 获取 Grain 引用（不需要知道 Grain 在哪里）
var playerGrain = _grainFactory.GetGrain<IPlayerGrain>(playerId);

// 调用 Grain 方法（Orleans 自动路由到正确的 Silo）
var info = await playerGrain.GetPlayerInfo();
```

### 2.2 Grain Key 类型

| Grain | Key 类型 | Key 含义 | 接口基类 |
|-------|----------|----------|----------|
| `LoginGrain` | `string` | 账号名 | `IGrainWithStringKey` |
| `PlayerGrain` | `long` | 玩家ID | `IGrainWithIntegerKey` |
| `RoomGrain` | `long` | 房间ID | `IGrainWithIntegerKey` |
| `MatchGrain` | `string` | 队列名 | `IGrainWithStringKey` |
| `ChatGrain` | `string` | 频道ID | `IGrainWithStringKey` |
| `RankGrain` | `string` | 排行榜类型 | `IGrainWithStringKey` |
| `PaymentGrain` | `string` | 支付标识 | `IGrainWithStringKey` |

### 2.3 Grain State（持久化状态）

```csharp
// 定义 State 类（标注 [GenerateSerializer] 和 [Id(n)]）
[GenerateSerializer]
public class PlayerState
{
    [Id(0)] public long PlayerId { get; set; }
    [Id(1)] public string Nickname { get; set; } = string.Empty;
    [Id(2)] public int Level { get; set; } = 1;
    // ...
}

// 在 Grain 中使用
public class PlayerGrain : Grain, IPlayerGrain
{
    private readonly IPersistentState<PlayerState> _state;

    public PlayerGrain(
        [PersistentState("PlayerState", "PostgreSQL")] IPersistentState<PlayerState> state)
    {
        _state = state;
    }

    // 读取状态
    public Task<string> GetNickname() => Task.FromResult(_state.State.Nickname);

    // 修改并持久化
    public async Task SetNickname(string nickname)
    {
        _state.State.Nickname = nickname;
        await _state.WriteStateAsync();  // 写入 PostgreSQL
    }
}
```

### 2.4 Observer 推送模式

Grain 主动向客户端推送消息的通道：

```
                                Observer 推送链路
Grain ──→ IPlayerGrain.PushMessage(msgId, payload)
              ↓
         PlayerGrain._observer.OnMessagePush(msgId, payload)
              ↓
         PlayerObserverProxy（Gateway 侧实现）
              ↓
         Socket.Send(packet)  →  Client
```

---

## 3. 消息系统详解

### 3.1 定义新消息

**步骤 1**：在 `src/GameShared/Messages/` 中创建消息类

```csharp
// src/GameShared/Messages/C2S_BuyItem.cs
using MemoryPack;

namespace GameShared.Messages;

[MemoryPackable]
public partial class C2S_BuyItem
{
    public int ItemId { get; set; }
    public int Count { get; set; }
}
```

```csharp
// src/GameShared/Messages/S2C_BuyItemResult.cs
using MemoryPack;

namespace GameShared.Messages;

[MemoryPackable]
public partial class S2C_BuyItemResult
{
    public int ErrorCode { get; set; }
    public int ItemId { get; set; }
    public int NewCount { get; set; }
}
```

**关键规则**：
- 类必须标注 `[MemoryPackable]` 和 `partial`
- 客户端→服务端：`C2S_` 前缀
- 服务端→客户端：`S2C_` 前缀

**步骤 2**：在 `MessageRegistry` 中注册

```csharp
// src/GameShared/MessageRegistry.cs
static MessageRegistry()
{
    // ... 现有消息 ...

    // 商城相关 (6501-6599)
    Register<C2S_BuyItem>(6501);
    Register<S2C_BuyItemResult>(6502);
}
```

### 3.2 编解码流程

```csharp
// 编码（发送方）
byte[] packet = PacketCodec.Encode(new S2C_Login { ErrorCode = 0, PlayerId = 12345 });
// packet = [Length(4B)] + [MsgId(4B)] + [MemoryPack payload]

// 解码（接收方）
var (messageId, message) = PacketCodec.Decode(packetSpan);
// messageId = 1002, message = S2C_Login 对象
```

### 3.3 粘包/拆包处理

`PacketFrameDecoder` 自动处理 TCP 粘包/拆包：

```csharp
var decoder = new PacketFrameDecoder();

// 模拟数据到达（可能是半包、粘包）
var messages = decoder.OnDataReceived(receivedBytes);
// 返回 0~N 条完整消息

// 内部维护 64KB 环形缓冲区
// 自动将未解析的尾部数据前移
```

### 3.4 消息 ID 规划

| 范围 | 模块 | 当前已用 |
|------|------|----------|
| 1001–1999 | 登录/账号 | 1001, 1002 |
| 2001–2999 | 房间/战斗 | 2001–2004 |
| 3001–3999 | 玩家信息 | 3001, 3002 |
| 3501–3599 | 帧同步 | 3501 |
| 4001–4999 | 匹配 | 4001, 4002 |
| 4501–4599 | 聊天 | 4501, 4502 |
| 5001–5999 | 排行榜 | 5001, 5002 |
| 6001–6999 | 支付 | 6001, 6002 |
| 9001–9999 | 系统/心跳 | 9001, 9002 |

---

## 4. Gateway 网关详解

### 4.1 GatewayService 生命周期

```
ExecuteAsync()
  ├─ 启动心跳扫描 Timer（每 15 秒）
  ├─ TcpListener.Start()
  └─ 循环 AcceptSocketAsync()
       └─ 为每个连接创建 ClientSession
          └─ HandleSessionAsync() → DispatchToOrleans()
```

### 4.2 消息分发（DispatchToOrleans）

所有客户端消息都在 `GatewayService.DispatchToOrleans()` 中路由：

```csharp
private async Task DispatchToOrleans(ClientSession session, ushort messageId, object message)
{
    switch (message)
    {
        case C2S_Login login:
            await HandleLogin(session, login);
            break;

        case C2S_EnterRoom enterRoom when session.IsAuthenticated:
            await HandleEnterRoom(session, enterRoom);
            break;

        case C2S_Heartbeat heartbeat:  // 心跳不需要认证
            await HandleHeartbeat(session, heartbeat);
            break;

        // 添加新消息处理：
        // case C2S_BuyItem buyItem when session.IsAuthenticated:
        //     await HandleBuyItem(session, buyItem);
        //     break;

        default:
            _logger.LogWarning("Unhandled message: {MessageId}", messageId);
            break;
    }
}
```

**要点**：
- `when session.IsAuthenticated` 确保只有登录后才能发送业务消息
- `C2S_Heartbeat` 不需要认证检查
- `C2S_Login` 登录成功后设置 `session.IsAuthenticated = true`

### 4.3 Observer 注册

登录成功后，Gateway 创建 Observer 并注册到 PlayerGrain：

```csharp
// 1. 创建 Observer 代理
var observer = new PlayerObserverProxy(session, this);

// 2. 创建 Orleans 对象引用
var observerRef = _orleansClient.CreateObjectReference<IPlayerObserver>(observer);

// 3. 订阅到 PlayerGrain
var playerGrain = _orleansClient.GetGrain<IPlayerGrain>(playerId);
await playerGrain.Subscribe(observerRef);
```

### 4.4 添加新的消息处理

在 `GatewayService` 中添加新的 `case` 分支和处理方法：

```csharp
case C2S_BuyItem buyItem when session.IsAuthenticated:
    await HandleBuyItem(session, buyItem);
    break;

// ...

private async Task HandleBuyItem(ClientSession session, C2S_BuyItem request)
{
    try
    {
        var playerGrain = _orleansClient.GetGrain<IPlayerGrain>(session.PlayerId);
        var result = await playerGrain.BuyItem(request);
        await SendToClient(session, result);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error handling BuyItem for player {PlayerId}", session.PlayerId);
    }
}
```

---

## 5. Grain 开发指南

### 5.1 创建新 Grain（完整步骤）

以创建 **GuildGrain（公会）** 为例：

#### 步骤 1：定义 State

```csharp
// src/GameGrainInterfaces/States/GuildState.cs
namespace GameGrainInterfaces.States;

using Orleans;

[GenerateSerializer]
public class GuildState
{
    [Id(0)] public long GuildId { get; set; }
    [Id(1)] public string GuildName { get; set; } = string.Empty;
    [Id(2)] public long LeaderId { get; set; }
    [Id(3)] public List<long> Members { get; set; } = new();
    [Id(4)] public int MaxMembers { get; set; } = 50;
    [Id(5)] public DateTime CreateTime { get; set; } = DateTime.UtcNow;
}
```

#### 步骤 2：定义接口

```csharp
// src/GameGrainInterfaces/IGuildGrain.cs
namespace GameGrainInterfaces;

using Orleans;

public interface IGuildGrain : IGrainWithIntegerKey  // Key = GuildId
{
    Task<bool> CreateGuild(long leaderId, string guildName);
    Task<bool> JoinGuild(long playerId);
    Task LeaveGuild(long playerId);
    Task<GuildState> GetGuildInfo();
}
```

#### 步骤 3：实现 Grain

```csharp
// src/GameGrains/GuildGrain.cs
namespace GameGrains;

using GameGrainInterfaces;
using GameGrainInterfaces.States;
using Microsoft.Extensions.Logging;
using Orleans;

public class GuildGrain : Grain, IGuildGrain
{
    private readonly ILogger<GuildGrain> _logger;
    private readonly IPersistentState<GuildState> _state;

    public GuildGrain(
        ILogger<GuildGrain> logger,
        [PersistentState("GuildState", "PostgreSQL")] IPersistentState<GuildState> state)
    {
        _logger = logger;
        _state = state;
    }

    public async Task<bool> CreateGuild(long leaderId, string guildName)
    {
        if (_state.State.GuildId != 0)
            return false; // 已存在

        _state.State.GuildId = this.GetPrimaryKeyLong();
        _state.State.GuildName = guildName;
        _state.State.LeaderId = leaderId;
        _state.State.Members.Add(leaderId);
        await _state.WriteStateAsync();

        _logger.LogInformation("Guild {GuildName} created by player {LeaderId}", guildName, leaderId);
        return true;
    }

    public async Task<bool> JoinGuild(long playerId)
    {
        if (_state.State.Members.Count >= _state.State.MaxMembers)
            return false;
        if (_state.State.Members.Contains(playerId))
            return false;

        _state.State.Members.Add(playerId);
        await _state.WriteStateAsync();
        return true;
    }

    public async Task LeaveGuild(long playerId)
    {
        _state.State.Members.Remove(playerId);
        await _state.WriteStateAsync();

        if (_state.State.Members.Count == 0)
            DeactivateOnIdle();
    }

    public Task<GuildState> GetGuildInfo() => Task.FromResult(_state.State);
}
```

#### 步骤 4：注册到 Gateway（如需客户端直接调用）

在 `GatewayService.DispatchToOrleans()` 中添加相应的 case。

### 5.2 Grain 间通信

Grain 之间通过 `IGrainFactory` 获取引用并调用：

```csharp
public class PlayerGrain : Grain, IPlayerGrain
{
    private readonly IGrainFactory _grainFactory;

    public async Task<S2C_EnterRoom> EnterRoom(C2S_EnterRoom request)
    {
        // PlayerGrain → RoomGrain
        var roomGrain = _grainFactory.GetGrain<IRoomGrain>(request.RoomId);
        bool joined = await roomGrain.JoinRoom(this.GetPrimaryKeyLong());
        // ...
    }
}
```

### 5.3 Grain Timer

用于定时任务（如匹配、帧同步）：

```csharp
// 注册 Timer（每 66ms 触发，即 15Hz）
#pragma warning disable CS0618
_timer = this.RegisterTimer(
    async (state) => await OnTick(),  // 回调
    null,                              // state
    TimeSpan.FromMilliseconds(66),     // 首次延迟
    TimeSpan.FromMilliseconds(66));    // 间隔
#pragma warning restore CS0618

// 停止 Timer
_timer?.Dispose();
```

### 5.4 消息推送（Grain → Client）

```csharp
// 方式 1：通过 Observer 直接推送（PlayerGrain 内部）
private Task PushToClient<T>(T message) where T : class
{
    if (_observer != null)
    {
        ushort msgId = MessageRegistry.GetId<T>();
        byte[] payload = MemoryPackSerializer.Serialize(message);
        _observer.OnMessagePush(msgId, payload);
    }
    return Task.CompletedTask;
}

// 方式 2：通过 IPlayerGrain.PushMessage（其他 Grain 调用）
var playerGrain = _grainFactory.GetGrain<IPlayerGrain>(playerId);
await playerGrain.PushMessage(messageId, payload);
```

---

## 6. 数据持久化

### 6.1 两种数据访问方式

| 方式 | 用途 | 示例 |
|------|------|------|
| **Grain State** | 单实体 CRUD（由 Orleans 自动管理） | `PlayerState`, `RoomState` |
| **GameDbRepository** | 复杂查询、跨实体操作 | 账号查询、登录日志 |

### 6.2 Grain State 配置

```csharp
// Silo 启动时配置存储提供者
siloBuilder.AddAdoNetGrainStorage("PostgreSQL", options =>
{
    options.ConnectionString = connectionString;
    options.Invariant = "Npgsql";
});

// Grain 中使用
[PersistentState("PlayerState", "PostgreSQL")]  // "PostgreSQL" 对应上面的名称
IPersistentState<PlayerState> state
```

### 6.3 GameDbRepository

用于 Orleans Grain State 无法覆盖的场景：

```csharp
// 直接 SQL 查询
public async Task<long?> GetPlayerIdByAccount(string account)
{
    var conn = await GetConnectionAsync();
    var cmd = new NpgsqlCommand(
        "SELECT player_id FROM player_accounts WHERE account = @account", conn);
    cmd.Parameters.AddWithValue("account", account);
    var result = await cmd.ExecuteScalarAsync();
    return result as long?;
}
```

### 6.4 数据库表结构

**Orleans 自动管理的表**（`sql/orleans_tables.sql`）：
- `OrleansMembershipTable` — 集群成员
- `OrleansRemindersTable` — 定时提醒
- `OrleansStorage` — Grain State 持久化

**业务表**（`sql/game_tables.sql`）：
- `player_accounts` — 玩家账号（account PK, player_id UNIQUE）
- `player_login_log` — 登录日志
- `payment_orders` — 支付订单

---

## 7. 实战：添加新功能

### 示例：实现「邮件系统」

**需求**：玩家可以收到系统/其他玩家发送的邮件，包含标题、内容和附件。

#### 7.1 定义消息

```csharp
// C2S_GetMailList.cs — 请求邮件列表
[MemoryPackable]
public partial class C2S_GetMailList { }

// S2C_MailList.cs — 邮件列表
[MemoryPackable]
public partial class S2C_MailList
{
    public List<MailInfo> Mails { get; set; } = new();
}

[MemoryPackable]
public partial class MailInfo
{
    public long MailId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public long SenderId { get; set; }
    public long Timestamp { get; set; }
    public bool IsRead { get; set; }
}
```

#### 7.2 注册消息 ID

```csharp
// MessageRegistry.cs
Register<C2S_GetMailList>(7001);
Register<S2C_MailList>(7002);
```

#### 7.3 定义 Grain 接口

```csharp
// IMailGrain.cs (Key = PlayerId)
public interface IMailGrain : IGrainWithIntegerKey
{
    Task SendMail(long senderId, string title, string content);
    Task<S2C_MailList> GetMailList();
    Task ReadMail(long mailId);
}
```

#### 7.4 实现 Grain + State

```csharp
[GenerateSerializer]
public class MailState
{
    [Id(0)] public List<MailInfo> Mails { get; set; } = new();
}

public class MailGrain : Grain, IMailGrain
{
    private readonly IPersistentState<MailState> _state;

    public MailGrain(
        [PersistentState("MailState", "PostgreSQL")] IPersistentState<MailState> state)
    {
        _state = state;
    }

    public async Task SendMail(long senderId, string title, string content)
    {
        _state.State.Mails.Add(new MailInfo
        {
            MailId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Title = title,
            Content = content,
            SenderId = senderId,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            IsRead = false
        });
        await _state.WriteStateAsync();

        // 推送新邮件通知
        var playerGrain = GrainFactory.GetGrain<IPlayerGrain>(this.GetPrimaryKeyLong());
        // ... push notification
    }

    public Task<S2C_MailList> GetMailList()
    {
        return Task.FromResult(new S2C_MailList { Mails = _state.State.Mails });
    }

    public async Task ReadMail(long mailId)
    {
        var mail = _state.State.Mails.FirstOrDefault(m => m.MailId == mailId);
        if (mail != null)
        {
            mail.IsRead = true;
            await _state.WriteStateAsync();
        }
    }
}
```

#### 7.5 在 Gateway 添加路由

```csharp
// GatewayService.DispatchToOrleans()
case C2S_GetMailList when session.IsAuthenticated:
    var mailGrain = _orleansClient.GetGrain<IMailGrain>(session.PlayerId);
    var mailList = await mailGrain.GetMailList();
    await SendToClient(session, mailList);
    break;
```

#### 7.6 编译验证

```bash
dotnet build GameServer.sln
```

---

## 8. 测试指南

### 8.1 运行测试

```bash
# 运行所有测试
dotnet test GameServer.sln

# 运行指定项目
dotnet test tests/GameShared.Tests/

# 详细输出
dotnet test --verbosity normal
```

### 8.2 测试覆盖

| 测试项目 | 用例数 | 覆盖内容 |
|----------|--------|----------|
| `GameShared.Tests` | 5 | 编解码、粘包/拆包、未知消息异常 |
| `GameGrains.Tests` | 1 | Grain 基础验证 |
| `GameGateway.Tests` | 1 | Gateway 基础验证 |

### 8.3 协议层测试示例

```csharp
[Fact]
public void EncodeAndDecode_ShouldReturnOriginalMessage()
{
    var original = new C2S_Login { Account = "test", Token = "token123", Platform = 1 };
    byte[] packet = PacketCodec.Encode(original);
    var (messageId, decoded) = PacketCodec.Decode(packet);

    Assert.Equal(1001, messageId);
    var login = Assert.IsType<C2S_Login>(decoded);
    Assert.Equal("test", login.Account);
}

[Fact]
public void PacketFrameDecoder_StickyPackets_ShouldDecodeAll()
{
    var decoder = new PacketFrameDecoder();
    byte[] pkt1 = PacketCodec.Encode(new C2S_Login { Account = "a" });
    byte[] pkt2 = PacketCodec.Encode(new C2S_Login { Account = "b" });
    byte[] combined = pkt1.Concat(pkt2).ToArray();  // 两个包粘在一起

    var results = decoder.OnDataReceived(combined);
    Assert.Equal(2, results.Count);  // 正确解出两条消息
}
```

---

## 9. 部署运维

### 9.1 Docker 部署

```bash
# 构建并启动
docker-compose up --build -d

# 查看状态
docker-compose ps

# 扩展 Gateway（多实例）
docker-compose up --scale gateway=3 -d

# 查看日志
docker-compose logs -f silo
docker-compose logs -f gateway

# 停止
docker-compose down
```

### 9.2 配置说明

**GameSilo/appsettings.json**：

```json
{
  "ConnectionStrings": {
    "PostgreSQL": "Host=localhost;Port=5432;Database=gamedb;Username=postgres;Password=xxx"
  },
  "Silo": {
    "ServiceId": "GameServer",       // 服务标识（所有节点必须相同）
    "ClusterId": "GameServerCluster", // 集群标识（所有节点必须相同）
    "SiloPort": 11111,                // Silo 间通讯端口
    "GatewayPort": 30000              // Orleans 内部 Gateway 端口（供 Client 连接）
  }
}
```

**GameGateway/appsettings.json**：

```json
{
  "ConnectionStrings": {
    "PostgreSQL": "Host=localhost;Port=5432;Database=gamedb;Username=postgres;Password=xxx"
  },
  "Orleans": {
    "ClusterId": "GameServerCluster",  // 必须与 Silo 一致
    "ServiceId": "GameServer"          // 必须与 Silo 一致
  },
  "Gateway": {
    "Port": 9001,                      // TCP 监听端口
    "Id": 1,                           // Gateway 编号（多实例时不同）
    "HeartbeatTimeoutSeconds": 60      // 心跳超时时间
  }
}
```

### 9.3 生产环境架构

```
┌──────────────────────────────────────────────────────┐
│                    生产环境部署                         │
│                                                      │
│  L4 LB (Nginx Stream / 云 SLB)                      │
│    ├── Gateway-1  (4C8G)  ← TCP :9001               │
│    ├── Gateway-2  (4C8G)  ← TCP :9001               │
│    └── Gateway-3  (4C8G)  ← TCP :9001               │
│                                                      │
│  Orleans Silo 集群                                    │
│    ├── Silo-1  (8C16G)  ← :11111 :30000             │
│    ├── Silo-2  (8C16G)  ← :11111 :30000             │
│    └── Silo-3  (8C16G)  ← :11111 :30000             │
│                                                      │
│  PostgreSQL (Primary + Replica)                      │
│    ├── Primary  (写)                                  │
│    └── Replica  (读)                                  │
└──────────────────────────────────────────────────────┘
```

---

## 10. 常见问题

### Q1: 客户端如何连接？

使用任意 TCP 客户端连接 Gateway 的 9001 端口，按照二进制协议格式发送/接收数据。Unity 客户端示例：

```csharp
var client = new TcpClient();
await client.ConnectAsync("127.0.0.1", 9001);
var stream = client.GetStream();

// 发送登录包
byte[] loginPacket = PacketCodec.Encode(new C2S_Login
{
    Account = "player1",
    Token = "any_token",
    Platform = 0
});
await stream.WriteAsync(loginPacket);
```

### Q2: 如何添加新的 Grain？

1. 在 `GameGrainInterfaces` 中定义接口和 State
2. 在 `GameGrains` 中实现 Grain
3. 在 `GameShared/Messages` 中定义相关消息
4. 在 `MessageRegistry` 中注册消息 ID
5. 在 `GatewayService` 中添加消息路由
6. 编译验证：`dotnet build GameServer.sln`

### Q3: Grain State 存在哪里？

存储在 PostgreSQL 的 `OrleansStorage` 表中（Orleans 自动管理），Key 由 Grain 类型 + Grain Key 组成。

### Q4: 如何水平扩展？

- **Gateway**：多实例启动，前面加 L4 负载均衡器
- **Silo**：多实例启动，Orleans 通过 PostgreSQL 自动发现彼此
- **数据库**：主从分离 + 连接池

### Q5: 帧同步如何工作？

1. 客户端发送操作（`C2S_PlayerAction`）
2. RoomGrain 收集所有输入到 `_pendingInputs`
3. Timer 定频触发（如 15Hz），将收集的输入打包为 `S2C_FrameData`
4. 广播给房间内所有玩家
5. 客户端按帧 ID 回放操作

### Q6: 错误码在哪里定义？

`src/GameShared/ErrorCodes.cs`，按功能模块分段：

| 范围 | 模块 |
|------|------|
| 0 | 成功 |
| 1000–1999 | 通用 / 登录 |
| 2001–2999 | 玩家 |
| 3001–3999 | 房间 |
| 4001–4999 | 匹配 |

---

## 附录：完整 Grain 列表

| Grain | Key | 功能 | 持久化 | Timer |
|-------|-----|------|--------|-------|
| `LoginGrain` | Account | 登录验证、账号创建 | ❌ | ❌ |
| `PlayerGrain` | PlayerId | 玩家状态、背包、推送 | ✅ PlayerState | ❌ |
| `RoomGrain` | RoomId | 房间管理、帧同步 | ✅ RoomState | ✅ 帧同步 |
| `MatchGrain` | QueueName | 匹配队列、自动配对 | ❌ | ✅ 每3秒匹配 |
| `ChatGrain` | ChannelId | 聊天频道、消息广播 | ❌ | ❌ |
| `RankGrain` | RankType | 排行榜（SortedSet） | ❌ | ❌ |
| `PaymentGrain` | PaymentKey | 支付订单管理 | ❌（需改为✅） | ❌ |
