using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Infersoft.Tests.Support;

/// <summary>An <see cref="ILogger"/> that records warning messages for assertions.</summary>
internal sealed class ListLogger : ILogger
{
    public List<string> Warnings { get; } = new();

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel == LogLevel.Warning)
        {
            Warnings.Add(formatter(state, exception));
        }
    }
}
