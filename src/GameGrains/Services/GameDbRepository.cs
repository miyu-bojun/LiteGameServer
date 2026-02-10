using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace GameGrains.Services;

/// <summary>
/// 游戏数据库访问服务，负责账号和登录日志的数据库操作
/// </summary>
public class GameDbRepository : IDisposable
{
    private readonly string _connectionString;
    private readonly ILogger<GameDbRepository> _logger;
    private NpgsqlConnection? _connection;

    public GameDbRepository(IConfiguration configuration, ILogger<GameDbRepository> logger)
    {
        _connectionString = configuration.GetConnectionString("PostgreSQL")
            ?? throw new ArgumentNullException("PostgreSQL connection string is required");
        _logger = logger;
    }

    /// <summary>
    /// 获取数据库连接（如果未连接则创建新连接）
    /// </summary>
    private async Task<NpgsqlConnection> GetConnectionAsync()
    {
        if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
        {
            _connection = new NpgsqlConnection(_connectionString);
            await _connection.OpenAsync();
            _logger.LogDebug("Database connection opened");
        }
        return _connection;
    }

    /// <summary>
    /// 根据账号查询玩家ID
    /// </summary>
    public async Task<long?> GetPlayerIdByAccount(string account)
    {
        try
        {
            var connection = await GetConnectionAsync();
            const string query = "SELECT player_id FROM player_accounts WHERE account = @account LIMIT 1";

            await using var command = new NpgsqlCommand(query, connection);
            command.Parameters.AddWithValue("account", account);

            var result = await command.ExecuteScalarAsync();
            if (result != null && result != DBNull.Value)
            {
                long playerId = (long)result;
                _logger.LogDebug("Found player_id {PlayerId} for account {Account}", playerId, account);
                return playerId;
            }

            _logger.LogDebug("No player_id found for account {Account}", account);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying player_id for account {Account}", account);
            throw;
        }
    }

    /// <summary>
    /// 创建新账号
    /// </summary>
    public async Task CreateAccount(string account, long playerId, string passwordHash, int platform)
    {
        try
        {
            var connection = await GetConnectionAsync();
            const string query = @"
                INSERT INTO player_accounts (account, player_id, password_hash, platform, created_at, last_login)
                VALUES (@account, @player_id, @password_hash, @platform, @created_at, @last_login)";

            await using var command = new NpgsqlCommand(query, connection);
            command.Parameters.AddWithValue("account", account);
            command.Parameters.AddWithValue("player_id", playerId);
            command.Parameters.AddWithValue("password_hash", passwordHash);
            command.Parameters.AddWithValue("platform", platform);
            command.Parameters.AddWithValue("created_at", DateTime.UtcNow);
            command.Parameters.AddWithValue("last_login", DateTime.UtcNow);

            await command.ExecuteNonQueryAsync();
            _logger.LogInformation("Created account {Account} with player_id {PlayerId}", account, playerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating account {Account}", account);
            throw;
        }
    }

    /// <summary>
    /// 记录玩家登录日志
    /// </summary>
    public async Task LogPlayerLogin(long playerId, int gatewayId, string ipAddress)
    {
        try
        {
            var connection = await GetConnectionAsync();
            const string query = @"
                INSERT INTO player_login_log (player_id, gateway_id, ip_address, login_at)
                VALUES (@player_id, @gateway_id, @ip_address, @login_at)";

            await using var command = new NpgsqlCommand(query, connection);
            command.Parameters.AddWithValue("player_id", playerId);
            command.Parameters.AddWithValue("gateway_id", gatewayId);
            command.Parameters.AddWithValue("ip_address", ipAddress);
            command.Parameters.AddWithValue("login_at", DateTime.UtcNow);

            await command.ExecuteNonQueryAsync();
            _logger.LogDebug("Logged login for player_id {PlayerId} from gateway {GatewayId}", playerId, gatewayId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging login for player_id {PlayerId}", playerId);
            throw;
        }
    }

    /// <summary>
    /// 更新账号的最后登录时间
    /// </summary>
    public async Task UpdateLastLogin(string account)
    {
        try
        {
            var connection = await GetConnectionAsync();
            const string query = "UPDATE player_accounts SET last_login = @last_login WHERE account = @account";

            await using var command = new NpgsqlCommand(query, connection);
            command.Parameters.AddWithValue("last_login", DateTime.UtcNow);
            command.Parameters.AddWithValue("account", account);

            await command.ExecuteNonQueryAsync();
            _logger.LogDebug("Updated last_login for account {Account}", account);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating last_login for account {Account}", account);
            throw;
        }
    }

    /// <summary>
    /// 释放数据库连接
    /// </summary>
    public void Dispose()
    {
        _connection?.Dispose();
        _logger.LogDebug("GameDbRepository disposed");
    }
}
