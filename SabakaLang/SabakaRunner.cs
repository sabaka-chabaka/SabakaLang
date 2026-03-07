using System.IO;
using SabakaLang.Types;
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

        // Wire up InvokeCallback for modules implementing ICallbackReceiver.
        // We use reflection because Compiler doesn't reference the SDK assembly.
        // The callback is called on the SCRIPT thread (inside ui.run()) — no threading issues.
        foreach (object receiver in compiler.CallbackReceivers)
        {
            var iface = receiver.GetType().GetInterfaces()
                .FirstOrDefault(i => i.Name == "ICallbackReceiver");
            if (iface == null) continue;

            var prop = iface.GetProperty("InvokeCallback");
            if (prop == null) continue;

            Func<string, string[], string> cb = (funcName, args) =>
            {
                Value[] vals = args.Select(Value.FromString).ToArray();
                vm.CallFunction(funcName, vals);
                return "";
            };

            prop.SetValue(receiver, cb);
        }

        vm.Execute(bytecode);
    }
}