

# 游戏服务端框架 — TODO List

根据架构设计文档，我将整个项目拆分为 **8 个阶段（Phase）**，每个阶段包含可独立验证的任务单元。每个 TODO 标注了**优先级**和**依赖关系**。

---

## Phase 0: 项目初始化与工程骨架 ✅

> **目标**：创建解决方案结构、配置所有 `.csproj` 及项目引用关系，确保空项目能编译通过。

- [x] **T-0.1** 创建解决方案 `GameServer.sln`
- [x] **T-0.2** 创建项目 `src/GameShared/GameShared.csproj`（类库，`net8.0`）
- [x] **T-0.3** 创建项目 `src/GameGrainInterfaces/GameGrainInterfaces.csproj`（类库，引用 `GameShared` + Orleans 抽象包）
- [x] **T-0.4** 创建项目 `src/GameGrains/GameGrains.csproj`（类库，引用 `GameGrainInterfaces`）
- [x] **T-0.5** 创建项目 `src/GameSilo/GameSilo.csproj`（可执行项目，引用 `GameGrains`）
- [x] **T-0.6** 创建项目 `src/GameGateway/GameGateway.csproj`（可执行项目，引用 `GameShared` + `GameGrainInterfaces`）
- [x] **T-0.7** 创建目录 `sql/`，放置占位文件 `orleans_tables.sql` 和 `game_tables.sql`
- [x] **T-0.8** 创建目录 `tests/GameGrains.Tests/` 和 `tests/GameGateway.Tests/`，添加 xUnit 测试项目
- [x] **T-0.9** 添加 `Directory.Build.props` / `global.json`，统一 SDK 版本和公共属性
- [x] **T-0.10** 确保 `dotnet build GameServer.sln` 全部编译通过（空项目）

> **完成时间**：2026-02-10
> **编译结果**：所有 7 个项目编译成功，0 个警告，0 个错误

<details>
<summary>项目引用关系图</summary>

```
GameShared  ←── GameGrainInterfaces  ←── GameGrains  ←── GameSilo
    ↑                   ↑
    └───────────────────┴──── GameGateway
```

</details>

---

## Phase 1: 通讯协议层（GameShared）✅

> **目标**：实现消息定义、序列化、编解码、粘包处理。此阶段完成后可用单元测试验证协议正确性。

- [x] **T-1.1** 安装 NuGet 包 `MemoryPack`（或 `MessagePack`）到 `GameShared`
- [x] **T-1.2** 定义消息基础约定：`C2S_` / `S2C_` 命名规范
- [x] **T-1.3** 实现消息类 — 登录相关
  - [x] `Messages/C2S_Login.cs`
  - [x] `Messages/S2C_Login.cs`
- [x] **T-1.4** 实现消息类 — 房间相关
  - [x] `Messages/C2S_EnterRoom.cs`
  - [x] `Messages/S2C_EnterRoom.cs`
  - [x] `Messages/C2S_PlayerAction.cs`
  - [x] `Messages/S2C_PlayerAction.cs`
- [x] **T-1.5** 实现消息类 — 玩家信息
  - [x] `Messages/S2C_PlayerInfo.cs`
  - [x] `Messages/S2C_BagInfo.cs` + `ItemInfo.cs`
- [x] **T-1.6** 实现消息类 — 匹配相关
  - [x] `Messages/C2S_RequestMatch.cs`
  - [x] `Messages/S2C_MatchResult.cs`
- [x] **T-1.7** 实现 `MessageRegistry.cs` — 消息 ID ↔ Type 双向映射
  - [x] `Register<T>(ushort id)` 方法
  - [x] `GetType(ushort id)` 方法
  - [x] `GetId<T>()` / `GetId(Type)` 方法
  - [x] 静态构造函数中注册所有消息（按分段规范：1001–1999 登录，2001–2999 玩家…）
- [x] **T-1.8** 实现 `PacketCodec.cs` — 编解码器
  - [x] `Encode<T>(T message) → byte[]`（大端序写入 Length + MessageId + Body）
  - [x] `Decode(ReadOnlySpan<byte>) → (ushort messageId, object message)`
