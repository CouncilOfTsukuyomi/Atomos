using System.Threading.Tasks;
using PenumbraModForwarder.Common.Enums;
using PenumbraModForwarder.UI.Models;

namespace PenumbraModForwarder.UI.Interfaces;

public interface INotificationService
{
    Task ShowNotification(string message, SoundType? soundType = null, int durationSeconds = 4);
    Task UpdateProgress(string title, string status, int progress);
    Task ShowErrorNotification(string errorMessage, SoundType? soundType = null, int durationSeconds = 6);
    Task RemoveNotification(Notification notification);
}