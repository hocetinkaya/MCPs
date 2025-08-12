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
public static class OrchestratorTools
{
    private static readonly string? ConnectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING") 
        ?? $"Server={Environment.GetEnvironmentVariable("SERVER_NAME")};Database={Environment.GetEnvironmentVariable("DATABASE_NAME")};User Id={Environment.GetEnvironmentVariable("DB_USER")};Password={Environment.GetEnvironmentVariable("DB_PASSWORD")};TrustServerCertificate=true;";

    // === PROJECT MANAGEMENT TOOLS ===

    [McpServerTool]
    [Description("Yeni proje oturumu oluşturur ve task decomposition yapar")]
    public static async Task<string> CreateProject(
        [Description("Proje adı")] string projectName,
        [Description("Proje açıklaması")] string description,
        [Description("Tahmini öncelik (1-10)")] int priority = 5)
    {
        try
        {
            using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
            await connection.OpenAsync();
            
            var query = @"
                INSERT INTO WorkSessions (SessionName, CreatedBy, Status, Priority, Description)
                OUTPUT INSERTED.ID
                VALUES (@projectName, 'AdminClaude', 'active', @priority, @description)";
            
            using var command = new Microsoft.Data.SqlClient.SqlCommand(query, connection);
            command.Parameters.AddWithValue("@projectName", projectName);
            command.Parameters.AddWithValue("@priority", priority);
            command.Parameters.AddWithValue("@description", description);
            
            var projectId = await command.ExecuteScalarAsync();
            
            return $"🚀 Yeni proje oluşturuldu: '{projectName}' (ID: {projectId})\n" +
                   $"📋 Proje açıklaması: {description}\n" +
                   $"⭐ Öncelik: {priority}/10\n" +
                   $"✅ Artık task'ları bu projeye ekleyebilirsiniz.";
        }
        catch (Exception ex)
        {
            return $"❌ Hata: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Büyük task'ları küçük parçalara böler ve kuyruğa ekler")]
    public static async Task<string> DecomposeTask(
        [Description("Proje ID'si")] int projectId,
        [Description("Ana görev açıklaması")] string mainTaskDescription,
        [Description("Alt görevler (JSON array formatında)")] string subTasksJson = "[]",
        [Description("Otomatik ayrıştırma modu")] bool autoDecompose = true)
    {
        try
        {
            using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
            await connection.OpenAsync();
            
            // Ana task'ı ekle
            var mainTaskQuery = @"
                INSERT INTO TaskQueue (SessionID, TaskType, Description, Status, Priority)
                OUTPUT INSERTED.ID
                VALUES (@projectId, 'planning', @description, 'pending', 8)";
                
            using var mainTaskCommand = new Microsoft.Data.SqlClient.SqlCommand(mainTaskQuery, connection);
            mainTaskCommand.Parameters.AddWithValue("@projectId", projectId);
            mainTaskCommand.Parameters.AddWithValue("@description", mainTaskDescription);
            
            var mainTaskId = await mainTaskCommand.ExecuteScalarAsync();
            
            var addedTasks = new List<string> { $"📋 Ana Task (ID: {mainTaskId}): {mainTaskDescription}" };
            
            if (autoDecompose)
            {
                // Otomatik task ayrıştırma logic'i
                var autoTasks = GenerateAutoSubTasks(mainTaskDescription);
                
                foreach (var subTask in autoTasks)
                {
                    var subTaskQuery = @"
                        INSERT INTO TaskQueue (SessionID, TaskType, Description, Dependencies, Status, Priority)
                        OUTPUT INSERTED.ID
                        VALUES (@projectId, @taskType, @description, @dependencies, 'pending', @priority)";
                        
                    using var subTaskCommand = new Microsoft.Data.SqlClient.SqlCommand(subTaskQuery, connection);
                    subTaskCommand.Parameters.AddWithValue("@projectId", projectId);
                    subTaskCommand.Parameters.AddWithValue("@taskType", subTask.TaskType);
                    subTaskCommand.Parameters.AddWithValue("@description", subTask.Description);
                    subTaskCommand.Parameters.AddWithValue("@dependencies", subTask.Dependencies);
                    subTaskCommand.Parameters.AddWithValue("@priority", subTask.Priority);
                    
                    var subTaskId = await subTaskCommand.ExecuteScalarAsync();
                    addedTasks.Add($"   └─ Sub Task (ID: {subTaskId}): {subTask.Description}");
                }
            }
            
            return $"✅ Task decomposition tamamlandı!\n\n" +
                   string.Join("\n", addedTasks) + "\n\n" +
                   $"📊 Toplam {addedTasks.Count} task oluşturuldu.";
        }
        catch (Exception ex)
        {
            return $"❌ Hata: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Worker'lara manual task assignment yapar")]
    public static async Task<string> AssignTaskToWorker(
        [Description("Task ID'si")] int taskId,
        [Description("Worker instance adı (opsiyonel, 'auto' için otomatik)")] string workerName = "auto",
        [Description("Tahmini süre (dakika)")] int estimatedMinutes = 30)
    {
        try
        {
            using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
            await connection.OpenAsync();
            
            int? assignedWorkerId = null;
            string assignedWorkerName = workerName;
            
            if (workerName == "auto")
            {
                // Otomatik worker selection algoritması
                var workerSelectionQuery = @"
                    SELECT TOP 1 mi.ID, mi.InstanceName
                    FROM ModelInstances mi
                    LEFT JOIN WorkerPool wp ON mi.ID = wp.ModelInstanceID
                    WHERE mi.Status = 'idle' OR mi.Status IS NULL
                    ORDER BY ISNULL(wp.LoadScore, 0) ASC, mi.TasksCompleted ASC";
                    
                using var workerCommand = new Microsoft.Data.SqlClient.SqlCommand(workerSelectionQuery, connection);
                using var workerReader = await workerCommand.ExecuteReaderAsync();
                
                if (await workerReader.ReadAsync())
                {
                    assignedWorkerId = workerReader.GetInt32(0);
                    assignedWorkerName = workerReader.GetString(1);
                }
                await workerReader.CloseAsync();
                
                if (assignedWorkerId == null)
                {
                    return "❌ Şu anda uygun worker bulunamadı. Tüm worker'lar meşgul.";
                }
            }
            else
            {
                // Belirli worker'ı bul
                var specificWorkerQuery = "SELECT ID FROM ModelInstances WHERE InstanceName = @workerName";
                using var specificCommand = new Microsoft.Data.SqlClient.SqlCommand(specificWorkerQuery, connection);
                specificCommand.Parameters.AddWithValue("@workerName", workerName);
                
                var result = await specificCommand.ExecuteScalarAsync();
                if (result != null)
                {
                    assignedWorkerId = (int)result;
                }
                else
                {
                    return $"❌ Worker '{workerName}' bulunamadı.";
                }
            }
            
            // Task'ı worker'a ata
            var assignmentQuery = @"
                UPDATE TaskQueue 
                SET AssignedModelID = @workerId, Status = 'assigned', 
                    AssignedAt = GETDATE(), EstimatedDuration = @estimatedMinutes
                WHERE ID = @taskId";
                
            using var assignCommand = new Microsoft.Data.SqlClient.SqlCommand(assignmentQuery, connection);
            assignCommand.Parameters.AddWithValue("@workerId", assignedWorkerId);
            assignCommand.Parameters.AddWithValue("@taskId", taskId);
            assignCommand.Parameters.AddWithValue("@estimatedMinutes", estimatedMinutes);
            
            var rowsAffected = await assignCommand.ExecuteNonQueryAsync();
            
            if (rowsAffected > 0)
            {
                // Worker status'unu güncelle
                var updateWorkerQuery = @"
                    UPDATE ModelInstances 
                    SET Status = 'busy', LastActive = GETDATE()
                    WHERE ID = @workerId";
                    
                using var updateWorkerCommand = new Microsoft.Data.SqlClient.SqlCommand(updateWorkerQuery, connection);
                updateWorkerCommand.Parameters.AddWithValue("@workerId", assignedWorkerId);
                await updateWorkerCommand.ExecuteNonQueryAsync();
                
                return $"✅ Task #{taskId} başarıyla atandı!\n" +
                       $"👤 Worker: {assignedWorkerName}\n" +
                       $"⏱️ Tahmini süre: {estimatedMinutes} dakika\n" +
                       $"📝 Worker artık bu task üzerinde çalışmaya başlayabilir.";
            }
            else
            {
                return $"❌ Task #{taskId} bulunamadı veya atanamadı.";
            }
        }
        catch (Exception ex)
        {
            return $"❌ Hata: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Aktif worker'ları keşfeder ve durumlarını gösterir")]
    public static async Task<string> DiscoverWorkers()
    {
        try
        {
            using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
            await connection.OpenAsync();
            
            var query = @"
                SELECT mi.ID, mi.InstanceName, mi.ModelType, mi.Status,
                       mi.TasksCompleted, DATEDIFF(MINUTE, mi.LastActive, GETDATE()) as MinutesInactive,
                       wp.LoadScore, wp.CurrentTaskID
                FROM ModelInstances mi
                LEFT JOIN WorkerPool wp ON mi.ID = wp.ModelInstanceID
                WHERE mi.InstanceName != 'AdminClaude'
                ORDER BY mi.Status DESC, mi.LastActive DESC";
            
            using var command = new Microsoft.Data.SqlClient.SqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();
            
            var workers = new List<string>();
            var activeCount = 0;
            var idleCount = 0;
            
            while (await reader.ReadAsync())
            {
                var workerId = reader.GetInt32(0);
                var workerName = reader.GetString(1);
                var modelType = reader.GetString(2);
                var status = reader.IsDBNull(3) ? "offline" : reader.GetString(3);
                var tasksCompleted = reader.GetInt32(4);
                var minutesInactive = reader.IsDBNull(5) ? 999 : reader.GetInt32(5);
                var loadScore = reader.IsDBNull(6) ? 0 : reader.GetInt32(6);
                
                string statusIcon = status switch
                {
                    "idle" => "🟢",
                    "busy" => "🟡",
                    "offline" => "🔴",
                    _ => "⚪"
                };
                
                if (status == "idle" || status == "busy") activeCount++;
                if (status == "idle") idleCount++;
                
                var workerInfo = $"{statusIcon} {workerName} ({modelType})\n" +
                                $"   └─ Status: {status.ToUpper()}, Tasks: {tasksCompleted}, Load: {loadScore}\n" +
                                $"   └─ Inactive: {minutesInactive} dakika";
                                
                workers.Add(workerInfo);
            }
            
            var summary = $"🔍 Worker Discovery Raporu\n\n" +
                         $"📊 Özet:\n" +
                         $"   • Toplam Worker: {workers.Count}\n" +
                         $"   • Aktif Worker: {activeCount}\n" +
                         $"   • Uygun Worker: {idleCount}\n\n" +
                         $"👥 Worker Listesi:\n" +
                         string.Join("\n\n", workers);
                         
            return summary;
        }
        catch (Exception ex)
        {
            return $"❌ Hata: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Proje durumunu detaylı şekilde gösterir")]
    public static async Task<string> GetProjectStatus(
        [Description("Proje ID'si (opsiyonel, tümü için boş bırak)")] int? projectId = null)
    {
        try
        {
            using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
            await connection.OpenAsync();
            
            string projectFilter = projectId.HasValue ? "WHERE ws.ID = @projectId" : "";
            
            var query = $@"
                SELECT ws.ID, ws.SessionName, ws.Status, ws.Priority, ws.CreatedAt,
                       ws.Description,
                       COUNT(tq.ID) as TotalTasks,
                       SUM(CASE WHEN tq.Status = 'pending' THEN 1 ELSE 0 END) as PendingTasks,
                       SUM(CASE WHEN tq.Status = 'assigned' THEN 1 ELSE 0 END) as AssignedTasks,
                       SUM(CASE WHEN tq.Status = 'in_progress' THEN 1 ELSE 0 END) as InProgressTasks,
                       SUM(CASE WHEN tq.Status = 'completed' THEN 1 ELSE 0 END) as CompletedTasks
                FROM WorkSessions ws
                LEFT JOIN TaskQueue tq ON ws.ID = tq.SessionID
                {projectFilter}
                GROUP BY ws.ID, ws.SessionName, ws.Status, ws.Priority, ws.CreatedAt, ws.Description
                ORDER BY ws.CreatedAt DESC";
            
            using var command = new Microsoft.Data.SqlClient.SqlCommand(query, connection);
            if (projectId.HasValue)
                command.Parameters.AddWithValue("@projectId", projectId.Value);
                
            using var reader = await command.ExecuteReaderAsync();
            
            var projects = new List<string>();
            
            while (await reader.ReadAsync())
            {
                var id = reader.GetInt32(0);
                var name = reader.GetString(1);
                var status = reader.GetString(2);
                var priority = reader.GetInt32(3);
                var createdAt = reader.GetDateTime(4);
                var description = reader.IsDBNull(5) ? "N/A" : reader.GetString(5);
                var totalTasks = reader.GetInt32(6);
                var pendingTasks = reader.GetInt32(7);
                var assignedTasks = reader.GetInt32(8);
                var inProgressTasks = reader.GetInt32(9);
                var completedTasks = reader.GetInt32(10);
                
                var completionRate = totalTasks > 0 ? (completedTasks * 100 / totalTasks) : 0;
                
                string statusIcon = status switch
                {
                    "active" => "🟢",
                    "paused" => "⏸️",
                    "completed" => "✅",
                    "cancelled" => "❌",
                    _ => "⚪"
                };
                
                var projectInfo = $"{statusIcon} {name} (ID: {id})\n" +
                                 $"   📝 Açıklama: {description}\n" +
                                 $"   📊 İlerleme: {completionRate}% ({completedTasks}/{totalTasks} task)\n" +
                                 $"   📋 Task Durumu: Pending({pendingTasks}) | Assigned({assignedTasks}) | Progress({inProgressTasks}) | Done({completedTasks})\n" +
                                 $"   ⭐ Öncelik: {priority}/10\n" +
                                 $"   📅 Oluşturma: {createdAt:dd.MM.yyyy HH:mm}";
                                 
                projects.Add(projectInfo);
            }
            
            if (projects.Count == 0)
            {
                return projectId.HasValue 
                    ? $"❌ Proje ID {projectId} bulunamadı."
                    : "📋 Henüz hiç proje oluşturulmamış.";
            }
            
            var title = projectId.HasValue 
                ? $"📊 Proje Detayları (ID: {projectId})"
                : $"📊 Tüm Projeler ({projects.Count} adet)";
                
            return $"{title}\n\n" + string.Join("\n\n", projects);
        }
        catch (Exception ex)
        {
            return $"❌ Hata: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Sistem geneli sağlık durumunu ve istatistikleri gösterir")]
    public static async Task<string> SystemHealthCheck()
    {
        try
        {
            using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
            await connection.OpenAsync();
            
            // Worker statistics
            var workerStatsQuery = @"
                SELECT 
                    COUNT(*) as TotalWorkers,
                    SUM(CASE WHEN Status = 'idle' THEN 1 ELSE 0 END) as IdleWorkers,
                    SUM(CASE WHEN Status = 'busy' THEN 1 ELSE 0 END) as BusyWorkers,
                    SUM(CASE WHEN Status = 'offline' THEN 1 ELSE 0 END) as OfflineWorkers
                FROM ModelInstances 
                WHERE InstanceName != 'AdminClaude'";
                
            // Project statistics  
            var projectStatsQuery = @"
                SELECT 
                    COUNT(*) as TotalProjects,
                    SUM(CASE WHEN Status = 'active' THEN 1 ELSE 0 END) as ActiveProjects,
                    SUM(CASE WHEN Status = 'completed' THEN 1 ELSE 0 END) as CompletedProjects
                FROM WorkSessions";
                
            // Task statistics
            var taskStatsQuery = @"
                SELECT 
                    COUNT(*) as TotalTasks,
                    SUM(CASE WHEN Status = 'pending' THEN 1 ELSE 0 END) as PendingTasks,
                    SUM(CASE WHEN Status = 'assigned' THEN 1 ELSE 0 END) as AssignedTasks,
                    SUM(CASE WHEN Status = 'in_progress' THEN 1 ELSE 0 END) as InProgressTasks,
                    SUM(CASE WHEN Status = 'completed' THEN 1 ELSE 0 END) as CompletedTasks
                FROM TaskQueue";
            
            // Execute queries
            var workerStats = await ExecuteStatsQuery(connection, workerStatsQuery);
            var projectStats = await ExecuteStatsQuery(connection, projectStatsQuery);
            var taskStats = await ExecuteStatsQuery(connection, taskStatsQuery);
            
            var healthReport = $"🏥 MMOS Sistem Sağlık Raporu\n" +
                              $"📅 Rapor Zamanı: {DateTime.Now:dd.MM.yyyy HH:mm:ss}\n\n" +
                              
                              $"👥 Worker Durumu:\n" +
                              $"   • Toplam: {workerStats[0]} worker\n" +
                              $"   • Uygun: {workerStats[1]} idle\n" +
                              $"   • Meşgul: {workerStats[2]} busy  \n" +
                              $"   • Offline: {workerStats[3]} offline\n\n" +
                              
                              $"📊 Proje Durumu:\n" +
                              $"   • Toplam: {projectStats[0]} proje\n" +
                              $"   • Aktif: {projectStats[1]} active\n" +
                              $"   • Tamamlanan: {projectStats[2]} completed\n\n" +
                              
                              $"📋 Task Durumu:\n" +
                              $"   • Toplam: {taskStats[0]} task\n" +
                              $"   • Bekleyen: {taskStats[1]} pending\n" +
                              $"   • Atanmış: {taskStats[2]} assigned\n" +
                              $"   • İşlemde: {taskStats[3]} in_progress\n" +
                              $"   • Tamamlanan: {taskStats[4]} completed\n\n";
            
            // System health indicators
            var healthIndicators = new List<string>();
            
            if (int.Parse(workerStats[1]) > 0)
                healthIndicators.Add("🟢 Uygun worker mevcut");
            else
                healthIndicators.Add("🟡 Tüm worker'lar meşgul/offline");
                
            if (int.Parse(taskStats[1]) > 0 && int.Parse(workerStats[1]) > 0)
                healthIndicators.Add("🟢 Task assignment mümkün");
            else if (int.Parse(taskStats[1]) > 0)
                healthIndicators.Add("🟡 Bekleyen task var ama worker yok");
            else
                healthIndicators.Add("🟢 Task kuyruğu temiz");
            
            healthReport += $"💡 Sistem Durumu:\n" + string.Join("\n", healthIndicators.Select(h => $"   {h}"));
            
            return healthReport;
        }
        catch (Exception ex)
        {
            return $"❌ Hata: {ex.Message}";
        }
    }
    
