using GameGrainInterfaces;
using GameGrainInterfaces.States;
using GameGrains.Services;
using GameShared;
using GameShared.Messages;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Concurrency;

namespace GameGrains;

/// <summary>
/// 登录服务 Grain 实现
/// 使用账号名作为 Grain Key
/// </summary>
[Reentrant]
public class LoginGrain : Grain, ILoginGrain
{
    private readonly ILogger<LoginGrain> _logger;
    private readonly GameDbRepository _dbRepository;
    private readonly IGrainFactory _grainFactory;

    // 用于跟踪玩家是否已在线
    private long? _currentPlayerId;

    public LoginGrain(
        ILogger<LoginGrain> logger,
        GameDbRepository dbRepository,
        IGrainFactory grainFactory)
    {
        _logger = logger;
        _dbRepository = dbRepository;
        _grainFactory = grainFactory;
    }

    /// <summary>
    /// 处理登录请求
    /// </summary>
    public async Task<S2C_Login> Login(C2S_Login request)
    {
        string account = this.GetPrimaryKeyString();
        _logger.LogInformation("Login request for account {Account}, platform {Platform}", account, request.Platform);

        // 验证参数
        if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(request.Token))
        {
            _logger.LogWarning("Invalid login parameters for account {Account}", account);
            return new S2C_Login
            {
                ErrorCode = ErrorCodes.InvalidParam,
                PlayerId = 0,
                Nickname = string.Empty
            };
        }

        // 检查玩家是否已在线
        if (_currentPlayerId.HasValue)
        {
            _logger.LogWarning("Account {Account} is already online with player_id {PlayerId}", account, _currentPlayerId.Value);
            return new S2C_Login
            {
                ErrorCode = ErrorCodes.AccountAlreadyOnline,
                PlayerId = 0,
                Nickname = string.Empty
            };
        }

        try
        {
            // 查询账号是否存在
            long? playerId = await _dbRepository.GetPlayerIdByAccount(account);

            if (!playerId.HasValue)
            {
                // 首次登录，创建新账号
                playerId = await CreateNewAccount(account, request);
                _logger.LogInformation("Created new account {Account} with player_id {PlayerId}", account, playerId.Value);
            }
            else
            {
                // 验证 Token（简化实现：实际项目中应验证 Token）
                // 这里简单检查 Token 是否为空
                if (string.IsNullOrEmpty(request.Token))
                {
                    _logger.LogWarning("Token verification failed for account {Account}", account);
                    return new S2C_Login
                    {
                        ErrorCode = ErrorCodes.PasswordError,
                        PlayerId = 0,
                        Nickname = string.Empty
                    };
                }

                // 更新最后登录时间
                await _dbRepository.UpdateLastLogin(account);
                _logger.LogInformation("Updated last_login for account {Account}", account);
            }

            // 获取玩家信息
            var playerGrain = _grainFactory.GetGrain<IPlayerGrain>(playerId.Value);
            var playerInfo = await playerGrain.GetPlayerInfo();

            // 标记玩家为在线状态
            _currentPlayerId = playerId.Value;

            // 记录登录日志（简化：不记录 IP 地址）
            await _dbRepository.LogPlayerLogin(playerId.Value, 0, "0.0.0.0");

            _logger.LogInformation("Login successful for account {Account}, player_id {PlayerId}", account, playerId.Value);

            return new S2C_Login
            {
                ErrorCode = ErrorCodes.Success,
                PlayerId = playerId.Value,
                Nickname = playerInfo.Nickname
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for account {Account}", account);
            return new S2C_Login
            {
                ErrorCode = ErrorCodes.CommonError,
                PlayerId = 0,
                Nickname = string.Empty
            };
        }
    }

    /// <summary>
    /// 创建新账号
    /// </summary>
    private async Task<long> CreateNewAccount(string account, C2S_Login request)
    {
        // 生成唯一的 PlayerId（简化实现：使用时间戳）
        long playerId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 创建密码哈希（简化实现：实际应使用 BCrypt 等算法）
        string passwordHash = request.Token; // 简化：直接使用 Token 作为密码哈希

        // 在数据库中创建账号记录
        await _dbRepository.CreateAccount(account, playerId, passwordHash, request.Platform);

        // 创建玩家状态
        var playerGrain = _grainFactory.GetGrain<IPlayerGrain>(playerId);
        await playerGrain.InitializePlayerState(account);

        return playerId;
    }

    /// <summary>
    /// 玩家登出（供 PlayerGrain 调用）
    /// </summary>
    public Task OnLogout()
    {
        if (_currentPlayerId.HasValue)
        {
            _logger.LogInformation("Player {PlayerId} logged out from account {Account}", _currentPlayerId.Value, this.GetPrimaryKeyString());
            _currentPlayerId = null;
        }
        return Task.CompletedTask;
    }
}
