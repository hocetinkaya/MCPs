using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using System.ComponentModel;

var builder = Host.CreateApplicationBuilder(args);
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();

[McpServerToolType]
public static class SqlServerTools
{
    private static readonly string? ConnectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING") 
        ?? $"Server={Environment.GetEnvironmentVariable("SERVER_NAME")};Database={Environment.GetEnvironmentVariable("DATABASE_NAME")};User Id={Environment.GetEnvironmentVariable("DB_USER")};Password={Environment.GetEnvironmentVariable("DB_PASSWORD")};TrustServerCertificate=true;";

    [McpServerTool]
    [Description("SQL Server veritabanındaki tabloları listeler")]
    public static async Task<string> ListTables()
    {
        try
        {
            using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
            await connection.OpenAsync();
            
            using var command = new Microsoft.Data.SqlClient.SqlCommand(@"
                SELECT TABLE_NAME 
                FROM INFORMATION_SCHEMA.TABLES 
                WHERE TABLE_TYPE = 'BASE TABLE'
                ORDER BY TABLE_NAME", connection);
            
            using var reader = await command.ExecuteReaderAsync();
            var tables = new List<string>();
            
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
            
            return $"Veritabanında {tables.Count} tablo bulundu:\n" + string.Join("\n", tables.Select(t => $"- {t}"));
        }
        catch (Exception ex)
        {
            return $"Hata: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Belirtilen tablodan veri çeker (SELECT sorgusu çalıştırır)")]
    public static async Task<string> QueryTable(
        [Description("Sorgulanacak tablo adı")] string tableName,
        [Description("WHERE koşulu (opsiyonel)")] string? whereClause = null,
        [Description("Döndürülecek maksimum kayıt sayısı")] int limit = 10)
    {
        try
        {
            using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
            await connection.OpenAsync();
            
            var query = $"SELECT TOP {limit} * FROM [{tableName}]";
            if (!string.IsNullOrWhiteSpace(whereClause))
            {
                query += $" WHERE {whereClause}";
            }
            
            using var command = new Microsoft.Data.SqlClient.SqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();
            
            var results = new List<Dictionary<string, object?>>();
            
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                results.Add(row);
            }
            
            return $"{tableName} tablosundan {results.Count} kayıt:\n" + 
                   System.Text.Json.JsonSerializer.Serialize(results, new System.Text.Json.JsonSerializerOptions 
                   { 
                       WriteIndented = true,
                       Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                   });
        }
        catch (Exception ex)
        {
            return $"Hata: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Tablo yapısını ve sütun bilgilerini gösterir")]
    public static async Task<string> DescribeTable([Description("İncelenecek tablo adı")] string tableName)
    {
        try
        {
            using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
            await connection.OpenAsync();
            
            using var command = new Microsoft.Data.SqlClient.SqlCommand(@"
                SELECT 
                    COLUMN_NAME,
                    DATA_TYPE,
                    IS_NULLABLE,
                    COLUMN_DEFAULT,
                    CHARACTER_MAXIMUM_LENGTH,
                    NUMERIC_PRECISION,
                    NUMERIC_SCALE
                FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_NAME = @tableName
                ORDER BY ORDINAL_POSITION", connection);
            
            command.Parameters.AddWithValue("@tableName", tableName);
            
            using var reader = await command.ExecuteReaderAsync();
            var columns = new List<string>();
            
            while (await reader.ReadAsync())
            {
                var columnName = reader.GetString(0);
                var dataType = reader.GetString(1);
                var isNullable = reader.GetString(2);
                var maxLength = reader.IsDBNull(4) ? "" : $"({reader.GetInt32(4)})";
                
                columns.Add($"- {columnName}: {dataType}{maxLength} {(isNullable == "YES" ? "NULL" : "NOT NULL")}");
            }
            
            return $"Tablo: {tableName}\n\nSütunlar:\n" + string.Join("\n", columns);
        }
        catch (Exception ex)
        {
            return $"Hata: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Doğal dille SQL sorgusu çalıştırır (SELECT, INSERT, UPDATE, DELETE)")]
    public static async Task<string> ExecuteQuery([Description("Çalıştırılacak SQL sorgusu")] string sqlQuery)
    {
        try
        {
            using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
            await connection.OpenAsync();
            
            var upperQuery = sqlQuery.Trim().ToUpper();
            
            if (upperQuery.StartsWith("SELECT"))
            {
                // SELECT sorguları için reader kullan
                using var command = new Microsoft.Data.SqlClient.SqlCommand(sqlQuery, connection);
                using var reader = await command.ExecuteReaderAsync();
                
                var results = new List<Dictionary<string, object?>>();
                
                while (await reader.ReadAsync())
                {
                    var row = new Dictionary<string, object?>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    }
                    results.Add(row);
                }
                
                return $"Sorgu sonucu ({results.Count} kayıt):\n" + 
                       System.Text.Json.JsonSerializer.Serialize(results, new System.Text.Json.JsonSerializerOptions 
                       { 
                           WriteIndented = true,
                           Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                       });
            }
            else if (upperQuery.StartsWith("INSERT") || upperQuery.StartsWith("UPDATE") || 
                     upperQuery.StartsWith("DELETE") || upperQuery.StartsWith("CREATE") || 
                     upperQuery.StartsWith("ALTER") || upperQuery.StartsWith("DROP"))
            {
                // DML/DDL sorguları için ExecuteNonQuery kullan
                using var command = new Microsoft.Data.SqlClient.SqlCommand(sqlQuery, connection);
                var rowsAffected = await command.ExecuteNonQueryAsync();
                
                return $"Sorgu başarıyla çalıştırıldı. {rowsAffected} satır etkilendi.";
            }
            else
            {
                return "Desteklenmeyen sorgu türü. SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, DROP sorgularını kullanabilirsiniz.";
            }
        }
        catch (Exception ex)
        {
            return $"Hata: {ex.Message}";
        }
    }

