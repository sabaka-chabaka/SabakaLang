using SabakaLang.Studio.Editor.Controls;
using SabakaLang.Studio.Panels;
using SabakaLang.Studio.ViewModels;

namespace SabakaLang.Studio;

public partial class MainPage
{
    private readonly MainViewModel _vm;
    private readonly ActivityBar   _activityBar;
    private readonly SidePanelHost _sidePanel;
    private int _shiftCount;
    private IDispatcherTimer? _shiftTimer;
    
    public MainPage()
    {
        InitializeComponent();

        _vm = new MainViewModel();

        _activityBar = new ActivityBar();
        _activityBar.PanelSelected += panel => _sidePanel?.ShowPanel(panel);
        ActivityBarHost.Content = _activityBar;
 
        _sidePanel = new SidePanelHost(_vm.Explorer, _vm.GitPanel);
        SidePanelHost.Content = _sidePanel;
        
        TabBarHost.Content     = _vm.TabBar;
        BottomPanelContent.Content = _vm.BottomPanel;
        FindReplaceHost.Content    = _vm.FindReplace;
        PaletteHost.Content        = _vm.Palette;
 
        var statusBar = new StatusBar(_vm);
        StatusBarHost.Content = statusBar;
 
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ActiveEditor))
                ShowActiveEditor();
        };
 
        WireGlobalKeyboard();
        _vm.NewUntitled();
    }
    
    private void ShowActiveEditor()
    {
        var editor = _vm.ActiveEditor;
        if (editor is null) return;
 
        foreach (var old in EditorHost.Children.OfType<EditorView>().ToList())
            EditorHost.Children.Remove(old);
 
        EditorHost.Children.Insert(0, editor);
        WireEditorInput(editor);
    }
    
    private void WireEditorInput(EditorView editor)
    {
        var ptr = new PointerGestureRecognizer();
        ptr.PointerPressed  += (_, e) => { var p = e.GetPosition(editor); if (p is not null) editor.OnPointerDown((float)p.Value.X, (float)p.Value.Y, false, false); };
        ptr.PointerMoved    += (_, e) => { var p = e.GetPosition(editor); if (p is not null) editor.OnPointerMove((float)p.Value.X, (float)p.Value.Y); };
        ptr.PointerReleased += (_, _) => editor.OnPointerUp();
 
        var dbl = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
        dbl.Tapped += (_, e) => { var p = e.GetPosition(editor); if (p is not null) editor.OnPointerDown((float)p.Value.X, (float)p.Value.Y, false, true); };
 
        editor.GestureRecognizers.Clear();
        editor.GestureRecognizers.Add(ptr);
        editor.GestureRecognizers.Add(dbl);
    }

    private void WireGlobalKeyboard()
    {
        HandlerChanged += (_, _) =>
        {
            if (Handler?.PlatformView is not Microsoft.UI.Xaml.FrameworkElement fe) return;
 
            fe.KeyDown += (_, e) =>
            {
                var ctrl  = IsKeyDown(Windows.System.VirtualKey.Control);
                var shift = IsKeyDown(Windows.System.VirtualKey.Shift);
                var alt   = IsKeyDown(Windows.System.VirtualKey.Menu);
                var key   = e.Key.ToString();
 
                if (e.Key == Windows.System.VirtualKey.Shift && !ctrl && !alt)
                {
                    _shiftCount++;
                    _shiftTimer?.Stop();
                    _shiftTimer = Dispatcher.CreateTimer();
                    _shiftTimer.Interval   = TimeSpan.FromMilliseconds(400);
                    _shiftTimer.IsRepeating = false;
                    _shiftTimer.Tick       += (_, _) => _shiftCount = 0;
                    _shiftTimer.Start();
                    if (_shiftCount >= 2) { _shiftCount = 0; MainThread.BeginInvokeOnMainThread(() => _vm.Palette.Open()); return; }
                }
                else _shiftCount = 0;
 
                if (_vm.ActiveEditor is not null) { e.Handled = true; _vm.ActiveEditor.OnKeyDown(key, ctrl, shift, alt); }
            };
 
            fe.CharacterReceived += (_, e) =>
            {
                if (_vm.ActiveEditor is not null && e.Character >= 32)
                    _vm.ActiveEditor.OnChar(e.Character);
            };
        };
    }
    
    private static bool IsKeyDown(Windows.System.VirtualKey k) =>
        Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(k)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
    
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.Dispose();
    }
}