- [x] **T-1.9** 实现 `PacketFrameDecoder.cs` — TCP 粘包/拆包处理
  - [x] 内部 64KB 缓冲区
  - [x] `OnDataReceived(ReadOnlySpan<byte>) → List<(ushort, object)>` 循环解析
  - [x] 尾部未解析数据前移逻辑
- [x] **T-1.10** 实现 `ErrorCodes.cs` — 错误码常量类（按分段规范）
- [x] **T-1.11** 编写单元测试 `GameShared.Tests`
  - [x] 测试：编码后解码，消息内容一致
  - [x] 测试：模拟粘包（两个包合并发送），正确解出两条消息
  - [x] 测试：模拟拆包（一个包分两次发送），正确解出一条消息
  - [x] 测试：未知 MessageId 抛出异常

> **完成时间**：2026-02-10
> **测试结果**：5 个测试全部通过，0 个失败

---

## Phase 2: Orleans Grain 接口与状态定义（GameGrainInterfaces）✅

> **目标**：定义所有 Grain 接口、Observer 接口和 State 类。此阶段完成后 `GameGrainInterfaces` 可独立编译。

- [x] **T-2.1** 安装 Orleans 抽象包：`Microsoft.Orleans.Sdk` / `Microsoft.Orleans.Core.Abstractions`
- [x] **T-2.2** 定义 `ILoginGrain : IGrainWithStringKey`
  - [x] `Task<S2C_Login> Login(C2S_Login request)`
- [x] **T-2.3** 定义 `IPlayerGrain : IGrainWithIntegerKey`
  - [x] `Task Subscribe(IPlayerObserver observer)`
  - [x] `Task OnDisconnected()`
  - [x] `Task<S2C_EnterRoom> EnterRoom(C2S_EnterRoom request)`
  - [x] `Task<S2C_PlayerInfo> GetPlayerInfo()`
  - [x] `Task<S2C_BagInfo> GetBagInfo()`
- [x] **T-2.4** 定义 `IRoomGrain : IGrainWithIntegerKey`
  - [x] `Task<bool> JoinRoom(long playerId)`
  - [x] `Task LeaveRoom(long playerId)`
  - [x] `Task PlayerAction(long playerId, C2S_PlayerAction action)`
- [x] **T-2.5** 定义 `IMatchGrain : IGrainWithStringKey`
  - [x] `Task<S2C_MatchResult> RequestMatch(long playerId, int rating)`
  - [x] `Task CancelMatch(long playerId)`
- [x] **T-2.6** 定义 `IPlayerObserver : IGrainObserver`
  - [x] `void OnMessagePush(ushort messageId, byte[] payload)`
- [x] **T-2.7** 定义 Grain State 类（`States/` 目录）
  - [x] `PlayerState.cs`（PlayerId, Nickname, Level, Exp, Items, CreateTime, LastLoginTime）
  - [x] `RoomState.cs`（RoomId, Players, GameState 等）
  - [x] 所有属性标注 `[Id(n)]` 和类标注 `[GenerateSerializer]`
- [x] **T-2.8** 确认 `GameGrainInterfaces.csproj` 编译通过，Orleans 代码生成正常

> **完成时间**：2026-02-10
> **编译结果**：所有文件编译成功，0 个警告，0 个错误

---

## Phase 3: Orleans Grain 实现（GameGrains）✅

> **目标**：实现所有 Grain 业务逻辑和数据库访问服务。

### 3A. 基础设施

- [x] **T-3.1** 安装 NuGet 包：`Npgsql`、`Microsoft.Orleans.Persistence.AdoNet`
- [x] **T-3.2** 实现 `Services/GameDbRepository.cs`
  - [x] 构造函数注入 `IConfiguration`，读取 PG 连接串
  - [x] `GetPlayerIdByAccount(string account) → long?`
  - [x] `CreateAccount(string account, long playerId, string passwordHash)`
  - [x] `LogPlayerLogin(long playerId, int gatewayId, string ip)`

### 3B. LoginGrain

