﻿using Atomos.BackgroundWorker.Extensions;
using Atomos.BackgroundWorker.Interfaces;
using NLog.Config;

namespace Atomos.BackgroundWorker.Helpers;

public static class WebHookTargetExtensions
{
    public static void AddWebHookTarget(
        this LoggingConfiguration config,
        string targetName,
        IWebSocketServer webSocketServer,
        NLog.LogLevel minLevel,
        string endpoint = "/error")
    {
        var target = new WebHookTarget(webSocketServer)
        {
            Name = targetName,
            WebSocketEndpoint = endpoint,
            Layout = $"${{level:uppercase=true}} | Background Worker | ${{message}}${{exception}}",
            MinimumLogLevel = minLevel
        };
        
        config.AddTarget(targetName, target);
        config.AddRule(minLevel, NLog.LogLevel.Fatal, target, "*");
    }
}