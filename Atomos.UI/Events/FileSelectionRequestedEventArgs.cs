﻿using System;
using System.Collections.Generic;

namespace Atomos.UI.Events;

public class FileSelectionRequestedEventArgs : EventArgs
{
    public List<string> AvailableFiles { get; }
    public string TaskId { get; }
    public bool IsArchiveSelection { get; set; }

    public FileSelectionRequestedEventArgs(List<string> availableFiles, string taskId)
    {
        AvailableFiles = availableFiles;
        TaskId = taskId;
        IsArchiveSelection = false;
    }
}