    // === HELPER METHODS ===
    
    private static async Task<string[]> ExecuteStatsQuery(Microsoft.Data.SqlClient.SqlConnection connection, string query)
    {
        using var command = new Microsoft.Data.SqlClient.SqlCommand(query, connection);
        using var reader = await command.ExecuteReaderAsync();
        
        if (await reader.ReadAsync())
        {
            var results = new string[reader.FieldCount];
            for (int i = 0; i < reader.FieldCount; i++)
            {
                results[i] = reader.GetValue(i).ToString() ?? "0";
            }
            return results;
        }
        
        return new string[0];
    }
    
    private static List<(string TaskType, string Description, string Dependencies, int Priority)> GenerateAutoSubTasks(string mainTaskDescription)
    {
        var subTasks = new List<(string TaskType, string Description, string Dependencies, int Priority)>();
        
        // Basit keyword-based task decomposition
        var lowerDescription = mainTaskDescription.ToLower();
        
        if (lowerDescription.Contains("api") || lowerDescription.Contains("endpoint") || lowerDescription.Contains("rest"))
        {
            subTasks.Add(("planning", "API tasarımı ve endpoint planning", "[]", 7));
            subTasks.Add(("coding", "Core API implementation", $"[]", 6));
            subTasks.Add(("testing", "API endpoint testing", $"[]", 5));
            subTasks.Add(("documentation", "API documentation oluştur", $"[]", 4));
        }
        else if (lowerDescription.Contains("web") || lowerDescription.Contains("frontend") || lowerDescription.Contains("ui"))
        {
            subTasks.Add(("planning", "UI/UX tasarım planlama", "[]", 7));
            subTasks.Add(("coding", "Frontend component geliştirme", $"[]", 6));
            subTasks.Add(("testing", "Frontend testing ve debugging", $"[]", 5));
        }
        else if (lowerDescription.Contains("database") || lowerDescription.Contains("db") || lowerDescription.Contains("sql"))
        {
            subTasks.Add(("planning", "Database schema tasarımı", "[]", 7));
            subTasks.Add(("coding", "Database migration ve setup", $"[]", 6));
            subTasks.Add(("testing", "Database query performance testi", $"[]", 4));
        }
        else
        {
            // Generic task breakdown
            subTasks.Add(("planning", $"'{mainTaskDescription}' için detaylı planlama", "[]", 7));
            subTasks.Add(("coding", $"'{mainTaskDescription}' implementasyon", $"[]", 6));
            subTasks.Add(("testing", $"'{mainTaskDescription}' test ve doğrulama", $"[]", 5));
        }
        
        return subTasks;
    }
}
