-- MMOS Database Schema for MCP_DB
-- Multi-Model Orchestration System Tables

-- 1. Model Instances Table - Kayıtlı worker ve admin instance'lar
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ModelInstances' AND xtype='U')
CREATE TABLE ModelInstances (
    ID INT IDENTITY(1,1) PRIMARY KEY,
    InstanceName NVARCHAR(100) NOT NULL UNIQUE,
    ModelType NVARCHAR(50) NOT NULL, -- 'Claude', 'Gemini', 'GPT-4', etc.
    Status NVARCHAR(20) DEFAULT 'offline', -- 'idle', 'busy', 'offline', 'error'
    Capabilities NVARCHAR(MAX) DEFAULT '[]', -- JSON array of capabilities
    WorkerCapacity INT DEFAULT 1, -- Parallel task capacity
    TasksCompleted INT DEFAULT 0, -- Statistics
    LastActive DATETIME2 DEFAULT GETDATE(),
    CreatedAt DATETIME2 DEFAULT GETDATE()
);

-- 2. Work Sessions Table - Proje oturumları ve koordinasyon  
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='WorkSessions' AND xtype='U')
CREATE TABLE WorkSessions (
    ID INT IDENTITY(1,1) PRIMARY KEY,
    SessionName NVARCHAR(200) NOT NULL,
    CreatedBy NVARCHAR(100) NOT NULL, -- User or admin instance name
    Status NVARCHAR(20) DEFAULT 'active', -- 'active', 'paused', 'completed', 'cancelled'
    Priority INT DEFAULT 5, -- 1-10 priority scale
    CreatedAt DATETIME2 DEFAULT GETDATE(),
    CompletedAt DATETIME2 NULL,
    Description NVARCHAR(MAX) NULL
);

-- 3. Task Queue Table - Task yönetimi ve workflow
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='TaskQueue' AND xtype='U')  
CREATE TABLE TaskQueue (
    ID INT IDENTITY(1,1) PRIMARY KEY,
    SessionID INT NOT NULL,
    TaskType NVARCHAR(50) NOT NULL, -- 'coding', 'planning', 'research', 'review', 'testing'
    Description NVARCHAR(MAX) NOT NULL,
    Dependencies NVARCHAR(MAX) DEFAULT '[]', -- JSON array of dependent task IDs
    Status NVARCHAR(20) DEFAULT 'pending', -- 'pending', 'assigned', 'in_progress', 'completed', 'failed'
    Priority INT DEFAULT 5, -- 1-10 priority scale
    AssignedModelID INT NULL, -- Foreign key to ModelInstances
    Result NVARCHAR(MAX) NULL, -- Task execution result/output
    CreatedAt DATETIME2 DEFAULT GETDATE(),
    AssignedAt DATETIME2 NULL,
    CompletedAt DATETIME2 NULL,
    EstimatedDuration INT NULL, -- In minutes
    
    CONSTRAINT FK_TaskQueue_Session FOREIGN KEY (SessionID) REFERENCES WorkSessions(ID)
);

-- 4. Worker Pool Table - Load balancing ve worker management
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='WorkerPool' AND xtype='U')
CREATE TABLE WorkerPool (
    ID INT IDENTITY(1,1) PRIMARY KEY,
    ModelInstanceID INT NOT NULL,
    Status NVARCHAR(20) DEFAULT 'available', -- 'available', 'busy', 'maintenance'
    CurrentTaskID INT NULL, -- Currently assigned task
    LastTaskCompletedAt DATETIME2 NULL,
    AverageTaskDuration INT DEFAULT 0, -- In minutes
    SuccessRate DECIMAL(5,2) DEFAULT 100.00, -- Success percentage
    LoadScore INT DEFAULT 0, -- Current workload score
    
    CONSTRAINT FK_WorkerPool_Model FOREIGN KEY (ModelInstanceID) REFERENCES ModelInstances(ID)
);

-- 5. Task Status Log Table - Audit trail ve debugging
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='TaskStatusLog' AND xtype='U')
CREATE TABLE TaskStatusLog (
    ID INT IDENTITY(1,1) PRIMARY KEY,
    TaskID INT NOT NULL,
    PreviousStatus NVARCHAR(20),
    NewStatus NVARCHAR(20) NOT NULL,
    ChangedBy NVARCHAR(100), -- Instance name that made the change
    ChangeReason NVARCHAR(500),
    CreatedAt DATETIME2 DEFAULT GETDATE(),
    
    -- CONSTRAINT FK_TaskStatusLog_Task FOREIGN KEY (TaskID) REFERENCES TaskQueue(ID) -- Will add after TaskQueue table exists
);

-- 6. Inter-Worker Messages Table - Cross-worker communication
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='InterWorkerMessages' AND xtype='U')
CREATE TABLE InterWorkerMessages (
    ID INT IDENTITY(1,1) PRIMARY KEY,
    FromWorkerID INT NOT NULL,
    ToWorkerID INT NOT NULL, -- Can be NULL for broadcast messages
    ProjectID INT NULL, -- Related to WorkSessions
    MessageType NVARCHAR(50) NOT NULL, -- 'task_result', 'resource_share', 'coordination', 'status_update'
    Content NVARCHAR(MAX) NOT NULL, -- JSON message payload
    Status NVARCHAR(20) DEFAULT 'sent', -- 'sent', 'delivered', 'processed'
    CreatedAt DATETIME2 DEFAULT GETDATE(),
    ProcessedAt DATETIME2 NULL,
    
    -- Foreign keys will be added after all tables are created
    -- CONSTRAINT FK_InterWorkerMessages_From FOREIGN KEY (FromWorkerID) REFERENCES ModelInstances(ID),
    -- CONSTRAINT FK_InterWorkerMessages_To FOREIGN KEY (ToWorkerID) REFERENCES ModelInstances(ID), 
    -- CONSTRAINT FK_InterWorkerMessages_Project FOREIGN KEY (ProjectID) REFERENCES WorkSessions(ID)
);

