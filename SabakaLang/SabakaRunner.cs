using SabakaLang.VM;

namespace SabakaLang;

public class SabakaRunner
{
    public static double Run(string source)
    {
        var lexer = new Lexer.Lexer(source);
        var tokens = lexer.Tokenize();

        var parser = new Parser.Parser(tokens);
        var ast = parser.Parse();

        var compiler = new Compiler.Compiler();
        var bytecode = compiler.Compile(ast);

        var vm = new VirtualMachine();
        return vm.Execute(bytecode);
    }
}