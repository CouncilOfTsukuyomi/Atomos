using System;
using System.Threading.Tasks;
using Atomos.UI.Interfaces;
using Atomos.UI.ViewModels;
using CommonLib.Interfaces;
using NLog;

namespace Atomos.UI.Services;

public class SentryManager : ISentryManager
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        
    private IDisposable? _sentryAcceptSubscription;
    private IDisposable? _sentryDeclineSubscription;

    public SentryPromptViewModel SentryPromptViewModel { get; }

    public event Func<Task>? SentryChoiceMade;

    public SentryManager(IConfigurationService configurationService, IWebSocketClient webSocketClient)
    {
        SentryPromptViewModel = new SentryPromptViewModel(configurationService, webSocketClient)
        {
            IsVisible = false
        };

        _sentryAcceptSubscription = SentryPromptViewModel.AcceptCommand.Subscribe(_ => OnSentryChoiceMade());
        _sentryDeclineSubscription = SentryPromptViewModel.DeclineCommand.Subscribe(_ => OnSentryChoiceMade());

        var userHasChosenSentry = (bool)configurationService.ReturnConfigValue(c => c.Common.UserChoseSentry);
        if (!userHasChosenSentry)
        {
            SentryPromptViewModel.IsVisible = true;
        }
    }

    private async void OnSentryChoiceMade()
    {
        _logger.Debug("Sentry choice made, notifying listeners");
            
        try
        {
            await Task.Delay(1000);
            if (SentryChoiceMade != null)
            {
                await SentryChoiceMade.Invoke();
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling Sentry choice completion");
        }
    }

    public void Dispose()
    {
        _sentryAcceptSubscription?.Dispose();
        _sentryDeclineSubscription?.Dispose();
        SentryPromptViewModel?.Dispose();
    }
}