-- 7. System Configuration Table - Runtime settings
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='SystemConfiguration' AND xtype='U')
CREATE TABLE SystemConfiguration (
    ID INT IDENTITY(1,1) PRIMARY KEY,
    ConfigKey NVARCHAR(100) NOT NULL UNIQUE,
    ConfigValue NVARCHAR(MAX) NOT NULL,
    ConfigType NVARCHAR(50) NOT NULL, -- 'string', 'int', 'bool', 'json'
    Description NVARCHAR(500),
    UpdatedBy NVARCHAR(100),
    UpdatedAt DATETIME2 DEFAULT GETDATE()
);

-- Initial Configuration Values
INSERT INTO SystemConfiguration (ConfigKey, ConfigValue, ConfigType, Description) VALUES
('max_concurrent_tasks_per_worker', '3', 'int', 'Maximum concurrent tasks per worker instance'),
('task_timeout_minutes', '60', 'int', 'Default task timeout in minutes'),
('worker_heartbeat_interval', '5', 'int', 'Worker heartbeat check interval in minutes'),
('auto_assignment_enabled', 'true', 'bool', 'Enable automatic task assignment'),
('system_admin_instance', 'AdminClaude', 'string', 'Primary admin instance name');

-- Indexes for performance
CREATE INDEX IX_TaskQueue_Status ON TaskQueue(Status);
CREATE INDEX IX_TaskQueue_SessionID ON TaskQueue(SessionID);
CREATE INDEX IX_TaskQueue_AssignedModelID ON TaskQueue(AssignedModelID);
CREATE INDEX IX_ModelInstances_Status ON ModelInstances(Status);
CREATE INDEX IX_WorkerPool_Status ON WorkerPool(Status);
CREATE INDEX IX_InterWorkerMessages_Status ON InterWorkerMessages(Status);

-- Sample Data for Testing
INSERT INTO ModelInstances (InstanceName, ModelType, Status, Capabilities) VALUES
('AdminClaude', 'Claude', 'idle', '["orchestration", "planning", "coordination", "admin"]'),
('Worker-Claude-1', 'Claude', 'offline', '["general", "coding", "analysis", "documentation"]'),
('Worker-Claude-2', 'Claude', 'offline', '["general", "planning", "research", "testing"]');

INSERT INTO WorkSessions (SessionName, CreatedBy, Description) VALUES
('MMOS System Test', 'AdminClaude', 'Initial system testing and validation'),
('Multi-Worker Demo', 'AdminClaude', 'Demonstration of multi-worker coordination');

-- Add Foreign Key Constraints after all tables are created
IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_TaskQueue_Model')
    ALTER TABLE TaskQueue ADD CONSTRAINT FK_TaskQueue_Model FOREIGN KEY (AssignedModelID) REFERENCES ModelInstances(ID);

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_WorkerPool_Task')  
    ALTER TABLE WorkerPool ADD CONSTRAINT FK_WorkerPool_Task FOREIGN KEY (CurrentTaskID) REFERENCES TaskQueue(ID);

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_TaskStatusLog_Task')
    ALTER TABLE TaskStatusLog ADD CONSTRAINT FK_TaskStatusLog_Task FOREIGN KEY (TaskID) REFERENCES TaskQueue(ID);

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_InterWorkerMessages_From')
    ALTER TABLE InterWorkerMessages ADD CONSTRAINT FK_InterWorkerMessages_From FOREIGN KEY (FromWorkerID) REFERENCES ModelInstances(ID);

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_InterWorkerMessages_To')
    ALTER TABLE InterWorkerMessages ADD CONSTRAINT FK_InterWorkerMessages_To FOREIGN KEY (ToWorkerID) REFERENCES ModelInstances(ID);

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_InterWorkerMessages_Project')
    ALTER TABLE InterWorkerMessages ADD CONSTRAINT FK_InterWorkerMessages_Project FOREIGN KEY (ProjectID) REFERENCES WorkSessions(ID);

-- Views for easy querying
IF NOT EXISTS (SELECT * FROM sys.views WHERE name = 'ActiveWorkers')
EXEC('CREATE VIEW ActiveWorkers AS
SELECT 
    mi.ID, mi.InstanceName, mi.ModelType, mi.Status,
    wp.LoadScore, wp.AverageTaskDuration, wp.SuccessRate,
    DATEDIFF(MINUTE, mi.LastActive, GETDATE()) as MinutesInactive
FROM ModelInstances mi
LEFT JOIN WorkerPool wp ON mi.ID = wp.ModelInstanceID
WHERE mi.Status IN (''idle'', ''busy'')');

IF NOT EXISTS (SELECT * FROM sys.views WHERE name = 'TaskQueueSummary')
EXEC('CREATE VIEW TaskQueueSummary AS
SELECT 
    ws.SessionName,
    COUNT(*) as TotalTasks,
    SUM(CASE WHEN tq.Status = ''pending'' THEN 1 ELSE 0 END) as PendingTasks,
    SUM(CASE WHEN tq.Status = ''in_progress'' THEN 1 ELSE 0 END) as InProgressTasks,
    SUM(CASE WHEN tq.Status = ''completed'' THEN 1 ELSE 0 END) as CompletedTasks,
    AVG(tq.Priority) as AvgPriority
FROM WorkSessions ws
LEFT JOIN TaskQueue tq ON ws.ID = tq.SessionID
GROUP BY ws.ID, ws.SessionName');