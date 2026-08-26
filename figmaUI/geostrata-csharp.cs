using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace GeoStrataFEA
{
    /// <summary>
    /// Main application entry point.
    /// </summary>
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    /// <summary>
    /// Host form that embeds the GeoStrata FEA HTML/JS UI
    /// inside a WebView2 control, filling the entire window.
    /// </summary>
    public class MainForm : Form
    {
        private WebView2 _webView = null!;

        public MainForm()
        {
            Text            = "GeoStrata FEA Pro v6.4.1";
            Width           = 1400;
            Height          = 860;
            MinimumSize     = new System.Drawing.Size(900, 600);
            StartPosition   = FormStartPosition.CenterScreen;
            Icon            = SystemIcons.Application;

            InitWebView();
        }

        private async void InitWebView()
        {
            _webView = new WebView2
            {
                Dock = DockStyle.Fill
            };
            Controls.Add(_webView);

            string udFolder = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
                "WebView2Data");

            var env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: udFolder,
                options: new CoreWebView2EnvironmentOptions());

            await _webView.EnsureCoreWebView2Async(env);

            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "geostrata.local", AppDirectory(), CoreWebView2HostResourceAccessKind.Allow);

            _webView.CoreWebView2.WebMessageReceived += OnWebMessage;

            string htmlPath = Path.Combine(AppDirectory(), "GeoStrataFEA.html");
            if (File.Exists(htmlPath))
                _webView.CoreWebView2.Navigate("https://geostrata.local/GeoStrataFEA.html");
            else
                LoadInlineHtml();
        }

        private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string raw = e.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(raw)) return;

            try
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if (!root.TryGetProperty("cmd", out var cmdEl)) return;
                if (cmdEl.GetString() != "geometryEngine") return;

                string response = GeometryEngineBridge.ProcessGeometry(root);
                if (_webView?.CoreWebView2 != null)
                    _webView.CoreWebView2.PostWebMessageAsString(response);
            }
            catch (Exception ex)
            {
                PostToUI(JsonSerializer.Serialize(new { cmd = "geometryEngineResult", ok = false, reason = ex.Message }));
            }
        }

        public void PostToUI(string jsonPayload)
        {
            if (_webView?.CoreWebView2 == null) return;
            Invoke((Action)(() =>
                _webView.CoreWebView2.PostWebMessageAsString(jsonPayload)));
        }

        private void LoadInlineHtml()
        {
            string html = GetEmbeddedHtml();
            _webView.CoreWebView2.NavigateToString(html);
        }

        private static string AppDirectory() =>
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;

        private static string GetEmbeddedHtml()
        {
            var asm  = Assembly.GetExecutingAssembly();
            string   resourceName = "GeoStrataFEA.GeoStrataFEA.html";

            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream != null)
                using (var reader = new StreamReader(stream))
                    return reader.ReadToEnd();

            return "<html><body style='font-family:sans-serif;padding:40px'>" +
                   "<h2>GeoStrata FEA</h2>" +
                   "<p>Place <b>GeoStrataFEA.html</b> next to the executable, " +
                   "or add it as an Embedded Resource in the project.</p>" +
                   "</body></html>";
        }
    }
}
