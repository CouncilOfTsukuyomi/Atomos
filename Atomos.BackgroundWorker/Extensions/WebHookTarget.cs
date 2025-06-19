using Atomos.BackgroundWorker.Interfaces;
using CommonLib.Models;
using NLog;
using NLog.Targets;

namespace Atomos.BackgroundWorker.Extensions;

[Target("WebHook")]
public class WebHookTarget : TargetWithLayout
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    
    private readonly IWebSocketServer _webSocketServer;

    public string WebSocketEndpoint { get; set; } = "/error";
    public NLog.LogLevel MinimumLogLevel { get; set; } = NLog.LogLevel.Error;
    
    public WebHookTarget(IWebSocketServer webSocketServer)
    {
        _webSocketServer = webSocketServer;
    }

    protected override void Write(LogEventInfo logEvent)
    {
        var message = new WebSocketMessage
        {
            Type = "log",
            Status = logEvent.Level.Name.ToLower(),
            Message = Layout.Render(logEvent),
        };

        try
        {
            _webSocketServer.BroadcastToEndpointAsync(WebSocketEndpoint, message).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to broadcast log event: {ex.Message}");
        }
    }
}