using System.Reflection;
using PassingTrace.Ai.Worker;
using PassingTrace.Core.Ai;
using PassingTrace.Events.Api.Ai;
using PassingTrace.Events.Api.Social;
using Xunit;

namespace PassingTrace.Events.IntegrationTests;

public sealed class AiPersistenceBoundaryTests
{
    [Fact]
    public void Application_services_and_tools_do_not_depend_on_database_types()
    {
        Type[] applicationTypes = [typeof(PersonalRecordTools), typeof(AssistantService), typeof(UserMemoryService),
            typeof(EventSemanticService), typeof(EventSemanticController), typeof(AssistantConversationHistory),
            typeof(ConversationContextSnapshot), typeof(SocialAiTools), typeof(SocialEvidenceGuard),
            typeof(SemanticPipeline), typeof(AnalysisWorker)];
        foreach (var type in applicationTypes)
        {
            var signatures = type.GetConstructors().SelectMany(x => x.GetParameters()).Select(x => x.ParameterType)
                .Concat(type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Select(x => x.FieldType))
                .Concat(type.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                    .SelectMany(x => x.GetParameters().Select(p => p.ParameterType).Append(x.ReturnType)));
            Assert.All(signatures.SelectMany(Expand), dependency => Assert.False(IsDatabaseType(dependency),
                $"{type.Name} depends on {dependency.FullName}; keep database access behind Core ports."));
        }
    }

    [Fact]
    public void Core_ports_do_not_expose_query_providers_or_database_dependencies()
    {
        Type[] ports = [typeof(IPersonalRecordQueries), typeof(IAiConversationRepository), typeof(IUserMemoryRepository),
            typeof(IEventSemanticRepository), typeof(ISocialAiQueries), typeof(IAiEvidenceQueries), typeof(IAnalysisOutbox),
            typeof(ISemanticPipelineRepository), typeof(IAnalysisJobRepository)];
        foreach (var port in ports)
            Assert.All(port.GetMethods().SelectMany(x => x.GetParameters().Select(p => p.ParameterType).Append(x.ReturnType)).SelectMany(Expand),
                dependency => Assert.False(IsDatabaseType(dependency), $"{port.Name} exposes {dependency.FullName}."));
        Assert.DoesNotContain(typeof(IPersonalRecordQueries).Assembly.GetReferencedAssemblies(), name =>
            name.Name!.Contains("EntityFrameworkCore") || name.Name.Contains("AspNetCore") || name.Name.Contains("Pgvector"));
    }

    private static bool IsDatabaseType(Type type) => typeof(IQueryable).IsAssignableFrom(type) ||
        type.Namespace?.StartsWith("PassingTrace.Infrastructure", StringComparison.Ordinal) == true ||
        type.Namespace?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true ||
        type.Namespace?.StartsWith("Pgvector", StringComparison.Ordinal) == true;

    private static IEnumerable<Type> Expand(Type type)
    {
        yield return type;
        if (type.HasElementType)
            foreach (var child in Expand(type.GetElementType()!)) yield return child;
        foreach (var argument in type.GenericTypeArguments)
            foreach (var child in Expand(argument)) yield return child;
    }
}
