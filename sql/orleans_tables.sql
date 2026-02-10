-- ============================================================================
-- Orleans PostgreSQL Storage Tables
-- ============================================================================
-- This file contains the required tables for Orleans to work with PostgreSQL:
-- - OrleansMembershipTable: For Silo cluster membership management
-- - OrleansRemindersTable: For Orleans Reminder service
-- - OrleansStorage: For Grain state persistence
--
-- Reference: https://github.com/dotnet/orleans/tree/main/src/AdoNet/PostgreSQL
-- ============================================================================

-- ----------------------------------------------------------------------------
-- Orleans Membership Table
-- ----------------------------------------------------------------------------
-- Used by Orleans to track active Silos in the cluster
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS "OrleansMembershipTable"
(
    "DeploymentId" VARCHAR(150) NOT NULL,
    "Address" VARCHAR(45) NOT NULL,
    "Port" INT NOT NULL,
    "Generation" INT NOT NULL,
    "HostName" VARCHAR(150),
    "Status" INT NOT NULL,
    "ProxyPort" INT,
    "RoleName" VARCHAR(150),
    "HostName" VARCHAR(150),
    "Silos" BYTEA,
    "StartTime" TIMESTAMP,
    "IsActive" BOOLEAN,
    PRIMARY KEY ("DeploymentId", "Address", "Port", "Generation")
);

-- Create index for faster lookups
CREATE INDEX IF NOT EXISTS "idx_OrleansMembershipTable_DeploymentId" 
    ON "OrleansMembershipTable"("DeploymentId");

-- ----------------------------------------------------------------------------
-- Orleans Reminders Table
-- ----------------------------------------------------------------------------
-- Used by Orleans Reminder service to store reminder registrations
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS "OrleansRemindersTable"
(
    "ServiceId" VARCHAR(150) NOT NULL,
    "GrainId" VARCHAR(150) NOT NULL,
    "ReminderName" VARCHAR(150) NOT NULL,
    "StartTime" TIMESTAMP NOT NULL,
    "Period" BIGINT NOT NULL,
    "GrainHash" INT NOT NULL,
    "Status" INT NOT NULL,
    "Version" BIGINT NOT NULL,
    PRIMARY KEY ("ServiceId", "GrainId", "ReminderName")
);

-- Create index for faster lookups
CREATE INDEX IF NOT EXISTS "idx_OrleansRemindersTable_ServiceId" 
    ON "OrleansRemindersTable"("ServiceId");

-- ----------------------------------------------------------------------------
-- Orleans Storage Table
-- ----------------------------------------------------------------------------
-- Used for Grain state persistence
-- Supports JSON format for state storage
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS "OrleansStorage"
(
    "GrainType" VARCHAR(512) NOT NULL,
    "GrainId" VARCHAR(150) NOT NULL,
    "GrainState" BYTEA,
    "GrainStateVersion" BIGINT,
    "PayloadBinary" BYTEA,
    "PayloadXml" TEXT,
    "PayloadJson" TEXT,
    "DateTime" TIMESTAMP,
    "Tag" VARCHAR(150),
    "DeploymentId" VARCHAR(150),
    "ServiceId" VARCHAR(150),
    "StatName" VARCHAR(150),
    "StatValue" VARCHAR(150),
    "StatValueInt" BIGINT,
    "StatValueDouble" DOUBLE PRECISION,
    "StatValueDec" NUMERIC(20, 4),
    "StatValueLong" BIGINT,
    "StatValueString" VARCHAR(150),
    "StatValueBinary" BYTEA,
    "StatValueXml" TEXT,
    "StatValueJson" TEXT,
    PRIMARY KEY ("GrainType", "GrainId")
);

-- Create index for faster lookups
CREATE INDEX IF NOT EXISTS "idx_OrleansStorage_GrainType" 
    ON "OrleansStorage"("GrainType");

-- Create index for deployment-based queries
CREATE INDEX IF NOT EXISTS "idx_OrleansStorage_DeploymentId" 
    ON "OrleansStorage"("DeploymentId");

-- ----------------------------------------------------------------------------
-- Orleans Query Table (Optional - for streaming queries)
-- ----------------------------------------------------------------------------
-- Used for Orleans streaming query functionality
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS "OrleansQuery"
(
    "StreamProviderName" VARCHAR(150) NOT NULL,
    "StreamId" VARCHAR(150) NOT NULL,
    "GrainHash" INT NOT NULL,
    "Data" BYTEA,
    "Version" BIGINT,
    "Timestamp" TIMESTAMP,
    PRIMARY KEY ("StreamProviderName", "StreamId", "GrainHash")
);

-- Create index for faster lookups
CREATE INDEX IF NOT EXISTS "idx_OrleansQuery_StreamProviderName" 
    ON "OrleansQuery"("StreamProviderName");

-- ----------------------------------------------------------------------------
-- Orleans Statistics Table (Optional - for monitoring)
-- ----------------------------------------------------------------------------
-- Used for Orleans statistics collection
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS "OrleansStatistics"
(
    "Id" BIGSERIAL PRIMARY KEY,
    "OrleansStatisticsId" VARCHAR(150),
    "DeploymentId" VARCHAR(150),
    "StatisticName" VARCHAR(150),
    "StatisticValue" VARCHAR(150),
    "StatisticValueInt" BIGINT,
    "StatisticValueDouble" DOUBLE PRECISION,
    "StatisticValueDec" NUMERIC(20, 4),
    "StatisticValueLong" BIGINT,
    "StatisticValueString" VARCHAR(150),
    "StatisticValueBinary" BYTEA,
    "StatisticValueXml" TEXT,
    "StatisticValueJson" TEXT,
    "DateTime" TIMESTAMP
);

-- Create index for faster lookups
CREATE INDEX IF NOT EXISTS "idx_OrleansStatistics_DeploymentId" 
    ON "OrleansStatistics"("DeploymentId");

CREATE INDEX IF NOT EXISTS "idx_OrleansStatistics_DateTime" 
    ON "OrleansStatistics"("DateTime");

-- ============================================================================
-- End of Orleans PostgreSQL Tables
-- ============================================================================
