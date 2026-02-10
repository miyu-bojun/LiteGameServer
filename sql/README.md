# 数据库初始化指南

本文档说明如何初始化游戏服务端的 PostgreSQL 数据库。

## 前置要求

- PostgreSQL 14 或更高版本
- psql 命令行工具（或使用 pgAdmin/DBeaver 等图形化工具）

## 快速开始

### 1. 创建数据库

```bash
# 使用 psql 创建数据库
psql -U postgres -c "CREATE DATABASE gameserver;"
```

或者在 psql 交互式命令行中：

```sql
CREATE DATABASE gameserver;
```

### 2. 执行建表脚本

```bash
# 执行 Orleans 表创建脚本
psql -U postgres -d gameserver -f sql/orleans_tables.sql

# 执行游戏业务表创建脚本
psql -U postgres -d gameserver -f sql/game_tables.sql
```

### 3. 验证表创建

```sql
-- 连接到数据库
\c gameserver

-- 查看所有表
\dt

-- 预期输出应包含：
-- OrleansMembershipTable
-- OrleansRemindersTable
-- OrleansStorage
-- OrleansQuery
-- OrleansStatistics
-- player_accounts
-- player_login_log
-- payment_orders
-- player_items
-- player_statistics
-- room_statistics
-- audit_log
```

## 表结构说明

### Orleans 表

| 表名 | 用途 |
|------|------|
| `OrleansMembershipTable` | Silo 集群成员管理 |
| `OrleansRemindersTable` | Orleans 提醒服务 |
| `OrleansStorage` | Grain 状态持久化 |
| `OrleansQuery` | Orleans 流查询（可选） |
| `OrleansStatistics` | Orleans 统计信息（可选） |

### 游戏业务表

| 表名 | 用途 |
|------|------|
| `player_accounts` | 玩家账号信息 |
| `player_login_log` | 玩家登录日志 |
| `payment_orders` | 支付订单记录 |
| `player_items` | 玩家物品背包 |
| `player_statistics` | 玩家统计数据 |
| `room_statistics` | 房间统计数据 |
| `audit_log` | 审计日志 |

## 配置连接字符串

在 `appsettings.json` 中配置数据库连接字符串：

```json
{
  "ConnectionStrings": {
    "PostgreSQL": "Host=localhost;Port=5432;Database=gameserver;Username=postgres;Password=your_password"
  }
}
```

或者在环境变量中设置：

```bash
export ConnectionStrings__PostgreSQL="Host=localhost;Port=5432;Database=gameserver;Username=postgres;Password=your_password"
```

## Docker 部署

如果使用 Docker 部署 PostgreSQL，可以使用以下 docker-compose.yml：

```yaml
version: '3.8'

services:
  postgres:
    image: postgres:15-alpine
    container_name: gameserver-postgres
    environment:
      POSTGRES_DB: gameserver
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: your_password
    ports:
      - "5432:5432"
    volumes:
      - postgres_data:/var/lib/postgresql/data
      - ./sql:/docker-entrypoint-initdb.d
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres"]
      interval: 10s
      timeout: 5s
      retries: 5

volumes:
  postgres_data:
```

启动容器后，初始化脚本会自动执行。

## 数据库权限

确保应用程序用户具有以下权限：

```sql
-- 创建角色
CREATE ROLE gameserver_user WITH LOGIN PASSWORD 'your_password';

-- 授予权限
GRANT CONNECT ON DATABASE gameserver TO gameserver_user;
GRANT USAGE ON SCHEMA public TO gameserver_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO gameserver_user;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO gameserver_user;

-- 为新创建的表自动授予权限
ALTER DEFAULT PRIVILEGES IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO gameserver_user;
ALTER DEFAULT PRIVILEGES IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO gameserver_user;
```

## 数据库备份与恢复

### 备份

