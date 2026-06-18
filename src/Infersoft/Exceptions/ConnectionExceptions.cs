using System;

namespace Infersoft;

/// <summary>
/// Raised when a request could not reach the API — connection failures, resets, or protocol
/// errors, i.e. no HTTP response was received. The underlying transport error is the
/// <see cref="Exception.InnerException"/>. Following the OpenAI/Anthropic convention,
/// <see cref="ApiTimeoutException"/> is a subclass.
/// </summary>
public class ApiConnectionException : InfersoftException
{
    /// <summary>Initializes a new instance of the <see cref="ApiConnectionException"/> class.</summary>
    public ApiConnectionException()
    {
    }

    /// <summary>Initializes a new instance with the specified message.</summary>
    /// <param name="message">The error message.</param>
    public ApiConnectionException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with the specified message and inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying transport error.</param>
    public ApiConnectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Raised when a request exceeds its timeout (a kind of connection error).</summary>
public sealed class ApiTimeoutException : ApiConnectionException
{
    /// <summary>Initializes a new instance of the <see cref="ApiTimeoutException"/> class.</summary>
    public ApiTimeoutException()
    {
    }

    /// <summary>Initializes a new instance with the specified message.</summary>
    /// <param name="message">The error message.</param>
    public ApiTimeoutException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with the specified message and inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying timeout.</param>
    public ApiTimeoutException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
