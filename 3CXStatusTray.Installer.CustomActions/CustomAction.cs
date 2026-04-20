using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
using WixToolset.Dtf.WindowsInstaller;

namespace WriteAppSettings;

public static class CustomActions
{
    // Entry point declared in Product.wxs via DllEntry="WriteAppSettings".
    // Runs deferred (after file copy), with elevated rights.
    //
    // Reads properties marshalled via CustomActionData (set by the
    // SetWriteAppSettingsData immediate action):
    //   INSTALLFOLDER, SERVER_URL, API_KEY, EXTENSION_ID, POLL_INTERVAL_MS
    [CustomAction]
    public static ActionResult WriteAppSettings(Session session)
    {
        try
        {
            var data = session.CustomActionData;

            if (!data.TryGetValue("INSTALLFOLDER", out var installFolder) || string.IsNullOrEmpty(installFolder))
            {
                session.Log("WriteAppSettings: INSTALLFOLDER missing from CustomActionData");
                return ActionResult.Failure;
            }

            var props = new Dictionary<string, string?>
            {
                ["SERVER_URL"] = data.TryGetValue("SERVER_URL", out var u) ? u : null,
                ["API_KEY"] = data.TryGetValue("API_KEY", out var k) ? k : null,
                ["EXTENSION_ID"] = data.TryGetValue("EXTENSION_ID", out var e) ? e : null,
                ["POLL_INTERVAL_MS"] = data.TryGetValue("POLL_INTERVAL_MS", out var p) ? p : null,
            };

            var json = AppSettingsWriter.GenerateJson(props);
            var path = Path.Combine(installFolder, "appsettings.json");
            File.WriteAllText(path, json);
            session.Log($"WriteAppSettings: wrote {path}");

            // Write upgrade-memory registry values so a subsequent install
            // finds them via <RegistrySearch> and can pre-populate.
            using var key = Registry.LocalMachine.CreateSubKey(@"Software\3CXStatusTray\Config");
            foreach (var kv in props)
            {
                key.SetValue(MapRegistryName(kv.Key), kv.Value ?? string.Empty);
            }
            session.Log("WriteAppSettings: wrote upgrade-memory registry values");

            return ActionResult.Success;
        }
        catch (Exception ex)
        {
            session.Log($"WriteAppSettings: {ex}");
            return ActionResult.Failure;
        }
    }

    private static string MapRegistryName(string propertyName) => propertyName switch
    {
        "SERVER_URL" => "ServerUrl",
        "API_KEY" => "ApiKey",
        "EXTENSION_ID" => "ExtensionId",
        "POLL_INTERVAL_MS" => "PollIntervalMs",
        _ => propertyName
    };
}
