using System;
using System.Drawing;
using System.Windows.Forms;
using System.Net.Http;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ToolBar;
using Timer = System.Windows.Forms.Timer;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace _3CXStatusTray
{
    class MyApplicationContext : ApplicationContext
    {


        // Build a config object, using env vars and JSON providers.
        IConfiguration config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .AddEnvironmentVariables()
            .Build();

        private static readonly HttpClient httpClient = new HttpClient();
        private static string BasePath = "http://localhost:8080/";
        private string? lastStatus = null;

        private static readonly string DefaultExtensionID = "100";
        private static readonly string CheckExtensionPath = "status/extension/" + DefaultExtensionID;
        private static readonly string CheckSystemModePath = "status/system/mode";
        //private static readonly string SetExtensionProfilePath = "status/extension/{ExtensionID}/profile";
        private static readonly string SetAllExtensionsProfilePath = "status/extensions/profile";


        //Component declarations
        private NotifyIcon TrayIcon;
        private ContextMenuStrip TrayIconContextMenu;
        private ToolStripMenuItem CloseMenuItem;

        public sealed class Settings
        {
            public string ServerURLBasePath { get; set; }
        }

        public MyApplicationContext()
        {

            // Get values from the config given their key and their target type.
            Settings settings = config.GetRequiredSection("Settings").Get<Settings>();
            // Write the values to the console.
            // Console.WriteLine($"KeyOne = {settings.ServerURLBasePath}");
            BasePath = settings.ServerURLBasePath;

            Application.ApplicationExit += new EventHandler(OnApplicationExit);
            InitializeComponent();
            TrayIcon.Visible = true;
            InitializeTimer();
        }

        private static ApiQueryResponse ApiRequest(string path)
        {
            async Task<ApiQueryResponse> GetResponseString(string path)
            {
                var Uri = new Uri(BasePath + path);

                //var parameters = new Dictionary<string, string>();
                //parameters["text"] = text;
                //var response = await httpClient.PostAsync("http://localhost:8889/status/extension/100", new FormUrlEncodedContent(parameters));
                //var contents = await response.Content.ReadAsStringAsync();
                HttpResponseMessage response;
                try
                {
                    response = httpClient.GetAsync(Uri, HttpCompletionOption.ResponseHeadersRead).Result;
                    var contents = response.Content.ReadAsStringAsync().Result;
                    var apiQueryResponse = JsonSerializer.Deserialize<ApiQueryResponse>(contents, new JsonSerializerOptions { IgnoreNullValues = true, PropertyNameCaseInsensitive = true });
                    return apiQueryResponse;
                }
                catch (HttpRequestException)
                {
                    // ...
                }
                //System.Diagnostics.Debug.WriteLine(apiQueryResponse.Result);                
                return new ApiQueryResponse("error", "error");
            }


            Task<ApiQueryResponse> apiQueryResponse = GetResponseString(path);
            // WE NEED TO CHECK THE REQUEST HERE

            try
            {
                var finalResult = apiQueryResponse.Result;
                return finalResult;
            }
            catch
            {
                // ...
            }
            //System.Diagnostics.Debug.WriteLine(finalResult.Result);
            return new ApiQueryResponse("error", "error");
        }

        public class ApiQueryResponse
        {
            public ApiQueryResponse(string message, string status)
            {
                Message = message;
                Status = status;
                TimeStamp = DateTime.Now;
            }
            public string? Message { get; set; }
            public string? Status { get; set; }
            public DateTime TimeStamp { get; set; }
        }

        private void InitializeTimer()
        {
            // Call this procedure when the application starts.  
            // Set to 10 second.  
            Timer Timer1 = new Timer();
            Timer1.Interval = 5000;
            Timer1.Tick += new EventHandler(Timer1_Tick);
            // Enable timer.  
            Timer1.Enabled = true;
        }

        private void Timer1_Tick(object Sender, EventArgs e)
        {
            //System.Diagnostics.Debug.WriteLine("Last Status :");
            //System.Diagnostics.Debug.WriteLine(lastStatus);
            //System.Diagnostics.Debug.WriteLine("Running fetch");
            var currentStatus = ApiRequest(CheckExtensionPath).Message;
            //System.Diagnostics.Debug.WriteLine("Current Status :");
            //System.Diagnostics.Debug.WriteLine(currentStatus);
            if (currentStatus != lastStatus)
            {
                //System.Diagnostics.Debug.WriteLine("Changed!");
                lastStatus = currentStatus;
                switch (currentStatus)
                {
                    case "Available":
                        {
                            TrayIcon.BalloonTipText = "Current status : " + currentStatus;
                            TrayIcon.Text = "Current status : " + currentStatus;
                            TrayIcon.Icon = new Icon("app-on.ico");
                            break;
                        }
                    case "Out of office":
                        {
                            TrayIcon.BalloonTipText = "Current status : " + currentStatus;
                            TrayIcon.Text = "Current status : " + currentStatus;
                            TrayIcon.Icon = new Icon("app-off.ico");
                            break;
                        }
                    default:
                        {
                            TrayIcon.BalloonTipText = "Current status : Unexpected profile";
                            TrayIcon.Text = "Current status : unknown";
                            TrayIcon.Icon = new Icon("app-default.ico");
                            break;
                        }
                }
                TrayIcon.ShowBalloonTip(10000);
                //System.Diagnostics.Debug.WriteLine("Show!");
            }

        }

        private void InitializeComponent()
        {
            TrayIcon = new NotifyIcon();

            TrayIcon.BalloonTipIcon = ToolTipIcon.Info;
            TrayIcon.BalloonTipText = "Status information";
            TrayIcon.BalloonTipTitle = "Phone Status Applet";
            TrayIcon.Text = "3CX System";

            //The icon is added to the project resources.
            //Here, I assume that the name of the file is 'TrayIcon.ico'
            //TrayIcon.Icon = Properties.Resources.TrayIcon;
            TrayIcon.Icon = new Icon("app-default.ico");

            //Optional - handle doubleclicks on the icon:
            TrayIcon.DoubleClick += TrayIcon_DoubleClick;

            //Optional - Add a context menu to the TrayIcon:
            TrayIconContextMenu = new ContextMenuStrip();
            CloseMenuItem = new ToolStripMenuItem();
            TrayIconContextMenu.SuspendLayout();

            // 
            // TrayIconContextMenu
            // 
            this.TrayIconContextMenu.Items.AddRange(new ToolStripItem[] {
            this.CloseMenuItem});
            this.TrayIconContextMenu.Name = "TrayIconContextMenu";
            this.TrayIconContextMenu.Size = new Size(153, 70);
            // 
            // CloseMenuItem
            // 
            this.CloseMenuItem.Name = "CloseMenuItem";
            this.CloseMenuItem.Size = new Size(152, 22);
            this.CloseMenuItem.Text = "Exit";
            this.CloseMenuItem.Click += new EventHandler(this.CloseMenuItem_Click);

            TrayIconContextMenu.ResumeLayout(false);
            TrayIcon.ContextMenuStrip = TrayIconContextMenu;
        }

        private void OnApplicationExit(object sender, EventArgs e)
        {
            //Cleanup so that the icon will be removed when the application is closed
            TrayIcon.Visible = false;
        }

        private void TrayIcon_DoubleClick(object sender, EventArgs e)
        {
            //Here, you can do stuff if the tray icon is doubleclicked
            //System.Diagnostics.Debug.WriteLine($"DoubleClicked");
            switch (lastStatus)
            {
                //Excellent use of DRY here man - functionalise it
                case "Available":
                    {
                        TrayIcon.BalloonTipText = "Setting status to : Out of office";
                        ApiRequest(SetAllExtensionsProfilePath+"/out_of_office");
                        var currentStatus = ApiRequest(CheckExtensionPath).Message;
                        if (currentStatus == "Out of office")
                        {
                            lastStatus = currentStatus;
                        }
                        else
                        {
                            lastStatus = "Unknown";
                        }
                        TrayIcon.Icon = new Icon("app-off.ico");
                        break;
                    }
                case "Out of office":
                    {
                        TrayIcon.BalloonTipText = "Setting status to : Available";
                        ApiRequest(SetAllExtensionsProfilePath+"/available");
                        var currentStatus = ApiRequest(CheckExtensionPath).Message;
                        if (currentStatus == "Available")
                        {
                            lastStatus = currentStatus;
                        }
                        else
                        {
                            lastStatus = "Unknown";
                        }
                        TrayIcon.Icon = new Icon("app-on.ico");
                        break;
                    }
                default:
                    {
                        TrayIcon.BalloonTipText = "The current status of the system is not known\n Setting status to : Available";
                        ApiRequest(SetAllExtensionsProfilePath+"/available");
                        var currentStatus = ApiRequest(CheckExtensionPath).Message;
                        if (currentStatus == "Available")
                        {
                            lastStatus = currentStatus;
                        } else
                        {
                            lastStatus = "Unknown";
                        }
                        TrayIcon.Icon = new Icon("app-on.ico");
                        break;
                    }
            }
            TrayIcon.ShowBalloonTip(10000);
        }

        private void CloseMenuItem_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Do you really want to close me?",
                    "Are you sure?", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation,
                    MessageBoxDefaultButton.Button2) == DialogResult.Yes)
            {
                Application.Exit();
            }
        }

    }

}