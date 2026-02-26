using System.Text.Json;
using SabakaLang.Compiler;

namespace SabakaLang.RuntimeEnvironment;

public class CompiledReader
{
    public List<Instruction> Read(byte[] bytes)
    {
        return JsonSerializer.Deserialize<List<Instruction>>(bytes)
               ?? throw new Exception("Failed to read .sabakac");
    }
}