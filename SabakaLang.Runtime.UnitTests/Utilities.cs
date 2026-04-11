using SabakaLang.Compiler;
using SabakaLang.Runtime;

namespace SabakaLang.Runtime.UnitTests;

public class Utilities
{
    protected static string Output(string source, string? stdin = null, int gcThreshold = 512)
    {
        var rt = new SabakaRuntime();
        var raw = rt.RunAndCapture(source, stdin, gcThreshold);
        return raw.TrimEnd('\r', '\n');
    }
 
    protected static string[] Lines(string source, string? stdin = null)
        => Output(source, stdin).Split('\n', StringSplitOptions.RemoveEmptyEntries);
 
    protected static void RunError(string source, string fragment)
    {
        var ex = Assert.Throws<RuntimeException>(() => Output(source));
        Assert.Contains(fragment, ex.Message, StringComparison.OrdinalIgnoreCase);
    }
 
    protected static void RunOk(string source)
    {
        var rt = new SabakaRuntime();
        rt.RunAndCapture(source); 
    }
 
 
    protected static (VirtualMachine vm, StringWriter sw) BuildVm(
        string? stdin = null,
        Dictionary<string, Func<Value[], Value>>? externals = null,
        int gcThreshold = 512)
    {
        var sw  = new StringWriter();
        var sr  = stdin is not null ? (TextReader)new StringReader(stdin) : TextReader.Null;
        var vm  = new VirtualMachine(sr, sw, externals, gcThreshold);
        return (vm, sw);
    }
 
    protected static List<Instruction> CompileToInstructions(string source)
    {
        var rt = new SabakaRuntime();
        var result = rt.Compile(source);
        Assert.False(result.HasErrors,
            "Compile errors:\n" + string.Join("\n", result.Errors));
        return result.Code.ToList();
    }
}