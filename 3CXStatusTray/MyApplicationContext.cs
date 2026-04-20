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
    private ToolStripMenuItem? _logToggleItem;
    private string? _lastStatus;
    private bool _hasPolled;

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

        // Defensive bounds on PollIntervalMilliseconds. We've seen absurd values
        // (1,253,924 ms = ~21 minutes) make it into appsettings.json via a
        // registry-roundtrip bug in the installer. If that happens again, clamp
        // to a sane range rather than let the tray sit silent for minutes on
        // end. Floor 500ms (anything lower DoS's the WebApi), ceiling 300000ms
        // (5 minutes; longer and the shared-state-indicator stops being useful).
        if (_settings.PollIntervalMilliseconds < 500 || _settings.PollIntervalMilliseconds > 300_000)
        {
            _settings.PollIntervalMilliseconds = 5000;
        }

        // Two providers: a console provider (invisible in a WinForms app -
        // stdout is detached - but cheap to keep for anyone running under a
        // debugger) and the opt-in FileLogger that the Enable logging menu
        // item flips on/off at runtime. Nothing is written until the user
        // explicitly enables it.
        _logger = LoggerFactory
            .Create(b =>
            {
                b.AddSimpleConsole(o => o.SingleLine = true);
                b.AddProvider(new FileLoggerProvider());
                b.SetMinimumLevel(LogLevel.Information);
            })
            .CreateLogger<MyApplicationContext>();

        _httpClient = new HttpClient { BaseAddress = new Uri(_settings.ServerURLBasePath) };
        if (!string.IsNullOrEmpty(_settings.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("X-API-Key", _settings.ApiKey);
        }

        _trayIcon = InitializeTrayIcon();

        Application.ApplicationExit += (_, _) =>
        {
            _trayIcon.Visible = false;
            FileLogger.Disable();
        };

        _pollTimer = new Timer { Interval = _settings.PollIntervalMilliseconds, Enabled = true };
        _pollTimer.Tick += async (_, _) => await PollAndUpdateAsync();
    }

    private NotifyIcon InitializeTrayIcon()
    {
        var contextMenu = new ContextMenuStrip();

        _logToggleItem = new ToolStripMenuItem("Enable logging");
        _logToggleItem.Click += (_, _) => ToggleLogging();
        contextMenu.Items.Add(_logToggleItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) =>
        {
            var answer = MessageBox.Show(
                "Close the tray? The phones indicator and toggle won't work on this desk until it's running again.",
                "Exit 3CX Status Tray",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
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

    private void ToggleLogging()
    {
        if (_logToggleItem is null) return;

        if (FileLogger.IsEnabled)
        {
            var path = FileLogger.CurrentPath;
            FileLogger.Disable();
            _logToggleItem.Text = "Enable logging";
            MessageBox.Show(
                $"Logging stopped.\n\nLog file saved to:\n{path}\n\nThe log is a plain text file; open it with Notepad.",
                "3CX Status Tray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        else
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "3CXStatusTray");
            var path = Path.Combine(dir, $"tray-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            FileLogger.Enable(path);
            _logToggleItem.Text = $"Stop logging  ({Path.GetFileName(path)})";
            _logger.LogInformation(
                "Logging started. Config: ServerURL={ServerURL}  Extension={ExtensionId}  PollInterval={PollMs}ms  ApiKeySet={HasKey}",
                _settings.ServerURLBasePath,
                _settings.ExtensionId,
                _settings.PollIntervalMilliseconds,
                !string.IsNullOrEmpty(_settings.ApiKey));
            _logger.LogInformation("Last known status: {LastStatus}", _lastStatus ?? "<none yet>");
        }
    }

    private async Task PollAndUpdateAsync()
    {
        var response = await GetExtensionStatusAsync();
        var current = response?.Message;
        _logger.LogInformation("Poll response: Message={Message} Status={Status}",
            current ?? "<null>", response?.Status ?? "<null>");

        // Always update on the very first poll so the tray tooltip/icon
        // reflect whatever the service returned, even if that's null (the
        // grey 'unknown' icon). Without this the first poll silently
        // no-ops when the response happens to equal the initial
        // _lastStatus (both null), leaving the tray stuck on the default
        // 'Status information' tooltip forever.
        if (_hasPolled && current == _lastStatus)
        {
            _logger.LogInformation("No change since last poll; skipping UI update");
            return;
        }

        _hasPolled = true;
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

        _logger.LogInformation("Toggle requested; current={Current}  target={Target}",
            _lastStatus ?? "<unknown>", targetShortCode);

        _trayIcon.BalloonTipText = $"Setting status to: {targetShortCode}";
        var set = await SetAllExtensionsAsync(targetShortCode);
        if (set is null)
        {
            _lastStatus = null;
            _trayIcon.BalloonTipText = "Failed to set status - check service";
            _logger.LogWarning("setAllExtensions returned null - service unreachable or errored");
        }
        else
        {
            _logger.LogInformation("setAllExtensions response: Message={Message} Status={Status}",
                set.Message ?? "<null>", set.Status ?? "<null>");
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
                // Show the actual status string (e.g. "Away", "Custom 1",
                // "unknown") so hovering reveals why we're on the grey
                // icon - previously this just said "unknown" regardless,
                // which was unhelpful.
                var display = string.IsNullOrEmpty(status) ? "unknown (no response)" : status;
                _trayIcon.BalloonTipText = $"Current status: {display} (no tray colour for this profile)";
                _trayIcon.Text = $"Current status: {display}";
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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Method} {Path} unexpected error", method, path);
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
