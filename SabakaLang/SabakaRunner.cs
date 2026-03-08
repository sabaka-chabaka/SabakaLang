using System.IO;
using SabakaLang.Types;
using SabakaLang.VM;

namespace SabakaLang;

public class SabakaRunner
{
    public static void Run(string source, string? filePath = null, TextReader? input = null, TextWriter? output = null)
    {
        var start = DateTime.Now;

        var lexer   = new Lexer.Lexer(source);
        var tokens  = lexer.Tokenize(false);
        var parser  = new Parser.Parser(tokens);
        var program = parser.ParseProgram();

        var compiler = new Compiler.Compiler();
        var bytecode = compiler.Compile(program, filePath);

        Console.WriteLine($"Compilation time: {(DateTime.Now - start).TotalMilliseconds}ms. Starting");
        Console.WriteLine();

        var vm = new VirtualMachine(input, output, compiler.ExternalDelegates);

        // Устанавливаем InvokeCallback для DLL-модулей (SabakaUI и т.п.)
        // чтобы они могли вызывать SabakaLang-функции обратно.
        WireCallbacks(compiler.CallbackReceivers, vm);

        vm.Execute(bytecode);
    }

    internal static void WireCallbacks(IReadOnlyList<object> receivers, VirtualMachine vm)
    {
        foreach (var receiver in receivers)
        {
            var iface = receiver.GetType().GetInterfaces()
                .FirstOrDefault(i => i.Name == "ICallbackReceiver");
            if (iface == null) continue;

            var prop = iface.GetProperty("InvokeCallback");
            if (prop == null) continue;

            Func<string, string[], string> callback = (funcName, args) =>
            {
                vm.CallFunction(funcName, args.Select(Value.FromString).ToArray());
                return "";
            };

            prop.SetValue(receiver, callback);
        }
    }
}