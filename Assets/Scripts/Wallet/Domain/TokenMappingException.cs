using System;

// Signals unrecoverable token mapping problems that should not be masked.
public sealed class TokenMappingException : Exception
{
    // Carbon token id that could not be resolved, when known. Callers can use this to
    // attempt a targeted lazy re-fetch of just this token before surfacing the error.
    // Null when the failure is not tied to a specific, individually fetchable token id.
    public ulong? CarbonId { get; }

    public TokenMappingException(string message) : base(message) { }

    public TokenMappingException(string message, ulong carbonId) : base(message)
    {
        CarbonId = carbonId;
    }
}
