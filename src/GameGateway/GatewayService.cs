using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using GameGrainInterfaces;
using GameShared;
using GameShared.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;

namespace GameGateway;

/// <summary>
/// Gateway 网关服务，负责 TCP 监听、连接管理、消息分发到 Orleans
/// </summary>
public class GatewayService : BackgroundService
{
    private readonly IClusterClient _orleansClient;
    private readonly ILogger<GatewayService> _logger;
    private readonly ConcurrentDictionary<string, ClientSession> _sessions = new();
    private readonly ConcurrentDictionary<long, ClientSession> _playerSessions = new();
    private readonly ConcurrentDictionary<long, IPlayerObserver> _playerObservers = new();
    private readonly int _port;
    private readonly int _gatewayId;
    private readonly int _heartbeatTimeoutSeconds;
    private Timer? _heartbeatTimer;

    public GatewayService(
        IClusterClient orleansClient,
        ILogger<GatewayService> logger,
        IConfiguration config)
    {
        _orleansClient = orleansClient;
        _logger = logger;
        _port = config.GetValue<int>("Gateway:Port", 9001);
        _gatewayId = config.GetValue<int>("Gateway:Id", 1);
        _heartbeatTimeoutSeconds = config.GetValue<int>("Gateway:HeartbeatTimeoutSeconds", 60);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 启动心跳超时扫描定时器（每 15 秒扫描一次）
        _heartbeatTimer = new Timer(ScanHeartbeatTimeout, null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));

        var listener = new TcpListener(IPAddress.Any, _port);
        listener.Start();
        _logger.LogInformation(
            "Gateway listening on port {Port}, GatewayId: {GatewayId}, HeartbeatTimeout: {Timeout}s",
            _port, _gatewayId, _heartbeatTimeoutSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var socket = await listener.AcceptSocketAsync(stoppingToken);
                var session = new ClientSession(socket)
                {
                    GatewayId = _gatewayId
                };
                
                if (_sessions.TryAdd(session.SessionId, session))
                {
                    _logger.LogInformation("New session connected: {SessionId}, RemoteEndPoint: {RemoteEndPoint}",
                        session.SessionId, socket.RemoteEndPoint);
                    
                    _ = HandleSessionAsync(session, stoppingToken);
                }
                else
                {
                    socket.Dispose();
                    _logger.LogWarning("Failed to add session: {SessionId}", session.SessionId);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常停止
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting connection");
            }
        }

