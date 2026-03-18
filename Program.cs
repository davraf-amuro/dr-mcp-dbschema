using mcp_db_schema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var projectPath = Environment.GetEnvironmentVariable("MCP_PROJECT_PATH")
    ?? Directory.GetCurrentDirectory();

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<ConnectionManager>(_ =>
{
    var cm = new ConnectionManager();
    cm.LoadFromPath(projectPath);
    return cm;
});

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<SchemaTools>();

await builder.Build().RunAsync();
