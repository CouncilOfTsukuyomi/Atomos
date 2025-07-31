namespace Atomos.UI.ViewModels;

public class ErrorWindowViewModel : ViewModelBase
{
    public string ErrorMessage { get; } = "Please launch Atomos.Launcher.exe.\n" +
                                          "This ensures proper monitoring and crash recovery.";
}