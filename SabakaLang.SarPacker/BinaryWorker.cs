using System.Text;
using SabakaLang.Compiler;
using SabakaLang.Types;

namespace SabakaLang.SarPacker;

public static class BinaryWriterWorker
{
    public static void Pack(List<Instruction> bytecode, string path)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs, Encoding.UTF8);

        bw.Write(bytecode.Count);

        foreach (var instr in bytecode)
        {
            bw.Write((int)instr.OpCode);

            WriteString(bw, instr.Name);

            WriteObject(bw, instr.Operand);
            WriteObject(bw, instr.Extra);
        }
    }

    private static void WriteString(BinaryWriter bw, string? s)
    {
        if (s == null)
        {
            bw.Write(-1);
        }
        else
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            bw.Write(bytes.Length);
            bw.Write(bytes);
        }
    }

    private static void WriteObject(BinaryWriter bw, object? obj)
    {
        if (obj == null)
        {
            bw.Write((byte)0); // Null
        }
        else if (obj is int i)
        {
            bw.Write((byte)1);
            bw.Write(i);
        }
        else if (obj is double f)
        {
            bw.Write((byte)2);
            bw.Write(f);
        }
        else if (obj is bool b)
        {
            bw.Write((byte)3);
            bw.Write(b);
        }
        else if (obj is string s)
        {
            bw.Write((byte)4);
            WriteString(new BinaryWriter(bw.BaseStream, Encoding.UTF8, true), s);
        }
        else if (obj is Value v)
        {
            bw.Write((byte)5); // Value
            WriteValue(bw, v);
        }
        else
        {
            throw new Exception("Unsupported type in SAR");
        }
    }

    private static void WriteValue(BinaryWriter bw, Value v)
    {
        bw.Write((int)v.Type);
        bw.Write(v.Int);
        bw.Write(v.Float);
        bw.Write(v.Bool);
        WriteString(bw, v.String);

        if (v.Array != null)
        {
            bw.Write(v.Array.Count);
            foreach (var item in v.Array)
                WriteValue(bw, item);
        }
        else
        {
            bw.Write(0);
        }

        if (v.Struct != null)
        {
            bw.Write(v.Struct.Count);
            foreach (var kv in v.Struct)
            {
                WriteString(bw, kv.Key);
                WriteValue(bw, kv.Value);
            }
        }
        else
        {
            bw.Write(0);
        }

        WriteString(bw, v.ClassName);
    }
}

public static class BinaryReaderWorker
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