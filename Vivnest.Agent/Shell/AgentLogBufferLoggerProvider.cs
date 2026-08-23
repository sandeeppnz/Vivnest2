using Microsoft.Extensions.Logging;
using Vivnest.Core.Runtime;

namespace Vivnest.Agent.Shell;

// Registered via builder.Logging.AddProvider before the host is built, so
// it observes every category's log calls the same way the Console
// provider does - LogShippingWorker only reads the buffer this writes to.
public sealed class AgentLogBufferLoggerProvider : ILoggerProvider
{
    private readonly IAgentLogBuffer _buffer;
    private readonly IAgentErrorSignalBuffer? _errorBuffer;
    private readonly LogLevel _minimumLevel;

    // errorBuffer is optional so the log-shipping behaviour this provider
    // originally existed for (ADR-027) is unchanged when operational
    // alerting is off - it just does not observe errors.
    public AgentLogBufferLoggerProvider(
        IAgentLogBuffer buffer,
        LogLevel minimumLevel,
        IAgentErrorSignalBuffer? errorBuffer = null)
    {
        _buffer = buffer;
        _minimumLevel = minimumLevel;
        _errorBuffer = errorBuffer;
    }

    public ILogger CreateLogger(string categoryName) =>
        new AgentLogBufferLogger(_buffer, _errorBuffer, categoryName, _minimumLevel);

    public void Dispose()
    {
    }

    private sealed class AgentLogBufferLogger : ILogger
    {
        private readonly IAgentLogBuffer _buffer;
        private readonly IAgentErrorSignalBuffer? _errorBuffer;
        private readonly string _category;
        private readonly LogLevel _minimumLevel;

        public AgentLogBufferLogger(
            IAgentLogBuffer buffer,
            IAgentErrorSignalBuffer? errorBuffer,
            string category,
            LogLevel minimumLevel)
        {
            _buffer = buffer;
            _errorBuffer = errorBuffer;
            _category = category;
            _minimumLevel = minimumLevel;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= _minimumLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var line = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}Z [{logLevel}] {_category}: {formatter(state, exception)}";

            if (exception is not null)
                line += Environment.NewLine + exception;

            _buffer.Add(line);

            // Sprint 8. Error and above only - Warning is far too noisy to
            // notify on, and this codebase logs Warning routinely for
            // things that are expected (AGENT_BUSY rejections, 404
            // fallbacks between blob layouts).
            //
            // The worker that drains this deliberately never logs Error
            // itself, so a failure to ship an error cannot generate
            // another one.
            if (_errorBuffer is not null && logLevel >= LogLevel.Error)
            {
                _errorBuffer.Add(new AgentErrorSignal(
                    _category,
                    formatter(state, exception),
                    exception?.ToString()));
            }
        }
    }
}
