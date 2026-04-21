using SabakaLang.LanguageServer;
using SabakaLang.Studio.Editor;
using SabakaLang.Studio.Editor.Highlighting;

namespace SabakaLang.Studio;

public partial class MainPage : ContentPage
{
    private readonly DocumentStore  _store;
    private readonly EditorSurface  _editor;

    private readonly Label _statusLeft;
    private readonly Label _statusRight;

    private int _shiftCount;
    private IDispatcherTimer? _shiftResetTimer;

    public void Dispose()
    {
        _editor.DirtyChanged      -= OnDirtyChanged;
        _editor.CaretMoved        -= OnCaretMoved;
        _editor.FindRequested     -= ShowFindBar;
        _editor.PaletteRequested  -= ShowPalette;
        _editor.Dispose();
    }

    private void OnCaretMoved(int row, int col) =>
        MainThread.BeginInvokeOnMainThread(() =>
            _statusRight.Text = $"Ln {row + 1}  Col {col + 1}");

    private void OnDirtyChanged(bool dirty) => UpdateTitle(dirty);

    public MainPage()
    {
        InitializeComponent();

        _statusLeft = new Label
        {
            Text       = "SabakaLang",
            FontFamily = Theme.Font,
            FontSize   = 11,
            TextColor  = Colors.LightGray,
            VerticalOptions = LayoutOptions.Center,
            Padding    = new Thickness(10, 0)
        };
        _statusRight = new Label
        {
            Text       = "Ln 1  Col 1",
            FontFamily = Theme.Font,
            FontSize   = 11,
            TextColor  = Colors.LightGray,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions   = LayoutOptions.Center,
            Padding    = new Thickness(10, 0)
        };

        _store  = new DocumentStore();
        _editor = new EditorSurface(_store)
        {
            VerticalOptions   = LayoutOptions.Fill,
            HorizontalOptions = LayoutOptions.Fill
        };

        _editor.DirtyChanged      += OnDirtyChanged;
        _editor.CaretMoved        += OnCaretMoved;
        _editor.FindRequested     += ShowFindBar;
        _editor.SaveRequested     += () => { /* no file system yet */ };
        _editor.PaletteRequested  += ShowPalette;

        var statusBar = new Grid
        {
            HeightRequest   = 24,
            BackgroundColor = Color.FromArgb("#3C3F41"),
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            ],
            Children = { _statusLeft, _statusRight }
        };
        Grid.SetColumn(_statusRight, 1);

        Content = new Grid
        {
            RowDefinitions =
            [
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            ],
            BackgroundColor = Theme.Bg,
            Children        = { _editor, statusBar }
        };
        Grid.SetRow(statusBar, 1);

        _editor.Text = DemoCode;
        _editor.MarkClean();

        Loaded += (_, _) => WirePlatformKeyboard();
    }

    private void UpdateTitle(bool dirty)
    {
        if (Shell.Current is not null)
            Shell.Current.Title = dirty ? "SabakaLang Studio •" : "SabakaLang Studio";
    }

    private void ShowFindBar()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                var query = await DisplayPromptAsync("Find", "Search in file:");
                if (string.IsNullOrEmpty(query)) return;
                var hits = _editor.Text
                    .Split('\n')
                    .Select((ln, i) =>
                    {
                        var c = ln.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                        return c >= 0 ? new Editor.Core.TextPosition(i, c) : (Editor.Core.TextPosition?)null;
                    })
                    .Where(p => p.HasValue)
                    .Select(p => p!.Value)
                    .ToList();

                _editor.SetSearchResults(hits, hits.Count > 0 ? 0 : -1);
                if (hits.Count > 0) _editor.ScrollToRow(hits[0].Row);
            }
            catch { /* ignored */ }
        });
    }

    private void ShowPalette()
    {
    }

    private void WirePlatformKeyboard()
    {
#if WINDOWS
        var window = Application.Current?.Windows.FirstOrDefault();
        if (window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window win)
        {
            win.Content.KeyDown += (s, e) =>
            {
                var key   = e.Key.ToString();
                var ctrl  = Microsoft.UI.Input.InputKeyboardSource
                    .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                    .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
                var shift = Microsoft.UI.Input.InputKeyboardSource
                    .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                    .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
                var alt   = Microsoft.UI.Input.InputKeyboardSource
                    .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu)
                    .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

                if (e.Key == Windows.System.VirtualKey.Shift && !ctrl && !alt)
                {
                    _shiftCount++;
                    _shiftResetTimer?.Stop();
                    _shiftResetTimer = Dispatcher.CreateTimer();
                    _shiftResetTimer.Interval    = TimeSpan.FromMilliseconds(400);
                    _shiftResetTimer.IsRepeating = false;
                    _shiftResetTimer.Tick        += (_, _) => _shiftCount = 0;
                    _shiftResetTimer.Start();
                    if (_shiftCount >= 2) { _shiftCount = 0; ShowPalette(); return; }
                }
                else _shiftCount = 0;

                e.Handled = _editor.HandleKeyDown(key, ctrl, shift, alt);
            };
        }
#endif
    }

    private const string DemoCode =
        """
        class Animal {
            public string name;
            public int age;

            Animal(string name, int age) {
                this.name = name;
                this.age  = age;
            }

            public string speak() {
                return "...";
            }
        }

        class Dog : Animal {
            public string speak() {
                return "Woof! I am " + name;
            }
        }

        Dog d = new Dog("Rex", 3);
        print(d.speak());

        int[] nums = [1, 2, 3, 4, 5];
        foreach (int n in nums) {
            if (n % 2 == 0) {
                print("even: " + n);
            }
        }
        """;
}