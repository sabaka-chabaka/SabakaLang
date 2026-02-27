using System.Collections;
using System.Reflection;
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

    public static object? ReadObject(BinaryReader reader)
    {
        byte typeMarker = reader.ReadByte();
        switch (typeMarker)
        {
            case 0: return null;
            case 1: return Value.FromInt(reader.ReadInt32());
            case 2: return Value.FromFloat(reader.ReadDouble());
            case 3: return Value.FromBool(reader.ReadBoolean());
            case 4: // string
            {
                string str = reader.ReadString();
                return Value.FromString(str); // <- теперь всегда Value
            }
            case 5:
            {
                string enumStr = reader.ReadString();
                string typeName = reader.ReadString();
                Type enumType = Type.GetType(typeName)!;
                return Enum.Parse(enumType, enumStr);
            }
            case 6: // list
            {
                int count = reader.ReadInt32();
                var list = new List<Value>();
                for (int i = 0; i < count; i++)
                    list.Add((Value)ReadObject(reader)!); // безопасно, потому что ReadObject всегда Value
                return Value.FromArray(list);
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
                    var field = fields[i];
                    var val = ReadObject(reader);

                    // если это Value, достаем нужный реальный тип
                    if (val is Value v)
                    {
                        object? realVal = v.Type switch
                        {
                            SabakaType.Int => v.Int,
                            SabakaType.Float => v.Float,
                            SabakaType.Bool => v.Bool,
                            SabakaType.String => v.String,
                            SabakaType.Array => v.Array?.Select(x => x).ToList(), // можно кастить внутрь
                            SabakaType.Struct => v.Struct?.ToDictionary(kv => kv.Key, kv => kv.Value),
                            _ => null
                        };
                        field.SetValue(obj, realVal);
                    }
                    else
                    {
                        field.SetValue(obj, val);
                    }
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