    // === MMOS Session Management Tools ===
    
    [McpServerTool]
    [Description("Yeni iş oturumu başlatır")]
    public static async Task<string> StartWorkSession(
        [Description("Oturum adı")] string sessionName,
        [Description("Oluşturan kişi/model")] string createdBy = "user")
    {
        try
        {
            using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
            await connection.OpenAsync();
            
            var query = @"
                INSERT INTO WorkSessions (SessionName, CreatedBy, Status, Priority)
                OUTPUT INSERTED.ID
                VALUES (@sessionName, @createdBy, 'active', 5)";
            
            using var command = new Microsoft.Data.SqlClient.SqlCommand(query, connection);
            command.Parameters.AddWithValue("@sessionName", sessionName);
            command.Parameters.AddWithValue("@createdBy", createdBy);
            
            var sessionId = await command.ExecuteScalarAsync();
            
            return $"✅ Yeni oturum oluşturuldu: '{sessionName}' (ID: {sessionId})";
        }
        catch (Exception ex)
        {
            return $"Hata: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Model instance'ı kaydeder ve sisteme ekler")]
    public static async Task<string> RegisterModelInstance(
        [Description("Instance adı")] string instanceName,
        [Description("Model türü (Claude, Gemini, GPT-4)")] string modelType,
        [Description("Yetenekler (JSON format)")] string capabilities = "[]",
        [Description("Paralel işlem kapasitesi")] int workerCapacity = 1)
    {
        try
        {
            using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
            await connection.OpenAsync();
            
            // Önce aynı isimde instance var mı kontrol et
            var checkQuery = "SELECT COUNT(*) FROM ModelInstances WHERE InstanceName = @instanceName";
            using var checkCommand = new Microsoft.Data.SqlClient.SqlCommand(checkQuery, connection);
            checkCommand.Parameters.AddWithValue("@instanceName", instanceName);
            var exists = (int)await checkCommand.ExecuteScalarAsync() > 0;
            
            if (exists)
            {
                // Update existing instance
                var updateQuery = @"
                    UPDATE ModelInstances 
                    SET ModelType = @modelType, Capabilities = @capabilities, 
                        WorkerCapacity = @workerCapacity, Status = 'idle', LastActive = GETDATE()
                    WHERE InstanceName = @instanceName";
                
                using var updateCommand = new Microsoft.Data.SqlClient.SqlCommand(updateQuery, connection);
                updateCommand.Parameters.AddWithValue("@instanceName", instanceName);
                updateCommand.Parameters.AddWithValue("@modelType", modelType);
                updateCommand.Parameters.AddWithValue("@capabilities", capabilities);
                updateCommand.Parameters.AddWithValue("@workerCapacity", workerCapacity);
                
                await updateCommand.ExecuteNonQueryAsync();
                return $"🔄 Model instance güncellendi: {instanceName} ({modelType})";
            }
            else
            {
                // Insert new instance
                var insertQuery = @"
                    INSERT INTO ModelInstances (InstanceName, ModelType, Status, Capabilities, WorkerCapacity, LastActive)
                    OUTPUT INSERTED.ID
                    VALUES (@instanceName, @modelType, 'idle', @capabilities, @workerCapacity, GETDATE())";
                
                using var insertCommand = new Microsoft.Data.SqlClient.SqlCommand(insertQuery, connection);
                insertCommand.Parameters.AddWithValue("@instanceName", instanceName);
                insertCommand.Parameters.AddWithValue("@modelType", modelType);
                insertCommand.Parameters.AddWithValue("@capabilities", capabilities);
                insertCommand.Parameters.AddWithValue("@workerCapacity", workerCapacity);
                
                var instanceId = await insertCommand.ExecuteScalarAsync();
                return $"✅ Yeni model instance kaydedildi: {instanceName} (ID: {instanceId}, Type: {modelType})";
            }
        }
        catch (Exception ex)
        {
            return $"Hata: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Task kuyruğuna yeni görev ekler")]
    public static async Task<string> AddTask(
        [Description("Oturum ID'si")] int sessionId,
        [Description("Görev türü (coding, planning, research, review)")] string taskType,
        [Description("Görev açıklaması")] string description,
        [Description("Bağımlılık task ID'leri (JSON array)")] string dependencies = "[]",
        [Description("Öncelik (1-10)")] int priority = 5)
    {
        try
        {
            using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
            await connection.OpenAsync();
            
            var query = @"
                INSERT INTO TaskQueue (SessionID, TaskType, Description, Dependencies, Status, Priority)
                OUTPUT INSERTED.ID
                VALUES (@sessionId, @taskType, @description, @dependencies, 'pending', @priority)";
            
            using var command = new Microsoft.Data.SqlClient.SqlCommand(query, connection);
            command.Parameters.AddWithValue("@sessionId", sessionId);
            command.Parameters.AddWithValue("@taskType", taskType);
            command.Parameters.AddWithValue("@description", description);
            command.Parameters.AddWithValue("@dependencies", dependencies);
            command.Parameters.AddWithValue("@priority", priority);
            
            var taskId = await command.ExecuteScalarAsync();
            
            return $"📋 Yeni görev eklendi: '{description}' (ID: {taskId}, Type: {taskType})";
        }
        catch (Exception ex)
        {
            return $"Hata: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Bekleyen görevleri getirir")]
    public static async Task<string> GetPendingTasks(
        [Description("Model türü filtresi (opsiyonel)")] string modelType = "",
        [Description("Instance adı filtresi (opsiyonel)")] string instanceName = "",
        [Description("Maksimum görev sayısı")] int limit = 10)
    {
        try
        {
            using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
            await connection.OpenAsync();
            
            var query = @"
                SELECT TOP (@limit) 
                    t.ID, t.TaskType, t.Description, t.Priority, t.CreatedAt,
                    s.SessionName, t.Status
                FROM TaskQueue t
                INNER JOIN WorkSessions s ON t.SessionID = s.ID
                WHERE t.Status = 'pending'
                ORDER BY t.Priority DESC, t.CreatedAt ASC";
            
            using var command = new Microsoft.Data.SqlClient.SqlCommand(query, connection);
            command.Parameters.AddWithValue("@limit", limit);
            
            using var reader = await command.ExecuteReaderAsync();
            var tasks = new List<Dictionary<string, object?>>();
            
            while (await reader.ReadAsync())
            {
                var task = new Dictionary<string, object?>
                {
                    ["ID"] = reader.GetInt32(0),
                    ["TaskType"] = reader.GetString(1),
                    ["Description"] = reader.GetString(2),
                    ["Priority"] = reader.GetInt32(3),
                    ["CreatedAt"] = reader.GetDateTime(4),
                    ["SessionName"] = reader.GetString(5),
                    ["Status"] = reader.GetString(6)
                };
                tasks.Add(task);
            }
            
            return $"⏳ Bekleyen görevler ({tasks.Count} adet):\n" + 
                   System.Text.Json.JsonSerializer.Serialize(tasks, new System.Text.Json.JsonSerializerOptions 
                   { 
                       WriteIndented = true,
                       Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                   });
        }
        catch (Exception ex)
        {
            return $"Hata: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Görev durumunu günceller ve tamamlar")]
    public static async Task<string> CompleteTask(
        [Description("Görev ID'si")] int taskId,
        [Description("Sonuç/çıktı")] string result,
        [Description("Worker instance ID'si")] int workerId = 0)
    {
        try
        {
            using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
            await connection.OpenAsync();
            
            var query = @"
                UPDATE TaskQueue 
                SET Status = 'completed', Result = @result, CompletedAt = GETDATE(),
                    AssignedModelID = CASE WHEN @workerId > 0 THEN @workerId ELSE AssignedModelID END
                WHERE ID = @taskId";
            
            using var command = new Microsoft.Data.SqlClient.SqlCommand(query, connection);
            command.Parameters.AddWithValue("@taskId", taskId);
            command.Parameters.AddWithValue("@result", result);
            command.Parameters.AddWithValue("@workerId", workerId);
            
            var rowsAffected = await command.ExecuteNonQueryAsync();
            
            if (rowsAffected > 0)
            {
                // Worker'ın task completed count'unu artır
                if (workerId > 0)
                {
                    var updateWorkerQuery = @"
                        UPDATE ModelInstances 
                        SET TasksCompleted = TasksCompleted + 1, Status = 'idle', LastActive = GETDATE()
                        WHERE ID = @workerId";
                    
                    using var updateWorkerCommand = new Microsoft.Data.SqlClient.SqlCommand(updateWorkerQuery, connection);
                    updateWorkerCommand.Parameters.AddWithValue("@workerId", workerId);
                    await updateWorkerCommand.ExecuteNonQueryAsync();
                }
                
                return $"✅ Görev tamamlandı: Task #{taskId}";
            }
            else
            {
                return $"❌ Görev bulunamadı: Task #{taskId}";
            }
        }
        catch (Exception ex)
        {
            return $"Hata: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Aktif worker instance'ların durumunu gösterir")]
    public static async Task<string> GetWorkerStatus()
    {
        try
        {
            using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
            await connection.OpenAsync();
            
            var query = @"
                SELECT InstanceName, ModelType, Status, WorkerCapacity, TasksCompleted, 
                       DATEDIFF(MINUTE, LastActive, GETDATE()) as MinutesInactive
                FROM ModelInstances
                WHERE Status != 'offline'
                ORDER BY LastActive DESC";
            
            using var command = new Microsoft.Data.SqlClient.SqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();
            
            var workers = new List<Dictionary<string, object?>>();
            
            while (await reader.ReadAsync())
            {
                var worker = new Dictionary<string, object?>
                {
                    ["InstanceName"] = reader.GetString(0),
                    ["ModelType"] = reader.GetString(1),
                    ["Status"] = reader.GetString(2),
                    ["WorkerCapacity"] = reader.GetInt32(3),
                    ["TasksCompleted"] = reader.GetInt32(4),
                    ["MinutesInactive"] = reader.GetInt32(5)
                };
                workers.Add(worker);
            }
            
            return $"👥 Worker Durumu ({workers.Count} aktif):\n" + 
                   System.Text.Json.JsonSerializer.Serialize(workers, new System.Text.Json.JsonSerializerOptions 
                   { 
                       WriteIndented = true,
                       Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                   });
        }
        catch (Exception ex)
        {
            return $"Hata: {ex.Message}";
        }
    }
}
