using System.Collections.Generic;
using System.Text.Json;
using WriteAppSettings;
using Xunit;

public class AppSettingsWriterTests
{
    [Fact]
    public void Generates_valid_json_with_all_properties_set()
    {
        var json = AppSettingsWriter.GenerateJson(new Dictionary<string, string?>
        {
            ["SERVER_URL"] = "http://pbx.office.local:8889/",
            ["API_KEY"] = "secret-key",
            ["EXTENSION_ID"] = "104",
            ["POLL_INTERVAL_MS"] = "3000"
        });

        using var doc = JsonDocument.Parse(json);
        var settings = doc.RootElement.GetProperty("Settings");

        Assert.Equal("http://pbx.office.local:8889/", settings.GetProperty("ServerURLBasePath").GetString());
        Assert.Equal("secret-key", settings.GetProperty("ApiKey").GetString());
        Assert.Equal("104", settings.GetProperty("ExtensionId").GetString());
        Assert.Equal(3000, settings.GetProperty("PollIntervalMilliseconds").GetInt32());
    }

    [Fact]
    public void Falls_back_to_defaults_when_properties_missing()
    {
        var json = AppSettingsWriter.GenerateJson(new Dictionary<string, string?>());

        using var doc = JsonDocument.Parse(json);
        var settings = doc.RootElement.GetProperty("Settings");

        Assert.Equal("http://localhost:8889/", settings.GetProperty("ServerURLBasePath").GetString());
        Assert.Equal("", settings.GetProperty("ApiKey").GetString());
        Assert.Equal("100", settings.GetProperty("ExtensionId").GetString());
        Assert.Equal(5000, settings.GetProperty("PollIntervalMilliseconds").GetInt32());
    }

    [Fact]
    public void Falls_back_to_defaults_when_properties_are_empty_strings()
    {
        var json = AppSettingsWriter.GenerateJson(new Dictionary<string, string?>
        {
            ["SERVER_URL"] = "",
            ["API_KEY"] = "",
            ["EXTENSION_ID"] = "",
            ["POLL_INTERVAL_MS"] = ""
        });

        using var doc = JsonDocument.Parse(json);
        var settings = doc.RootElement.GetProperty("Settings");

        // Empty API_KEY should stay empty (that's a valid choice);
        // everything else falls back to its default.
        Assert.Equal("http://localhost:8889/", settings.GetProperty("ServerURLBasePath").GetString());
        Assert.Equal("", settings.GetProperty("ApiKey").GetString());
        Assert.Equal("100", settings.GetProperty("ExtensionId").GetString());
        Assert.Equal(5000, settings.GetProperty("PollIntervalMilliseconds").GetInt32());
    }

    [Fact]
    public void Falls_back_to_default_when_poll_interval_is_not_a_number()
    {
        var json = AppSettingsWriter.GenerateJson(new Dictionary<string, string?>
        {
            ["POLL_INTERVAL_MS"] = "not-a-number"
        });

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(5000, doc.RootElement.GetProperty("Settings").GetProperty("PollIntervalMilliseconds").GetInt32());
    }

    [Fact]
    public void Always_includes_icons_and_profile_short_codes_sections()
    {
        var json = AppSettingsWriter.GenerateJson(new Dictionary<string, string?>());

        using var doc = JsonDocument.Parse(json);
        var settings = doc.RootElement.GetProperty("Settings");

        Assert.Equal("app-on.ico", settings.GetProperty("Icons").GetProperty("Available").GetString());
        Assert.Equal("app-off.ico", settings.GetProperty("Icons").GetProperty("OutOfOffice").GetString());
        Assert.Equal("app-default.ico", settings.GetProperty("Icons").GetProperty("Default").GetString());
        Assert.Equal("available", settings.GetProperty("ProfileShortCodes").GetProperty("Available").GetString());
        Assert.Equal("out_of_office", settings.GetProperty("ProfileShortCodes").GetProperty("OutOfOffice").GetString());
    }
}
