using SabakaLang.VM;

namespace SabakaLang;

public class SabakaRunner
{
    public static void Run(string source)
    {
        var lexer = new Lexer.Lexer(source);
        var tokens = lexer.Tokenize();

        var parser = new Parser.Parser(tokens);
        var program = parser.ParseProgram();

        var compiler = new Compiler.Compiler();
        var bytecode = compiler.Compile(program);

        var vm = new VirtualMachine();
        vm.Execute(bytecode);
    }
}