using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace SabakaLang.Studio;

public partial class MainPage : ContentPage
{
    EditorEngine engine = new();
    
    public MainPage()
    {
        InitializeComponent();
    }
    
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        var platformWindow = (Microsoft.UI.Xaml.Window)Window!.Handler!.PlatformView!;
        var rootElement = platformWindow.Content as Microsoft.UI.Xaml.FrameworkElement;

        rootElement.KeyDown += (s, e) => 
        {
            switch (e.Key)
            {
                case VirtualKey.Enter:
                    engine.InsertNewLine();
                    break;
                case VirtualKey.Back:
                    engine.Backspace();
                    break;
                case VirtualKey.Left:
                    if (engine.CursorCol > 0) engine.CursorCol--;
                    break;
                case VirtualKey.Right:
                    if (engine.CursorCol < engine.Lines[engine.CursorRow].Length) engine.CursorCol++;
                    break;
                case VirtualKey.Up:
                    if (engine.CursorRow > 0) engine.CursorRow--;
                    break;
                case VirtualKey.Down:
                    if (engine.CursorRow < engine.Lines.Count - 1) engine.CursorRow++;
                    break;
            }
            MainGraphicsView.Invalidate(); 
        };

        platformWindow.CoreWindow.CharacterReceived += (s, e) => 
        {
            char c = (char)e.KeyCode;
            if (c >= 32) 
            {
                engine.InsertChar(c);
                MainGraphicsView.Invalidate();
            }
        };
    }
}