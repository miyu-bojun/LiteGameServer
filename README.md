

# 游戏服务端框架架构设计文档

> **文档版本**：v1.0  
> **适用范围**：项目全体技术人员  
> **技术栈**：C# / .NET 8 / Orleans 8 / PostgreSQL / 自定义TCP网络层

---

## 1. 架构总览

### 1.1 设计目标

- 基于 Orleans 虚拟 Actor 模型构建游戏逻辑，天然支持高并发和分布式
- 网络层与游戏逻辑层完全解耦，网络服务负责协议编解码，Orleans 内部只处理强类型数据对象
- Gateway 层承担负载均衡与消息路由，支持水平扩展
- PostgreSQL 作为持久化存储，Orleans Grain State 直接对接 PG

### 1.2 整体架构图

```
                        ┌─────────────────────────────────────────────┐
                        │              客户端 (Client)                 │
                        │         Unity / Unreal / 自研引擎            │
                        └──────────────┬──────────────────────────────┘
                                       │ TCP/WebSocket
                                       ▼
               ┌───────────────────────────────────────────────┐
               │              Gateway 集群 (网络服务层)          │
               │                                               │
               │  ┌─────────┐  ┌─────────┐  ┌─────────┐      │
               │  │Gateway-1│  │Gateway-2│  │Gateway-N│      │
               │  └────┬────┘  └────┬────┘  └────┬────┘      │
               │       │            │            │             │
               │  ┌────┴────────────┴────────────┴──────┐     │
               │  │  负载均衡 / 连接管理 / Encode+Decode  │     │
               │  └────┬────────────────────────────────┘     │
               └───────┼──────────────────────────────────────┘
                       │ 强类型对象调用 (Orleans Client → Grain)
                       ▼
               ┌───────────────────────────────────────────────┐
               │            Orleans Silo 集群 (游戏逻辑层)       │
               │                                               │
               │  ┌─────────┐  ┌─────────┐  ┌─────────┐      │
               │  │ Silo-1  │  │ Silo-2  │  │ Silo-N  │      │
               │  └────┬────┘  └────┬────┘  └────┬────┘      │
               │       │            │            │             │
               │  PlayerGrain / RoomGrain / MatchGrain / ...   │
               └───────┼──────────────────────────────────────┘
                       │ ADO.NET / Npgsql
                       ▼
               ┌───────────────────────────────────────────────┐
               │             PostgreSQL 集群 (数据层)            │
               │                                               │
               │   玩家数据 / 道具 / 排行榜 / 订单 / 日志         │
               └───────────────────────────────────────────────┘
```

### 1.3 分层职责一览

| 层级 | 组件 | 职责 |
|------|------|------|
| **网络层** | Gateway | 连接管理、协议编解码、负载均衡、消息路由、心跳检测 |
| **逻辑层** | Orleans Silo | 游戏业务逻辑、状态管理、Grain 生命周期 |
| **数据层** | PostgreSQL | 持久化存储、Grain State 存取 |

---

## 2. 通讯协议设计

### 2.1 协议格式

采用 **包头 + 包体** 的二进制协议，所有多字节字段使用 **大端序 (Big-Endian)**：

```
┌──────────────────────────────── Packet ────────────────────────────────┐
│                                                                        │
│  ┌─────────── Header (6 bytes) ──────────┐  ┌─── Body (N bytes) ───┐  │
│  │ Length (4 bytes) │ MessageId (2 bytes) │  │  Payload (序列化数据)  │  │
│  └──────────────────┴────────────────────┘  └──────────────────────┘  │
│                                                                        │
└────────────────────────────────────────────────────────────────────────┘
```

| 字段 | 类型 | 大小 | 说明 |
|------|------|------|------|
| `Length` | `uint` | 4 bytes | **包体长度**（不含包头自身的 6 字节） |
| `MessageId` | `ushort` | 2 bytes | 消息类型 ID，用于反射/查表分发到对应 Handler |
| `Payload` | `byte[]` | N bytes | 序列化后的数据对象（MessagePack / Protobuf / MemoryPack） |

### 2.2 序列化方案选型

推荐使用 **MemoryPack**（.NET 生态最高性能）或 **MessagePack**：

```csharp
// 消息定义示例
[MemoryPackable]
public partial class C2S_Login
{
    public string Account { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public int Platform { get; set; }
}

[MemoryPackable]
public partial class S2C_Login
{
    public int ErrorCode { get; set; }
    public long PlayerId { get; set; }
    public string Nickname { get; set; } = string.Empty;
}
```

### 2.3 消息 ID 注册表

