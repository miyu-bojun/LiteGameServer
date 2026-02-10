-- ============================================================================
-- Game Business Tables
-- ============================================================================
-- This file contains game-specific tables for player accounts, login logs,
-- and payment orders.
-- ============================================================================

-- ----------------------------------------------------------------------------
-- Player Accounts Table
-- ----------------------------------------------------------------------------
-- Stores player account information
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS "player_accounts"
(
    "account" VARCHAR(64) NOT NULL PRIMARY KEY,
    "player_id" BIGINT NOT NULL UNIQUE,
    "password_hash" VARCHAR(128) NOT NULL,
    "platform" INT NOT NULL DEFAULT 0,
    "created_at" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "last_login" TIMESTAMP,
    "last_gateway_id" INT,
    "last_ip_address" VARCHAR(45),
    "is_banned" BOOLEAN NOT NULL DEFAULT FALSE,
    "ban_reason" VARCHAR(255),
    "ban_until" TIMESTAMP,
    "total_login_count" INT NOT NULL DEFAULT 0
);

-- Create index for player_id lookups
CREATE INDEX IF NOT EXISTS "idx_player_accounts_player_id" 
    ON "player_accounts"("player_id");

-- Create index for platform queries
CREATE INDEX IF NOT EXISTS "idx_player_accounts_platform" 
    ON "player_accounts"("platform");

-- Create index for banned players
CREATE INDEX IF NOT EXISTS "idx_player_accounts_is_banned" 
    ON "player_accounts"("is_banned");

-- ----------------------------------------------------------------------------
-- Player Login Log Table
-- ----------------------------------------------------------------------------
-- Records all player login attempts for analytics and security
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS "player_login_log"
(
    "id" BIGSERIAL PRIMARY KEY,
    "player_id" BIGINT NOT NULL,
    "gateway_id" INT NOT NULL,
    "ip_address" VARCHAR(45) NOT NULL,
    "login_at" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "logout_at" TIMESTAMP,
    "login_result" INT NOT NULL DEFAULT 0,
    "device_info" VARCHAR(255),
    "client_version" VARCHAR(32),
    "session_duration_seconds" INT
);

-- Create index for player_id lookups
CREATE INDEX IF NOT EXISTS "idx_login_log_player" 
    ON "player_login_log"("player_id");

-- Create index for gateway_id queries
CREATE INDEX IF NOT EXISTS "idx_login_log_gateway" 
    ON "player_login_log"("gateway_id");

-- Create index for login_at time range queries
CREATE INDEX IF NOT EXISTS "idx_login_log_login_at" 
    ON "player_login_log"("login_at");

-- Create index for ip_address queries (for security analysis)
CREATE INDEX IF NOT EXISTS "idx_login_log_ip_address" 
    ON "player_login_log"("ip_address");

-- ----------------------------------------------------------------------------
-- Payment Orders Table
-- ----------------------------------------------------------------------------
-- Stores payment order information for in-app purchases
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS "payment_orders"
(
    "order_id" VARCHAR(64) NOT NULL PRIMARY KEY,
    "player_id" BIGINT NOT NULL,
    "product_id" VARCHAR(64) NOT NULL,
    "product_name" VARCHAR(128),
    "amount" NUMERIC(10, 2) NOT NULL,
    "currency" VARCHAR(8) NOT NULL DEFAULT 'CNY',
    "status" INT NOT NULL DEFAULT 0,
    "payment_method" VARCHAR(32),
    "third_party_order_id" VARCHAR(128),
    "created_at" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "completed_at" TIMESTAMP,
    "failed_at" TIMESTAMP,
    "failure_reason" VARCHAR(255),
    "items_received" BOOLEAN NOT NULL DEFAULT FALSE,
    "gateway_id" INT
);

-- Create index for player_id lookups
CREATE INDEX IF NOT EXISTS "idx_payment_player" 
    ON "payment_orders"("player_id");

-- Create index for status queries
CREATE INDEX IF NOT EXISTS "idx_payment_status" 
    ON "payment_orders"("status");

-- Create index for created_at time range queries
CREATE INDEX IF NOT EXISTS "idx_payment_created_at" 
    ON "payment_orders"("created_at");

-- Create index for third_party_order_id lookups
CREATE INDEX IF NOT EXISTS "idx_payment_third_party_order" 
    ON "payment_orders"("third_party_order_id");

