using SabakaLang.Compiler;
using SabakaLang.Runtime;

namespace SabakaLang.Runner;

public static class Program
{
    public static void Main(string[] args)
    {
        List<Instruction> instructions = Reader.Read(args[0]);
        
        VirtualMachine virtualMachine = new();
        virtualMachine.Execute(instructions);
    }
}