```csharp
// 自动或手动维护的消息映射
public static class MessageRegistry
{
    private static readonly Dictionary<ushort, Type> _idToType = new();
    private static readonly Dictionary<Type, ushort> _typeToId = new();

    static MessageRegistry()
    {
        Register<C2S_Login>(1001);
        Register<S2C_Login>(1002);
        Register<C2S_EnterRoom>(1003);
        Register<S2C_EnterRoom>(1004);
        // ... 更多消息
    }

    public static void Register<T>(ushort id)
    {
        _idToType[id] = typeof(T);
        _typeToId[typeof(T)] = id;
    }

    public static Type? GetType(ushort id) 
        => _idToType.GetValueOrDefault(id);
    
    public static ushort GetId<T>() 
        => _typeToId[typeof(T)];
    
    public static ushort GetId(Type type) 
        => _typeToId[type];
}
```

### 2.4 编解码器实现

```csharp
public class PacketCodec
{
    public const int HeaderSize = 6; // 4(Length) + 2(MessageId)

    /// <summary>
    /// 编码：将强类型消息对象编码为二进制包
    /// </summary>
    public static byte[] Encode<T>(T message) where T : class
    {
        ushort messageId = MessageRegistry.GetId<T>();
        byte[] body = MemoryPackSerializer.Serialize(message);

        byte[] packet = new byte[HeaderSize + body.Length];
        
        // 写入包体长度 (大端序)
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(0, 4), (uint)body.Length);
        // 写入消息ID (大端序)
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(4, 2), messageId);
        // 写入包体
        body.CopyTo(packet.AsSpan(HeaderSize));

        return packet;
    }

    /// <summary>
    /// 解码：从二进制数据解析出消息ID和强类型对象
    /// </summary>
    public static (ushort messageId, object message) Decode(ReadOnlySpan<byte> packet)
    {
        uint bodyLength = BinaryPrimitives.ReadUInt32BigEndian(packet[..4]);
        ushort messageId = BinaryPrimitives.ReadUInt16BigEndian(packet[4..6]);
        ReadOnlySpan<byte> body = packet.Slice(HeaderSize, (int)bodyLength);

        Type? messageType = MessageRegistry.GetType(messageId);
        if (messageType == null)
            throw new InvalidOperationException($"Unknown message ID: {messageId}");

        object message = MemoryPackSerializer.Deserialize(messageType, body)!;
        return (messageId, message);
    }
}
```

### 2.5 TCP 粘包/拆包处理

```csharp
public class PacketFrameDecoder
{
    private readonly byte[] _buffer = new byte[64 * 1024]; // 64KB 缓冲区
    private int _writePos = 0;

    /// <summary>
    /// 将新收到的数据追加到缓冲区，尝试解析出完整的包
    /// </summary>
    public List<(ushort messageId, object message)> OnDataReceived(
        ReadOnlySpan<byte> data)
    {
        var results = new List<(ushort, object)>();
        
        // 追加数据到缓冲区
        data.CopyTo(_buffer.AsSpan(_writePos));
        _writePos += data.Length;

        // 循环尝试解析完整包
        int readPos = 0;
        while (true)
        {
            int remaining = _writePos - readPos;
            if (remaining < PacketCodec.HeaderSize)
                break; // 不够包头

            uint bodyLength = BinaryPrimitives.ReadUInt32BigEndian(
                _buffer.AsSpan(readPos, 4));
            int totalPacketSize = PacketCodec.HeaderSize + (int)bodyLength;

            if (remaining < totalPacketSize)
                break; // 不够一个完整包

            var packet = _buffer.AsSpan(readPos, totalPacketSize);
            var decoded = PacketCodec.Decode(packet);
            results.Add(decoded);

            readPos += totalPacketSize;
        }

        // 将未解析的数据移动到缓冲区头部
        if (readPos > 0)
        {
            int leftover = _writePos - readPos;
            _buffer.AsSpan(readPos, leftover).CopyTo(_buffer);
            _writePos = leftover;
        }

        return results;
    }
}
```

---

## 3. Gateway 网关设计

### 3.1 Gateway 职责

```
┌──────────────────────────────────────────────────────┐
│                     Gateway 进程                      │
│                                                      │
│  ┌──────────────────────────────────────────────┐    │
│  │            TCP Listener (端口 9001)            │    │
│  │  接受客户端连接，每连接分配一个 ClientSession   │    │
│  └──────────────┬───────────────────────────────┘    │
│                 │                                     │
│  ┌──────────────▼───────────────────────────────┐    │
│  │          Connection Manager                   │    │
│  │  管理所有 ClientSession 的生命周期             │    │
│  │  心跳检测 / 超时断开 / 流量控制                │    │
│  └──────────────┬───────────────────────────────┘    │
│                 │                                     │
│  ┌──────────────▼───────────────────────────────┐    │
│  │        Packet Codec (Decode / Encode)         │    │
│  │  二进制 ←→ 强类型消息对象                      │    │
│  └──────────────┬───────────────────────────────┘    │
│                 │                                     │
│  ┌──────────────▼───────────────────────────────┐    │
│  │          Message Dispatcher                   │    │
│  │  根据 MessageId 路由到对应 Orleans Grain       │    │
│  │  维护 PlayerId ←→ ClientSession 映射          │    │
│  └──────────────┬───────────────────────────────┘    │
│                 │                                     │
│  ┌──────────────▼───────────────────────────────┐    │
│  │         Orleans Client (IClusterClient)       │    │
│  │  连接到 Orleans Silo 集群                      │    │
│  │  调用 Grain 方法，接收 Grain 推送              │    │
│  └──────────────────────────────────────────────┘    │
│                                                      │
└──────────────────────────────────────────────────────┘
```

