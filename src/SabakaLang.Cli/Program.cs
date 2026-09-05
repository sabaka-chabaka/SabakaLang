using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using SabakaLang.Compiler;
using SabakaLang.Compiler.Binding;
using SabakaLang.Compiler.Compiling;
using SabakaLang.Compiler.Lexing;
using SabakaLang.Compiler.Parsing;
using SabakaLang.Runtime;
using Spectre.Console;
using SQR;

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

                /*if (args.Length > 1 && args[2] == "--to-il")
                {
                    var transpiler = new Transpiler.Transpiler();
                    
                    var csharp = transpiler.Transpile(src);
                    
                    Console.WriteLine(csharp);
                    
                    var options = ScriptOptions.Default
                        .AddReferences(
                            typeof(Console).Assembly,
                            typeof(System.Linq.Enumerable).Assembly,
                            typeof(System.Collections.Generic.List<>).Assembly,
                            typeof(System.IO.File).Assembly,
                            typeof(System.Threading.Thread).Assembly
                        )
                        .AddImports("System", "System.IO", "System.Linq", "System.Collections.Generic", "System.Threading");
                    
                    CSharpScript.RunAsync(csharp, options).Wait();
                    
                    break;
                }*/

                var lexer = new Lexer(src);
                var parser = new Parser(lexer.Tokenize()).Parse();
                var binder = new Binder();
                var compiler = new Compiler.Compiling.Compiler();
                var result = compiler.Compile(parser.Statements, binder.Bind(parser.Statements));

                var vm = new VirtualMachine();
                vm.Execute(result.Code.ToList());
                
                break;
            
            case "check":
                CheckCode(File.ReadAllText(args[1]));
                break;
            
            case "qr":
                var source = File.ReadAllText(args[1]);
                
                var lex = new Lexer(source);
                var parse = new Parser(lex.Tokenize()).Parse();
                var bind = new Binder();
                var comp = new Compiler.Compiling.Compiler();
                var res = comp.Compile(parse.Statements, bind.Bind(parse.Statements));

                var gen = new SQRGenerator();
                var qr = gen.Generate(BinaryWriterWorker.Pack(res.Code.ToList()));
                
                File.WriteAllBytes("resultqr.png", qr);
                break;
            
            case "fromqr":
                var qrc = File.ReadAllBytes(args[1]);
                var bytes = new SQRDecoder().Decode(qrc);
                var bytecode = BinaryReaderWorker.Read(bytes);
                var vmc = new VirtualMachine();
                if (bytecode != null) vmc.Execute(bytecode);
                break;
            
            case "version":
                Console.WriteLine("0.1");
                break;
            
            case "pack":
                new Packer().Pack(args[1].Trim(), args[2].Trim(), args.Any(x => x == "--to-il"));
                break;
        }
    }

    private static void CheckCode(string src)
    {
        AnsiConsole.Status().Spinner(Spinner.Known.Dots).Start("[green] sabaka check running[/]", ctx =>
        {
            var start = DateTime.UtcNow;

            try
            {
                var lexer = new Lexer(src);
                var parser = new Parser(lexer.Tokenize()).Parse();
                var binder = new Binder();
                var compiler = new Compiler.Compiling.Compiler();
                compiler.Compile(parser.Statements, binder.Bind(parser.Statements));

            }
            catch (Exception e)
            {
                ctx.Status($"[red] sabaka check failed for {DateTime.Now - start}. Exception: {e} [/]");
            }
            
            ctx.Status($"[green] sabaka check successfully runned for {DateTime.UtcNow - start}[/]");
        });
    }
}
