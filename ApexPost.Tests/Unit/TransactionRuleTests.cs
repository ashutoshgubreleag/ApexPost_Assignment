using ApexPost.Models.Domain;
using ApexPost.Models.Exceptions;
using ApexPost.Models.ValueObjects;
using ApexPost.Services.Branch;
using ApexPost.Services.Common;
using ApexPost.Services.Events;
using ApexPost.Services.Inventory;
using ApexPost.Services.Staff;
using ApexPost.Services.Transactions;
using ApexPost.Tests.Support;

namespace ApexPost.Tests.Unit;

public sealed class TransactionRuleTests
{
    [Fact]
    public async Task InactiveStaff_CannotOpenTransactions()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        var service = harness.Scoped<ITransactionService>();
        var formerClerk = new StaffContext(24, StaffRole.CounterClerk, new BranchCode("BR-002"), "Sam Former"); // inactive in seed data

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => service.OpenAsync(formerClerk, 1, CancellationToken.None));

        Assert.Equal("STAFF_INACTIVE", ex.RuleCode);
    }

    [Fact]
    public async Task DeactivatedClerk_WithAStillValidCookie_CannotProcessTheirBasket()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        var clerk = ServiceHarness.Clerk();
        var tx = await harness.Scoped<ITransactionService>().OpenAsync(clerk, 1, CancellationToken.None);
        await harness.Scoped<IStaffService>().DeactivateAsync(clerk.StaffId, ServiceHarness.Manager(), CancellationToken.None);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            harness.Scoped<ITransactionService>().AddRetailItemAsync(tx.Id, new AddRetailItemCommand { Sku = "PKG-001" }, clerk, CancellationToken.None));
    }

    [Fact]
    public async Task Clerk_CanOnlyTransactAtTheirAssignedBranch()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        var br002Basket = await harness.Scoped<ITransactionService>().OpenAsync(ServiceHarness.Clerk(10, "BR-002"), 1, CancellationToken.None);

        // A BR-001 clerk tries to add to a BR-002 basket.
        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            harness.Scoped<ITransactionService>().AddRetailItemAsync(br002Basket.Id, new AddRetailItemCommand { Sku = "PKG-001" }, ServiceHarness.Clerk(7, "BR-001"), CancellationToken.None));

        Assert.Equal("WRONG_BRANCH", ex.RuleCode);
    }

    [Fact]
    public async Task ForgedBranchClaim_IsRejected()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        var forged = new StaffContext(7, StaffRole.CounterClerk, new BranchCode("BR-003"), "clerk 7 claiming BR-003");

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => harness.Scoped<ITransactionService>().OpenAsync(forged, 1, CancellationToken.None));

        Assert.Equal("WRONG_BRANCH", ex.RuleCode);
    }

    [Fact]
    public async Task ClosedBranch_CannotAcceptTransactions()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        var hobartClerk = new StaffContext(22, StaffRole.CounterClerk, new BranchCode("BR-006"), "Hobart clerk");

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => harness.Scoped<ITransactionService>().OpenAsync(hobartClerk, 1, CancellationToken.None));

        Assert.Equal("BRANCH_CLOSED", ex.RuleCode);
    }

    [Fact]
    public async Task ClosingABranch_StopsOpenBasketsBeingChanged()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        var clerk = ServiceHarness.Clerk();
        var tx = await harness.Scoped<ITransactionService>().OpenAsync(clerk, 1, CancellationToken.None);
        await harness.Scoped<IBranchService>().SetStatusAsync(new BranchCode("BR-001"), BranchStatus.Closed, ServiceHarness.Manager(), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            harness.Scoped<ITransactionService>().PayAsync(tx.Id, PaymentMethod.Cash, clerk, CancellationToken.None));
        Assert.Equal("BRANCH_CLOSED", ex.RuleCode);
    }

    [Fact]
    public async Task Clerk_CannotVoid()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        var clerk = ServiceHarness.Clerk();
        var tx = await harness.Scoped<ITransactionService>().OpenAsync(clerk, 1, CancellationToken.None);

        await Assert.ThrowsAsync<ForbiddenOperationException>(() =>
            harness.Scoped<ITransactionService>().VoidAsync(tx.Id, new VoidOrRefundCommand { Reason = "Clerk tries" }, clerk, CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no")]
    public async Task Void_RequiresAReason(string reason)
    {
        await using var harness = await ServiceHarness.CreateAsync();
        var tx = await harness.Scoped<ITransactionService>().OpenAsync(ServiceHarness.Clerk(), 1, CancellationToken.None);

        await Assert.ThrowsAsync<CommandValidationException>(() =>
            harness.Scoped<ITransactionService>().VoidAsync(tx.Id, new VoidOrRefundCommand { Reason = reason }, ServiceHarness.Manager(), CancellationToken.None));
    }

    [Fact]
    public async Task ManagerFromAnotherBranch_CannotVoid()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        var tx = await harness.Scoped<ITransactionService>().OpenAsync(ServiceHarness.Clerk(), 1, CancellationToken.None);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            harness.Scoped<ITransactionService>().VoidAsync(tx.Id, new VoidOrRefundCommand { Reason = "Not my branch" }, ServiceHarness.Manager(9, "BR-002"), CancellationToken.None));
    }

    [Fact]
    public async Task Refund_RestocksAndRaisesEvent_AndOnlyAppliesToPaidTransactions()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        var clerk = ServiceHarness.Clerk();
        var service = harness.Scoped<ITransactionService>();
        var inventory = harness.Scoped<IInventoryService>();
        await inventory.AdjustAsync(new StockAdjustmentCommand { BranchCode = "BR-001", Sku = "OFF-003", QuantityChange = 20, Reason = "Test" }, ServiceHarness.Manager(), CancellationToken.None);
        var before = await inventory.GetAvailableQuantityAsync(new BranchCode("BR-001"), new Sku("OFF-003"), CancellationToken.None);
        var voided = new List<TransactionVoidedEventArgs>();
        harness.Get<IApexPostEvents>().TransactionVoided += (_, e) => voided.Add(e);

        var tx = await service.OpenAsync(clerk, 1, CancellationToken.None);
        await service.AddRetailItemAsync(tx.Id, new AddRetailItemCommand { Sku = "OFF-003", Quantity = 3 }, clerk, CancellationToken.None);
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => service.RefundAsync(tx.Id, new VoidOrRefundCommand { Reason = "Too early" }, ServiceHarness.Manager(), CancellationToken.None));

        await service.PayAsync(tx.Id, PaymentMethod.Card, clerk, CancellationToken.None);
        Assert.Equal(before - 3, await inventory.GetAvailableQuantityAsync(new BranchCode("BR-001"), new Sku("OFF-003"), CancellationToken.None));

        var refunded = await service.RefundAsync(tx.Id, new VoidOrRefundCommand { Reason = "Damaged in store" }, ServiceHarness.Manager(), CancellationToken.None);

        Assert.Equal(TransactionStatus.Refunded, refunded.Status);
        Assert.Equal(6, refunded.AuthorisedByStaffId);
        Assert.Equal(before, await inventory.GetAvailableQuantityAsync(new BranchCode("BR-001"), new Sku("OFF-003"), CancellationToken.None));
        Assert.True(Assert.Single(voided).IsRefund);
    }

    [Fact]
    public async Task PaidTransaction_CannotBeChanged()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        var clerk = ServiceHarness.Clerk();
        var service = harness.Scoped<ITransactionService>();
        var tx = await service.OpenAsync(clerk, 1, CancellationToken.None);
        await service.AddIdentityVerificationAsync(tx.Id, new() { CustomerName = "A", DocumentType = DocumentType.NationalId, DocumentNumber = "X99887766", Result = VerificationResult.Passed }, clerk, CancellationToken.None);
        await service.PayAsync(tx.Id, PaymentMethod.Cash, clerk, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            service.AddRetailItemAsync(tx.Id, new AddRetailItemCommand { Sku = "PKG-001" }, clerk, CancellationToken.None));
        Assert.Equal("TRANSACTION_NOT_OPEN", ex.RuleCode);
    }

    [Fact]
    public async Task EmptyBasket_CannotBePaid()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        var clerk = ServiceHarness.Clerk();
        var tx = await harness.Scoped<ITransactionService>().OpenAsync(clerk, 1, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => harness.Scoped<ITransactionService>().PayAsync(tx.Id, PaymentMethod.Cash, clerk, CancellationToken.None));
        Assert.Equal("BASKET_EMPTY", ex.RuleCode);
    }

    [Fact]
    public async Task Payment_RaisesTransactionCompletedEvent()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        var completed = new List<TransactionCompletedEventArgs>();
        harness.Get<IApexPostEvents>().TransactionCompleted += (_, e) => completed.Add(e);
        var clerk = ServiceHarness.Clerk();
        var service = harness.Scoped<ITransactionService>();
        var tx = await service.OpenAsync(clerk, 1, CancellationToken.None);
        await service.AddIdentityVerificationAsync(tx.Id, new() { CustomerName = "A", DocumentType = DocumentType.NationalId, DocumentNumber = "X99887766", Result = VerificationResult.Passed }, clerk, CancellationToken.None);

        await service.PayAsync(tx.Id, PaymentMethod.Cash, clerk, CancellationToken.None);

        Assert.Equal(tx.Id, Assert.Single(completed).TransactionId);
    }
}
