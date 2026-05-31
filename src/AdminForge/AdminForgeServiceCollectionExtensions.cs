using AdminForge.Core.Configuration;
using AdminForge.Core.Contracts;
using AdminForge.DataAccess.EfCore;
using AdminForge.Middleware;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AdminForge;

/// <summary>
/// Host-side composition root for AdminForge. The host calls
/// <see cref="AddAdminForge{TDbContext}"/> once at startup; AdminForge then
/// (a) scans the EF model for entity metadata,
/// (b) lets the host customise that metadata via the fluent builder, and
/// (c) registers the data provider + frozen options into DI.
/// </summary>
public static class AdminForgeServiceCollectionExtensions
{
    /// <summary>
    /// Registers AdminForge against the host's <typeparamref name="TDbContext"/>.
    /// The <paramref name="configure"/> callback receives a pre-seeded
    /// <see cref="AdminForgeBuilder"/> populated with metadata for every entity
    /// in the context's model.
    /// </summary>
    public static IServiceCollection AddAdminForge<TDbContext>(
        this IServiceCollection services,
        Action<AdminForgeBuilder>? configure = null
    )
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<EfCoreReflectionScanner>();

        // Resolve options lazily: we need a scope to access the DbContext model.
        services.AddSingleton(sp =>
        {
            using var scope = sp.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<TDbContext>();
            var scanner = sp.GetRequiredService<EfCoreReflectionScanner>();
            var scanned = scanner.Scan(context);

            var builder = new AdminForgeBuilder(scanned);
            configure?.Invoke(builder);
            return builder.Build();
        });

        services.AddScoped(typeof(IAdminDataProvider<>), typeof(HostScopedDataProvider<>));

        services.AddSingleton(new HostDbContextMarker(typeof(TDbContext)));

        // DbContext-as-DbContext alias so the renderer bridge can resolve an EF model
        // without knowing the host's TDbContext type at compile time.
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TDbContext>());

        // Wire up Blazor + auth pieces from the Middleware project.
        services.AddAdminForgeBlazor();
        services.TryAddSingleton<IUserAccessor, HttpContextUserAccessor>();
        services.TryAddSingleton<IAdminAuthorizationPolicy, AllowAllAuthorizationPolicy>();

        return services;
    }
}

/// <summary>
/// Carries the host's <c>DbContext</c> type from <see cref="AdminForgeServiceCollectionExtensions.AddAdminForge{TDbContext}"/>
/// down to <see cref="HostScopedDataProvider{TEntity}"/>, which can't take that type as a generic argument
/// (open-generic DI registrations require matching arity with the requested interface).
/// </summary>
internal sealed record HostDbContextMarker(Type ContextType);

/// <summary>
/// Open-generic, single-arg <see cref="IAdminDataProvider{T}"/> implementation that
/// delegates to <see cref="EfCoreDataProvider{TContext, TEntity}"/> with the host's
/// <c>DbContext</c> type resolved via the registered <see cref="HostDbContextMarker"/>.
/// </summary>
internal sealed class HostScopedDataProvider<TEntity> : IAdminDataProvider<TEntity>
    where TEntity : class
{
    private readonly IAdminDataProvider<TEntity> _inner;

    public HostScopedDataProvider(IServiceProvider serviceProvider, HostDbContextMarker marker)
    {
        var context = (DbContext)serviceProvider.GetRequiredService(marker.ContextType);
        var options = serviceProvider.GetService<AdminForgeOptions>();
        var userAccessor = serviceProvider.GetService<IUserAccessor>();
        var providerType = typeof(EfCoreDataProvider<,>).MakeGenericType(
            marker.ContextType,
            typeof(TEntity)
        );
        _inner =
            (IAdminDataProvider<TEntity>)
                Activator.CreateInstance(providerType, context, options?.AuditSink, userAccessor)!;
    }

    public Task<ListResult<TEntity>> ListAsync(
        ListQuery query,
        CancellationToken cancellationToken = default
    ) => _inner.ListAsync(query, cancellationToken);

    public Task<TEntity?> FindAsync(
        object?[] keyValues,
        CancellationToken cancellationToken = default
    ) => _inner.FindAsync(keyValues, cancellationToken);

    public Task<TEntity> CreateAsync(
        TEntity entity,
        CancellationToken cancellationToken = default
    ) => _inner.CreateAsync(entity, cancellationToken);

    public Task<TEntity> UpdateAsync(
        TEntity entity,
        CancellationToken cancellationToken = default
    ) => _inner.UpdateAsync(entity, cancellationToken);

    public Task<bool> DeleteAsync(
        object?[] keyValues,
        CancellationToken cancellationToken = default
    ) => _inner.DeleteAsync(keyValues, cancellationToken);
}
