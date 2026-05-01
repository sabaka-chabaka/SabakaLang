using SabakaLang.Compiler;
using SabakaLang.Compiler.Binding;
using SabakaLang.Compiler.Lexing;
using SabakaLang.Compiler.Parsing;
using SabakaLang.Runtime;

namespace SabakaLang.Cli;

public static class Program
{
    public static void Main(string[] args)
    {
        if (args.Length < 1)
            throw new ArgumentException("Usage: sabaka <command> <args>");

        switch (args[0])
        {
            default:
                throw new Exception($"Unknown command '{args[0]}'");

            case "run":
                var src = File.ReadAllText(args[1]);

                var lexer = new Lexer(src);
                var parser = new Parser(lexer.Tokenize());
                var binder = new Binder();
                var compiler = new Compiler.Compiling.Compiler();
                var result = compiler.Compile(parser.Parse().Statements, binder.Bind(parser.Parse().Statements));

                var vm = new VirtualMachine();
                vm.Execute(result.Code.ToList());
                
                break;
            
            case "version":
                Console.WriteLine("0.1");
                break;
            
            case "pack":
                new Packer().Pack(args[1].Trim(), args[2].Trim());
                break;
        }
    }
}