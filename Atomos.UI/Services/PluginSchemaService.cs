using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Atomos.UI.Models;
using NLog;

namespace Atomos.UI.Services;

public static class PluginSchemaService
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static async Task<JsonDocument?> GetPluginSchemaAsync(string pluginDirectory)
    {
        try
        {
            var pluginJsonPath = Path.Combine(pluginDirectory, "plugin.json");
            if (!File.Exists(pluginJsonPath))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(pluginJsonPath);
            var jsonDoc = JsonDocument.Parse(json);
            
            if (jsonDoc.RootElement.TryGetProperty("configuration", out var configElement) &&
                configElement.TryGetProperty("schema", out var schemaElement))
            {
                var schemaJson = schemaElement.GetRawText();
                return JsonDocument.Parse(schemaJson);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get plugin schema for {PluginDirectory}", pluginDirectory);
        }

        return null;
    }

    public static (string displayName, string description, List<EnumOption>? enumOptions) GetSchemaMetadata(string key, JsonDocument? schema)
    {
        var displayName = FormatDisplayName(key);
        var description = $"Configuration setting for {key}";
        List<EnumOption>? enumOptions = null;

        if (schema?.RootElement.TryGetProperty("properties", out var properties) == true)
        {
            if (properties.TryGetProperty(key, out var propertySchema))
            {
                if (propertySchema.TryGetProperty("title", out var titleElement))
                {
                    var title = titleElement.GetString();
                    if (!string.IsNullOrEmpty(title))
                    {
                        displayName = title;
                    }
                }

                if (propertySchema.TryGetProperty("description", out var descriptionElement))
                {
                    var desc = descriptionElement.GetString();
                    if (!string.IsNullOrEmpty(desc))
                    {
                        description = desc;
                    }
                }
                
                if (propertySchema.TryGetProperty("enum", out var enumElement) && enumElement.ValueKind == JsonValueKind.Array)
                {
                    enumOptions = ExtractEnumOptions(propertySchema, enumElement);
                }
                else if (propertySchema.TryGetProperty("type", out var typeElement) && typeElement.GetString() == "array")
                {
                    if (propertySchema.TryGetProperty("items", out var itemsElement) && 
                        itemsElement.TryGetProperty("enum", out var arrayEnumElement))
                    {
                        enumOptions = ExtractEnumOptions(itemsElement, arrayEnumElement);
                    }
                }
            }
        }

        return (displayName, description, enumOptions);
    }

    private static List<EnumOption> ExtractEnumOptions(JsonElement propertySchema, JsonElement enumElement)
    {
        var enumOptions = new List<EnumOption>();
        var enumTitles = new List<string>();
        
        if (propertySchema.TryGetProperty("enumTitles", out var enumTitlesElement) && enumTitlesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var enumTitleElement in enumTitlesElement.EnumerateArray())
            {
                enumTitles.Add(enumTitleElement.GetString() ?? "");
            }
        }
        
        var enumIndex = 0;
        foreach (var enumValueElement in enumElement.EnumerateArray())
        {
            object? value = enumValueElement.ValueKind switch
            {
                JsonValueKind.String => enumValueElement.GetString(),
                JsonValueKind.Number => enumValueElement.GetInt32(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => enumValueElement.GetRawText()
            };
            
            var title = enumIndex < enumTitles.Count ? enumTitles[enumIndex] : value?.ToString() ?? "";
            enumOptions.Add(new EnumOption(value!, title));
            enumIndex++;
        }
        
        return enumOptions;
    }

    private static string FormatDisplayName(string key)
    {
        return System.Text.RegularExpressions.Regex.Replace(key, "(\\B[A-Z])", " $1");
    }
}