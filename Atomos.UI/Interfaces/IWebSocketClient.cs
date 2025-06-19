using System;
using System.Threading.Tasks;
using Atomos.UI.Events;
using CommonLib.Models;

namespace Atomos.UI.Interfaces;

public interface IWebSocketClient
{
    Task ConnectAsync(int port);
    Task SendMessageAsync(WebSocketMessage message, string endpoint);
    event EventHandler<FileSelectionRequestedEventArgs> FileSelectionRequested;
    event EventHandler ModInstalled;
}