using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace SabakaUI;

/// <summary>
/// WPF window hosting WebView2.
/// Receives messages from JS via window.chrome.webview.postMessage()
/// and dispatches them to the script thread via onMessage callback.
/// </summary>
internal class SabakaWindow : Window
{
    public WebView2 WebView { get; } = new();

    private readonly string _htmlPath;
    private readonly Action<string, string> _onMessage;

    public SabakaWindow(
        string title,
        int width, int height,
        bool resizable,
        string htmlPath,
        Action<string, string> onMessage)
    {
        Title      = title;
        Width      = width;
        Height     = height;
        ResizeMode = resizable ? ResizeMode.CanResize : ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _htmlPath  = htmlPath;
        _onMessage = onMessage;

        Content = WebView;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Initialize WebView2 with a temp user data folder
        string dataDir = Path.Combine(
            Path.GetTempPath(), "SabakaUI_WebView2");

        var env = await CoreWebView2Environment.CreateAsync(
            userDataFolder: dataDir);

        await WebView.EnsureCoreWebView2Async(env);

        // Inject bridge BEFORE the page loads
        await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(GetBridgeScript());

        // Receive messages from JS
        WebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

        // Navigate to the HTML file
        WebView.CoreWebView2.Navigate(new Uri(_htmlPath).AbsoluteUri);
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            // Messages from JS are JSON: { "event": "btnClick", "data": "hello" }
            string json = e.TryGetWebMessageAsString();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string evt  = root.GetProperty("event").GetString() ?? "";
            string data = root.TryGetProperty("data", out var d) ? (d.GetString() ?? "") : "";

            _onMessage(evt, data);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SabakaUI] Bad message from JS: {ex.Message}");
        }
    }

    /// <summary>
    /// The bridge script injected into every page.
    /// Defines window.sabaka with send/set/call/get.
    /// Developers use sabaka.send() in their own HTML/JS.
    /// </summary>
    private static string GetBridgeScript() => """
        (function() {
            window.sabaka = {
                // Send an event to SabakaLang
                // Usage: sabaka.send("btnClick", "some data")
                send: function(event, data) {
                    window.chrome.webview.postMessage(
                        JSON.stringify({ event: event, data: data ?? "" })
                    );
                },

                // Called by SabakaLang to set a named value in the page.
                // Override in your JS: sabaka._set = function(key, value) { ... }
                _set: function(key, value) {
                    var el = document.getElementById(key);
                    if (el) el.textContent = value;
                },

                // Called by SabakaLang to invoke a JS function by name.
                // Override in your JS: sabaka._call = function(fn, arg) { ... }
                _call: function(fn, arg) {
                    if (typeof window[fn] === 'function') window[fn](arg);
                },

                // Called by SabakaLang to get a named value from the page.
                // Override in your JS: sabaka._get = function(key) { return ...; }
                _get: function(key) {
                    var el = document.getElementById(key);
                    return el ? el.value || el.textContent : "";
                }
            };
        })();
        """;
}