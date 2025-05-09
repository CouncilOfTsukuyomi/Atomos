using System.Net.WebSockets;
using CommonLib.Models;
using PenumbraModForwarder.BackgroundWorker.Events;

namespace PenumbraModForwarder.BackgroundWorker.Interfaces;

public interface IWebSocketServer
{
    void Start(int port);
    Task HandleConnectionAsync(WebSocket webSocket, string endpoint);
    Task BroadcastToEndpointAsync(string endpoint, WebSocketMessage message);
    bool HasConnectedClients();
    event EventHandler<WebSocketMessageEventArgs> MessageReceived;
}