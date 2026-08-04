using Microsoft.Extensions.Logging;

namespace Vivnest.Agent.Runtime.Shell;

// Registered via builder.Logging.AddProvider before the host is built, so
// it observes every category's log calls the same way the Console
// provider does - LogShippingWorker only reads the buffer this writes to.
public sealed class AgentLogBufferLoggerProvider : ILoggerProvider
{
    private readonly IAgentLogBuffer _buffer;
    private readonly LogLevel _minimumLevel;

    public AgentLogBufferLoggerProvider(IAgentLogBuffer buffer, LogLevel minimumLevel)
    {
        _buffer = buffer;
        _minimumLevel = minimumLevel;
    }

    public ILogger CreateLogger(string categoryName) => new AgentLogBufferLogger(_buffer, categoryName, _minimumLevel);

    public void Dispose()
    {
    }

    private sealed class AgentLogBufferLogger : ILogger
    {
        private readonly IAgentLogBuffer _buffer;
        private readonly string _category;
        private readonly LogLevel _minimumLevel;

        public AgentLogBufferLogger(IAgentLogBuffer buffer, string category, LogLevel minimumLevel)
        {
            _buffer = buffer;
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
        }
    }
}
