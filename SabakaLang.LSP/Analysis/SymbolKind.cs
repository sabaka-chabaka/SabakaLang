namespace SabakaLang.LSP.Analysis;

public enum SymbolKind
{
    Variable,
    Function,
    Class,
    Parameter,
    BuiltIn,
    Method,
    Field,
    Module   // alias from "import X as alias"
}