### 3.2 核心数据结构

```csharp
/// <summary>
/// 客户端会话，维护一个TCP连接的完整上下文
/// </summary>
public class ClientSession : IDisposable
{
    public string SessionId { get; } = Guid.NewGuid().ToString("N");
    public long PlayerId { get; set; }              // 登录后绑定
    public Socket Socket { get; }
    public bool IsAuthenticated { get; set; }
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;
    public int GatewayId { get; set; }              // 所在 Gateway 编号

    private readonly PacketFrameDecoder _decoder = new();

    public ClientSession(Socket socket)
    {
        Socket = socket;
    }

    public void Dispose()
    {
        Socket?.Dispose();
    }
}
```

### 3.3 Gateway 主服务

```csharp
public class GatewayService : BackgroundService
{
    private readonly IClusterClient _orleansClient;
    private readonly ILogger<GatewayService> _logger;
    private readonly ConcurrentDictionary<string, ClientSession> _sessions = new();
    private readonly ConcurrentDictionary<long, ClientSession> _playerSessions = new();
    private readonly int _port;

    public GatewayService(
        IClusterClient orleansClient,
        ILogger<GatewayService> logger,
        IConfiguration config)
    {
        _orleansClient = orleansClient;
        _logger = logger;
        _port = config.GetValue<int>("Gateway:Port", 9001);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var listener = new TcpListener(IPAddress.Any, _port);
        listener.Start();
        _logger.LogInformation("Gateway listening on port {Port}", _port);

        while (!stoppingToken.IsCancellationRequested)
        {
            var socket = await listener.AcceptSocketAsync(stoppingToken);
            var session = new ClientSession(socket);
            _sessions.TryAdd(session.SessionId, session);

            _ = HandleSessionAsync(session, stoppingToken);
        }
    }

    private async Task HandleSessionAsync(ClientSession session, CancellationToken ct)
    {
        var buffer = new byte[8192];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int bytesRead = await session.Socket.ReceiveAsync(buffer, SocketFlags.None, ct);
                if (bytesRead == 0) break;

                // 解码 → 得到强类型消息对象
                var messages = session.Decoder.OnDataReceived(buffer.AsSpan(0, bytesRead));
                
                foreach (var (messageId, message) in messages)
                {
                    await DispatchToOrleans(session, messageId, message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Session {SessionId} error", session.SessionId);
        }
        finally
        {
            OnSessionDisconnected(session);
        }
    }

    /// <summary>
    /// 将解码后的消息分发到对应的 Orleans Grain
    /// </summary>
    private async Task DispatchToOrleans(
        ClientSession session, ushort messageId, object message)
    {
        switch (message)
        {
            case C2S_Login login:
                var loginGrain = _orleansClient.GetGrain<ILoginGrain>(login.Account);
                var loginResult = await loginGrain.Login(login);
                if (loginResult.ErrorCode == 0)
                {
                    session.PlayerId = loginResult.PlayerId;
                    session.IsAuthenticated = true;
                    _playerSessions.TryAdd(loginResult.PlayerId, session);
                }
                await SendToClient(session, loginResult);
                break;

            case C2S_EnterRoom enterRoom when session.IsAuthenticated:
                var playerGrain = _orleansClient.GetGrain<IPlayerGrain>(session.PlayerId);
                var enterResult = await playerGrain.EnterRoom(enterRoom);
                await SendToClient(session, enterResult);
                break;

            // ... 更多消息路由
            default:
                _logger.LogWarning("Unhandled message: {MessageId}", messageId);
                break;
        }
    }

    /// <summary>
    /// 向客户端发送响应（编码 + 发送）
    /// </summary>
    public async Task SendToClient<T>(ClientSession session, T message) where T : class
    {
        byte[] packet = PacketCodec.Encode(message);
        await session.Socket.SendAsync(packet, SocketFlags.None);
    }

    /// <summary>
    /// 根据 PlayerId 推送消息（供 Orleans Grain 回调使用）
    /// </summary>
    public async Task PushToPlayer<T>(long playerId, T message) where T : class
    {
        if (_playerSessions.TryGetValue(playerId, out var session))
        {
            await SendToClient(session, message);
        }
    }

    private void OnSessionDisconnected(ClientSession session)
    {
        _sessions.TryRemove(session.SessionId, out _);
        if (session.PlayerId > 0)
        {
            _playerSessions.TryRemove(session.PlayerId, out _);
            // 通知 Orleans 玩家下线
            var grain = _orleansClient.GetGrain<IPlayerGrain>(session.PlayerId);
            _ = grain.OnDisconnected();
        }
        session.Dispose();
    }
}
```

