using SabakaLang.VM;

namespace SabakaLang.RuntimeEnvironment;

public class Program
{
    public static void Main(string[] args)
    {
        string filePath = args.Length > 0 ? args[0] : "test_threads.sabaka";
        if (!File.Exists(filePath)) {
            Console.WriteLine($"File not found: {filePath}");
            return;
        }
        string source = File.ReadAllText(filePath);
        SabakaLang.SabakaRunner.Run(source, filePath);
    }

    private static void Run(string source, TextReader? input = null, TextWriter? output = null)
    {
        var sar = CompiledReader.Read(source); 
        
        var vm = new VirtualMachine(input, output);
        vm.Execute(sar); 
    }
}
