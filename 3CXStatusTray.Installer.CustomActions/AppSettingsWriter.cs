using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WriteAppSettings;

/// <summary>
/// Pure logic for generating the tray's appsettings.json from install-time
/// property values. No file I/O, no MSI dependencies — lives here so it can
/// be unit-tested on any platform.
/// </summary>
public static class AppSettingsWriter
{
    public static string GenerateJson(IReadOnlyDictionary<string, string?> properties)
    {
        var settings = new JsonObject
        {
            ["ServerURLBasePath"] = ValueOrDefault(properties, "SERVER_URL", "http://localhost:8889/"),
            ["ApiKey"] = ValueOrDefault(properties, "API_KEY", ""),
            ["PollIntervalMilliseconds"] = ParseIntOrDefault(properties, "POLL_INTERVAL_MS", 5000),
            ["ExtensionId"] = ValueOrDefault(properties, "EXTENSION_ID", "100"),
            ["BalloonTipDisplayMilliseconds"] = 10000,
            ["Icons"] = new JsonObject
            {
                ["Available"] = "app-on.ico",
                ["OutOfOffice"] = "app-off.ico",
                ["Default"] = "app-default.ico"
            },
            ["ProfileShortCodes"] = new JsonObject
            {
                ["Available"] = "available",
                ["OutOfOffice"] = "out_of_office"
            }
        };

        var root = new JsonObject
        {
            ["Settings"] = settings
        };

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static string ValueOrDefault(IReadOnlyDictionary<string, string?> props, string key, string fallback)
    {
        if (props.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v))
        {
            return v!;
        }
        return fallback;
    }

    private static int ParseIntOrDefault(IReadOnlyDictionary<string, string?> props, string key, int fallback)
    {
        if (props.TryGetValue(key, out var v) && int.TryParse(v, out var parsed))
        {
            return parsed;
        }
        return fallback;
    }
}
