using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace PassingTrace.Identity.Application.DependencyInjection;

/// <summary>
/// 按“实现类名 = 接口名去掉 I”的约定自动注册服务。
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAutoInject(
        this IServiceCollection services,
        params string[] assemblyNames)
    {
        var implementationTypes = assemblyNames
            .Distinct()
            .Select(Assembly.Load)
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false });

        foreach (var implementationType in implementationTypes)
        {
            var serviceType = implementationType.GetInterfaces()
                .SingleOrDefault(candidate =>
                    candidate.Name == $"I{implementationType.Name}");

            if (serviceType is null)
            {
                continue;
            }

            var lifetime = implementationType
                .GetCustomAttribute<InjectAttribute>()?
                .Lifetime ?? ServiceLifetime.Scoped;

            services.Add(new ServiceDescriptor(
                serviceType,
                implementationType,
                lifetime));
        }

        return services;
    }
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class InjectAttribute : Attribute
{
    public ServiceLifetime Lifetime { get; init; } = ServiceLifetime.Scoped;
}
