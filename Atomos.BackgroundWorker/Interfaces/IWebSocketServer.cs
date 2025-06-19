using System.Net.WebSockets;
using Atomos.BackgroundWorker.Events;
using CommonLib.Models;

namespace Atomos.BackgroundWorker.Interfaces;

public interface IWebSocketServer
{
    void Start(int port);
    Task HandleConnectionAsync(WebSocket webSocket, string endpoint);
    Task BroadcastToEndpointAsync(string endpoint, WebSocketMessage message);
    bool HasConnectedClients();
    event EventHandler<WebSocketMessageEventArgs> MessageReceived;
}