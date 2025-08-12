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
    [Description("Doğal dille SQL sorgusu çalıştırır")]
    public static async Task<string> ExecuteQuery([Description("Çalıştırılacak SQL sorgusu")] string sqlQuery)
    {
        try
        {
            // Sadece SELECT sorgularına izin ver (güvenlik)
            if (!sqlQuery.Trim().ToUpper().StartsWith("SELECT"))
            {
                return "Güvenlik nedeniyle sadece SELECT sorguları çalıştırılabilir.";
            }

            using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
            await connection.OpenAsync();
            
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
        catch (Exception ex)
        {
            return $"Hata: {ex.Message}";
        }
    }
}
