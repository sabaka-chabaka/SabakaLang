// ALL WPF types live here and ONLY here.
// This file is only JIT-compiled when SabakaUIHost.Start() is first called
// — which happens inside ui.run(), never at module load time.

using Microsoft.Web.WebView2.Wpf;
using System.Collections.Concurrent;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace SabakaUI;

internal static class SabakaUIHost
{
    /// <summary>
    /// Start WPF on a dedicated STA thread.
    /// Returns the thread. Calls onReady(hostObject) once the window is shown.
    /// hostObject is a SabakaWindow — typed as object so callers stay WPF-free.
    /// </summary>
    public static Thread Start(
        string htmlPath,
        string title, int width, int height, bool resizable,
        ConcurrentQueue<string> jsQueue,
        Action<string, string> onMessage,
        Action onClosed,
        Action<object> onReady)
    {
        var thread = new Thread(() =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };

            var window = new SabakaWindow(
                title: title, width: width, height: height,
                resizable: resizable, htmlPath: htmlPath,
                onMessage: onMessage);

            window.Closed += (_, _) => onClosed();
            app.MainWindow = window;
            window.Show();

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(8) };
            timer.Tick += async (_, _) =>
            {
                while (jsQueue.TryDequeue(out string? js))
                    await window.WebView.ExecuteScriptAsync(js);
            };
            timer.Start();

            onReady(window);  // passes SabakaWindow as object
            app.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = false;
        thread.Name = "SabakaUI-WPF";
        thread.Start();
        return thread;
    }

    public static void ApplyTitle(object host, string t)
    {
        var w = (SabakaWindow)host;
        w.Dispatcher.BeginInvoke(() => w.Title = t);
    }

    public static void ApplySize(object host, int width, int height)
    {
        var w = (SabakaWindow)host;
        w.Dispatcher.BeginInvoke(() => { w.Width = width; w.Height = height; });
    }

    public static void ApplyResizable(object host, bool allow)
    {
        var w = (SabakaWindow)host;
        w.Dispatcher.BeginInvoke(() =>
            w.ResizeMode = allow ? ResizeMode.CanResize : ResizeMode.NoResize);
    }

    public static string JsGet(object host, string key)
    {
        var w = (SabakaWindow)host;
        string result = "";
        var done = new ManualResetEventSlim(false);
        w.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                string raw = await w.WebView.ExecuteScriptAsync(
                    "window.sabaka?._get?.(\"" + key.Replace("\"", "\\\"") + "\")??''");
                result = raw.Trim('"');
            }
            finally { done.Set(); }
        });
        done.Wait(2000);
        return result;
    }

    public static void CloseWindow(object host)
    {
        var w = (SabakaWindow)host;
        w.Dispatcher.BeginInvoke(() => w.Close());
    }
}