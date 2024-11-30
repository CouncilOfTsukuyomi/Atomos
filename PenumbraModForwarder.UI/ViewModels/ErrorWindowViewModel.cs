﻿namespace PenumbraModForwarder.UI.ViewModels;

public class ErrorWindowViewModel : ViewModelBase
{
    public string ErrorMessage { get; } = "Please launch the application through the main executable.\n" +
                                          "This ensures proper monitoring and crash recovery.";
}