using System;

namespace Infersoft;

/// <summary>
/// Base class for every error raised by the Infersoft SDK. Catch this to handle any SDK
/// failure; catch a specific subclass to react to a particular condition.
/// </summary>
public class InfersoftException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="InfersoftException"/> class.</summary>
    public InfersoftException()
    {
    }

    /// <summary>Initializes a new instance with the specified message.</summary>
    /// <param name="message">The error message.</param>
    public InfersoftException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with the specified message and inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying cause.</param>
    public InfersoftException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
