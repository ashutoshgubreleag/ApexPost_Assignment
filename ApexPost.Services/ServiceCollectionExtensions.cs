using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ApexPost.Services.Abstractions;
using ApexPost.Services.Authentication;
using ApexPost.Services.Branch;
using ApexPost.Services.Caching;
using ApexPost.Services.Catalog;
using ApexPost.Services.Common;
using ApexPost.Services.Courier;
using ApexPost.Services.Dashboard;
using ApexPost.Services.Tracking;
using ApexPost.Services.Events;
using ApexPost.Services.Inventory;
using ApexPost.Services.MoneyOrders;
using ApexPost.Services.Notifications;
using ApexPost.Services.Reports;
using ApexPost.Services.Shipping;
using ApexPost.Services.Staff;
using ApexPost.Services.Sync;
using ApexPost.Services.Transactions;

namespace ApexPost.Services;

/// <summary>
/// Registers the repositories and services. Lifetimes (see DESIGN.md, ADR-W2-07):
/// <list type="bullet">
/// <item><b>Singleton</b>: repositories (one SemaphoreSlim + snapshot per file), the reference-data
/// cache, the event hub, locks, the courier clients (they own a throttle), business rules.</item>
/// <item><b>Scoped</b>: business services. They are stateless today, but scoped keeps them safe to
/// gain per-request state (a unit of work next week) without a lifetime audit.</item>
/// </list>
/// Singletons never depend on scoped services, and ValidateScopes is on in Development to prove it.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApexPostServices(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddTimeProvider();
        services.AddMemoryCache();
        services.Configure<BusinessRulesOptions>(configuration.GetSection(BusinessRulesOptions.SectionName));
        services.Configure<CourierOptions>(configuration.GetSection(CourierOptions.SectionName));
        services.AddSingleton(sp => BusinessRules.FromOptions(sp.GetRequiredService<IOptions<BusinessRulesOptions>>()));

        services.AddSingleton<IApexPostEvents, ApexPostEventHub>();
        services.AddSingleton<IReferenceDataCache, ReferenceDataCache>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ICourierRateService, SimulatedCourierRateService>();
        services.AddSingleton<IShippingPricingStrategy, DefaultShippingPricingStrategy>();
        services.AddHostedService<NotificationDispatcher>();

        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IBranchService, BranchService>();
        services.AddScoped<IStaffService, StaffService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IProductCatalogService, ProductCatalogService>();
        services.AddScoped<IShippingService, ShippingService>();
        services.AddScoped<IMoneyOrderService, MoneyOrderService>();
        services.AddScoped<ITransactionService, TransactionService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IOfflineSaleService, OfflineSaleService>();
        services.AddScoped<IShipmentTrackingService, ShipmentTrackingService>();

        // Reads the shipment through the request's query service, so it is scoped like the DbContext.
        services.AddScoped<ICourierTrackingService, SimulatedCourierTrackingService>();

        return services;
    }

    private static void TryAddTimeProvider(this IServiceCollection services)
    {
        if (!services.Any(d => d.ServiceType == typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
    }
}

/// <summary>Copies seed files into the data directory before the app starts serving requests.</summary>
