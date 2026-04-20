using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

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

        EditorPainter.Engine = engine;

        var platformWindow = (Microsoft.UI.Xaml.Window)Window!.Handler!.PlatformView!;
        var rootElement = platformWindow.Content as Microsoft.UI.Xaml.FrameworkElement;
        
        if (rootElement == null) return;

        var graphicsViewPlatformView = MainGraphicsView.Handler?.PlatformView as Microsoft.UI.Xaml.FrameworkElement;
        if (graphicsViewPlatformView != null)
        {
            graphicsViewPlatformView.IsTabStop = true;
        }

        // We attach to the root element to catch events globally, and also specifically 
        // focus the graphics view if available.
        var targetElement = graphicsViewPlatformView ?? rootElement;

        void OnPointerPressed(object s, PointerRoutedEventArgs e)
        {
            targetElement.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            var point = e.GetCurrentPoint(targetElement);
            float x = (float)point.Position.X;
            float y = (float)point.Position.Y;

            float lineHeight = 18f;
            float charWidth = 8.4f;

            int row = (int)((y - 4) / lineHeight);
            int col = (int)Math.Round((x - 10) / charWidth);

            if (row < 0) row = 0;
            if (row >= engine.Lines.Count) row = engine.Lines.Count - 1;
            if (col < 0) col = 0;
            if (col > engine.Lines[row].Length) col = engine.Lines[row].Length;

            engine.CursorRow = row;
            engine.CursorCol = col;
            engine.ClearSelection();
            
            if (point.Properties.IsLeftButtonPressed)
            {
                engine.SelectionStartRow = row;
                engine.SelectionStartCol = col;
                targetElement.CapturePointer(e.Pointer);
            }

            MainGraphicsView.Invalidate();
        }

        void OnPointerMoved(object s, PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(targetElement);
            if (point.Properties.IsLeftButtonPressed && engine.HasSelection)
            {
                float x = (float)point.Position.X;
                float y = (float)point.Position.Y;

                float lineHeight = 18f;
                float charWidth = 8.4f;

                int row = (int)((y - 4) / lineHeight);
                int col = (int)Math.Round((x - 10) / charWidth);

                if (row < 0) row = 0;
                if (row >= engine.Lines.Count) row = engine.Lines.Count - 1;
                if (col < 0) col = 0;
                if (col > engine.Lines[row].Length) col = engine.Lines[row].Length;

                engine.CursorRow = row;
                engine.CursorCol = col;
                MainGraphicsView.Invalidate();
            }
        }

        void OnPointerReleased(object s, PointerRoutedEventArgs e)
        {
            if (engine.HasSelection && engine.SelectionStartRow == engine.CursorRow && engine.SelectionStartCol == engine.CursorCol)
            {
                engine.ClearSelection();
            }
            targetElement.ReleasePointerCapture(e.Pointer);
            MainGraphicsView.Invalidate();
        }

        void OnKeyDown(object s, KeyRoutedEventArgs e)
        {
            var shift = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
            bool handled = true;

            switch (e.Key)
            {
                case VirtualKey.Enter:
                    engine.InsertNewLine();
                    break;
                case VirtualKey.Back:
                    engine.Backspace();
                    break;
                case VirtualKey.Left:
                    if (shift && !engine.HasSelection)
                    {
                        engine.SelectionStartRow = engine.CursorRow;
                        engine.SelectionStartCol = engine.CursorCol;
                    }
                    else if (!shift) engine.ClearSelection();
                    
                    if (engine.CursorCol > 0) engine.CursorCol--;
                    else if (engine.CursorRow > 0)
                    {
                        engine.CursorRow--;
                        engine.CursorCol = engine.Lines[engine.CursorRow].Length;
                    }
                    break;
                case VirtualKey.Right:
                    if (shift && !engine.HasSelection)
                    {
                        engine.SelectionStartRow = engine.CursorRow;
                        engine.SelectionStartCol = engine.CursorCol;
                    }
                    else if (!shift) engine.ClearSelection();

                    if (engine.CursorCol < engine.Lines[engine.CursorRow].Length) engine.CursorCol++;
                    else if (engine.CursorRow < engine.Lines.Count - 1)
                    {
                        engine.CursorRow++;
                        engine.CursorCol = 0;
                    }
                    break;
                case VirtualKey.Up:
                    if (shift && !engine.HasSelection)
                    {
                        engine.SelectionStartRow = engine.CursorRow;
                        engine.SelectionStartCol = engine.CursorCol;
                    }
                    else if (!shift) engine.ClearSelection();

                    if (engine.CursorRow > 0)
                    {
                        engine.CursorRow--;
                        if (engine.CursorCol > engine.Lines[engine.CursorRow].Length) engine.CursorCol = engine.Lines[engine.CursorRow].Length;
                    }
                    break;
                case VirtualKey.Down:
                    if (shift && !engine.HasSelection)
                    {
                        engine.SelectionStartRow = engine.CursorRow;
                        engine.SelectionStartCol = engine.CursorCol;
                    }
                    else if (!shift) engine.ClearSelection();

                    if (engine.CursorRow < engine.Lines.Count - 1)
                    {
                        engine.CursorRow++;
                        if (engine.CursorCol > engine.Lines[engine.CursorRow].Length) engine.CursorCol = engine.Lines[engine.CursorRow].Length;
                    }
                    break;
                default:
                    handled = false;
                    break;
            }

            if (handled)
            {
                e.Handled = true;
                MainGraphicsView.Invalidate();
            }
        }

        void OnCharacterReceived(Microsoft.UI.Xaml.UIElement s, CharacterReceivedRoutedEventArgs e)
        {
            if (e.Character >= 32)
            {
                engine.InsertChar(e.Character);
                MainGraphicsView.Invalidate();
                e.Handled = true;
            }
        }

        // Clear existing events by removing and adding back is hard here without named methods.
        // Instead, we just use the new named methods and make sure we don't double-subscribe.
        // Actually, since this is called in OnHandlerChanged, it might be called multiple times.
        // However, it's better to always attach to rootElement as a reliable fallback.
        rootElement.PointerPressed -= OnPointerPressed;
        rootElement.PointerPressed += OnPointerPressed;
        rootElement.PointerMoved -= OnPointerMoved;
        rootElement.PointerMoved += OnPointerMoved;
        rootElement.PointerReleased -= OnPointerReleased;
        rootElement.PointerReleased += OnPointerReleased;
        rootElement.KeyDown -= OnKeyDown;
        rootElement.KeyDown += OnKeyDown;
        rootElement.CharacterReceived -= OnCharacterReceived;
        rootElement.CharacterReceived += OnCharacterReceived;

        if (graphicsViewPlatformView != null && graphicsViewPlatformView != rootElement)
        {
            graphicsViewPlatformView.PointerPressed -= OnPointerPressed;
            graphicsViewPlatformView.PointerPressed += OnPointerPressed;
            graphicsViewPlatformView.PointerMoved -= OnPointerMoved;
            graphicsViewPlatformView.PointerMoved += OnPointerMoved;
            graphicsViewPlatformView.PointerReleased -= OnPointerReleased;
            graphicsViewPlatformView.PointerReleased += OnPointerReleased;
            graphicsViewPlatformView.KeyDown -= OnKeyDown;
            graphicsViewPlatformView.KeyDown += OnKeyDown;
            graphicsViewPlatformView.CharacterReceived -= OnCharacterReceived;
            graphicsViewPlatformView.CharacterReceived += OnCharacterReceived;
        }

        try
        {
            if (platformWindow.CoreWindow != null)
            {
                platformWindow.CoreWindow.CharacterReceived -= (s, e) => { }; // Won't work for anonymous methods but we try to be safe.
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
        catch
        {
        }
    }
}