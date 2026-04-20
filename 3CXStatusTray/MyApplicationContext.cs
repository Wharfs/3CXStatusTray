using System.Drawing;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Timer = System.Windows.Forms.Timer;

namespace _3CXStatusTray;

internal sealed class MyApplicationContext : ApplicationContext
{
    private readonly Settings _settings;
    private readonly HttpClient _httpClient;
    private readonly ILogger<MyApplicationContext> _logger;
    private readonly NotifyIcon _trayIcon;
    private readonly Timer _pollTimer;
    private string? _lastStatus;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public MyApplicationContext()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddEnvironmentVariables()
            .Build();

        _settings = config.GetRequiredSection("Settings").Get<Settings>()
            ?? throw new InvalidOperationException("Settings section missing from appsettings.json");

        _logger = LoggerFactory
            .Create(b => b.AddSimpleConsole(o => o.SingleLine = true))
            .CreateLogger<MyApplicationContext>();

        _httpClient = new HttpClient { BaseAddress = new Uri(_settings.ServerURLBasePath) };
        if (!string.IsNullOrEmpty(_settings.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("X-API-Key", _settings.ApiKey);
        }

        _trayIcon = InitializeTrayIcon();

        Application.ApplicationExit += (_, _) => _trayIcon.Visible = false;

        _pollTimer = new Timer { Interval = _settings.PollIntervalMilliseconds, Enabled = true };
        _pollTimer.Tick += async (_, _) => await PollAndUpdateAsync();
    }

    private NotifyIcon InitializeTrayIcon()
    {
        var contextMenu = new ContextMenuStrip();
        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) =>
        {
            var answer = MessageBox.Show(
                "Do you really want to close me?",
                "Are you sure?",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Exclamation,
                MessageBoxDefaultButton.Button2);
            if (answer == DialogResult.Yes)
            {
                Application.Exit();
            }
        };
        contextMenu.Items.Add(exitItem);

        var icon = new NotifyIcon
        {
            BalloonTipIcon = ToolTipIcon.Info,
            BalloonTipTitle = "Phone Status Applet",
            BalloonTipText = "Status information",
            Text = "3CX System",
            Icon = LoadIcon(_settings.Icons.Default),
            ContextMenuStrip = contextMenu,
            Visible = true
        };
        icon.DoubleClick += async (_, _) => await OnDoubleClickAsync();
        return icon;
    }

    private async Task PollAndUpdateAsync()
    {
        var response = await GetExtensionStatusAsync();
        var current = response?.Message;
        if (current == _lastStatus) return;

        _lastStatus = current;
        UpdateTrayDisplay(current);
    }

    private async Task OnDoubleClickAsync()
    {
        // Whatever the current status is, toggle to its opposite. "Unknown"
        // defaults to Available - matches the original app's "if we don't
        // know the state, put the phones on" behaviour so a confused tray
        // never silently leaves voicemail enabled.
        string targetShortCode = _lastStatus switch
        {
            "Available"     => _settings.ProfileShortCodes.OutOfOffice,
            "Out of office" => _settings.ProfileShortCodes.Available,
            _               => _settings.ProfileShortCodes.Available
        };

        _trayIcon.BalloonTipText = $"Setting status to: {targetShortCode}";
        var set = await SetAllExtensionsAsync(targetShortCode);
        if (set is null)
        {
            _lastStatus = null;
            _trayIcon.BalloonTipText = "Failed to set status - check service";
        }
        else
        {
            await PollAndUpdateAsync();
        }
        _trayIcon.ShowBalloonTip(_settings.BalloonTipDisplayMilliseconds);
    }

    private void UpdateTrayDisplay(string? status)
    {
        switch (status)
        {
            case "Available":
                _trayIcon.BalloonTipText = $"Current status: {status}";
                _trayIcon.Text = $"Current status: {status}";
                _trayIcon.Icon = LoadIcon(_settings.Icons.Available);
                break;
            case "Out of office":
                _trayIcon.BalloonTipText = $"Current status: {status}";
                _trayIcon.Text = $"Current status: {status}";
                _trayIcon.Icon = LoadIcon(_settings.Icons.OutOfOffice);
                break;
            default:
                _trayIcon.BalloonTipText = "Current status: Unexpected profile";
                _trayIcon.Text = "Current status: unknown";
                _trayIcon.Icon = LoadIcon(_settings.Icons.Default);
                break;
        }
        _trayIcon.ShowBalloonTip(_settings.BalloonTipDisplayMilliseconds);
    }

    private Task<ApiQueryResponse?> GetExtensionStatusAsync()
        => SendAsync(HttpMethod.Get, $"status/extension/{_settings.ExtensionId}");

    private Task<ApiQueryResponse?> SetAllExtensionsAsync(string shortCode)
        => SendAsync(HttpMethod.Get, $"status/extensions/profile/{shortCode}");

    private async Task<ApiQueryResponse?> SendAsync(HttpMethod method, string path)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("{Method} {Path} returned {Status}", method, path, (int)response.StatusCode);
                return null;
            }
            return await response.Content.ReadFromJsonAsync<ApiQueryResponse>(JsonOptions);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "{Method} {Path} failed - service unreachable?", method, path);
            return null;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "{Method} {Path} timed out", method, path);
            return null;
        }
    }

    private static Icon LoadIcon(string filename)
    {
        var path = Path.Combine(AppContext.BaseDirectory, filename);
        return new Icon(path);
    }

    private sealed class ApiQueryResponse
    {
        public string? Message { get; set; }
        public string? Status { get; set; }
        public DateTime TimeStamp { get; set; }
    }

    public sealed class Settings
    {
        public string ServerURLBasePath { get; set; } = "http://localhost:8889/";
        public string ApiKey { get; set; } = string.Empty;
        public int PollIntervalMilliseconds { get; set; } = 5000;
        public string ExtensionId { get; set; } = "100";
        public int BalloonTipDisplayMilliseconds { get; set; } = 10000;
        public IconSet Icons { get; set; } = new();
        public ShortCodes ProfileShortCodes { get; set; } = new();

        public sealed class IconSet
        {
            public string Available { get; set; } = "app-on.ico";
            public string OutOfOffice { get; set; } = "app-off.ico";
            public string Default { get; set; } = "app-default.ico";
        }

        public sealed class ShortCodes
        {
            public string Available { get; set; } = "available";
            public string OutOfOffice { get; set; } = "out_of_office";
        }
    }
}