### 3.4 负载均衡策略

多个 Gateway 实例前置一个 L4 负载均衡器（如 Nginx Stream / HAProxy / 云 LB）：

```
                      客户端
                        │
                        ▼
              ┌───────────────────┐
              │   L4 负载均衡器    │
              │  (Nginx/HAProxy)  │
              └──┬─────┬─────┬───┘
                 │     │     │
                 ▼     ▼     ▼
            GW-1    GW-2    GW-3     ← 每个都是独立进程
                 │     │     │
                 └──┬──┘──┬──┘
                    ▼     ▼
              Orleans Silo 集群       ← Gateway 内的 IClusterClient
                                       自动路由到正确的 Silo
```

> **关键点**：Gateway → Orleans 的路由**不需要手动负载均衡**。Orleans 的 `IClusterClient` 内置了 Grain 定位能力，会自动将请求路由到 Grain 所在的 Silo 节点。Gateway 层的负载均衡仅针对**客户端 TCP 连接的分散**。

### 3.5 Gateway 向 Orleans 推送通道（Observer / Stream）

当 Grain 需要主动推送消息给客户端时，使用 Orleans **GrainObserver** 模式：

```csharp
// 1. 定义 Observer 接口
public interface IPlayerObserver : IGrainObserver
{
    void OnMessagePush(ushort messageId, byte[] payload);
}

// 2. Gateway 侧实现 Observer
public class PlayerObserverProxy : IPlayerObserver
{
    private readonly ClientSession _session;

    public PlayerObserverProxy(ClientSession session)
    {
        _session = session;
    }

    public void OnMessagePush(ushort messageId, byte[] payload)
    {
        // 直接将已编码的包发送给客户端
        var packet = new byte[PacketCodec.HeaderSize + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(0, 4), (uint)payload.Length);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(4, 2), messageId);
        payload.CopyTo(packet.AsSpan(PacketCodec.HeaderSize));
        
        _ = _session.Socket.SendAsync(packet, SocketFlags.None);
    }
}

// 3. Gateway 登录成功后注册 Observer
var observer = new PlayerObserverProxy(session);
var observerRef = _orleansClient.CreateObjectReference<IPlayerObserver>(observer);
await playerGrain.Subscribe(observerRef);

// 4. Grain 内部推送
public class PlayerGrain : Grain, IPlayerGrain
{
    private IPlayerObserver? _observer;

    public Task Subscribe(IPlayerObserver observer)
    {
        _observer = observer;
        return Task.CompletedTask;
    }

    // 推送示例
    private void PushToClient<T>(T message) where T : class
    {
        if (_observer != null)
        {
            ushort id = MessageRegistry.GetId<T>();
            byte[] body = MemoryPackSerializer.Serialize(message);
            _observer.OnMessagePush(id, body);
        }
    }
}
```

---

## 4. Orleans 游戏逻辑层设计

### 4.1 Grain 设计总览

```
┌─────────────────────────────────────────────────────────────┐
│                     Orleans Silo 集群                        │
│                                                             │
│  ┌─────────────┐  ┌─────────────┐  ┌──────────────┐       │
│  │ LoginGrain   │  │ PlayerGrain │  │  RoomGrain    │       │
│  │ Key: Account │  │ Key: PlayerId│ │ Key: RoomId   │       │
│  │              │  │              │  │               │       │
│  │ - 账号验证    │  │ - 玩家状态   │  │ - 房间状态    │       │
│  │ - Token生成  │  │ - 背包       │  │ - 对局逻辑    │       │
│  │              │  │ - 任务       │  │ - 帧同步/状态 │       │
│  └─────────────┘  │ - 推送       │  │   同步        │       │
│                    └─────────────┘  └──────────────┘       │
│                                                             │
│  ┌─────────────┐  ┌──────────────┐  ┌──────────────┐      │
│  │ MatchGrain   │  │ RankGrain    │  │  ChatGrain   │      │
│  │ Key: QueueId │  │ Key: Type    │  │ Key: ChannelId│     │
│  │              │  │              │  │               │      │
│  │ - 匹配队列   │  │ - 排行榜     │  │ - 聊天频道    │      │
│  │ - 分配房间   │  │ - 数据查询   │  │ - 消息广播    │      │
│  └─────────────┘  └──────────────┘  └──────────────┘      │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### 4.2 Grain 接口定义

```csharp
// ─── 登录 ───
public interface ILoginGrain : IGrainWithStringKey  // Key = Account
{
    Task<S2C_Login> Login(C2S_Login request);
}

