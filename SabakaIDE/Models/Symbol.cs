namespace SabakaIDE.Models;

public class Symbol
{
    public string Name { get; set; }
    public SymbolType Type { get; set; }

    public int StartOffset { get; set; }
    public int EndOffset { get; set; }

    public object DeclarationNode { get; set; }
}
