﻿namespace Atomos.UI.Interfaces;

public interface IFileLinkingService
{
    void EnableFileLinking();
    void DisableFileLinking();
    void EnableStartup();
    void DisableStartup();
}