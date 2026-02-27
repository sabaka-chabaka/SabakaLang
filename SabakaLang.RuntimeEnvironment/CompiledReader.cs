using System.Collections;
using System.Resources;
using System.Text;
using SabakaLang.Compiler;
using SabakaLang.Types;

namespace SabakaLang.RuntimeEnvironment;

public class CompiledReader
{
    public static List<Instruction> Read(string path)
    {
        return BinaryReaderWorker.Read(path);

        throw new KeyNotFoundException("No instructions found in SAR file.");
    }
}

internal static class BinaryReaderWorker
{
    public static List<Instruction> Read(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        using var br = new BinaryReader(fs, Encoding.UTF8);

        int count = br.ReadInt32();
        var list = new List<Instruction>();

        for (int i = 0; i < count; i++)
        {
            var instr = new Instruction
            {
                OpCode = (OpCode)br.ReadInt32(),
                Name = ReadString(br),
                Operand = ReadObject(br),
                Extra = ReadObject(br)
            };
            list.Add(instr);
        }

        return list;
    }

    private static string? ReadString(BinaryReader br)
    {
        int len = br.ReadInt32();
        if (len == -1) return null;
        var bytes = br.ReadBytes(len);
        return Encoding.UTF8.GetString(bytes);
    }

    private static object? ReadObject(BinaryReader br)
    {
        byte type = br.ReadByte();
        return type switch
        {
            0 => null,
            1 => br.ReadInt32(),
            2 => br.ReadDouble(),
            3 => br.ReadBoolean(),
            4 => ReadString(br),
            5 => ReadValue(br),
            _ => throw new Exception("Unknown type in SAR")
        };
    }

    private static Value ReadValue(BinaryReader br)
    {
        var v = new Value
        {
            Type = (SabakaType)br.ReadInt32(),
            Int = br.ReadInt32(),
            Float = br.ReadDouble(),
            Bool = br.ReadBoolean(),
            String = ReadString(br)
        };

        int arrayLen = br.ReadInt32();
        if (arrayLen > 0)
        {
            v.Array = new List<Value>();
            for (int i = 0; i < arrayLen; i++)
                v.Array.Add(ReadValue(br));
        }

        int structLen = br.ReadInt32();
        if (structLen > 0)
        {
            v.Struct = new Dictionary<string, Value>();
            for (int i = 0; i < structLen; i++)
            {
                string key = ReadString(br)!;
                v.Struct[key] = ReadValue(br);
            }
        }

        v.ClassName = ReadString(br);

        return v;
    }
}