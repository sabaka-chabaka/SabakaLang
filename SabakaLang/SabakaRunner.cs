using SabakaLang.VM;

namespace SabakaLang;

public class SabakaRunner
{
    public static void Run(string source, string? filePath = null, TextReader? input = null, TextWriter? output = null)
    {
        var start = DateTime.Now;
        
        var lexer = new Lexer.Lexer(source);
        var tokens = lexer.Tokenize(false);

        var parser = new Parser.Parser(tokens);
        var program = parser.ParseProgram();

        var compiler = new Compiler.Compiler();
        var bytecode = compiler.Compile(program, filePath);
        
        var stop = DateTime.Now;
        
        Console.WriteLine($"Compilation time: {(stop - start).TotalMilliseconds}ms. Starting");
        Console.WriteLine();
        
        var externals = compiler.ExternalDelegates;
        
        var vm = new VirtualMachine(input, output, externals);
        vm.Execute(bytecode);
    }
}