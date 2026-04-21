using SabakaLang.Compiler;
using SabakaLang.LanguageServer;
using SabakaLang.Studio.Editor.Highlighting;

namespace SabakaLang.Studio.Editor.Intellisense;

public sealed class CompletionEntry
{
    public string Label    { get; set; } = "";
    public string Detail   { get; set; } = "";
    public string Insert   { get; set; } = "";
    public CompletionKind Kind { get; set; }
    public bool   IsSnippet    { get; set; }
}
 
public enum CompletionKind
{
    Keyword, Function, Method, Variable, Class, Interface,
    Struct, Enum, EnumMember, Property, Parameter, Namespace, Snippet
}

public sealed class HoverInfo
{
    public string Markdown { get; set; } = "";
    public int    Line     { get; set; }
    public int    Column   { get; set; }
}

public sealed class SignatureInfo
{
    public string Label      { get; set; } = "";
    public int    ActiveParam{ get; set; }
    public List<string> Params { get; set; } = [];
}

public sealed class CompletionProvider(DocumentStore store)
{
    private readonly DocumentStore _store = store;
    
    public List<CompletionEntry> GetCompletions(string uri, int line, int col)
    {
        var analysis = _store.Get(uri);
        if (analysis is null) return [];
 
        var source = analysis.Source;
        var offset = LineColToOffset(source, line, col);
        var result = new List<CompletionEntry>();
 
        foreach (var kw in Keywords)
            result.Add(new CompletionEntry { Label = kw, Kind = CompletionKind.Keyword, Insert = kw });
 
        foreach (var sym in analysis.Bind.Symbols.All)
        {
            if (sym.ParentName is not null &&
                sym.Kind is SymbolKind.Field or SymbolKind.Method or SymbolKind.EnumMember)
                continue;
 
            result.Add(new CompletionEntry
            {
                Label   = sym.Name,
                Detail  = FormatDetail(sym),
                Insert  = sym.Kind is SymbolKind.Function or SymbolKind.Method or SymbolKind.BuiltIn
                    ? $"{sym.Name}($0)" : sym.Name,
                IsSnippet = sym.Kind is SymbolKind.Function or SymbolKind.Method or SymbolKind.BuiltIn,
                Kind    = MapKind(sym.Kind)
            });
        }
 
        return result;
    }
    
    public HoverInfo? GetHover(string uri, int line, int col)
    {
        var analysis = _store.Get(uri);
        if (analysis is null) return null;
 
        var word = WordAt(analysis.Source, line, col);
        if (word is null) return null;
 
        var sym = analysis.Bind.Symbols.Lookup(word).FirstOrDefault();
        if (sym is null) return null;
 
        return new HoverInfo { Markdown = FormatDetail(sym), Line = line, Column = col };
    }
 
    public SignatureInfo? GetSignature(string uri, int line, int col)
    {
        var analysis = _store.Get(uri);
        if (analysis is null) return null;
 
        var source = analysis.Source;
        var offset = LineColToOffset(source, line, col);
 
        var depth = 0;
        var activeParam = 0;
        for (var i = offset - 1; i >= 0; i--)
        {
            var c = source[i];
            if (c == ')') depth++;
            else if (c == '(')
            {
                if (depth > 0) { depth--; continue; }
                var nameEnd = i;
                while (nameEnd > 0 && char.IsWhiteSpace(source[nameEnd - 1])) nameEnd--;
                var nameStart = nameEnd;
                while (nameStart > 0 && (char.IsLetterOrDigit(source[nameStart - 1]) || source[nameStart - 1] == '_'))
                    nameStart--;
                var name = source[nameStart..nameEnd];
                var sym  = analysis.Bind.Symbols.Lookup(name).FirstOrDefault();
                if (sym is null) return null;
 
                var parms = sym.Parameters?.Split(',').Select(p => p.Trim()).ToList() ?? [];
                return new SignatureInfo
                {
                    Label       = $"{sym.Name}({sym.Parameters})",
                    ActiveParam = activeParam,
                    Params      = parms
                };
            }
            else if (c == ',') activeParam++;
        }
        return null;
    }
    
    private static int LineColToOffset(string source, int line, int col)
    {
        var l = 0; var i = 0;
        while (i < source.Length && l < line)
            if (source[i++] == '\n') l++;
        return Math.Min(i + col, source.Length);
    }
 
    private static string? WordAt(string source, int line, int col)
    {
        var offset = LineColToOffset(source, line, col);
        if (offset >= source.Length) return null;
        static bool IsId(char c) => char.IsLetterOrDigit(c) || c == '_';
        var start = offset; while (start > 0 && IsId(source[start - 1])) start--;
        var end   = offset; while (end < source.Length && IsId(source[end])) end++;
        return start == end ? null : source[start..end];
    }
 
