using SabakaLang.VM;

namespace SabakaLang.RuntimeEnvironment;

public class Program
{
    public static void Main(string[] args)
    {
        Run("D:\\builderTest\\build\\MyApp.sar");
    }

    private static void Run(string source, TextReader? input = null, TextWriter? output = null)
    {
        var sar = CompiledReader.Read(source); 
        
        var vm = new VirtualMachine(input, output);
        vm.Execute(sar); 
    }
}
