using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using Atomos.BackgroundWorker.Events;
using Atomos.BackgroundWorker.Interfaces;
using CommonLib.Events;
using CommonLib.Interfaces;
using CommonLib.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using CustomWebSocketMessageType = CommonLib.Models.WebSocketMessageType;
using WebSocketMessageType = System.Net.WebSockets.WebSocketMessageType;

namespace Atomos.BackgroundWorker.Services;

public class WebSocketServer : IWebSocketServer, IDisposable
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly IConfigurationService _configurationService;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<WebSocket, ConnectionInfo>> _endpoints;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private IWebHost? _webHost;
    private bool _isStarted;
    private int _port;
    private readonly string _serverId;
    
    private static readonly ConcurrentDictionary<WebSocket, SemaphoreSlim> SocketLockMap = new();

    private readonly ConcurrentDictionary<string, DateTime> _lastMessageTime = new();
    private readonly TimeSpan _messageDebounceInterval = TimeSpan.FromMilliseconds(100);

    // Valid endpoint allowlist for security
    private static readonly HashSet<string> ValidEndpoints = new(StringComparer.OrdinalIgnoreCase)
    {
        "/config",
        "/error",
        "/status",
        "/notifications",
        "/install"
    };

    public event EventHandler<WebSocketMessageEventArgs> MessageReceived;

    /// <summary>
    /// Validates and sanitises the endpoint path to prevent log injection and path traversal attacks
    /// </summary>
    private static string SanitizeEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return "/unknown";

        // Normalise the path
        endpoint = endpoint.Trim();

        // Ensure it starts with /
        if (!endpoint.StartsWith('/'))
            endpoint = "/" + endpoint;

        // Remove any dangerous characters that could be used for log injection
        endpoint = endpoint.Replace("\r", "").Replace("\n", "").Replace("\t", "");

        // Validate against allowlist
        if (!ValidEndpoints.Contains(endpoint))
        {
            _logger.Warn("Attempted connection to non-whitelisted endpoint, rejecting");
            return "/invalid";
        }

        return endpoint;
    }

    public WebSocketServer(IConfigurationService configurationService)
    {
        _configurationService = configurationService;
        _endpoints = new ConcurrentDictionary<string, ConcurrentDictionary<WebSocket, ConnectionInfo>>();
        _cancellationTokenSource = new CancellationTokenSource();

        _serverId = Guid.NewGuid().ToString("N");

        _configurationService.ConfigurationChanged += OnConfigurationChanged;
    }

    public void Start(int port)
    {
        if (_isStarted)
            return;

        _port = port;
        try
        {
            _webHost = new WebHostBuilder()
                .UseKestrel(options =>
                {
                    options.ListenLocalhost(_port);
                })
                .Configure(app =>
                {
                    app.UseWebSockets();
                    app.Run(async context =>
                    {
                        if (context.WebSockets.IsWebSocketRequest)
                        {
                            var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                            var rawEndpoint = context.Request.Path.ToString();
                            var sanitizedEndpoint = SanitizeEndpoint(rawEndpoint);

                            // Reject invalid endpoints
                            if (sanitizedEndpoint == "/invalid")
                            {
                                await webSocket.CloseAsync(
                                    WebSocketCloseStatus.PolicyViolation,
                                    "Invalid endpoint",
                                    CancellationToken.None);
                                return;
                            }

                            await HandleConnectionAsync(webSocket, sanitizedEndpoint);
                        }
                        else
                        {
                            context.Response.StatusCode = 400;
                        }
                    });
                })
                .Build();

            _webHost.Start();
            _isStarted = true;

            _logger.Info("WebSocket server started successfully on port {Port}", _port);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to start WebSocket server");
            throw;
        }
    }

    public async Task HandleConnectionAsync(WebSocket webSocket, string endpoint)
    {
        var connections = _endpoints.GetOrAdd(endpoint, _ => new ConcurrentDictionary<WebSocket, ConnectionInfo>());
        var connectionInfo = new ConnectionInfo
        {
            LastPing = DateTime.UtcNow
        };
        connections.TryAdd(webSocket, connectionInfo);

        _logger.Info("Client connected to endpoint {Endpoint}", endpoint);

        try
        {
            await ReceiveMessagesAsync(webSocket, endpoint);
        }
        catch (WebSocketException ex) when (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            _logger.Error(ex, "WebSocket error for endpoint {Endpoint}", endpoint);
        }
        catch (OperationCanceledException)
        {
            _logger.Debug("WebSocket connection closed during shutdown for endpoint {Endpoint}", endpoint);
        }
        finally
        {
            await RemoveConnectionAsync(webSocket, endpoint);
        }
    }

    private async Task ReceiveMessagesAsync(WebSocket webSocket, string endpoint)
    {
        var buffer = new byte[1024 * 4];

        while (webSocket.State == WebSocketState.Open && !_cancellationTokenSource.Token.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer), _cancellationTokenSource.Token);
            }
            catch (WebSocketException ex) when (ex.InnerException is HttpListenerException { ErrorCode: 995 })
            {
                _logger.Debug("WebSocket aborted by HttpListener (995) for endpoint {Endpoint}", endpoint);
                break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error receiving WebSocket messages from {Endpoint}", endpoint);
                break;
            }

            if (result.MessageType == WebSocketMessageType.Text)
            {
                var messageJson = Encoding.UTF8.GetString(buffer, 0, result.Count);
                _logger.Info("Received message from {Endpoint}: {MessageJson}", endpoint, messageJson);

                var message = JsonConvert.DeserializeObject<WebSocketMessage>(messageJson);
                if (message == null)
                {
                    _logger.Warn("Unable to deserialize WebSocketMessage from {Endpoint}", endpoint);
                    continue;
                }

                if (message.ClientId == _serverId)
                {
                    _logger.Debug("Ignoring message from this server: {MessageJson}", messageJson);
                    continue;
                }

                if (message.Type?.Equals("configuration_change", StringComparison.OrdinalIgnoreCase) == true)
                {
                    _logger.Debug("Handling message type 'configuration_change' from {Endpoint}", endpoint);
                    HandleConfigurationChange(message);
                }
                else if (message.Type == CustomWebSocketMessageType.Status && message.Status == "config_update")
                {
                    HandleConfigUpdateMessage(message);
                }
                else
                {
                    MessageReceived?.Invoke(this, new WebSocketMessageEventArgs
                    {
                        Endpoint = endpoint,
                        Message = message
                    });
                }
            }
            else if (result.MessageType == WebSocketMessageType.Close)
            {
                await CloseWebSocketAsync(webSocket);
                break;
            }
        }
    }
    
    private void HandleConfigurationChange(WebSocketMessage message)
    {
        _logger.Debug("Message indicates a configuration change: {Message}", message.Message);
        try
        {
            var updateData = JsonConvert.DeserializeObject<Dictionary<string, object>>(message.Message);
            if (updateData == null)
            {
                _logger.Warn("configuration_change message had invalid or non-JSON payload.");
                return;
            }

            if (!updateData.ContainsKey("PropertyPath") || !updateData.ContainsKey("NewValue"))
            {
                _logger.Warn("configuration_change payload missing 'PropertyPath' or 'NewValue'.");
                return;
            }

            var propertyPath = updateData["PropertyPath"]?.ToString();
            var newValue = updateData["NewValue"];
            var sourceId = $"websocket_{message.ClientId ?? "unknown"}";
            
            if (newValue is JArray jArray)
            {
                var convertedList = jArray.ToObject<List<string>>();
                newValue = convertedList;
                _logger.Debug("Converted JArray to List<string> with {Count} items: [{Items}]", 
                    convertedList.Count, string.Join(", ", convertedList));
            }
            else if (newValue is JObject jObject)
            {
                newValue = jObject.ToObject<Dictionary<string, object>>();
                _logger.Debug("Converted JObject to Dictionary");
            }
            
            _logger.Debug("About to call UpdateConfigFromExternal - PropertyPath: '{PropertyPath}', Value: '{Value}', ValueType: '{ValueType}', SourceId: '{SourceId}'", 
                propertyPath, newValue, newValue?.GetType().Name, sourceId);

            _configurationService.UpdateConfigFromExternal(propertyPath, newValue, sourceId);
            
            _logger.Debug("UpdateConfigFromExternal completed successfully");
            
            _logger.Info("Configuration updated for '{PropertyPath}' from source: {SourceId}", propertyPath, sourceId);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling configuration_change message");
        }
    }

    private void HandleConfigUpdateMessage(WebSocketMessage message)
    {
        _logger.Info("Processing config update request: {Data}", message.Message);
        try
        {
            var updateData = JsonConvert.DeserializeObject<Dictionary<string, object>>(message.Message);
            if (updateData != null &&
                updateData.ContainsKey("PropertyPath") &&
                updateData.ContainsKey("Value"))
            {
                var propertyPath = updateData["PropertyPath"].ToString();
                var newValue = updateData["Value"];
                var sourceId = $"websocket_{message.ClientId ?? "unknown"}";

                _logger.Debug("Updating config property at path {Path} to new value: {Value} from source: {SourceId}",
                    propertyPath, newValue, sourceId);

                _configurationService.UpdateConfigFromExternal(propertyPath, newValue, sourceId);
                _logger.Debug("Config update completed for property path {Path} from source: {SourceId}", 
                    propertyPath, sourceId);
            }
            else
            {
                _logger.Warn("Unable to process config update, required keys not found in message data.");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling config update message.");
        }
    }

    private async Task CloseWebSocketAsync(WebSocket webSocket)
    {
        if (webSocket.State == WebSocketState.Open)
        {
            try
            {
                await webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Server shutting down",
                    CancellationToken.None
                );
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Error during WebSocket closure");
            }
        }
    }

    public async Task BroadcastToEndpointAsync(string endpoint, WebSocketMessage message)
    {
        message.ClientId = _serverId;

        if (!_endpoints.TryGetValue(endpoint, out var connections) || !connections.Any())
        {
            _logger.Debug("No clients connected to endpoint {Endpoint}, message not sent", endpoint);
            return;
        }

        var json = JsonConvert.SerializeObject(message);
        var bytes = Encoding.UTF8.GetBytes(json);

        var deadSockets = new List<WebSocket>();
        foreach (var (socket, _) in connections)
        {
            var sem = SocketLockMap.GetOrAdd(socket, _ => new SemaphoreSlim(1, 1));

            try
            {
                await sem.WaitAsync(_cancellationTokenSource.Token);

                if (socket.State == WebSocketState.Open)
                {
                    _logger.Info("Sending message to endpoint {Endpoint}: {Message}", endpoint, json);
                    await socket.SendAsync(
                        new ArraySegment<byte>(bytes),
                        WebSocketMessageType.Text,
                        true,
                        _cancellationTokenSource.Token
                    );
                }
                else
                {
                    deadSockets.Add(socket);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error broadcasting to client");
                deadSockets.Add(socket);
            }
            finally
            {
                sem.Release();
            }
        }

        foreach (var socket in deadSockets)
        {
            await RemoveConnectionAsync(socket, endpoint);
        }
    }

    public bool HasConnectedClients()
    {
        return _endpoints.Any(e => e.Value.Any());
    }

    private async Task RemoveConnectionAsync(WebSocket socket, string endpoint)
    {
        if (_endpoints.TryGetValue(endpoint, out var connections))
        {
            connections.TryRemove(socket, out _);
            await CloseWebSocketAsync(socket);
        }
    }

    public void Dispose()
    {
        try
        {
            _isStarted = false;
            _cancellationTokenSource.Cancel();

            var closeTasks = _endpoints
                .SelectMany(ep => ep.Value.Select(async connection => await CloseWebSocketAsync(connection.Key)))
                .ToList();

            Task.WhenAll(closeTasks).Wait(TimeSpan.FromSeconds(5));

            _webHost?.StopAsync(TimeSpan.FromSeconds(5)).Wait();
            _webHost?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during WebSocket server disposal");
        }
    }

    private async void OnConfigurationChanged(object sender, ConfigurationChangedEventArgs e)
    {
        try
        {
            var debounceKey = $"{e.PropertyName}:{e.NewValue}";
            var now = DateTime.UtcNow;
            
            if (_lastMessageTime.TryGetValue(debounceKey, out var lastTime) && 
                now - lastTime < _messageDebounceInterval)
            {
                _logger.Debug("Debouncing configuration change for {PropertyName}", e.PropertyName);
                return;
            }
            
            _lastMessageTime[debounceKey] = now;
            
            var cutoff = now.Subtract(TimeSpan.FromMinutes(1));
            var keysToRemove = _lastMessageTime
                .Where(kvp => kvp.Value < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();
            
            foreach (var key in keysToRemove)
            {
                _lastMessageTime.TryRemove(key, out _);
            }

            _logger.Debug(
                "OnConfigurationChanged triggered for property {PropertyName} with new value: {NewValue} from source: {SourceId}",
                e.PropertyName,
                e.NewValue,
                e.SourceId ?? "local"
            );

            if (e.SourceId != null && e.SourceId.StartsWith("websocket_"))
            {
                _logger.Debug("Skipping WebSocket broadcast for change from WebSocket source: {SourceId}", e.SourceId);
                await BroadcastConfigurationChange(e.PropertyName, e.NewValue, e.SourceId);
                return;
            }

            var updateData = new Dictionary<string, object>
            {
                { "PropertyPath", e.PropertyName },
                { "Value", e.NewValue },
                { "Timestamp", e.Timestamp.ToString("O") },
                { "SourceId", _serverId }
            };

            var message = new WebSocketMessage
            {
                Type = CustomWebSocketMessageType.Status,
                Status = "config_update",
                Message = JsonConvert.SerializeObject(updateData),
                ClientId = _serverId
            };

            _logger.Debug("Broadcasting config change to /config endpoint: {Payload}", message.Message);
            await BroadcastToEndpointAsync("/config", message);
            _logger.Debug("Config change broadcast completed");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error broadcasting config change");
        }
    }

    private async Task BroadcastConfigurationChange(string propertyPath, object newValue, string sourceId)
    {
        var changeNotification = new WebSocketMessage
        {
            Type = CustomWebSocketMessageType.Status,
            Status = "config_changed",
            Message = JsonConvert.SerializeObject(new
            {
                PropertyPath = propertyPath,
                NewValue = newValue,
                SourceId = sourceId
            })
        };

        await BroadcastToEndpointAsync("/config", changeNotification);
    }
}

public class ConnectionInfo
{
    public DateTime LastPing { get; set; }
}