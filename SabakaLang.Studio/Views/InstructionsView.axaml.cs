using System;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using SabakaLang.Compiler;
using SabakaLang.Compiler.Binding;
using SabakaLang.Compiler.Lexing;
using SabakaLang.Compiler.Parsing;

namespace SabakaLang.Studio.Views;

public partial class InstructionsView : Window
{
    private string? _src;
    
    public InstructionsView(string source)
    {
        InitializeComponent();
    }

    public void SetSource(string source)
    {
        _src = source;
    }
    
    protected override void OnOpened(EventArgs e)
    {
        var val = new Compiler.Compiling.Compiler().Compile(new Parser(new Lexer(_src!).Tokenize()).Parse().Statements,
            new Binder().Bind(new Parser(new Lexer(_src!).Tokenize()).Parse().Statements)).Code.ToList();
        
        var sb = new StringBuilder();

        foreach (var vars in val)
        {
            sb.Append($"{vars}\n");
        }
        
        CodeArea.Text = sb.ToString();
    }
}