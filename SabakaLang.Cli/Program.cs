using SabakaLang.Compiler;
using SabakaLang.Runtime;

namespace SabakaLang.Cli;

public class Program
{
    public static void Main(string[] args)
    {
        if (args.Length < 2)
            throw new ArgumentException("Usage: sabaka <command> <args>");

        switch (args[0])
        {
            default:
                throw new Exception($"Unknown command '{args[0]}'");
                break;
            
            case "run":
                var src = File.ReadAllText(args[1]);

                var lexer = new Lexer(src);
                var parser = new Parser(lexer.Tokenize());
                var binder = new Binder();
                var compiler = new Compiler.Compiler();
                var result = compiler.Compile(parser.Parse().Statements, binder.Bind(parser.Parse().Statements));

                var vm = new VirtualMachine();
                vm.Execute(result.Code.ToList());
                
                break;
            
            case "version":
                Console.WriteLine("0.1");
                break;
        }
    }
}