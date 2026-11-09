namespace ApexPost.Courier.Offline.Domain;

/// <summary>Document id format for scans: <c>scan::{Guid}</c>. The Guid is the scan id the API de-duplicates on.</summary>
public static class ScanIds
{
    public const string Prefix = "scan::";

    public static string ToDocumentId(Guid scanId) => Prefix + scanId.ToString("D");

    public static bool TryParse(string? documentId, out Guid scanId)
    {
        scanId = Guid.Empty;
        return documentId is not null
            && documentId.StartsWith(Prefix, StringComparison.Ordinal)
            && Guid.TryParseExact(documentId.AsSpan(Prefix.Length), "D", out scanId);
    }
}
