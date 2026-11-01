using ApexPost.Database;
using ApexPost.Database.Conventions;
using ApexPost.Repositories.Auditing;
using ApexPost.Repositories.Persistence;
using ApexPost.Repositories.Queries;
using ApexPost.Services.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ApexPost.Repositories;

public sealed class PersistenceOptions
{
    /// <summary>SQL Server connection string (user-secrets in Development, never committed).</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Logs every SQL command with parameter values (Development only) to prove there is no N+1.</summary>
    public bool LogSqlCommands { get; set; }

    /// <summary>Transient-fault retries for the SqlServer execution strategy (0 disables it).</summary>
    public int MaxRetryCount { get; set; } = 3;
}

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the EF Core persistence layer. Lifetimes (ADR-W3-04): the DbContext, every
    /// repository, every query service and the unit of work are SCOPED, so one request shares one
    /// DbContext. The context factory (used only by the audit store) is a singleton that creates
    /// short-lived, separately-disposed contexts. Nothing holds a DbContext in a singleton.
    /// </summary>
    public static IServiceCollection AddApexPostPersistence(this IServiceCollection services, Func<IServiceProvider, PersistenceOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton<IAuditActor, SystemAuditActor>();
        services.AddScoped<AuditColumnsInterceptor>();

        void Configure(IServiceProvider sp, DbContextOptionsBuilder builder)
        {
            var settings = options(sp);
            if (string.IsNullOrWhiteSpace(settings.ConnectionString))
            {
                throw new InvalidOperationException(
                    "ConnectionStrings:ApexPostDb is not set. Run: dotnet user-secrets set \"ConnectionStrings:ApexPostDb\" \"Server=.;Database=ApexPostDb;Trusted_Connection=True;TrustServerCertificate=True\" --project ApexPost.MVC");
            }

            builder.UseSqlServer(settings.ConnectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(ApexPostDbContext).Assembly.GetName().Name);
                if (settings.MaxRetryCount > 0)
                {
                    sql.EnableRetryOnFailure(settings.MaxRetryCount, TimeSpan.FromSeconds(5), null);
                }
            });
            builder.ConfigureWarnings(w => w.Throw(RelationalEventId.MultipleCollectionIncludeWarning));

            if (settings.LogSqlCommands)
            {
                var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                builder.UseLoggerFactory(loggerFactory).EnableSensitiveDataLogging()
                    .LogTo(message => loggerFactory.CreateLogger("ApexPost.Sql").LogInformation("{Sql}", message), [DbLoggerCategory.Database.Command.Name], LogLevel.Information);
            }
        }

        services.AddDbContext<ApexPostDbContext>((sp, builder) =>
        {
            Configure(sp, builder);
            builder.AddInterceptors(sp.GetRequiredService<AuditColumnsInterceptor>());
        });
        // A separate factory (own options instance) so it cannot pick up the scoped options above.
        services.AddSingleton<IDbContextFactory<ApexPostDbContext>>(sp =>
        {
            var builder = new DbContextOptionsBuilder<ApexPostDbContext>();
            Configure(sp, builder);
            return new IndependentContextFactory(builder.Options);
        });

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IBranchRepository, BranchRepository>();
        services.AddScoped<IStaffRepository, StaffRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IStockRepository, StockRepository>();
        services.AddScoped<IStockAdjustmentProcedure, StockAdjustmentProcedure>();
        services.AddScoped<ICounterTransactionRepository, CounterTransactionRepository>();
        services.AddScoped<IShipmentRepository, ShipmentRepository>();
        services.AddScoped<IShippingZoneRepository, ShippingZoneRepository>();
        services.AddScoped<IMoneyOrderRepository, MoneyOrderRepository>();
        services.AddScoped<IIdentityVerificationRepository, IdentityVerificationRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<ISyncInboxRepository, SyncInboxRepository>();

        services.AddScoped<IBranchQueries, BranchQueries>();
        services.AddScoped<IStaffQueries, StaffQueries>();
        services.AddScoped<ICatalogQueries, CatalogQueries>();
        services.AddScoped<ITransactionQueries, TransactionQueries>();
        services.AddScoped<IReportQueries, ReportQueries>();
        services.AddScoped<INotificationQueries, NotificationQueries>();
        services.AddScoped<ISyncQueries, SyncQueries>();

        services.AddSingleton<IAuditStore, SqlAuditStore>();
        return services;
    }
}

/// <summary>Creates short-lived contexts that are independent of the request's unit of work.</summary>
internal sealed class IndependentContextFactory(DbContextOptions<ApexPostDbContext> options) : IDbContextFactory<ApexPostDbContext>
{
    public ApexPostDbContext CreateDbContext() => new(options);
}
