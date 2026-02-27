using System.Collections;
using System.Reflection;
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

    public static void WriteObject(BinaryWriter writer, object? obj)
    {
        if (obj == null)
        {
            writer.Write((byte)0);
        }
        else if (obj is int i)
        {
            writer.Write((byte)1);
            writer.Write(i);
        }
        else if (obj is double d)
        {
            writer.Write((byte)2);
            writer.Write(d);
        }
        else if (obj is bool b)
        {
            writer.Write((byte)3);
            writer.Write(b);
        }
        else if (obj is string s)
        {
            writer.Write((byte)4);
            writer.Write(s);
        }
        else if (obj.GetType().IsEnum)
        {
            writer.Write((byte)5);
            writer.Write(obj.ToString());
            writer.Write(obj.GetType().AssemblyQualifiedName!);
        }
        else if (obj is IList list)
        {
            writer.Write((byte)6);
            writer.Write(list.Count);
            foreach (var item in list)
                WriteObject(writer, item); // каждый элемент сам хранит тип
        }
        else // класс / struct
        {
            writer.Write((byte)7);
            writer.Write(obj.GetType().AssemblyQualifiedName!);

            var fields = obj.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            writer.Write(fields.Length);
            foreach (var field in fields)
                WriteObject(writer, field.GetValue(obj));
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

    public static object? ReadObject(BinaryReader reader)
    {
        byte typeMarker = reader.ReadByte();
        switch (typeMarker)
        {
            case 0: return null;
            case 1: return reader.ReadInt32();
            case 2: return reader.ReadDouble();
            case 3: return reader.ReadBoolean();
            case 4: return reader.ReadString();
            case 5:
            {
                string enumStr = reader.ReadString();
                string typeName = reader.ReadString();
                Type enumType = Type.GetType(typeName)!;
                return Enum.Parse(enumType, enumStr);
            }
            case 6:
            {
                int count = reader.ReadInt32();
                var list = new List<object?>();
                for (int i = 0; i < count; i++)
                    list.Add(ReadObject(reader));
                return list;
            }
            case 7:
            {
                string typeName = reader.ReadString();
                Type type = Type.GetType(typeName)!;
                object obj = Activator.CreateInstance(type)!;

                int fieldCount = reader.ReadInt32();
                var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                for (int i = 0; i < fieldCount; i++)
                {
                    var val = ReadObject(reader);
                    fields[i].SetValue(obj, val);
                }

                return obj;
            }
            default:
                throw new Exception("Unknown type marker");
        }
    }

    public static T Unpack<T>(string path)
    {
        using var fs = File.OpenRead(path);
        using var reader = new BinaryReader(fs);
        return (T)ReadObject(reader)!;
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