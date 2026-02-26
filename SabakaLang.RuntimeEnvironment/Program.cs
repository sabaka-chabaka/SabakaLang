using SabakaLang.VM;

namespace SabakaLang.RuntimeEnvironment;

public class Program
{
    public static void Main(string[] args)
    {
        Run("print(2 * 2 + 3 + 4 + 2 - 8 / 2 + 24 * 21 + 3 / 2 + (2 / 2));");
    }

    private static void Run(string source, TextReader? input = null, TextWriter? output = null)
    {
        var loader = new SarLoader();
        var sar = loader.Load(source);

        var entryBytes = sar.GetEntryBytes();
        var module = new CompiledReader().Read(entryBytes);  // здесь уже весь код, включая импортированные файлы

        var vm = new VirtualMachine(input, output);
        vm.Execute(module);  // выполняем линейный поток инструкций
    }
}