// ─── 玩家 ───
public interface IPlayerGrain : IGrainWithIntegerKey  // Key = PlayerId
{
    Task Subscribe(IPlayerObserver observer);
    Task OnDisconnected();
    
    Task<S2C_EnterRoom> EnterRoom(C2S_EnterRoom request);
    Task<S2C_PlayerInfo> GetPlayerInfo();
    Task<S2C_BagInfo> GetBagInfo();
}

// ─── 房间 ───
public interface IRoomGrain : IGrainWithIntegerKey  // Key = RoomId
{
    Task<bool> JoinRoom(long playerId);
    Task LeaveRoom(long playerId);
    Task PlayerAction(long playerId, C2S_PlayerAction action);
}

// ─── 匹配 ───
public interface IMatchGrain : IGrainWithStringKey  // Key = "default" / "ranked"
{
    Task<S2C_MatchResult> RequestMatch(long playerId, int rating);
    Task CancelMatch(long playerId);
}
```

### 4.3 Grain 实现示例 — PlayerGrain

```csharp
[StorageProvider(ProviderName = "PostgreSQL")]
public class PlayerGrain : Grain, IPlayerGrain
{
    private readonly IPersistentState<PlayerState> _state;
    private readonly ILogger<PlayerGrain> _logger;
    private IPlayerObserver? _observer;

    public PlayerGrain(
        [PersistentState("player", "PostgreSQL")] IPersistentState<PlayerState> state,
        ILogger<PlayerGrain> logger)
    {
        _state = state;
        _logger = logger;
    }

    // Grain 激活时自动加载 State
    public override async Task OnActivateAsync(CancellationToken ct)
    {
        _logger.LogInformation("PlayerGrain {PlayerId} activated", this.GetPrimaryKeyLong());
        await base.OnActivateAsync(ct);
    }

    public Task Subscribe(IPlayerObserver observer)
    {
        _observer = observer;
        return Task.CompletedTask;
    }

    public async Task OnDisconnected()
    {
        _observer = null;
        _logger.LogInformation("Player {PlayerId} disconnected", this.GetPrimaryKeyLong());
        // 可选：设置延迟停用
        DelayDeactivation(TimeSpan.FromMinutes(5));
    }

    public Task<S2C_PlayerInfo> GetPlayerInfo()
    {
        return Task.FromResult(new S2C_PlayerInfo
        {
            PlayerId = this.GetPrimaryKeyLong(),
            Nickname = _state.State.Nickname,
            Level = _state.State.Level,
            Exp = _state.State.Exp,
        });
    }

    public async Task<S2C_EnterRoom> EnterRoom(C2S_EnterRoom request)
    {
        var roomGrain = GrainFactory.GetGrain<IRoomGrain>(request.RoomId);
        bool joined = await roomGrain.JoinRoom(this.GetPrimaryKeyLong());
        
        return new S2C_EnterRoom
        {
            ErrorCode = joined ? 0 : 1,
            RoomId = request.RoomId,
        };
    }

    public async Task<S2C_BagInfo> GetBagInfo()
    {
        return new S2C_BagInfo
        {
            Items = _state.State.Items.Select(i => new ItemInfo
            {
                ItemId = i.ItemId,
                Count = i.Count,
            }).ToList(),
        };
    }

    // 内部方法：修改状态后持久化
    private async Task AddItem(int itemId, int count)
    {
        var existing = _state.State.Items.FirstOrDefault(i => i.ItemId == itemId);
        if (existing != null)
            existing.Count += count;
        else
            _state.State.Items.Add(new ItemData { ItemId = itemId, Count = count });

        await _state.WriteStateAsync(); // 写入 PostgreSQL
    }
}
```

### 4.4 Grain State 定义

```csharp
[GenerateSerializer]
public class PlayerState
{
    [Id(0)] public long PlayerId { get; set; }
    [Id(1)] public string Nickname { get; set; } = string.Empty;
    [Id(2)] public int Level { get; set; } = 1;
    [Id(3)] public long Exp { get; set; }
    [Id(4)] public List<ItemData> Items { get; set; } = new();
    [Id(5)] public DateTime CreateTime { get; set; } = DateTime.UtcNow;
    [Id(6)] public DateTime LastLoginTime { get; set; }
}

