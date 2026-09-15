using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using PassingTrace.Events.Api.Ai;
using PassingTrace.Events.Api.Ai.Capabilities;
using PassingTrace.Infrastructure;
using Pgvector.EntityFrameworkCore;
using Xunit;

namespace PassingTrace.Events.IntegrationTests;

public sealed class PersonalMcpSchemaTests
{
    [Fact]
    public void Internal_record_schemas_express_constraints_without_prompt_only_conventions()
    {
        using var db = new TraceDbContext(new DbContextOptionsBuilder<TraceDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=test", options => options.UseVector()).Options);
        var user = new CurrentUserContext(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "17")], "test")),
            },
        });
        var tools = new PersonalRecordTools(db, user, new NoEmbedding());
        var definitions = new PersonalRecordsCapabilityPackage(tools).CreateTools().OfType<AIFunction>()
            .ToDictionary(x => x.Name, x => new ValidatedMcpServerTool(McpServerTool.Create(x)).ProtocolTool.InputSchema);
        Assert.Equal(9, definitions.Count);
        foreach (var definition in definitions.Values)
        {
            Assert.False(definition.GetProperty("additionalProperties").GetBoolean());
            Assert.False(definition.GetProperty("properties").TryGetProperty("userId", out _));
        }
        var record = definitions["SearchMyRecords"].GetProperty("properties");
        Assert.Equal(1, record.GetProperty("limit").GetProperty("minimum").GetInt32());
        Assert.Equal(20, record.GetProperty("limit").GetProperty("maximum").GetInt32());
        Assert.Equal(-90, record.GetProperty("centerLatitude").GetProperty("minimum").GetInt32());
        Assert.Equal("^(Trace|Plan)$", record.GetProperty("kind").GetProperty("pattern").GetString());
        Assert.Equal("date-time", record.GetProperty("from").GetProperty("format").GetString());
        Assert.Equal("date-time", definitions["AggregateMyRecords"].GetProperty("properties").GetProperty("to").GetProperty("format").GetString());
    }

    [Theory]
    [InlineData("{\"query\":\"test\",\"from\":\"yesterday\"}")]
    [InlineData("{\"query\":\"test\",\"from\":\"2026-02-30T00:00:00+08:00\"}")]
    [InlineData("{\"query\":\"test\",\"from\":\"2026-09-01\"}")]
    [InlineData("{\"query\":\"test\",\"limit\":999}")]
    [InlineData("{\"query\":\"test\",\"centerLatitude\":100}")]
    [InlineData("{\"query\":\"test\",\"kind\":\"anything\"}")]
    [InlineData("{\"query\":\"test\",\"status\":\"anything\"}")]
    public async Task Invalid_filters_do_not_reach_database(string json)
    {
        await using var db = new TraceDbContext(new DbContextOptionsBuilder<TraceDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=test", options => options.UseVector()).Options);
        var tools = new PersonalRecordTools(db, new CurrentUserContext(new HttpContextAccessor()), new NoEmbedding());
        var function = new PersonalRecordsCapabilityPackage(tools).CreateTools().OfType<AIFunction>()
            .Single(x => x.Name == "SearchMyRecords");
        await using var session = await InternalMcpToolSession.CreateAsync([function]);
        var arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
        var response = Assert.IsType<JsonElement>(await ((AIFunction)session.Tools.Single()).InvokeAsync(
            new AIFunctionArguments(arguments.ToDictionary(x => x.Key, x => (object?)x.Value))));
        Assert.True(response.GetProperty("isError").GetBoolean());
        Assert.Equal("invalid_tool_arguments", response.GetProperty("structuredContent").GetProperty("code").GetString());
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Empty(tools.Snapshot.Records);
    }

    private sealed class NoEmbedding : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Invalid arguments must never request embeddings.");
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
