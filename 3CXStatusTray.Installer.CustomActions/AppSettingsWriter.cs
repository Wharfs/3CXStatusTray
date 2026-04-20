using System.Collections.Generic;
using System.Text;

namespace WriteAppSettings;

/// <summary>
/// Pure logic for generating the tray's appsettings.json from install-time
/// property values. No file I/O, no MSI dependencies — and deliberately no
/// NuGet dependencies either, so the Custom Action assembly is packable
/// by WiX DTF's MakeSfxCA without dragging in a tree of System.* NuGet
/// DLLs that'd need to be bundled alongside it.
/// </summary>
public static class AppSettingsWriter
{
    public static string GenerateJson(IReadOnlyDictionary<string, string?> properties)
    {
        var serverUrl = ValueOrDefault(properties, "SERVER_URL", "http://localhost:8889/");
        var apiKey = ValueOrDefault(properties, "API_KEY", "");
        var extensionId = ValueOrDefault(properties, "EXTENSION_ID", "100");
        var pollInterval = ParseIntOrDefault(properties, "POLL_INTERVAL_MS", 5000);

        var sb = new StringBuilder();
        sb.Append('{').Append('\n');
        sb.Append("  \"Settings\": {").Append('\n');
        AppendString(sb, "ServerURLBasePath", serverUrl, 4, comma: true);
        AppendString(sb, "ApiKey", apiKey, 4, comma: true);
        AppendNumber(sb, "PollIntervalMilliseconds", pollInterval, 4, comma: true);
        AppendString(sb, "ExtensionId", extensionId, 4, comma: true);
        AppendNumber(sb, "BalloonTipDisplayMilliseconds", 10000, 4, comma: true);
        sb.Append("    \"Icons\": {").Append('\n');
        AppendString(sb, "Available", "app-on.ico", 6, comma: true);
        AppendString(sb, "OutOfOffice", "app-off.ico", 6, comma: true);
        AppendString(sb, "Default", "app-default.ico", 6, comma: false);
        sb.Append("    },").Append('\n');
        sb.Append("    \"ProfileShortCodes\": {").Append('\n');
        AppendString(sb, "Available", "available", 6, comma: true);
        AppendString(sb, "OutOfOffice", "out_of_office", 6, comma: false);
        sb.Append("    }").Append('\n');
        sb.Append("  }").Append('\n');
        sb.Append('}').Append('\n');
        return sb.ToString();
    }

    private static void AppendString(StringBuilder sb, string key, string value, int indent, bool comma)
    {
        sb.Append(' ', indent).Append('"').Append(key).Append("\": \"").Append(EscapeJson(value)).Append('"');
        if (comma) sb.Append(',');
        sb.Append('\n');
    }

    private static void AppendNumber(StringBuilder sb, string key, int value, int indent, bool comma)
    {
        sb.Append(' ', indent).Append('"').Append(key).Append("\": ").Append(value);
        if (comma) sb.Append(',');
        sb.Append('\n');
    }

    private static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            switch (c)
            {
                case '"':  sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.AppendFormat("\\u{0:X4}", (int)c);
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
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