    private static string FormatDetail(Symbol sym) => sym.Kind switch
    {
        SymbolKind.Function or SymbolKind.Method or SymbolKind.BuiltIn
            => $"{sym.Type} {sym.Name}({sym.Parameters ?? ""})",
        _   => $"{sym.Type} {sym.Name}"
    };
 
    private static CompletionKind MapKind(SymbolKind k) => k switch
    {
        SymbolKind.Function  => CompletionKind.Function,
        SymbolKind.Method    => CompletionKind.Method,
        SymbolKind.BuiltIn   => CompletionKind.Function,
        SymbolKind.Class     => CompletionKind.Class,
        SymbolKind.Interface => CompletionKind.Interface,
        SymbolKind.Struct    => CompletionKind.Struct,
        SymbolKind.Enum      => CompletionKind.Enum,
        SymbolKind.EnumMember=> CompletionKind.EnumMember,
        SymbolKind.Field     => CompletionKind.Property,
        SymbolKind.Parameter => CompletionKind.Parameter,
        SymbolKind.Module    => CompletionKind.Namespace,
        _                    => CompletionKind.Variable
    };
 
    private static readonly string[] Keywords =
    [
        "if", "else", "while", "for", "foreach", "in", "return",
        "class", "interface", "struct", "enum", "import", "new",
        "override", "super", "null", "is", "switch", "case", "default",
        "true", "false", "public", "private", "protected",
        "int", "float", "bool", "string", "void"
    ];
}

public sealed class CompletionPopup : ContentView
{
    private readonly ListView          _list;
    private List<CompletionEntry>      _entries = [];
    private int                        _selected;
 
    public event Action<CompletionEntry>? ItemCommitted;
 
    public CompletionPopup()
    {
        IsVisible = false;
 
        _list = new ListView
        {
            BackgroundColor   = StudioTheme.PopupBackground,
            SeparatorColor    = StudioTheme.Border,
            SeparatorVisibility = SeparatorVisibility.Default,
            HeightRequest     = 200,
            WidthRequest      = 340,
            ItemTemplate      = new DataTemplate(CreateCell)
        };
 
        _list.ItemSelected += (_, e) =>
        {
            if (e.SelectedItem is CompletionEntry entry)
                ItemCommitted?.Invoke(entry);
        };
 
        Content = new Frame
        {
            Padding         = 0,
            CornerRadius    = 4,
            BackgroundColor = StudioTheme.PopupBackground,
            BorderColor     = StudioTheme.PopupBorder,
            HasShadow       = true,
            Content         = _list
        };
    }
 
    public void Show(List<CompletionEntry> entries, double x, double y)
    {
        _entries  = entries;
        _selected = 0;
        _list.ItemsSource = entries;
        TranslationX = x;
        TranslationY = y;
        IsVisible    = true;
        if (entries.Count > 0) _list.SelectedItem = entries[0];
    }
 
    public void Hide() => IsVisible = false;
 
    public void MoveSelection(int delta)
    {
        if (_entries.Count == 0) return;
        _selected = (_selected + delta + _entries.Count) % _entries.Count;
        _list.SelectedItem = _entries[_selected];
        _list.ScrollTo(_entries[_selected], ScrollToPosition.MakeVisible, false);
    }
 
    public void CommitSelected()
    {
        if (_entries.Count > 0)
            ItemCommitted?.Invoke(_entries[_selected]);
    }
 
    public bool IsOpen => IsVisible;
 
    private static Cell CreateCell()
    {
        var icon  = new Label { FontSize = 11, WidthRequest = 18, TextColor = Colors.Gray };
        var label = new Label { FontFamily = StudioTheme.FontFamily, FontSize = StudioTheme.FontSize,
                                TextColor = StudioTheme.PopupText };
        var detail= new Label { FontSize = 11, TextColor = StudioTheme.PopupDetail };
 
        var cell = new ViewCell();
        var row  = new HorizontalStackLayout { Padding = new Thickness(6, 2), Spacing = 6,
                                               Children = { icon, label, detail } };
 
        cell.View = row;
 
        cell.BindingContextChanged += (_, _) =>
        {
            if (cell.BindingContext is not CompletionEntry e) return;
            icon.Text   = KindIcon(e.Kind);
            label.Text  = e.Label;
            detail.Text = e.Detail;
        };
 
        return cell;
    }
 
    private static string KindIcon(CompletionKind k) => k switch
    {
        CompletionKind.Keyword   => "K",
        CompletionKind.Function  => "F",
        CompletionKind.Method    => "M",
        CompletionKind.Class     => "C",
        CompletionKind.Interface => "I",
        CompletionKind.Enum      => "E",
        CompletionKind.Property  => "P",
        _                        => "·"
    };
}
 