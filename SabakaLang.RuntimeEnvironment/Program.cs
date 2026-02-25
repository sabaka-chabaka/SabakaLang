using SabakaLang.Compiler;
using SabakaLang.VM;

namespace SabakaLang.RuntimeEnvironment;

public class Program
{
    public static void Main(string[] args)
    {
        Run("print(2 * 2 + 3 + 4 + 2 - 8 / 2 + 24 * 21 + 3 / 2 + (2 / 2));");
    }

    private static void Run(string source, string? filePath = null, TextReader? input = null, TextWriter? output = null)
    {
        /* var lexer = new Lexer.Lexer(source);
        var tokens = lexer.Tokenize(false);

        var parser = new Parser.Parser(tokens);
        var program = parser.ParseProgram();

        var compiler = new Compiler.Compiler();
        var bytecode = compiler.Compile(program, filePath);
        
        var externals = compiler.ExternalDelegates;
        
        var vm = new VirtualMachine(input, output, externals);
        vm.Execute(bytecode); */
        
        var lexer = new Lexer.Lexer(source);
        var tokens = lexer.Tokenize(false);

        var parser = new Parser.Parser(tokens);
        var program = parser.ParseProgram();

        var compiler = new Compiler.Compiler();
        var bytecode = compiler.Compile(program, filePath);

        foreach (Instruction instruction in bytecode)
        {
            Console.WriteLine($"{instruction.OpCode} {instruction.Operand} ");
        }

        /*var loader = new SarLoader();
        var sar = loader.Load(source);
        var entryBytes = sar.GetEntryBytes();
        var module = new SabakacReader().Read(entryBytes);
        var vm = new VirtualMachine(input, output);
        vm.Execute(module);*/
    }
}