        listener.Stop();
    }

    private async Task HandleSessionAsync(ClientSession session, CancellationToken ct)
    {
        var buffer = new byte[8192];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int bytesRead = await session.Socket.ReceiveAsync(buffer, SocketFlags.None, ct);
                if (bytesRead == 0)
                {
                    _logger.LogInformation("Session {SessionId} disconnected gracefully", session.SessionId);
                    break;
                }

                // 解码 → 得到强类型消息对象
                var messages = session.Decoder.OnDataReceived(buffer.AsSpan(0, bytesRead));
                
                foreach (var (messageId, message) in messages)
                {
                    await DispatchToOrleans(session, messageId, message);
                }
            }
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset ||
                                         ex.SocketErrorCode == SocketError.ConnectionAborted)
        {
            _logger.LogInformation("Session {SessionId} connection reset/aborted", session.SessionId);
        }
        catch (OperationCanceledException)
        {
            // 正常停止
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
        try
        {
            switch (message)
            {
                case C2S_Login login:
                    await HandleLogin(session, login);
                    break;

                case C2S_EnterRoom enterRoom when session.IsAuthenticated:
                    await HandleEnterRoom(session, enterRoom);
                    break;

                case C2S_PlayerAction playerAction when session.IsAuthenticated:
                    await HandlePlayerAction(session, playerAction);
                    break;

                case C2S_RequestMatch requestMatch when session.IsAuthenticated:
                    await HandleRequestMatch(session, requestMatch);
                    break;

                case C2S_Heartbeat heartbeat:
                    await HandleHeartbeat(session, heartbeat);
                    break;

                default:
                    _logger.LogWarning("Unhandled message: {MessageId} from session {SessionId}",
                        messageId, session.SessionId);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error dispatching message {MessageId} from session {SessionId}",
                messageId, session.SessionId);
        }
    }

    private async Task HandleLogin(ClientSession session, C2S_Login login)
    {
        try
        {
            var loginGrain = _orleansClient.GetGrain<ILoginGrain>(login.Account);
            var loginResult = await loginGrain.Login(login);
            
            await SendToClient(session, loginResult);

            if (loginResult.ErrorCode == 0)
            {
                session.PlayerId = loginResult.PlayerId;
                session.IsAuthenticated = true;
                _playerSessions.TryAdd(loginResult.PlayerId, session);

                // 创建 Observer 并订阅
                var observer = new PlayerObserverProxy(session, this);
                var observerRef =  _orleansClient.CreateObjectReference<IPlayerObserver>(observer);
                _playerObservers.TryAdd(loginResult.PlayerId, observerRef);

                var playerGrain = _orleansClient.GetGrain<IPlayerGrain>(loginResult.PlayerId);
                await playerGrain.Subscribe(observerRef);

                _logger.LogInformation("Player {PlayerId} logged in successfully, session: {SessionId}",
                    loginResult.PlayerId, session.SessionId);
            }
            else
            {
                _logger.LogWarning("Login failed for account {Account}, error: {ErrorCode}",
                    login.Account, loginResult.ErrorCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling login for account {Account}", login.Account);
            await SendToClient(session, new S2C_Login
            {
                ErrorCode = (int)ErrorCodes.CommonError,
                PlayerId = 0,
                Nickname = string.Empty
            });
        }
    }

    private async Task HandleEnterRoom(ClientSession session, C2S_EnterRoom enterRoom)
    {
        try
        {
            var playerGrain = _orleansClient.GetGrain<IPlayerGrain>(session.PlayerId);
            var enterResult = await playerGrain.EnterRoom(enterRoom);
            await SendToClient(session, enterResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling EnterRoom for player {PlayerId}", session.PlayerId);
        }
    }

    private async Task HandlePlayerAction(ClientSession session, C2S_PlayerAction playerAction)
    {
        try
        {
            // 玩家操作应该由 RoomGrain 处理
            // 这里暂时返回一个简单的响应
            var actionResult = new S2C_PlayerAction
            {
                PlayerId = session.PlayerId,
                ActionType = playerAction.ActionType,
                ActionData = playerAction.ActionData
            };
            await SendToClient(session, actionResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling PlayerAction for player {PlayerId}", session.PlayerId);
        }
    }

    private async Task HandleHeartbeat(ClientSession session, C2S_Heartbeat heartbeat)
    {
        // 更新最后心跳时间
        session.LastHeartbeat = DateTime.UtcNow;

        // 回复心跳
        await SendToClient(session, new S2C_Heartbeat
        {
            ServerTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });
    }

    private async Task HandleRequestMatch(ClientSession session, C2S_RequestMatch requestMatch)
    {
        try
        {
            var matchGrain = _orleansClient.GetGrain<IMatchGrain>("default");
            var matchResult = await matchGrain.RequestMatch(session.PlayerId, requestMatch.Rating);
            await SendToClient(session, matchResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling RequestMatch for player {PlayerId}", session.PlayerId);
        }
    }

    /// <summary>
    /// 向客户端发送响应（编码 + 发送）
    /// </summary>
    public async Task SendToClient<T>(ClientSession session, T message) where T : class
    {
        try
        {
            byte[] packet = PacketCodec.Encode(message);
            await session.Socket.SendAsync(packet, SocketFlags.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message to session {SessionId}", session.SessionId);
            OnSessionDisconnected(session);
        }
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

    /// <summary>
    /// 会话断开时的清理工作
    /// </summary>
    public void OnSessionDisconnected(ClientSession session)
    {
        _sessions.TryRemove(session.SessionId, out _);
        
        if (session.PlayerId > 0)
        {
            _playerSessions.TryRemove(session.PlayerId, out _);
            
            // 清理 Observer 引用
            _playerObservers.TryRemove(session.PlayerId, out _);

            // 通知 Orleans 玩家下线
            try
            {
                var grain = _orleansClient.GetGrain<IPlayerGrain>(session.PlayerId);
                _ = grain.OnDisconnected();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error notifying player {PlayerId} disconnected", session.PlayerId);
            }

            _logger.LogInformation("Player {PlayerId} disconnected, session: {SessionId}",
                session.PlayerId, session.SessionId);
        }

        session.Dispose();
    }

    /// <summary>
    /// 定时扫描心跳超时的会话并断开
    /// </summary>
    private void ScanHeartbeatTimeout(object? state)
    {
        var now = DateTime.UtcNow;
        var timeoutThreshold = TimeSpan.FromSeconds(_heartbeatTimeoutSeconds);

        foreach (var session in _sessions.Values)
        {
            if (now - session.LastHeartbeat > timeoutThreshold)
            {
                _logger.LogWarning(
                    "Session {SessionId} heartbeat timeout (last: {LastHeartbeat}), PlayerId: {PlayerId}, disconnecting",
                    session.SessionId, session.LastHeartbeat, session.PlayerId);
                OnSessionDisconnected(session);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Gateway stopping...");

        // 停止心跳扫描定时器
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
        
        // 断开所有会话
        foreach (var session in _sessions.Values)
        {
            OnSessionDisconnected(session);
        }

        await base.StopAsync(cancellationToken);
    }
}