-- ----------------------------------------------------------------------------
-- Player Items Table (Optional - for item tracking)
-- ----------------------------------------------------------------------------
-- Stores player's inventory items
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS "player_items"
(
    "id" BIGSERIAL PRIMARY KEY,
    "player_id" BIGINT NOT NULL,
    "item_id" INT NOT NULL,
    "item_name" VARCHAR(128),
    "item_count" INT NOT NULL DEFAULT 1,
    "item_quality" INT NOT NULL DEFAULT 1,
    "acquired_at" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "expires_at" TIMESTAMP,
    "is_equipped" BOOLEAN NOT NULL DEFAULT FALSE,
    "slot_index" INT
);

-- Create index for player_id lookups
CREATE INDEX IF NOT EXISTS "idx_player_items_player_id" 
    ON "player_items"("player_id");

-- Create index for item_id queries
CREATE INDEX IF NOT EXISTS "idx_player_items_item_id" 
    ON "player_items"("item_id");

-- Create unique constraint for equipped items per player
CREATE UNIQUE INDEX IF NOT EXISTS "idx_player_items_equipped_slot" 
    ON "player_items"("player_id", "slot_index") 
    WHERE "is_equipped" = TRUE;

-- ----------------------------------------------------------------------------
-- Player Statistics Table (Optional - for analytics)
-- ----------------------------------------------------------------------------
-- Stores player statistics for ranking and analytics
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS "player_statistics"
(
    "player_id" BIGINT NOT NULL PRIMARY KEY,
    "total_play_time_seconds" BIGINT NOT NULL DEFAULT 0,
    "total_matches_played" INT NOT NULL DEFAULT 0,
    "total_matches_won" INT NOT NULL DEFAULT 0,
    "total_matches_lost" INT NOT NULL DEFAULT 0,
    "rating" INT NOT NULL DEFAULT 1000,
    "max_rating" INT NOT NULL DEFAULT 1000,
    "total_kills" BIGINT NOT NULL DEFAULT 0,
    "total_deaths" BIGINT NOT NULL DEFAULT 0,
    "updated_at" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- Create index for rating queries (for leaderboard)
CREATE INDEX IF NOT EXISTS "idx_player_statistics_rating" 
    ON "player_statistics"("rating" DESC);

-- Create index for play time queries
CREATE INDEX IF NOT EXISTS "idx_player_statistics_play_time" 
    ON "player_statistics"("total_play_time_seconds" DESC);

-- ----------------------------------------------------------------------------
-- Room Statistics Table (Optional - for room analytics)
-- ----------------------------------------------------------------------------
-- Stores room statistics for analytics
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS "room_statistics"
(
    "room_id" BIGINT NOT NULL PRIMARY KEY,
    "room_type" INT NOT NULL DEFAULT 0,
    "total_players_joined" INT NOT NULL DEFAULT 0,
    "total_matches_played" INT NOT NULL DEFAULT 0,
    "created_at" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "last_active_at" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- Create index for room_type queries
CREATE INDEX IF NOT EXISTS "idx_room_statistics_room_type" 
    ON "room_statistics"("room_type");

-- Create index for last_active_at queries
CREATE INDEX IF NOT EXISTS "idx_room_statistics_last_active" 
    ON "room_statistics"("last_active_at" DESC);

-- ----------------------------------------------------------------------------
-- Audit Log Table (Optional - for security auditing)
-- ----------------------------------------------------------------------------
-- Records important system events for security auditing
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS "audit_log"
(
    "id" BIGSERIAL PRIMARY KEY,
    "event_type" VARCHAR(64) NOT NULL,
    "player_id" BIGINT,
    "operator_id" BIGINT,
    "event_data" JSONB,
    "ip_address" VARCHAR(45),
    "created_at" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- Create index for event_type queries
CREATE INDEX IF NOT EXISTS "idx_audit_log_event_type" 
    ON "audit_log"("event_type");

-- Create index for player_id queries
CREATE INDEX IF NOT EXISTS "idx_audit_log_player_id" 
    ON "audit_log"("player_id");

-- Create index for created_at time range queries
CREATE INDEX IF NOT EXISTS "idx_audit_log_created_at" 
    ON "audit_log"("created_at" DESC);

-- ============================================================================
-- End of Game Business Tables
-- ============================================================================
