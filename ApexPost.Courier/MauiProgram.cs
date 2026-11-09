using ApexPost.Courier.Core;
using ApexPost.Courier.Core.Auth;
using ApexPost.Courier.Core.Sync;
using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Infrastructure.Couchbase;
using ApexPost.Courier.Pages;
using ApexPost.Courier.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ApexPost.Courier;

public static class MauiProgram
{
    /// <summary>Port of ApexPost.Api's https launch profile.</summary>
    private const int ApiPort = 7153;

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        // Local store (Part A): one Couchbase Lite database in the app's private folder.
        builder.Services.AddSingleton(_ => new CourierDatabase(Path.Combine(FileSystem.AppDataDirectory, "courier-data")));
        builder.Services.AddSingleton<IScanRepository>(sp => new CouchbaseScanRepository(sp.GetRequiredService<CourierDatabase>()));

        // Device services.
        builder.Services.AddSingleton<ITokenStore, SecureStorageTokenStore>();
        builder.Services.AddSingleton<INetworkStatus, MauiConnectivityService>();

        // API client + Polly + JSON source-gen + sync (Part D).
        var http = builder.Services.AddApexCourierCore(o => o.BaseAddress = ApiBaseAddress());
#if DEBUG
        // The ASP.NET Core development certificate is not trusted by the Android emulator. Debug builds only,
        // and only for the local development host; release builds validate certificates normally.
        http.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (request, _, _, errors) =>
                errors == System.Net.Security.SslPolicyErrors.None
                || request?.RequestUri?.Host is "10.0.2.2" or "localhost",
        });
#endif

        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<NewScanPage>();
        builder.Services.AddTransient<PendingScansPage>();
        builder.Services.AddTransient<SyncStatusPage>();
        builder.Services.AddSingleton<AppShell>();

        return builder.Build();
    }

    /// <summary>The Android emulator reaches the PC's localhost as 10.0.2.2; on Windows the app is on the same machine.</summary>
    private static Uri ApiBaseAddress() =>
        DeviceInfo.Platform == DevicePlatform.Android
            ? new Uri($"https://10.0.2.2:{ApiPort}/")
            : new Uri($"https://localhost:{ApiPort}/");
}
