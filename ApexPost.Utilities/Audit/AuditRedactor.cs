using ApexPost.Services.Abstractions;
using System.Text.RegularExpressions;

namespace ApexPost.Utilities.Audit;

/// <summary>
/// Second line of defence for personal data in the audit log. The audit filter only records
/// whitelisted fields, but if a developer whitelists something sensitive by mistake this still
/// blanks it: anything whose name looks like a password, secret, key, token, card or document
/// number is replaced, and long digit runs (card or document numbers typed into a free-text field)
/// are masked to the last four.
/// </summary>
public static partial class AuditRedactor
{
    public const string Redacted = "[REDACTED]";

    public static bool IsSensitiveName(string name) => SensitiveName().IsMatch(name ?? string.Empty);

    public static string Redact(string name, string? value)
    {
        if (IsSensitiveName(name))
        {
            return Redacted;
        }

        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var truncated = value.Length > 200 ? value[..200] + "…" : value;
        return LongDigitRun().Replace(truncated, m => new string('*', m.Length - 4) + m.Value[^4..]);
    }

    [GeneratedRegex("pass(word)?|secret|token|api[-_]?key|card|cvv|^pan$|document[-_]?(number|no)|^pin$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveName();

    [GeneratedRegex("[0-9]{8,}")]
    private static partial Regex LongDigitRun();
}