```bash
# 备份整个数据库
pg_dump -U postgres -d gameserver -f backup_$(date +%Y%m%d_%H%M%S).sql

# 仅备份游戏业务表
pg_dump -U postgres -d gameserver -t player_accounts -t player_login_log -t payment_orders -f game_backup.sql
```

### 恢复

```bash
# 恢复数据库
psql -U postgres -d gameserver -f backup_20240110_120000.sql
```

## 常见问题

### 1. 连接被拒绝

确保 PostgreSQL 服务正在运行：

```bash
# Linux/Mac
sudo systemctl status postgresql

# Windows
# 检查 PostgreSQL 服务是否已启动
```

### 2. 权限不足

确保应用程序用户具有足够的权限，参考上面的"数据库权限"部分。

### 3. 表已存在

脚本使用 `IF NOT EXISTS` 语法，可以安全地重复执行。如果需要重建表，请先删除：

```sql
DROP TABLE IF EXISTS audit_log CASCADE;
DROP TABLE IF EXISTS room_statistics CASCADE;
DROP TABLE IF EXISTS player_statistics CASCADE;
DROP TABLE IF EXISTS player_items CASCADE;
DROP TABLE IF EXISTS payment_orders CASCADE;
DROP TABLE IF EXISTS player_login_log CASCADE;
DROP TABLE IF EXISTS player_accounts CASCADE;
DROP TABLE IF EXISTS OrleansStatistics CASCADE;
DROP TABLE IF EXISTS OrleansQuery CASCADE;
DROP TABLE IF EXISTS OrleansStorage CASCADE;
DROP TABLE IF EXISTS OrleansRemindersTable CASCADE;
DROP TABLE IF EXISTS OrleansMembershipTable CASCADE;
```

## 性能优化建议

### 1. 连接池配置

在应用程序中配置适当的连接池大小：

```json
{
  "ConnectionStrings": {
    "PostgreSQL": "Host=localhost;Port=5432;Database=gameserver;Username=postgres;Password=your_password;Maximum Pool Size=100;Minimum Pool Size=10"
  }
}
```

### 2. 索引优化

根据实际查询模式，可能需要添加额外的索引：

```sql
-- 示例：为频繁查询的组合添加复合索引
CREATE INDEX idx_login_log_player_gateway ON player_login_log(player_id, login_at DESC);
```

### 3. 定期维护

定期执行 VACUUM 和 ANALYZE 以保持数据库性能：

```sql
VACUUM ANALYZE;
```

可以设置自动维护任务：

```sql
-- 创建定期维护任务（需要 pg_cron 扩展）
SELECT cron.schedule('vacuum-analyze', '0 3 * * *', 'VACUUM ANALYZE');
```

## 监控

### 查看表大小

```sql
SELECT 
    schemaname,
    tablename,
    pg_size_pretty(pg_total_relation_size(schemaname||'.'||tablename)) AS size
FROM pg_tables
WHERE schemaname = 'public'
ORDER BY pg_total_relation_size(schemaname||'.'||tablename) DESC;
```

### 查看慢查询

需要启用 pg_stat_statements 扩展：

```sql
CREATE EXTENSION IF NOT EXISTS pg_stat_statements;

SELECT 
    query,
    calls,
    total_time,
    mean_time,
    max_time
FROM pg_stat_statements
ORDER BY mean_time DESC
LIMIT 10;
```

## 安全建议

1. 使用强密码
2. 限制数据库访问 IP
3. 定期备份数据
4. 启用 SSL 连接（生产环境）
5. 定期更新 PostgreSQL 版本
6. 使用防火墙限制数据库端口访问

## 下一步

数据库初始化完成后，可以继续：

1. 配置 GameSilo 连接到数据库
2. 配置 GameGateway 连接到数据库
3. 运行集成测试验证数据库连接
4. 启动完整的服务端系统

## 相关文档

- [PostgreSQL 官方文档](https://www.postgresql.org/docs/)
- [Orleans 文档](https://learn.microsoft.com/en-us/dotnet/orleans/)
- [项目 README](../README.md)
