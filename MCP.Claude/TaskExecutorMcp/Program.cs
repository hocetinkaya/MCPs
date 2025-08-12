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
public static class TaskExecutorTools
{
    private static readonly string? ConnectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING") 
        ?? $"Server={Environment.GetEnvironmentVariable("SERVER_NAME")};Database={Environment.GetEnvironmentVariable("DATABASE_NAME")};User Id={Environment.GetEnvironmentVariable("DB_USER")};Password={Environment.GetEnvironmentVariable("DB_PASSWORD")};TrustServerCertificate=true;";
        
    private static readonly string? WorkerInstanceName = Environment.GetEnvironmentVariable("WORKER_INSTANCE_NAME") ?? "UnknownWorker";
    private static readonly string? WorkerType = Environment.GetEnvironmentVariable("WORKER_TYPE") ?? "Claude";

    // === WORKER REGISTRATION TOOLS ===

    [McpServerTool]
    [Description("Bu worker instance'ı sisteme kaydeder")]
    public static async Task<string> RegisterWorker(
        [Description("Worker capabilities (JSON array formatında)")] string capabilities = "[\"general\", \"coding\", \"analysis\"]",
        [Description("Paralel işlem kapasitesi")] int workerCapacity = 1)
    {
        try
        {
            using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
            await connection.OpenAsync();
            
            // Önce bu worker zaten kayıtlı mı kontrol et
            var checkQuery = "SELECT ID, Status FROM ModelInstances WHERE InstanceName = @instanceName";
            using var checkCommand = new Microsoft.Data.SqlClient.SqlCommand(checkQuery, connection);
            checkCommand.Parameters.AddWithValue("@instanceName", WorkerInstanceName);
            
            using var checkReader = await checkCommand.ExecuteReaderAsync();
            
            if (await checkReader.ReadAsync())
            {
                // Mevcut kaydı güncelle
                var existingId = checkReader.GetInt32(0);
                var currentStatus = checkReader.GetString(1);
                await checkReader.CloseAsync();
                
                var updateQuery = @"
                    UPDATE ModelInstances 
                    SET Status = 'idle', Capabilities = @capabilities, 
                        WorkerCapacity = @workerCapacity, LastActive = GETDATE()
                    WHERE InstanceName = @instanceName";
                
                using var updateCommand = new Microsoft.Data.SqlClient.SqlCommand(updateQuery, connection);
                updateCommand.Parameters.AddWithValue("@instanceName", WorkerInstanceName);
                updateCommand.Parameters.AddWithValue("@capabilities", capabilities);
                updateCommand.Parameters.AddWithValue("@workerCapacity", workerCapacity);
                
                await updateCommand.ExecuteNonQueryAsync();
                
                // WorkerPool tablosuna da ekle/güncelle
                await EnsureWorkerInPool(connection, existingId);
                
                return $"🔄 Worker güncellendi: {WorkerInstanceName} (ID: {existingId})\n" +
                       $"🏷️ Type: {WorkerType}\n" +
                       $"⚡ Capabilities: {capabilities}\n" +
                       $"📊 Capacity: {workerCapacity} concurrent tasks\n" +
                       $"✅ Status: IDLE - görevler için hazır";
            }
            else
            {
                // Yeni worker kaydı oluştur
                await checkReader.CloseAsync();
                
                var insertQuery = @"
                    INSERT INTO ModelInstances (InstanceName, ModelType, Status, Capabilities, WorkerCapacity, LastActive)
                    OUTPUT INSERTED.ID
                    VALUES (@instanceName, @workerType, 'idle', @capabilities, @workerCapacity, GETDATE())";
                
                using var insertCommand = new Microsoft.Data.SqlClient.SqlCommand(insertQuery, connection);
                insertCommand.Parameters.AddWithValue("@instanceName", WorkerInstanceName);
                insertCommand.Parameters.AddWithValue("@workerType", WorkerType);
                insertCommand.Parameters.AddWithValue("@capabilities", capabilities);
                insertCommand.Parameters.AddWithValue("@workerCapacity", workerCapacity);
                
                var newWorkerId = await insertCommand.ExecuteScalarAsync();
                
                // WorkerPool tablosuna da ekle
                await EnsureWorkerInPool(connection, (int)newWorkerId);
                
                return $"✅ Yeni worker kaydedildi: {WorkerInstanceName} (ID: {newWorkerId})\n" +
                       $"🏷️ Type: {WorkerType}\n" +
                       $"⚡ Capabilities: {capabilities}\n" +
                       $"📊 Capacity: {workerCapacity} concurrent tasks\n" +
                       $"🚀 Status: IDLE - sistem tarafından keşfedilebilir";
            }
        }
        catch (Exception ex)
        {
            return $"❌ Worker registration hatası: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Atanmış task'ları kontrol eder ve alır")]
    public static async Task<string> PollForTasks(
        [Description("Task türü filtresi (opsiyonel)")] string taskTypeFilter = "",
        [Description("Maksimum task sayısı")] int maxTasks = 1)
    {
        try
        {
            using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
            await connection.OpenAsync();
            
            // Önce worker ID'sini al
            var workerIdQuery = "SELECT ID FROM ModelInstances WHERE InstanceName = @instanceName";
            using var workerIdCommand = new Microsoft.Data.SqlClient.SqlCommand(workerIdQuery, connection);
            workerIdCommand.Parameters.AddWithValue("@instanceName", WorkerInstanceName);
            
            var workerIdResult = await workerIdCommand.ExecuteScalarAsync();
            if (workerIdResult == null)
            {
                return "❌ Worker kayıtlı değil. Önce RegisterWorker kullanın.";
            }
            
            var workerId = (int)workerIdResult;
            
            // Bu worker'a atanmış task'ları bul
            var taskFilter = string.IsNullOrEmpty(taskTypeFilter) ? "" : "AND tq.TaskType = @taskType";
            
            var taskQuery = $@"
                SELECT TOP ({maxTasks}) tq.ID, tq.TaskType, tq.Description, tq.Priority, 
                       tq.EstimatedDuration, ws.SessionName, tq.CreatedAt
                FROM TaskQueue tq
                INNER JOIN WorkSessions ws ON tq.SessionID = ws.ID
                WHERE tq.AssignedModelID = @workerId 
                  AND tq.Status = 'assigned'
                  {taskFilter}
                ORDER BY tq.Priority DESC, tq.CreatedAt ASC";
            
            using var taskCommand = new Microsoft.Data.SqlClient.SqlCommand(taskQuery, connection);
            taskCommand.Parameters.AddWithValue("@workerId", workerId);
            
            if (!string.IsNullOrEmpty(taskTypeFilter))
                taskCommand.Parameters.AddWithValue("@taskType", taskTypeFilter);
            
            using var taskReader = await taskCommand.ExecuteReaderAsync();
            var tasks = new List<string>();
            
            while (await taskReader.ReadAsync())
            {
                var taskId = taskReader.GetInt32(0);
                var taskType = taskReader.GetString(1);
                var description = taskReader.GetString(2);
                var priority = taskReader.GetInt32(3);
                var estimatedDuration = taskReader.IsDBNull(4) ? 0 : taskReader.GetInt32(4);
                var sessionName = taskReader.GetString(5);
                var createdAt = taskReader.GetDateTime(6);
                
                var taskInfo = $"📋 Task #{taskId} ({taskType.ToUpper()})\n" +
                              $"   📝 Açıklama: {description}\n" +
                              $"   📊 Proje: {sessionName}\n" +
                              $"   ⭐ Öncelik: {priority}/10\n" +
                              $"   ⏱️ Tahmini süre: {estimatedDuration} dakika\n" +
                              $"   📅 Oluşturulma: {createdAt:dd.MM.yyyy HH:mm}";
                              
                tasks.Add(taskInfo);
            }
            
            if (tasks.Count == 0)
            {
                return $"📭 {WorkerInstanceName} için atanmış task bulunamadı.\n" +
                       "🔄 Otomatik task assignment için Admin Claude'u bekleyin.";
            }
            
            return $"📬 {WorkerInstanceName} için {tasks.Count} adet atanmış task:\n\n" +
                   string.Join("\n\n", tasks) + "\n\n" +
                   "💡 Bir task'ı kabul etmek için AcceptTask komutunu kullanın.";
        }
        catch (Exception ex)
        {
            return $"❌ Task polling hatası: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Belirli bir task'ı kabul eder ve işleme başlar")]
    public static async Task<string> AcceptTask([Description("Task ID'si")] int taskId)
    {
        try
        {
            using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
            await connection.OpenAsync();
            
            // Worker ID'sini al
            var workerIdQuery = "SELECT ID FROM ModelInstances WHERE InstanceName = @instanceName";
            using var workerIdCommand = new Microsoft.Data.SqlClient.SqlCommand(workerIdQuery, connection);
            workerIdCommand.Parameters.AddWithValue("@instanceName", WorkerInstanceName);
            
            var workerIdResult = await workerIdCommand.ExecuteScalarAsync();
            if (workerIdResult == null)
            {
                return "❌ Worker kayıtlı değil. Önce RegisterWorker kullanın.";
            }
            
            var workerId = (int)workerIdResult;
            
            // Task'ın durumunu 'in_progress' yap
            var acceptTaskQuery = @"
                UPDATE TaskQueue 
                SET Status = 'in_progress'
                WHERE ID = @taskId AND AssignedModelID = @workerId AND Status = 'assigned'";
                
            using var acceptCommand = new Microsoft.Data.SqlClient.SqlCommand(acceptTaskQuery, connection);
            acceptCommand.Parameters.AddWithValue("@taskId", taskId);
            acceptCommand.Parameters.AddWithValue("@workerId", workerId);
            
            var rowsAffected = await acceptCommand.ExecuteNonQueryAsync();
            
            if (rowsAffected == 0)
            {
                return $"❌ Task #{taskId} kabul edilemedi. Task bulunamadı veya zaten işlenmekte.";
            }
            
            // Worker status'unu güncelle
            var updateWorkerQuery = @"
                UPDATE ModelInstances 
                SET Status = 'busy', LastActive = GETDATE()
                WHERE ID = @workerId;
                
                UPDATE WorkerPool 
                SET Status = 'busy', CurrentTaskID = @taskId 
                WHERE ModelInstanceID = @workerId";
                
            using var updateCommand = new Microsoft.Data.SqlClient.SqlCommand(updateWorkerQuery, connection);
            updateCommand.Parameters.AddWithValue("@workerId", workerId);
            updateCommand.Parameters.AddWithValue("@taskId", taskId);
            
            await updateCommand.ExecuteNonQueryAsync();
            
            // Task detaylarını al
            var taskDetailQuery = @"
                SELECT tq.Description, tq.TaskType, ws.SessionName
                FROM TaskQueue tq
                INNER JOIN WorkSessions ws ON tq.SessionID = ws.ID
                WHERE tq.ID = @taskId";
                
            using var detailCommand = new Microsoft.Data.SqlClient.SqlCommand(taskDetailQuery, connection);
            detailCommand.Parameters.AddWithValue("@taskId", taskId);
            
            using var detailReader = await detailCommand.ExecuteReaderAsync();
            
            if (await detailReader.ReadAsync())
            {
                var description = detailReader.GetString(0);
                var taskType = detailReader.GetString(1);
                var sessionName = detailReader.GetString(2);
                
                return $"✅ Task #{taskId} başarıyla kabul edildi!\n\n" +
                       $"📋 Task Detayları:\n" +
                       $"   🏷️ Tür: {taskType.ToUpper()}\n" +
                       $"   📝 Açıklama: {description}\n" +
                       $"   📊 Proje: {sessionName}\n\n" +
                       $"🚀 {WorkerInstanceName} artık bu task üzerinde çalışıyor.\n" +
                       $"💡 Task tamamlandığında ReportProgress ve CompleteTask kullanın.";
            }
            
            return $"✅ Task #{taskId} kabul edildi ancak detaylar alınamadı.";
        }
        catch (Exception ex)
        {
            return $"❌ Task acceptance hatası: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Task üzerindeki ilerleme durumunu raporlar")]
    public static async Task<string> ReportProgress(
        [Description("Task ID'si")] int taskId,
        [Description("İlerleme yüzdesi (0-100)")] int progressPercent,
        [Description("İlerleme açıklaması")] string progressNote)
    {
        try
        {
            using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
            await connection.OpenAsync();
            
            // Worker ID'sini al
            var workerIdQuery = "SELECT ID FROM ModelInstances WHERE InstanceName = @instanceName";
            using var workerIdCommand = new Microsoft.Data.SqlClient.SqlCommand(workerIdQuery, connection);
            workerIdCommand.Parameters.AddWithValue("@instanceName", WorkerInstanceName);
            
            var workerIdResult = await workerIdCommand.ExecuteScalarAsync();
            if (workerIdResult == null)
            {
                return "❌ Worker kayıtlı değil.";
            }
            
            var workerId = (int)workerIdResult;
            
            // Progress log'u TaskStatusLog tablosuna ekle
            var progressLogQuery = @"
                INSERT INTO TaskStatusLog (TaskID, PreviousStatus, NewStatus, ChangedBy, ChangeReason)
                VALUES (@taskId, 'in_progress', 'in_progress', @workerName, @progressNote)";
                
            using var logCommand = new Microsoft.Data.SqlClient.SqlCommand(progressLogQuery, connection);
            logCommand.Parameters.AddWithValue("@taskId", taskId);
            logCommand.Parameters.AddWithValue("@workerName", WorkerInstanceName);
            logCommand.Parameters.AddWithValue("@progressNote", $"Progress: {progressPercent}% - {progressNote}");
            
            await logCommand.ExecuteNonQueryAsync();
            
            // Worker'ın LastActive zamanını güncelle
            var updateWorkerQuery = "UPDATE ModelInstances SET LastActive = GETDATE() WHERE ID = @workerId";
            using var updateCommand = new Microsoft.Data.SqlClient.SqlCommand(updateWorkerQuery, connection);
            updateCommand.Parameters.AddWithValue("@workerId", workerId);
            await updateCommand.ExecuteNonQueryAsync();
            
            return $"📊 Progress raporu kaydedildi!\n" +
                   $"📋 Task #{taskId}\n" +
                   $"⚡ İlerleme: {progressPercent}%\n" +
                   $"📝 Not: {progressNote}\n" +
                   $"🕐 Zaman: {DateTime.Now:dd.MM.yyyy HH:mm:ss}\n\n" +
                   $"✅ Progress Admin Claude tarafından görülebilir.";
        }
        catch (Exception ex)
        {
            return $"❌ Progress reporting hatası: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Task'ı tamamlar ve sonucu raporlar")]
    public static async Task<string> CompleteTask(
        [Description("Task ID'si")] int taskId,
        [Description("Task sonucu ve çıktı")] string taskResult,
        [Description("Başarı durumu")] bool isSuccessful = true)
    {
        try
        {
            using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
            await connection.OpenAsync();
            
            // Worker ID'sini al
            var workerIdQuery = "SELECT ID FROM ModelInstances WHERE InstanceName = @instanceName";
            using var workerIdCommand = new Microsoft.Data.SqlClient.SqlCommand(workerIdQuery, connection);
            workerIdCommand.Parameters.AddWithValue("@instanceName", WorkerInstanceName);
            
            var workerIdResult = await workerIdCommand.ExecuteScalarAsync();
            if (workerIdResult == null)
            {
                return "❌ Worker kayıtlı değil.";
            }
            
            var workerId = (int)workerIdResult;
            
            // Task'ı tamamla
            var completeTaskQuery = @"
                UPDATE TaskQueue 
                SET Status = @status, Result = @result, CompletedAt = GETDATE()
                WHERE ID = @taskId AND AssignedModelID = @workerId";
                
            using var completeCommand = new Microsoft.Data.SqlClient.SqlCommand(completeTaskQuery, connection);
            completeCommand.Parameters.AddWithValue("@status", isSuccessful ? "completed" : "failed");
            completeCommand.Parameters.AddWithValue("@result", taskResult);
            completeCommand.Parameters.AddWithValue("@taskId", taskId);
            completeCommand.Parameters.AddWithValue("@workerId", workerId);
            
            var rowsAffected = await completeCommand.ExecuteNonQueryAsync();
            
            if (rowsAffected == 0)
            {
                return $"❌ Task #{taskId} tamamlanamadı. Task bulunamadı veya zaten tamamlanmış.";
            }
            
            // Worker statistics'i güncelle
            var updateWorkerQuery = @"
                UPDATE ModelInstances 
                SET Status = 'idle', TasksCompleted = TasksCompleted + 1, LastActive = GETDATE()
                WHERE ID = @workerId;
                
                UPDATE WorkerPool 
                SET Status = 'available', CurrentTaskID = NULL, LastTaskCompletedAt = GETDATE()
                WHERE ModelInstanceID = @workerId";
                
            using var updateCommand = new Microsoft.Data.SqlClient.SqlCommand(updateWorkerQuery, connection);
            updateCommand.Parameters.AddWithValue("@workerId", workerId);
            await updateCommand.ExecuteNonQueryAsync();
            
            // Completion log'u ekle
            var logQuery = @"
                INSERT INTO TaskStatusLog (TaskID, PreviousStatus, NewStatus, ChangedBy, ChangeReason)
                VALUES (@taskId, 'in_progress', @newStatus, @workerName, @reason)";
                
            using var logCommand = new Microsoft.Data.SqlClient.SqlCommand(logQuery, connection);
            logCommand.Parameters.AddWithValue("@taskId", taskId);
            logCommand.Parameters.AddWithValue("@newStatus", isSuccessful ? "completed" : "failed");
            logCommand.Parameters.AddWithValue("@workerName", WorkerInstanceName);
            logCommand.Parameters.AddWithValue("@reason", $"Task {(isSuccessful ? "successfully completed" : "failed")}: {taskResult.Substring(0, Math.Min(taskResult.Length, 200))}");
            
            await logCommand.ExecuteNonQueryAsync();
            
            string statusIcon = isSuccessful ? "✅" : "❌";
            string statusText = isSuccessful ? "BAŞARIYLA TAMAMLANDI" : "BAŞARISIZ";
            
            return $"{statusIcon} Task #{taskId} {statusText}!\n\n" +
                   $"📊 Sonuç Özeti:\n" +
                   $"   🏷️ Durum: {statusText}\n" +
                   $"   📝 Çıktı: {taskResult}\n" +
                   $"   👤 Worker: {WorkerInstanceName}\n" +
                   $"   🕐 Tamamlanma: {DateTime.Now:dd.MM.yyyy HH:mm:ss}\n\n" +
                   $"🚀 Worker artık yeni task'lar için uygun durumda.";
        }
        catch (Exception ex)
        {
            return $"❌ Task completion hatası: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Worker'ın mevcut durumunu ve istatistiklerini gösterir")]
    public static async Task<string> GetWorkerStatus()
    {
        try
        {
            using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
            await connection.OpenAsync();
            
            var statusQuery = @"
                SELECT mi.ID, mi.Status, mi.TasksCompleted, mi.LastActive,
                       wp.Status as PoolStatus, wp.CurrentTaskID, wp.LastTaskCompletedAt,
                       tq.Description as CurrentTaskDescription
                FROM ModelInstances mi
                LEFT JOIN WorkerPool wp ON mi.ID = wp.ModelInstanceID
                LEFT JOIN TaskQueue tq ON wp.CurrentTaskID = tq.ID
                WHERE mi.InstanceName = @instanceName";
                
            using var command = new Microsoft.Data.SqlClient.SqlCommand(statusQuery, connection);
            command.Parameters.AddWithValue("@instanceName", WorkerInstanceName);
            
            using var reader = await command.ExecuteReaderAsync();
            
            if (await reader.ReadAsync())
            {
                var workerId = reader.GetInt32(0);
                var status = reader.GetString(1);
                var tasksCompleted = reader.GetInt32(2);
                var lastActive = reader.GetDateTime(3);
                var poolStatus = reader.IsDBNull(4) ? "unknown" : reader.GetString(4);
                var currentTaskId = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5);
                var lastTaskCompleted = reader.IsDBNull(6) ? (DateTime?)null : reader.GetDateTime(6);
                var currentTaskDesc = reader.IsDBNull(7) ? null : reader.GetString(7);
                
                string statusIcon = status switch
                {
                    "idle" => "🟢",
                    "busy" => "🟡",
                    "offline" => "🔴",
                    _ => "⚪"
                };
                
                var statusReport = $"{statusIcon} Worker Status: {WorkerInstanceName}\n\n" +
                                  $"📊 Genel Bilgiler:\n" +
                                  $"   🆔 Worker ID: {workerId}\n" +
                                  $"   🏷️ Type: {WorkerType}\n" +
                                  $"   📍 Status: {status.ToUpper()}\n" +
                                  $"   🎯 Pool Status: {poolStatus.ToUpper()}\n" +
                                  $"   ✅ Tamamlanan Tasks: {tasksCompleted}\n" +
                                  $"   🕐 Son Aktivite: {lastActive:dd.MM.yyyy HH:mm:ss}\n";
                
                if (currentTaskId.HasValue && !string.IsNullOrEmpty(currentTaskDesc))
                {
                    statusReport += $"\n📋 Şu Anki Task:\n" +
                                   $"   🆔 Task ID: {currentTaskId}\n" +
                                   $"   📝 Açıklama: {currentTaskDesc}\n";
                }
                else
                {
                    statusReport += $"\n💤 Şu anda aktif task yok.\n";
                }
                
                if (lastTaskCompleted.HasValue)
                {
                    var timeSinceLastTask = DateTime.Now - lastTaskCompleted.Value;
                    statusReport += $"   ⏱️ Son task tamamlanma: {timeSinceLastTask.TotalMinutes:F0} dakika önce\n";
                }
                
                return statusReport + $"\n🔄 Real-time sistem durumu.";
            }
            else
            {
                return $"❌ Worker '{WorkerInstanceName}' kayıtlı değil.\n" +
                       "💡 Önce RegisterWorker komutunu kullanın.";
            }
        }
        catch (Exception ex)
        {
            return $"❌ Worker status hatası: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Worker heartbeat gönderir ve sistem bağlantısını teyit eder")]
    public static async Task<string> SendHeartbeat()
    {
        try
        {
            using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
            await connection.OpenAsync();
            
            var heartbeatQuery = @"
                UPDATE ModelInstances 
                SET LastActive = GETDATE()
                WHERE InstanceName = @instanceName";
                
            using var command = new Microsoft.Data.SqlClient.SqlCommand(heartbeatQuery, connection);
            command.Parameters.AddWithValue("@instanceName", WorkerInstanceName);
            
            var rowsAffected = await command.ExecuteNonQueryAsync();
            
            if (rowsAffected > 0)
            {
                return $"💓 Heartbeat gönderildi: {WorkerInstanceName}\n" +
                       $"🕐 Zaman: {DateTime.Now:dd.MM.yyyy HH:mm:ss}\n" +
                       $"✅ Sistem bağlantısı aktif.";
            }
            else
            {
                return $"⚠️ Heartbeat gönderilemedi. Worker kayıtlı değil.\n" +
                       "💡 RegisterWorker komutunu kullanın.";
            }
        }
        catch (Exception ex)
        {
            return $"❌ Heartbeat hatası: {ex.Message}";
        }
    }

    // === HELPER METHODS ===
    
    private static async Task EnsureWorkerInPool(Microsoft.Data.SqlClient.SqlConnection connection, int workerId)
    {
        var checkPoolQuery = "SELECT COUNT(*) FROM WorkerPool WHERE ModelInstanceID = @workerId";
        using var checkPoolCommand = new Microsoft.Data.SqlClient.SqlCommand(checkPoolQuery, connection);
        checkPoolCommand.Parameters.AddWithValue("@workerId", workerId);
        
        var exists = (int)await checkPoolCommand.ExecuteScalarAsync() > 0;
        
        if (!exists)
        {
            var insertPoolQuery = @"
                INSERT INTO WorkerPool (ModelInstanceID, Status, LoadScore)
                VALUES (@workerId, 'available', 0)";
                
            using var insertPoolCommand = new Microsoft.Data.SqlClient.SqlCommand(insertPoolQuery, connection);
            insertPoolCommand.Parameters.AddWithValue("@workerId", workerId);
            await insertPoolCommand.ExecuteNonQueryAsync();
        }
    }
}