[GenerateSerializer]
public class ItemData
{
    [Id(0)] public int ItemId { get; set; }
    [Id(1)] public int Count { get; set; }
}
```

---

## 5. 数据层设计（PostgreSQL）

### 5.1 Orleans + PostgreSQL 集成配置

```csharp
// Silo 启动配置
var builder = Host.CreateDefaultBuilder(args)
    .UseOrleans((ctx, siloBuilder) =>
    {
        var pgConnectionString = ctx.Configuration.GetConnectionString("PostgreSQL")!;

        siloBuilder
            // 集群成员管理使用 PG
            .UseAdoNetClustering(options =>
            {
                options.Invariant = "Npgsql";
                options.ConnectionString = pgConnectionString;
            })
            // Grain 状态持久化使用 PG
            .AddAdoNetGrainStorage("PostgreSQL", options =>
            {
                options.Invariant = "Npgsql";
                options.ConnectionString = pgConnectionString;
                options.UseJsonFormat = true;  // State 以 JSON 格式存入 PG
            })
            // Reminder 使用 PG
            .UseAdoNetReminderService(options =>
            {
                options.Invariant = "Npgsql";
                options.ConnectionString = pgConnectionString;
            })
            .ConfigureEndpoints(siloPort: 11111, gatewayPort: 30000);
    });
```

### 5.2 数据库表结构

Orleans ADO.NET Provider 会自动创建以下核心表（需先执行 [官方 SQL 脚本](https://github.com/dotnet/orleans/tree/main/src/AdoNet/Orleans.Persistence.AdoNet)）：

```sql
-- Orleans 自动管理的表 (由官方脚本创建)
-- OrleansQuery          -- 存储过程/函数
-- OrleansMembershipTable -- 集群成员
-- OrleansRemindersTable  -- 定时提醒
-- OrleansStorage         -- Grain State 持久化

-- 业务扩展表 (自行创建，用于复杂查询场景)
CREATE TABLE IF NOT EXISTS player_accounts (
    account     VARCHAR(128) PRIMARY KEY,
    player_id   BIGINT NOT NULL UNIQUE,
    password_hash VARCHAR(256) NOT NULL,
    platform    INT NOT NULL DEFAULT 0,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_login  TIMESTAMPTZ
);

CREATE TABLE IF NOT EXISTS player_login_log (
    id          BIGSERIAL PRIMARY KEY,
    player_id   BIGINT NOT NULL,
    gateway_id  INT NOT NULL,
    ip_address  VARCHAR(45),
    login_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS payment_orders (
    order_id    VARCHAR(64) PRIMARY KEY,
    player_id   BIGINT NOT NULL,
    product_id  VARCHAR(64) NOT NULL,
    amount      DECIMAL(10,2) NOT NULL,
    status      INT NOT NULL DEFAULT 0,  -- 0:pending, 1:success, 2:failed
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    completed_at TIMESTAMPTZ
);

-- 索引
CREATE INDEX idx_login_log_player ON player_login_log(player_id);
CREATE INDEX idx_payment_player ON payment_orders(player_id);
```

### 5.3 自定义数据访问（绕过 Grain State，用于复杂查询）

```csharp
/// <summary>
/// 用于非 Grain State 的直接数据库访问
/// 注册为 Silo 的单例服务
/// </summary>
public class GameDbRepository
{
    private readonly string _connectionString;

    public GameDbRepository(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("PostgreSQL")!;
    }

    public async Task<long?> GetPlayerIdByAccount(string account)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        
        await using var cmd = new NpgsqlCommand(
            "SELECT player_id FROM player_accounts WHERE account = @account", conn);
        cmd.Parameters.AddWithValue("account", account);
        
        var result = await cmd.ExecuteScalarAsync();
        return result as long?;
    }

    public async Task CreateAccount(string account, long playerId, string passwordHash)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO player_accounts (account, player_id, password_hash)
            VALUES (@account, @player_id, @password_hash)", conn);
        
        cmd.Parameters.AddWithValue("account", account);
        cmd.Parameters.AddWithValue("player_id", playerId);
        cmd.Parameters.AddWithValue("password_hash", passwordHash);
        
        await cmd.ExecuteNonQueryAsync();
    }
}
```

---

## 6. 项目工程结构

```
GameServer/
├── GameServer.sln
│
├── src/
│   ├── GameShared/                         # 共享库（Gateway + Orleans 都引用）
│   │   ├── Messages/                       # 消息定义
│   │   │   ├── C2S_Login.cs
│   │   │   ├── S2C_Login.cs
│   │   │   ├── C2S_EnterRoom.cs
│   │   │   └── ...
│   │   ├── MessageRegistry.cs              # 消息 ID 注册表
│   │   ├── PacketCodec.cs                  # 编解码器
│   │   └── GameShared.csproj
│   │
│   ├── GameGrainInterfaces/                # Orleans Grain 接口
│   │   ├── ILoginGrain.cs
│   │   ├── IPlayerGrain.cs
│   │   ├── IRoomGrain.cs
│   │   ├── IMatchGrain.cs
│   │   ├── IPlayerObserver.cs
│   │   ├── States/                         # Grain State 定义
│   │   │   ├── PlayerState.cs
│   │   │   └── RoomState.cs
│   │   └── GameGrainInterfaces.csproj
│   │
│   ├── GameGrains/                         # Orleans Grain 实现
│   │   ├── LoginGrain.cs
│   │   ├── PlayerGrain.cs
│   │   ├── RoomGrain.cs
│   │   ├── MatchGrain.cs
│   │   ├── Services/
│   │   │   └── GameDbRepository.cs         # 自定义 PG 数据访问
│   │   └── GameGrains.csproj
│   │
│   ├── GameSilo/                           # Orleans Silo 宿主
│   │   ├── Program.cs                      # Silo 启动配置
│   │   ├── appsettings.json
│   │   └── GameSilo.csproj
│   │
│   └── GameGateway/                        # Gateway 网络服务
│       ├── Program.cs                      # Gateway 启动入口
│       ├── GatewayService.cs               # TCP 监听 + 连接管理
│       ├── ClientSession.cs                # 客户端会话
│       ├── PacketFrameDecoder.cs           # 粘包处理
│       ├── MessageDispatcher.cs            # 消息分发到 Orleans
│       ├── PlayerObserverProxy.cs          # Observer 代理
│       ├── appsettings.json
│       └── GameGateway.csproj
│
├── sql/
│   ├── orleans_tables.sql                  # Orleans 官方建表脚本 (PG版)
│   └── game_tables.sql                     # 业务扩展表
│
└── tests/
    ├── GameGrains.Tests/                   # Grain 单元测试
    └── GameGateway.Tests/                  # Gateway 测试
