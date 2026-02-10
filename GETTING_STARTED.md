# 🚀 快速入门指南

## 项目简介

**GameServer** 是一个基于 **C# / .NET 8 / Orleans 8 / PostgreSQL** 的分布式游戏服务端框架。采用 Orleans 虚拟 Actor 模型构建游戏逻辑，自定义 TCP 二进制协议实现 Gateway 网关，天然支持高并发和水平扩展。

## 技术栈一览

| 组件 | 技术 | 版本 | 用途 |
|------|------|------|------|
| 运行时 | .NET | 8.0 | 基础运行环境 |
| Actor 框架 | Microsoft Orleans | 8.2.0 | 游戏逻辑层（虚拟 Actor） |
| 数据库 | PostgreSQL | 16+ | 持久化存储 |
| 序列化 | MemoryPack | 1.21.1 | 高性能二进制序列化 |
| 网络层 | 原生 Socket | — | TCP 二进制通讯 |
| 测试 | xUnit | 2.5.3 | 单元测试 |
| 容器化 | Docker + Compose | — | 一键部署 |

---

## 项目结构

```
GameServer/
├── src/
│   ├── GameShared/                  # 🔗 共享库（Gateway + Silo 都引用）
│   │   ├── Messages/                #    消息定义（C2S_/S2C_ 前缀）
│   │   ├── MessageRegistry.cs       #    消息 ID ↔ Type 映射
│   │   ├── PacketCodec.cs           #    二进制编解码器
│   │   ├── PacketFrameDecoder.cs    #    TCP 粘包/拆包处理
│   │   └── ErrorCodes.cs            #    统一错误码
│   │
│   ├── GameGrainInterfaces/         # 📋 Grain 接口定义
│   │   ├── ILoginGrain.cs           #    登录
│   │   ├── IPlayerGrain.cs          #    玩家
│   │   ├── IRoomGrain.cs            #    房间（含帧同步）
│   │   ├── IMatchGrain.cs           #    匹配
│   │   ├── IChatGrain.cs            #    聊天
│   │   ├── IRankGrain.cs            #    排行榜
│   │   ├── IPaymentGrain.cs         #    支付
│   │   ├── IPlayerObserver.cs       #    Observer 推送接口
│   │   └── States/                  #    Grain State（持久化数据）
│   │       ├── PlayerState.cs
│   │       └── RoomState.cs
│   │
│   ├── GameGrains/                  # ⚙️ Grain 实现（业务逻辑）
│   │   ├── LoginGrain.cs
│   │   ├── PlayerGrain.cs
│   │   ├── RoomGrain.cs             #    含帧同步 Timer
│   │   ├── MatchGrain.cs            #    含自动匹配 Timer
│   │   ├── ChatGrain.cs
│   │   ├── RankGrain.cs
│   │   ├── PaymentGrain.cs
│   │   └── Services/
│   │       └── GameDbRepository.cs  #    直接 PG 数据访问
│   │
│   ├── GameSilo/                    # 🏠 Orleans Silo 宿主
│   │   ├── Program.cs               #    Silo 启动配置
│   │   └── appsettings.json
│   │
│   └── GameGateway/                 # 🌐 TCP 网关服务
│       ├── Program.cs               #    Gateway 启动配置
│       ├── GatewayService.cs        #    TCP 监听 + 消息分发
│       ├── ClientSession.cs         #    客户端会话
│       ├── PlayerObserverProxy.cs   #    Observer 代理（Grain→客户端推送）
│       └── appsettings.json
│
├── sql/
│   ├── orleans_tables.sql           # Orleans 官方 PG 建表脚本
│   └── game_tables.sql              # 业务扩展表
│
├── tests/
│   ├── GameShared.Tests/            # 协议层测试（5 个用例）
│   ├── GameGrains.Tests/            # Grain 测试
│   └── GameGateway.Tests/           # Gateway 测试
│
├── Dockerfile.Silo                  # Silo Docker 镜像
├── Dockerfile.Gateway               # Gateway Docker 镜像
├── docker-compose.yml               # 一键启动编排
├── README.md                        # 架构设计文档
└── TUTORIAL.md                      # 开发教程
```

---

## 快速启动

### 方式一：Docker Compose 一键启动（推荐）

```bash
# 1. 克隆项目
git clone <repo-url>
cd GameServer

# 2. 一键启动（自动创建 PG + 建表 + Silo + Gateway）
docker-compose up --build -d

# 3. 查看日志
docker-compose logs -f silo
docker-compose logs -f gateway
```

启动后的服务端口：

| 服务 | 端口 | 说明 |
|------|------|------|
| PostgreSQL | 5432 | 数据库 |
| Silo | 11111 / 30000 | Orleans 内部端口 |
| Gateway | **9001** | 客户端 TCP 接入端口 |

### 方式二：手动启动（本地开发）

