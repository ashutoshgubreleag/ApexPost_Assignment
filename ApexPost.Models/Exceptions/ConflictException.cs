namespace ApexPost.Models.Exceptions;

/// <summary>
/// The request is valid but clashes with the current state of the resource (duplicate tracking
/// number, illegal status transition, editing a locked shipment). Mapped to HTTP 409 by the API.
/// </summary>
public sealed class ConflictException : DomainException
{
    public ConflictException(string errorCode, string message) : base(message) => ErrorCode = errorCode;

    /// <summary>Stable machine-readable code such as <c>INVALID_STATUS_TRANSITION</c>.</summary>
    public string ErrorCode { get; }
}
