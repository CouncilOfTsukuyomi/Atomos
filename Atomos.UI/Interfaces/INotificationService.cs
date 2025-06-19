using System.Threading.Tasks;
using Atomos.UI.Models;
using CommonLib.Enums;

namespace Atomos.UI.Interfaces;

public interface INotificationService
{
    Task ShowNotification(string title, string message, SoundType? soundType = null, int durationSeconds = 4);
    Task UpdateProgress(string taskId, string title, string status, int progress);
    Task ShowErrorNotification(string title, string message, SoundType? soundType = null, int durationSeconds = 6);
    Task RemoveNotificationAsync(Notification notification);
}