#### 前提条件

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [PostgreSQL 16+](https://www.postgresql.org/download/)

#### 步骤

```bash
# 1. 创建数据库并执行建表脚本
createdb -U postgres gamedb
psql -U postgres -d gamedb -f sql/orleans_tables.sql
psql -U postgres -d gamedb -f sql/game_tables.sql

# 2. 修改连接字符串（两个文件都需要改）
#    src/GameSilo/appsettings.json
#    src/GameGateway/appsettings.json
#    → "PostgreSQL": "Host=localhost;Port=5432;Database=gamedb;Username=postgres;Password=your_password"

# 3. 编译
dotnet build GameServer.sln

# 4. 启动 Silo（终端 1）
cd src/GameSilo && dotnet run

# 5. 启动 Gateway（终端 2）
cd src/GameGateway && dotnet run

# 6. 运行测试
dotnet test GameServer.sln
```

---

## 通讯协议速览

### 包格式

```
┌─────────── Header (6 bytes) ──────────┐  ┌─── Body (N bytes) ───┐
│ Length (4B, Big-Endian) │ MsgId (2B)  │  │  MemoryPack Payload  │
└────────────────────────┴─────────────┘  └──────────────────────┘
```

- **Length**：包体长度（不含 6 字节包头）
- **MsgId**：消息类型 ID，映射到具体的 C# 类
- **Payload**：MemoryPack 序列化的消息对象

### 消息 ID 分段

| 范围 | 用途 | 示例 |
|------|------|------|
| 1001–1999 | 登录/账号 | `C2S_Login`(1001) `S2C_Login`(1002) |
| 2001–2999 | 房间/战斗 | `C2S_EnterRoom`(2001) `S2C_PlayerAction`(2004) |
| 3001–3999 | 玩家信息 | `S2C_PlayerInfo`(3001) `S2C_BagInfo`(3002) |
| 3501–3599 | 帧同步 | `S2C_FrameData`(3501) |
| 4001–4999 | 匹配 | `C2S_RequestMatch`(4001) |
| 4501–4599 | 聊天 | `C2S_SendChat`(4501) `S2C_ChatMessage`(4502) |
| 5001–5999 | 排行榜 | `C2S_GetRank`(5001) `S2C_RankList`(5002) |
| 6001–6999 | 支付 | `C2S_CreateOrder`(6001) |
| 9001–9999 | 系统/心跳 | `C2S_Heartbeat`(9001) `S2C_Heartbeat`(9002) |

### 心跳机制

- 客户端每 **30 秒** 发送 `C2S_Heartbeat`
- 服务端回复 `S2C_Heartbeat`
- 服务端每 **15 秒** 扫描，**60 秒** 无心跳断开连接
- 可通过 `Gateway:HeartbeatTimeoutSeconds` 配置

---

## 核心流程

### 登录流程

```
Client → [TCP] → Gateway → [Decode] → C2S_Login
                   Gateway → ILoginGrain.Login(account)
                              LoginGrain → GameDbRepository.GetPlayerIdByAccount()
                              LoginGrain → IPlayerGrain.GetPlayerInfo()
                   Gateway ← S2C_Login(playerId, nickname)
                   Gateway → 创建 Observer → IPlayerGrain.Subscribe()
Client ← [Encode] ← Gateway
```

### 进入房间

```
Client → C2S_EnterRoom(roomId)
         Gateway → IPlayerGrain.EnterRoom()
                    PlayerGrain → IRoomGrain.JoinRoom(playerId)
         Gateway ← S2C_EnterRoom(errorCode, roomId)
Client ← S2C_EnterRoom
```

### 帧同步战斗

```
IRoomGrain.StartFrameSync(15)  // 15Hz
   ↓ Timer 每 66ms 触发一次
   RoomGrain.OnFrameTick()
   → 收集 pendingInputs
   → 广播 S2C_FrameData 给房间内所有玩家
   → 通过 IPlayerGrain.PushMessage() → Observer → Client
```

---

## 部署架构

```
┌─── L4 负载均衡器 ───┐
│  (Nginx/HAProxy)    │
└──┬─────┬─────┬──────┘
   │     │     │
   ▼     ▼     ▼
 GW-1  GW-2  GW-3     ← TCP :9001（客户端接入）
   │     │     │
   └──┬──┘──┬──┘
      ▼     ▼
  Orleans Silo 集群    ← :11111(Silo) :30000(Orleans Gateway)
      │                   IClusterClient 自动路由到正确 Silo
      ▼
  PostgreSQL           ← :5432 (Grain State + 业务数据)
```

> **关键**：Gateway 层的负载均衡仅分散客户端 TCP 连接。Gateway → Orleans 的路由由 `IClusterClient` 自动处理。

---

## 下一步

- 📖 阅读 [TUTORIAL.md](TUTORIAL.md) 了解如何扩展开发新功能
- 📐 阅读 [README.md](README.md) 了解完整架构设计
- 📋 阅读 [plan/framework_todolist.md](plan/framework_todolist.md) 了解开发进度
