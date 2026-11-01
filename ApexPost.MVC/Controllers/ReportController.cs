using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ApexPost.Models.ValueObjects;
using ApexPost.MVC.Security;
using ApexPost.Services.Reports;

namespace ApexPost.MVC.Controllers;

/// <summary>
/// Reports. Each accepts <c>?format=json</c> and returns the same data as JSON (for export or a
/// spreadsheet), which also demonstrates JsonResult. Bad query strings return 400.
/// </summary>
public sealed class ReportController(IReportService reports) : ApexPostController
{
    public IActionResult Index() => View();

    [Authorize(Policy = Policies.SameBranchOnly)]
    public async Task<IActionResult> SyncConflicts(string? branch, string? format, CancellationToken cancellationToken)
    {
        if (!TryParseBranch(branch, out var code))
        {
            return BadRequest("Invalid branch code.");
        }

        var conflicts = await reports.GetSyncConflictsAsync(code, Actor, cancellationToken);
        return IsJson(format) ? Json(conflicts) : View(conflicts);
    }

    [Authorize(Policy = Policies.SameBranchOnly)]
    public async Task<IActionResult> DailySales(string? date, string? branch, string? format, CancellationToken cancellationToken)
    {
        if (!TryParseBranch(branch, out var code))
        {
            return BadRequest("Invalid branch code.");
        }

        DateOnly day;
        if (string.IsNullOrEmpty(date))
        {
            day = await reports.GetLatestTradingDayAsync(cancellationToken);
        }
        else if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day))
        {
            return BadRequest("Date must be yyyy-MM-dd.");
        }

        var report = await reports.GetDailySalesAsync(day, code, Actor, cancellationToken);
        return IsJson(format) ? Json(report) : View(report);
    }

    [Authorize(Policy = Policies.SameBranchOnly)]
    public async Task<IActionResult> LowStock(string? branch, int? threshold, string? format, CancellationToken cancellationToken)
    {
        if (!TryParseBranch(branch, out var code))
        {
            return BadRequest("Invalid branch code.");
        }

        var report = await reports.GetLowStockAsync(code, threshold, Actor, cancellationToken);
        return IsJson(format) ? Json(report) : View(report);
    }

    [Authorize(Policy = Policies.SameBranchOnly)]
    public async Task<IActionResult> ShippingVolume(string? from, string? to, string? branch, string? format, CancellationToken cancellationToken)
    {
        if (!TryParseBranch(branch, out var code) || !TryParseRange(from, to, out var start, out var end))
        {
            return BadRequest("Invalid branch or date range (yyyy-MM-dd).");
        }

        var report = await reports.GetShippingVolumeAsync(start, end, code, Actor, cancellationToken);
        return IsJson(format) ? Json(report) : View(report);
    }

    /// <summary>The auditor view: money orders and identity verifications per branch.</summary>
    [Authorize(Policy = Policies.AuditViewers)]
    [Authorize(Policy = Policies.SameBranchOnly)]
    public async Task<IActionResult> MoneyOrders(string? from, string? to, string? branch, string? format, CancellationToken cancellationToken)
    {
        if (!TryParseBranch(branch, out var code) || !TryParseRange(from, to, out var start, out var end))
        {
            return BadRequest("Invalid branch or date range (yyyy-MM-dd).");
        }

        var report = await reports.GetMoneyOrderAuditAsync(start, end, code, Actor, cancellationToken);
        return IsJson(format) ? Json(report) : View(report);
    }

    private static bool IsJson(string? format) => string.Equals(format, "json", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseBranch(string? branch, out BranchCode? code)
    {
        code = null;
        return string.IsNullOrEmpty(branch) || BranchCode.TryParse(branch, out code);
    }

    private static bool TryParseRange(string? from, string? to, out DateOnly start, out DateOnly end)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        start = today.AddDays(-30);
        end = today;
        return (string.IsNullOrEmpty(from) || DateOnly.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out start))
            && (string.IsNullOrEmpty(to) || DateOnly.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out end));
    }
}
