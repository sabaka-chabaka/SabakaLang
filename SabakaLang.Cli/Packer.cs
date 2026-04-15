using SabakaLang.Compiler;
using Binder = SabakaLang.Compiler.Binder;

namespace SabakaLang.Cli;

public class Packer
{
    public void Pack(string srcDir, string destDir)
    {
        if (!Directory.Exists(srcDir))
            throw new DirectoryNotFoundException($"Source dir not found: {srcDir}");
        if (!Directory.Exists(destDir))
            Directory.CreateDirectory(destDir);
        
        string mainFile = Path.Combine(srcDir, "main.sabaka");

        var lexer = new Lexer(File.ReadAllText(mainFile)).Tokenize();
        var parser = new Parser(lexer).Parse();
        var binder = new Binder().Bind(parser.Statements);
        var compiler = new Compiler.Compiler();
        var result = compiler.Compile(parser.Statements, binder);
        
        //TODO
    }
}