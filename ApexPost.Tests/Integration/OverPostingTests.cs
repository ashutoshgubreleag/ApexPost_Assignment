using System.Net;
using System.Net.Http.Json;
using ApexPost.Contracts;
using ApexPost.Models.Domain;
using ApexPost.Models.ValueObjects;
using ApexPost.Tests.Support;
using static ApexPost.Tests.Support.ApexPostWebApplicationFactory;

namespace ApexPost.Tests.Integration;

/// <summary>
/// Over-posting (mass assignment): a client adds fields the form or API does not offer, hoping the
/// server binds them onto the entity. Each controller binds a view model or wire DTO and maps the
/// allowed fields explicitly into an application command, so ids, statuses, amounts, owners and
/// audit columns always come from the route, the signed-in user or the domain, never the request.
/// </summary>
public sealed class OverPostingTests : IClassFixture<ApexPostWebApplicationFactory>
{
    private readonly ApexPostWebApplicationFactory _factory;

    public OverPostingTests(ApexPostWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Opening_a_basket_ignores_posted_branch_clerk_status_and_totals()
    {
        using var clerk = _factory.CreateBrowser();
        await LoginAsync(clerk, "clerk.br001a@apexpost.local");

        using var response = await PostFormAsync(clerk, "/Transaction/Create", "/Transaction/Create", new()
        {
            ["CounterTerminalId"] = "1",
            ["Id"] = "1",
            ["BranchCode"] = "BR-002",
            ["ClerkStaffId"] = "10",
            ["Status"] = "Paid",
            ["TotalAmount"] = "0.01",
            ["Source"] = "OfflineTerminal",
            ["CreatedBy"] = "attacker",
        });

        var tx = _factory.Store.All<CounterTransaction>().Single(t => t.Id == IdFrom(response));
        Assert.Equal(new BranchCode("BR-001"), tx.BranchCode); // the clerk's own branch, from the auth cookie
        Assert.Equal(7, tx.ClerkStaffId);                      // the signed-in clerk
        Assert.Equal(TransactionStatus.Open, tx.Status);       // the domain's starting state
        Assert.Equal(Money.Zero, tx.TotalAmount);              // derived from lines, of which there are none
        Assert.Equal(TransactionSource.Counter, tx.Source);
    }

    [Fact]
    public async Task Money_order_form_ignores_posted_fee_status_serial_and_target_transaction()
    {
        using var clerk = _factory.CreateBrowser();
        await LoginAsync(clerk, "clerk.br001a@apexpost.local");
        var txId = IdFrom(await PostFormAsync(clerk, "/Transaction/Create", "/Transaction/Create", new() { ["CounterTerminalId"] = "1" }));

        await PostFormAsync(clerk, $"/MoneyOrder/Create/{txId}", $"/MoneyOrder/Create/{txId}", new()
        {
            ["Amount"] = "200",
            ["Sender"] = "Ada Fictional",
            ["Recipient"] = "Bo Example",
            ["TransactionId"] = "1",          // try to attach the order to another basket
            ["Fee"] = "0",
            ["Status"] = "Issued",
            ["SerialNumber"] = "MO-HACKED",
            ["IdentityVerificationId"] = "1",
        });

        var order = _factory.Store.All<MoneyOrder>().Single(m => m.TransactionId == txId);
        Assert.True(order.Fee > Money.Zero);                  // computed by the business rules
        Assert.NotEqual(MoneyOrderStatus.Issued, order.Status); // issued only when the basket is paid
        Assert.NotEqual("MO-HACKED", order.SerialNumber);
        Assert.Null(order.IdentityVerificationId);
        Assert.DoesNotContain(_factory.Store.All<MoneyOrder>(), m => m.TransactionId == 1 && m.SerialNumber == order.SerialNumber);
    }

    [Fact]
    public async Task Staff_edit_uses_the_route_id_and_never_binds_the_password_hash()
    {
        using var manager = _factory.CreateBrowser();
        await LoginAsync(manager, "manager.br001@apexpost.local");
        var admin = _factory.Store.All<StaffMember>().Single(s => s.Id == 1);
        var adminBefore = (admin.Name, admin.Email, admin.Role, admin.PasswordHash);
        var hashBefore = _factory.Store.All<StaffMember>().Single(s => s.Id == 8).PasswordHash;

        using var response = await PostFormAsync(manager, "/Staff/Edit/8", "/Staff/Edit/8", new()
        {
            ["Id"] = "1",                          // try to redirect the update to the administrator
            ["Name"] = "Mei Wilson-Renamed",
            ["Email"] = "clerk.br001b@apexpost.local",
            ["Role"] = "CounterClerk",
            ["HomeBranchCode"] = "BR-001",
            ["IsActive"] = "true",
            ["PasswordHash"] = "PBKDF2$1$attacker$attacker",
            ["CreatedBy"] = "attacker",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var edited = _factory.Store.All<StaffMember>().Single(s => s.Id == 8);
        Assert.Equal("Mei Wilson-Renamed", edited.Name);
        Assert.Equal(hashBefore, edited.PasswordHash);
        var adminAfter = _factory.Store.All<StaffMember>().Single(s => s.Id == 1);
        Assert.Equal(adminBefore, (adminAfter.Name, adminAfter.Email, adminAfter.Role, adminAfter.PasswordHash));
    }

    [Fact]
    public async Task Branch_manager_cannot_promote_a_clerk_to_administrator_through_the_form()
    {
        using var manager = _factory.CreateBrowser();
        await LoginAsync(manager, "manager.br001@apexpost.local");

        using var response = await PostFormAsync(manager, "/Staff/Edit/8", "/Staff/Edit/8", new()
        {
            ["Name"] = "Mei Wilson",
            ["Email"] = "clerk.br001b@apexpost.local",
            ["Role"] = "Administrator",
            ["HomeBranchCode"] = "BR-001",
            ["IsActive"] = "true",
        });

        Assert.False(response.IsSuccessStatusCode && response.RequestMessage?.RequestUri?.AbsolutePath == "/Staff");
        Assert.Equal(StaffRole.CounterClerk, _factory.Store.All<StaffMember>().Single(s => s.Id == 8).Role);
    }

    [Fact]
    public async Task Sync_push_ignores_server_owned_fields_and_uses_the_device_branch()
    {
        using var device = _factory.CreateBrowser("127.0.0.1");
        device.DefaultRequestHeaders.Add(SyncHeaders.DeviceId, "TERM-TEST-01");
        device.DefaultRequestHeaders.Add(SyncHeaders.DeviceKey, TestDeviceKey);
        var localId = Guid.NewGuid();

        // Raw JSON with fields the contract does not have (branch, status, totals, server id, line totals).
        var body = $$"""
            {
              "schemaVersion": 1,
              "sales": [{
                "localTransactionId": "{{localId}}",
                "counterNumber": 1,
                "clerkStaffId": 7,
                "capturedAtUtc": "2026-10-25T02:00:00Z",
                "paymentMethod": "Cash",
                "branchCode": "BR-002",
                "status": "Voided",
                "serverTransactionId": 1,
                "totalAmount": 0.01,
                "lines": [{ "sku": "PKG-003", "quantity": 1, "unitPrice": 4.95, "lineTotal": 0.01, "taxAmount": 0 }],
                "moneyOrders": []
              }]
            }
            """;
        using var response = await device.PostAsync(new Uri("api/sync/transactions", UriKind.Relative), new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        var result = (await response.Content.ReadFromJsonAsync<PushTransactionsResponse>())!.Results.Single();

        var tx = _factory.Store.All<CounterTransaction>().Single(t => t.Id == result.ServerTransactionId);
        Assert.Equal(new BranchCode("BR-001"), tx.BranchCode);   // the device's registered branch
        Assert.Equal(TransactionStatus.Paid, tx.Status);          // the server's outcome
        Assert.NotEqual(1, tx.Id);
        Assert.Equal(new Money(4.95m), tx.LineItems.Single().LineTotal); // recomputed, not the posted 0.01
        Assert.True(tx.TotalAmount > new Money(4.95m));                  // includes server-calculated tax
    }

    private static int IdFrom(HttpResponseMessage response)
    {
        var location = response.Headers.Location!.ToString();
        Assert.Contains("/Transaction/Details/", location, StringComparison.Ordinal);
        return int.Parse(location[(location.LastIndexOf('/') + 1)..], System.Globalization.CultureInfo.InvariantCulture);
    }
}
