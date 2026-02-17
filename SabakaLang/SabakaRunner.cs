using SabakaLang.VM;

namespace SabakaLang;

public class SabakaRunner
{
    public static void Run(string source, TextReader? input = null, TextWriter? output = null)
    {
        var lexer = new Lexer.Lexer(source);
        var tokens = lexer.Tokenize(false);

        var parser = new Parser.Parser(tokens);
        var program = parser.ParseProgram();

        var compiler = new Compiler.Compiler();
        var bytecode = compiler.Compile(program);

        var vm = new VirtualMachine(input, output);
        vm.Execute(bytecode);
    }
}