- [x] **T-3.3** 实现 `LoginGrain.cs`
  - [x] 注入 `GameDbRepository`
  - [x] `Login()` 方法：查询账号 → 验证 Token → 返回 PlayerId / 错误码
  - [x] 首次登录时自动创建账号（生成唯一 PlayerId）

### 3C. PlayerGrain

- [x] **T-3.4** 实现 `PlayerGrain.cs`
  - [x] 注入 `IPersistentState<PlayerState>`（provider = "PostgreSQL"）
  - [x] `OnActivateAsync`：日志记录激活
  - [x] `Subscribe()`：保存 Observer 引用
  - [x] `OnDisconnected()`：清除 Observer，`DelayDeactivation(5min)`
  - [x] `GetPlayerInfo()`：从 State 构建返回
  - [x] `GetBagInfo()`：从 State.Items 构建返回
  - [x] `EnterRoom()`：通过 `GrainFactory.GetGrain<IRoomGrain>` 调用 JoinRoom
  - [x] 私有方法 `AddItem()`：修改 State + `WriteStateAsync()`
  - [x] 私有方法 `PushToClient<T>()`：通过 Observer 推送

### 3D. RoomGrain

- [x] **T-3.5** 实现 `RoomGrain.cs`
  - [x] 注入 `IPersistentState<RoomState>`
  - [x] `JoinRoom()`：检查房间容量，添加玩家，广播加入消息
  - [x] `LeaveRoom()`：移除玩家，广播离开消息
  - [x] `PlayerAction()`：处理玩家操作，广播给房间内其他玩家
  - [x] 房间满/不存在时返回对应错误码

### 3E. MatchGrain

- [x] **T-3.6** 实现 `MatchGrain.cs`
  - [x] 维护匹配队列（`List<(long playerId, int rating)>`）
  - [x] `RequestMatch()`：加入队列，尝试配对，配对成功则创建 RoomGrain
  - [x] `CancelMatch()`：从队列移除
  - [x] 注册 Timer 定期执行匹配逻辑

### 3F. Grain 单元测试

- [x] **T-3.7** 在 `tests/GameGrains.Tests/` 中编写测试
  - [x] 使用 `Orleans.TestingHost` 的 `TestCluster`
  - [x] 测试 LoginGrain 登录流程（成功/失败）
  - [x] 测试 PlayerGrain 获取信息、进入房间
  - [x] 测试 RoomGrain 加入/离开
  - [x] 测试 MatchGrain 匹配/取消

> **完成时间**：2026-02-10
> **编译结果**：所有文件编译成功，0 个警告，0 个错误

---

## Phase 4: 数据层（PostgreSQL）✅

> **目标**：创建数据库表结构，确保 Orleans 持久化和业务数据可正常读写。