```

---

## 7. 启动与部署

### 7.1 Silo 启动 (`GameSilo/Program.cs`)

```csharp
var builder = Host.CreateDefaultBuilder(args)
    .UseOrleans((ctx, siloBuilder) =>
    {
        var pg = ctx.Configuration.GetConnectionString("PostgreSQL")!;

        siloBuilder
            .UseAdoNetClustering(o =>
            {
                o.Invariant = "Npgsql";
                o.ConnectionString = pg;
            })
            .AddAdoNetGrainStorage("PostgreSQL", o =>
            {
                o.Invariant = "Npgsql";
                o.ConnectionString = pg;
                o.UseJsonFormat = true;
            })
            .UseAdoNetReminderService(o =>
            {
                o.Invariant = "Npgsql";
                o.ConnectionString = pg;
            })
            .ConfigureEndpoints(
                siloPort: 11111,
                gatewayPort: 30000)
            .ConfigureServices(services =>
            {
                services.AddSingleton<GameDbRepository>();
            });
    })
    .ConfigureLogging(logging => logging.AddConsole());

await builder.Build().RunAsync();
```

### 7.2 Gateway 启动 (`GameGateway/Program.cs`)

```csharp
var builder = Host.CreateDefaultBuilder(args)
    .ConfigureServices((ctx, services) =>
    {
        // 注入 Orleans Client
        services.AddOrleansClient(clientBuilder =>
        {
            var pg = ctx.Configuration.GetConnectionString("PostgreSQL")!;
            clientBuilder.UseAdoNetClustering(o =>
            {
                o.Invariant = "Npgsql";
                o.ConnectionString = pg;
            });
        });

        // 注入 Gateway 后台服务
        services.AddHostedService<GatewayService>();
    })
    .ConfigureLogging(logging => logging.AddConsole());

await builder.Build().RunAsync();
```

### 7.3 部署拓扑

```
┌─────────────────────────────────────────────────────────────┐
│                     生产环境部署拓扑                          │
│                                                             │
│  L4 LB (Nginx Stream / 云 SLB)                             │
│    ├── Gateway-1  (4核8G)   ← TCP :9001                    │
│    ├── Gateway-2  (4核8G)   ← TCP :9001                    │
│    └── Gateway-3  (4核8G)   ← TCP :9001                    │
│                                                             │
│  Orleans Silo 集群 (通过 PG 发现彼此)                        │
│    ├── Silo-1  (8核16G)  ← :11111(Silo) :30000(Gateway)   │
│    ├── Silo-2  (8核16G)  ← :11111(Silo) :30000(Gateway)   │
│    └── Silo-3  (8核16G)  ← :11111(Silo) :30000(Gateway)   │
│                                                             │
│  PostgreSQL                                                 │
│    ├── Primary   (写)                                       │
│    └── Replica   (读，可选)                                  │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

---

## 8. 核心流程时序图

### 8.1 登录流程

