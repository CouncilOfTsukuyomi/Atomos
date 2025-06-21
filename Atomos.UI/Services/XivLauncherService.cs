using System;
using System.IO;
using System.Reflection;
using Atomos.UI.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;

namespace Atomos.UI.Services;

public class XivLauncherService : IXivLauncherService
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private const string ConfigFileName = "launcherConfigV3.json";

    public void EnableAutoStart(bool enable, string appPath, string label)
    {
        _logger.Debug("Entering EnableAutoStart with enable={Enable}, appPath={AppPath}, label={Label}", enable, appPath, label);

        var absoluteAppPath = Path.GetFullPath(appPath);

        if (enable)
        {
            _logger.Info("Enabling auto-start for {Label}", label);
            ModifyAddonList(addonArray => AddOrUpdateAddon(addonArray, absoluteAppPath, label));
        }
        else
        {
            _logger.Info("Disabling auto-start for {Label}", label);
            ModifyAddonList(addonArray => RemoveAddon(addonArray, label));
        }

        _logger.Debug("Exiting EnableAutoStart for label={Label}", label);
    }


    // .exe has been renamed to Launcher but I cba changing all the names
    public void EnableAutoStartWatchdog(bool enable)
    {
        _logger.Debug("Entering EnableAutoStartWatchdog with enable={Enable}", enable);

        var baseDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
        // NOTE: If the .exe's get changed again for some reason - check this
        var relativeWatchdogPath = "Atomos.Launcher.exe";
        var absoluteWatchdogPath = Path.Combine(baseDirectory, relativeWatchdogPath);

        if (!Path.IsPathRooted(absoluteWatchdogPath))
        {
            absoluteWatchdogPath = Path.GetFullPath(absoluteWatchdogPath);
        }

        const string watchdogLabel = "Atomos";

        EnableAutoStart(enable, absoluteWatchdogPath, watchdogLabel);

        _logger.Debug("Exiting EnableAutoStartWatchdog");
    }


    private void ModifyAddonList(Action<JArray> modifyAction)
    {
        var configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XIVLauncher"
        );
        var configFilePath = Path.Combine(configDir, ConfigFileName);

        if (!File.Exists(configFilePath))
        {
            _logger.Warn("XIVLauncher config not found; cannot modify AddonList.");
            return;
        }

        try
        {
            var backupPath = configFilePath + ".bak";
            File.Copy(configFilePath, backupPath, true);
            _logger.Debug("Created backup at {BackupPath}", backupPath);

            var jsonContent = File.ReadAllText(configFilePath);
            var root = JObject.Parse(jsonContent);

            var addonToken = root["AddonList"];
            var storedAsString = false;
            JArray addonArray;

            if (addonToken == null)
            {
                _logger.Debug("No AddonList found, creating new array.");
                addonArray = new JArray();
            }
            else if (addonToken.Type == JTokenType.Array)
            {
                _logger.Debug("AddonList found as array, using existing array.");
                addonArray = (JArray)addonToken;
            }
            else if (addonToken.Type == JTokenType.String)
            {
                _logger.Debug("AddonList found as string, parsing it into an array.");
                storedAsString = true;
                var strValue = addonToken.ToString();
                addonArray = string.IsNullOrWhiteSpace(strValue) ? new JArray() : JArray.Parse(strValue);
            }
            else
            {
                _logger.Debug("AddonList is in an unexpected format; using empty array instead.");
                addonArray = new JArray();
            }

            modifyAction(addonArray);

            if (storedAsString)
            {
                root["AddonList"] = addonArray.ToString(Formatting.None);
            }
            else
            {
                root["AddonList"] = addonArray;
            }

            File.WriteAllText(configFilePath, root.ToString(Formatting.Indented));
            _logger.Debug("Successfully modified XIVLauncher AddonList.");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to modify XIVLauncher AddonList.");
        }
    }

    private void AddOrUpdateAddon(JArray addonArray, string appPath, string label)
    {
        var existingObj = FindAddonObjectByLabel(addonArray, label);

        if (existingObj == null)
        {
            _logger.Debug("No existing addon found with label={Label}, creating a new entry.", label);

            var newEntry = new JObject
            {
                ["IsEnabled"] = true,
                ["Addon"] = new JObject
                {
                    ["Path"] = appPath,
                    ["CommandLine"] = "",
                    ["RunAsAdmin"] = false,
                    ["RunOnClose"] = false,
                    ["KillAfterClose"] = false,
                    ["Name"] = label
                }
            };

            addonArray.Add(newEntry);
        }
        else
        {
            _logger.Debug("Found existing addon with label={Label}, updating path and enabling.", label);
            existingObj["IsEnabled"] = true;
            existingObj["Addon"]["Path"] = appPath;
        }
    }

    private void RemoveAddon(JArray addonArray, string label)
    {
        var removedCount = 0;
        for (int i = addonArray.Count - 1; i >= 0; i--)
        {
            var entry = addonArray[i];
            var nameToken = entry["Addon"]?["Name"];
            if (nameToken?.ToString() == label)
            {
                addonArray.RemoveAt(i);
                removedCount++;
            }
        }

        if (removedCount == 0)
        {
            _logger.Debug("No addons found to remove with label={Label}.", label);
        }
        else
        {
            _logger.Debug("Removed {RemovedCount} addon(s) with label={Label}.", removedCount, label);
        }
    }

    private static JObject? FindAddonObjectByLabel(JArray addonArray, string label)
    {
        foreach (var item in addonArray)
        {
            if (item is JObject obj)
            {
                var addonNode = obj["Addon"];
                if (addonNode?["Name"]?.ToString() == label)
                {
                    return obj;
                }
            }
        }

        return null;
    }
}
