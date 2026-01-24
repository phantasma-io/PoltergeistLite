using System;

// Signals unrecoverable token mapping problems that should not be masked.
public sealed class TokenMappingException : Exception
{
    public TokenMappingException(string message) : base(message) { }
}
