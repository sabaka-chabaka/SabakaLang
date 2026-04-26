using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SabakaLang.Compiler;
using SabakaLang.Runtime;
using SabakaLang.Studio.Helpers;

namespace SabakaLang.Debugger;

public partial class MainWindow : Window
{
    private List<Instruction> _bytecode = new();
    private int _ip = 0;

    public MainWindow()
    {
        InitializeComponent();
        StepButton.Click += StepButton_OnClick;
        if (Program.InitPath != null!)
        {
            PathBlock.Text = Program.InitPath;
        }
        DebugButton.Click += (o, args) => _ = DebugButton_OnClickAsync();
    }

    private async Task DebugButton_OnClickAsync()
    {
        if (string.IsNullOrEmpty(PathBlock.Text))
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
                Title = "Open SabakaSource",
                AllowMultiple = false
            });

            if (files.Count >= 1) PathBlock.Text = files[0].Path.LocalPath;
            else return;
        }

        try 
        {
            var source = await File.ReadAllTextAsync(PathBlock.Text);
            var lex = new Lexer(source).Tokenize();
            var parser = new Parser(lex).Parse();
            var bind = new Binder().Bind(parser.Statements);
            var comp = new Compiler.Compiler();
            
            _bytecode = comp.Compile(parser.Statements, bind).Code.ToList(); 
            
            _ip = 0;
            ConsoleOutput.Text = "Compilation successful. Ready to debug.\n";
            UpdateCodeDisplay();
            
            ConsoleHelper.Show();
        }
        catch (Exception ex)
        {
            ConsoleOutput.Text = $"Error: {ex.Message}";
        }
    }

    private void StepButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_ip >= _bytecode.Count)
            return;

        var vm = new VirtualMachine();

        vm.Execute(_bytecode.Take(_ip + 1).ToList());

        ConsoleOutput.Text += $"[{_ip}] {_bytecode[_ip]}\n";

        _ip++;
        UpdateCodeDisplay();
    }

    private void UpdateCodeDisplay()
    {
        var lines = _bytecode.Select((inst, i) => 
            (i == _ip ? " > " : "   ") + $"[{i:D3}] {inst.ToString()}");
        
        CodeArea.Text = string.Join(Environment.NewLine, lines);
    }
}