```
Client          Gateway              LoginGrain         PlayerGrain       PostgreSQL
  │                │                      │                  │                │
  │── TCP连接 ────→│                      │                  │                │
  │── C2S_Login ──→│                      │                  │                │
  │                │── Decode ──→         │                  │                │
  │                │   (消息对象)          │                  │                │
  │                │── Login() ──────────→│                  │                │
  │                │                      │── 查询账号 ─────→│                │
  │                │                      │                  │── SELECT ─────→│
  │                │                      │                  │←── 返回 ───────│
  │                │                      │── 验证Token ──→  │                │
  │                │                      │←── PlayerId ──── │                │
  │                │←── S2C_Login ────────│                  │                │
  │                │── Encode ──→         │                  │                │
  │←── S2C_Login ──│                      │                  │                │
  │                │                      │                  │                │
  │                │── Subscribe(Observer) ──────────────────→│               │
  │                │                      │                  │                │
```

### 8.2 战斗消息流程

```
Client          Gateway              PlayerGrain          RoomGrain
  │                │                      │                   │
  │── C2S_Action ─→│                      │                   │
  │                │── Decode             │                   │
  │                │── PlayerAction() ───→│                   │
  │                │                      │── PlayerAction() ─→│
  │                │                      │                   │── 广播给房间
  │                │                      │                   │   内所有玩家
  │                │                      │←── Observer推送 ──│
  │                │←── OnMessagePush() ──│                   │
  │←── S2C_Action ─│                      │                   │
```

---

## 9. 关键设计约定

### 9.1 消息命名规范

| 方向 | 前缀 | 示例 |
|------|------|------|
| 客户端 → 服务端 | `C2S_` | `C2S_Login`, `C2S_EnterRoom` |
| 服务端 → 客户端 | `S2C_` | `S2C_Login`, `S2C_EnterRoom` |
| 服务端推送 | `S2C_Push_` | `S2C_Push_ChatMessage` |

### 9.2 消息 ID 分段

| 范围 | 用途 |
|------|------|
| 1001–1999 | 登录 / 账号相关 |
| 2001–2999 | 玩家信息 / 背包 |
| 3001–3999 | 房间 / 战斗 |
| 4001–4999 | 社交 / 聊天 |
| 5001–5999 | 匹配 / 排行 |
| 9001–9999 | 系统 / 心跳 / 错误 |

### 9.3 Grain Key 规范

| Grain | Key 类型 | Key 含义 |
|-------|----------|----------|
| `ILoginGrain` | `string` | 账号名 |
| `IPlayerGrain` | `long` | 玩家ID |
| `IRoomGrain` | `long` | 房间ID |
| `IMatchGrain` | `string` | 队列名 ("default", "ranked") |
| `IRankGrain` | `string` | 排行榜类型 ("level", "score") |

### 9.4 错误码规范

```csharp
public static class ErrorCodes
{
    public const int Success = 0;
    public const int Unknown = -1;
    
    // 1xxx - 登录相关
    public const int AccountNotFound = 1001;
    public const int InvalidToken = 1002;
    public const int AccountBanned = 1003;
    
    // 2xxx - 玩家相关
    public const int PlayerNotFound = 2001;
    public const int ItemNotEnough = 2002;
    
    // 3xxx - 房间相关
    public const int RoomFull = 3001;
    public const int RoomNotFound = 3002;
    public const int AlreadyInRoom = 3003;
}
```

---

## 10. 扩展与演进

### 10.1 已支持

- [x] 基础登录 / 玩家数据管理
- [x] 房间对战（状态同步）
- [x] 匹配系统
- [x] PostgreSQL 持久化
- [x] Gateway 水平扩展
- [x] Silo 集群自动发现

### 10.2 后续计划

| 特性 | 实现方式 | 优先级 |
|------|---------|--------|
| 帧同步 | RoomGrain + Timer 驱动定帧 | 高 |
| 聊天系统 | ChatGrain + Orleans Stream | 中 |
| 排行榜 | RankGrain + Redis Sorted Set | 中 |
| 热更新 | Grain 接口不变，实现 DLL 热加载 | 中 |
| 跨服玩法 | 多 Silo 集群 + 联邦 Grain | 低 |
| AI 对战 | 独立 AIGrain 模拟玩家操作 | 低 |

---

## 11. 参考资料

- [opendeep.wiki - Orleans 架构总览](https://opendeep.wiki/dotnet/orleans/architecture-overview)
- [opendeep.wiki - Orleans 扩展与集成](https://opendeep.wiki/dotnet/orleans/extension-and-integration)
- [opendeep.wiki - Orleans 序列化机制](https://opendeep.wiki/dotnet/orleans/serialization)
- [Orleans 官方 GitHub](https://github.com/dotnet/orleans)
- [csdn.net - 游戏架构设计文档模板](https://blog.csdn.net/qq_33060405/article/details/149546987)