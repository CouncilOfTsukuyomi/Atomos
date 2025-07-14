using System.Threading.Tasks;
using Avalonia.Controls.Notifications;

namespace Atomos.UI.Interfaces;

public interface ISystemNotificationService
{
    Task ShowSystemNotificationAsync(string title, string message, NotificationType type = NotificationType.Information);
    bool IsWindowHidden { get; }
}
