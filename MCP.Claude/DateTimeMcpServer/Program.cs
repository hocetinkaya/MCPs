using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;

namespace DateTimeMcpServer;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("DateTime MCP Server başlatılıyor...");
        
        var builder = Host.CreateApplicationBuilder(args);
        
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        var host = builder.Build();
        
        Console.WriteLine("DateTime MCP Server hazır!");
        
        await host.RunAsync();
    }
}
