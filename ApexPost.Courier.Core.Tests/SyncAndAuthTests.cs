using System.Net;
using ApexPost.Courier.Core.Api;
using ApexPost.Courier.Core.Auth;
using ApexPost.Courier.Core.Sync;
using ApexPost.Courier.Offline.Domain;

namespace ApexPost.Courier.Core.Tests;

public sealed class SyncAndAuthTests
{
    private const string Created = """{"accepted":1,"duplicatesSkipped":0,"rejected":[]}""";

    [Fact]
    public async Task Offline_sync_does_nothing_and_scans_stay_pending()
    {
        var handler = new StubHandler((_, _) => StubHandler.Json(HttpStatusCode.Created, Created));
        using var host = new Host(handler, online: false);
        host.AddScan();
        var sync = host.Get<SyncCoordinator>();

        var result = await sync.SyncNowAsync();

        Assert.Null(result);
        Assert.Equal(0, handler.Calls);
        Assert.Equal(1, host.Repo.CountPending());
        Assert.False(sync.Current.IsOnline);
    }

    [Fact]
    public async Task Online_sync_uploads_and_marks_scans_synced()
    {
        var handler = new StubHandler((_, _) => StubHandler.Json(HttpStatusCode.Created, Created));
        using var host = new Host(handler);
        host.AddScan();
        var sync = host.Get<SyncCoordinator>();

        var result = await sync.SyncNowAsync();

        Assert.NotNull(result);
        Assert.Equal(1, result!.Synced);
        Assert.Equal(0, host.Repo.CountPending());
        Assert.Equal(0, sync.Current.PendingCount);
        Assert.NotNull(sync.Current.LastSuccessUtc);
    }

    [Fact]
    public async Task Failure_then_recovery_leaves_nothing_lost()
    {
        var healthy = false;
        var handler = new StubHandler((_, _) => healthy
            ? StubHandler.Json(HttpStatusCode.Created, Created)
            : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var host = new Host(handler);
        host.AddScan();
        var sync = host.Get<SyncCoordinator>();

        var first = await sync.SyncNowAsync();
        Assert.True(first!.Interrupted);
        Assert.Equal(1, host.Repo.CountPending());
        Assert.Null(sync.Current.LastSuccessUtc);

        healthy = true;
        var second = await sync.SyncNowAsync();
        Assert.Equal(1, second!.Synced);
        Assert.Equal(0, host.Repo.CountPending());
    }

    [Fact]
    public async Task Coming_back_online_wakes_the_background_loop()
    {
        var handler = new StubHandler((_, _) => StubHandler.Json(HttpStatusCode.Created, Created));
        using var host = new Host(handler, online: false);
        host.AddScan();
        var sync = host.Get<SyncCoordinator>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var loop = sync.RunAsync(TimeSpan.FromMinutes(5), cts.Token);

        host.Connectivity.Set(true);
        while (host.Repo.CountPending() > 0 && !cts.IsCancellationRequested)
        {
            await Task.Delay(20);
        }

        Assert.Equal(0, host.Repo.CountPending());
        await cts.CancelAsync();
        await loop;
    }

    [Fact]
    public async Task Login_returns_the_token_and_wrong_password_is_unauthorized()
    {
        using var ok = new Host(new StubHandler((_, _) => StubHandler.Json(HttpStatusCode.OK, """{"accessToken":"abc.def.ghi"}""")), token: null);
        Assert.Equal("abc.def.ghi", await ok.Get<ApexApiClient>().LoginAsync("courier1", "pw", CancellationToken.None));

        using var bad = new Host(new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized)), token: null);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => bad.Get<ApexApiClient>().LoginAsync("courier1", "nope", CancellationToken.None));
    }

    [Fact]
    public void Jwt_claims_are_read_from_the_payload()
    {
        var expires = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var token = JwtClaims.BuildForTests("courier-42", expires);

        Assert.Equal("courier-42", JwtClaims.GetCourierId(token));
        Assert.Equal(expires, JwtClaims.GetExpiry(token)!.Value);
        Assert.Null(JwtClaims.GetCourierId("not-a-jwt"));
        Assert.Null(JwtClaims.GetCourierId(null));
    }
}