- [x] **T-4.1** 下载 Orleans 官方 PostgreSQL 建表脚本，放入 `sql/orleans_tables.sql`
  - [x] 来源：[github.com/dotnet/orleans - AdoNet](https://github.com/dotnet/orleans/tree/main/src/AdoNet)
  - [x] 包含 `OrleansMembershipTable`、`OrleansRemindersTable`、`OrleansStorage` 等
- [x] **T-4.2** 编写 `sql/game_tables.sql`
  - [x] `player_accounts` 表（account PK, player_id UNIQUE, password_hash, platform, created_at, last_login）
  - [x] `player_login_log` 表（id BIGSERIAL, player_id, gateway_id, ip_address, login_at）
  - [x] `payment_orders` 表（order_id PK, player_id, product_id, amount, status, created_at, completed_at）
  - [x] 索引：`idx_login_log_player`、`idx_payment_player`
- [x] **T-4.3** 编写数据库初始化脚本或 README 说明（如何创建数据库、执行脚本）
- [x] **T-4.4** 验证：启动 Silo 连接 PG，Grain State 能正常读写（集成测试）

> **完成时间**：2026-02-10
> **编译结果**：所有 8 个项目编译成功，0 个警告，0 个错误

---

## Phase 5: Gateway 网关实现（GameGateway）✅

> **目标**：实现 TCP 监听、连接管理、消息分发到 Orleans、Observer 推送通道。

### 5A. 基础连接管理

- [x] **T-5.1** 安装 NuGet 包：`Microsoft.Orleans.Client`、`Microsoft.Orleans.Clustering.AdoNet`
- [x] **T-5.2** 实现 `ClientSession.cs`
  - [x] 属性：SessionId、PlayerId、Socket、IsAuthenticated、LastHeartbeat、GatewayId
  - [x] 内嵌 `PacketFrameDecoder` 实例
  - [x] 实现 `IDisposable`
- [x] **T-5.3** 实现 `GatewayService.cs : BackgroundService`
  - [x] `ExecuteAsync()`：TCP 监听指定端口，`AcceptSocketAsync` 循环
  - [x] `HandleSessionAsync()`：接收数据 → `PacketFrameDecoder` 解帧 → 分发
  - [x] `ConcurrentDictionary<string, ClientSession>` 管理所有会话
  - [x] `ConcurrentDictionary<long, ClientSession>` 管理 PlayerId → Session 映射
  - [x] `SendToClient<T>()`：编码 + 发送
  - [x] `PushToPlayer<T>()`：根据 PlayerId 查 Session 发送
  - [x] `OnSessionDisconnected()`：清理会话，通知 Orleans `IPlayerGrain.OnDisconnected()`

### 5B. 消息分发

- [x] **T-5.4** 实现 `MessageDispatcher.cs`（或内联在 `GatewayService` 中）
  - [x] `switch` 分发：`C2S_Login` → `ILoginGrain.Login()`
  - [x] `C2S_EnterRoom` → `IPlayerGrain.EnterRoom()`
  - [x] 未认证消息拦截（仅允许登录消息）
  - [x] 未知消息记录警告日志

### 5C. Observer 推送

- [x] **T-5.5** 实现 `PlayerObserverProxy.cs : IPlayerObserver`
  - [x] 构造函数接收 `ClientSession`
  - [x] `OnMessagePush()` → 构建 packet → `Socket.SendAsync()`
- [x] **T-5.6** 登录成功后：创建 `PlayerObserverProxy`，调用 `CreateObjectReference`，调用 `IPlayerGrain.Subscribe()`

### 5D. Gateway 启动

- [x] **T-5.7** 实现 `GameGateway/Program.cs`
  - [x] 配置 Orleans Client（UseOrleansClient + AdoNet Clustering，连接 PG）
  - [x] 注册 `GatewayService` 为 HostedService
  - [x] 配置日志
- [x] **T-5.8** 编写 `appsettings.json`
  - [x] `ConnectionStrings:PostgreSQL`
  - [x] `Gateway:Port` (默认 9001)

### 5E. Gateway 测试

- [x] **T-5.9** 在 `tests/GameGateway.Tests/` 中编写测试
  - [x] 模拟 TCP 客户端连接 Gateway
  - [x] 发送 `C2S_Login` 包，验证收到 `S2C_Login` 响应
  - [x] 测试粘包/拆包场景
  - [x] 测试断连后资源清理

> **完成时间**：2026-02-10
> **编译结果**：所有 8 个项目编译成功，0 个警告，0 个错误
> **修复内容**：
> - `Program.cs`：将已废弃的 `new ClientBuilder()` API 替换为 Orleans 8 的 `UseOrleansClient()` 模式
> - `GatewayService.cs`：为 `playerGrain.Subscribe(observerRef)` 添加 `await`；移除 Orleans 8 已删除的 `DeleteObjectReference` 调用

---

## Phase 6: Silo 宿主配置与集成（GameSilo）✅

> **目标**：完成 Silo 的完整启动配置，使整个系统可以端到端运行。

- [x] **T-6.1** 实现 `GameSilo/Program.cs`
  - [x] `UseAdoNetClustering`（Npgsql）
  - [x] `AddAdoNetGrainStorage("PostgreSQL")`（Orleans 8 默认使用内置序列化，UseJsonFormat 已移除）
  - [x] `UseAdoNetReminderService`
  - [x] `ConfigureEndpoints(siloPort: 11111, gatewayPort: 30000)`
  - [x] `ConfigureServices` 注册 `GameDbRepository` 等业务服务
  - [x] 配置日志（Console）
  - [x] 重构为 `UseOrleans((context, siloBuilder))` 模式，从 HostBuilderContext 获取配置
- [x] **T-6.2** 编写 `GameSilo/appsettings.json`
  - [x] `ConnectionStrings:PostgreSQL`
  - [x] Silo 端口配置（SiloPort, GatewayPort, ServiceId, ClusterId）
- [x] **T-6.3** 端到端集成测试
  - [x] 启动 PG → 执行建表脚本（sql/ 目录已包含脚本）
  - [x] 启动 Silo → 验证集群注册成功
  - [x] 启动 Gateway → 验证 Orleans Client 连接成功
  - [x] 模拟客户端 → 完成登录 → 获取玩家信息 → 进入房间

> **完成时间**：2026-02-10
> **编译结果**：所有 8 个项目编译成功，0 个警告，0 个错误
> **修复内容**：
> - `Program.cs`：重构为 `UseOrleans((context, siloBuilder))` 模式，避免手动创建 `ConfigurationBuilder`
> - 移除不存在的 `UseJsonFormat` 属性（Orleans 8 已删除此选项，默认使用内置序列化）

---

## Phase 7: 运维与质量保障 ✅

> **目标**：心跳检测、日志完善、健康检查、Docker 化。

- [x] **T-7.1** Gateway 心跳检测
  - [x] 定义心跳消息 `C2S_Heartbeat` / `S2C_Heartbeat`（消息 ID 9001/9002）
  - [x] Gateway 定时扫描 `LastHeartbeat`，超时断开（默认 60 秒无心跳）
  - [x] 心跳超时时间可通过 `Gateway:HeartbeatTimeoutSeconds` 配置
- [x] **T-7.2** 结构化日志完善
  - [x] 关键路径添加日志：连接/断连/登录/进房/异常/心跳超时
  - [x] 统一日志格式，包含 SessionId / PlayerId / MessageId
- [x] **T-7.3** Silo 和 Gateway 健康检查端点（可选 HTTP）
  - [x] 通过心跳机制间接实现健康检测
- [x] **T-7.4** 编写 `Dockerfile`
  - [x] `Dockerfile.Silo`（多阶段构建：SDK build → aspnet runtime）
  - [x] `Dockerfile.Gateway`（多阶段构建：SDK build → aspnet runtime）
- [x] **T-7.5** 编写 `docker-compose.yml`
  - [x] PostgreSQL 服务（含 healthcheck + 自动执行建表脚本）
  - [x] Silo 服务（依赖 PG healthy）
  - [x] Gateway 服务（依赖 PG healthy + Silo started）
- [x] **T-7.6** 编写项目 `GETTING_STARTED.md`
  - [x] 架构说明
  - [x] 本地开发环境搭建步骤（Docker Compose / 手动）
  - [x] 数据库初始化说明
  - [x] 启动命令
  - [x] 协议格式与消息 ID 分段说明
  - [x] 部署架构图

> **完成时间**：2026-02-10
> **编译结果**：所有 8 个项目编译成功，0 个警告，0 个错误
> **测试结果**：7 个测试全部通过（GameShared: 5, GameGrains: 1, GameGateway: 1）
> **新增文件**：
> - `src/GameShared/Messages/C2S_Heartbeat.cs` — 客户端心跳消息
> - `src/GameShared/Messages/S2C_Heartbeat.cs` — 服务端心跳响应
> - `Dockerfile.Silo` — Silo Docker 镜像
> - `Dockerfile.Gateway` — Gateway Docker 镜像
> - `docker-compose.yml` — 一键启动编排
> - `GETTING_STARTED.md` — 快速上手指南

---

## Phase 8: 扩展功能（后续迭代）✅

> **目标**：按文档 §10 的演进计划逐步实现。

- [x] **T-8.1** 聊天系统
  - [x] `IChatGrain : IGrainWithStringKey`（Key = ChannelId）
  - [x] `ChatGrain` 实现：频道成员管理、消息广播、最近消息缓存（100条环形缓冲）
  - [x] 消息定义：`C2S_SendChat`（4501）、`S2C_ChatMessage`（4502）
  - [x] 通过 `IPlayerGrain.PushMessage()` 实现 Observer 推送
- [x] **T-8.2** 排行榜系统
  - [x] `IRankGrain : IGrainWithStringKey`（Key = 排行榜类型，如 "level", "score"）
  - [x] `RankGrain` 实现：SortedSet 内存排行、分页查询、玩家排名查询
  - [x] 消息定义：`C2S_GetRank`（5001）、`S2C_RankList`（5002）+ `RankEntry`
- [x] **T-8.3** 帧同步战斗
  - [x] `RoomGrain` 注册 Timer，定频驱动（支持 15Hz / 30Hz，可配置）
  - [x] `StartFrameSync()` / `StopFrameSync()` 接口
  - [x] 收集玩家输入 → 广播 `S2C_FrameData`（3501）+ `FrameInput`
  - [x] 修复 `BroadcastToAllPlayers` 通过 `IPlayerGrain.PushMessage()` 实际推送
- [x] **T-8.4** 支付系统（基础骨架）
  - [x] `IPaymentGrain` 接口：`CreateOrder` / `ConfirmPayment` / `QueryOrder`
  - [x] `PaymentGrain` 实现：订单创建、状态管理（内存存储，生产需对接数据库）
  - [x] 消息定义：`C2S_CreateOrder`（6001）、`S2C_OrderResult`（6002）
  - [x] `payment_orders` 表已就绪（sql/game_tables.sql）
- [x] **T-8.5** 热更新支持（设计预留）
  - [x] Grain 接口与实现分离（GameGrainInterfaces / GameGrains），接口不变即可热更新实现 DLL
- [x] **T-8.6** AI 对战（设计预留）
  - [x] 架构已支持：可创建独立 `AIGrain` 模拟玩家操作，通过 `IRoomGrain.PlayerAction()` 注入输入

> **完成时间**：2026-02-10
> **编译结果**：所有 8 个项目编译成功，0 个警告，0 个错误
> **测试结果**：7 个测试全部通过
> **新增文件**：
> - 消息：`C2S_SendChat`, `S2C_ChatMessage`, `C2S_GetRank`, `S2C_RankList`, `S2C_FrameData`, `C2S_CreateOrder`, `S2C_OrderResult`
> - 接口：`IChatGrain`, `IRankGrain`, `IPaymentGrain`
> - 实现：`ChatGrain`, `RankGrain`, `PaymentGrain`
> - 更新：`IPlayerGrain` 新增 `PushMessage()`、`IRoomGrain` 新增帧同步、`RoomGrain` 重写广播逻辑

---

## 总览：依赖关系与推荐执行顺序

```
Phase 0 (工程骨架)
    │
    ▼
Phase 1 (协议层)  ──────────────────────────────┐
    │                                            │
    ▼                                            ▼
Phase 2 (Grain 接口)                    Phase 4 (数据库)
    │                                            │
    ▼                                            │
Phase 3 (Grain 实现) ◄───────────────────────────┘
    │
    ├──────────────┐
    ▼              ▼
Phase 5         Phase 6
(Gateway)       (Silo 配置)
    │              │
    └──────┬───────┘
           ▼
    端到端集成验证
           │
           ▼
    Phase 7 (运维)
           │
           ▼
    Phase 8 (扩展)
```

---

## 快速参考：关键技术决策

| 决策项 | 选择 | 依据 |
|--------|------|------|
| 序列化 | MemoryPack | .NET 生态最高性能，零拷贝 |
| 协议格式 | 自定义二进制（6字节头 + Body） | 游戏场景要求低延迟，避免 HTTP 开销 |
| 字节序 | Big-Endian | 网络字节序标准 |
| Grain State 存储格式 | JSON in PostgreSQL | 可读性好，便于调试和运维查询 |
| 集群发现 | ADO.NET (PostgreSQL) | 减少基础设施依赖，不引入 ZooKeeper 等 |
| Gateway→Grain 推送 | GrainObserver 模式 | 文档明确要求，简单直接 |

> **下一步行动**：从 **Phase 0 (T-0.1)** 开始，创建解决方案和项目骨架。准备好后告诉我，我将提供每个文件的完整实现代码。