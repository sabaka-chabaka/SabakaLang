using System.Text;
using SabakaLang.Compiler;
using SabakaLang.Compiler.Compiling;
using SabakaLang.Compiler.Runtime;

namespace SabakaLang.Runner;

public static class Reader
{
    public static List<Instruction> Read(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        using var br = new BinaryReader(fs, Encoding.UTF8);
 
        int count = br.ReadInt32();
        var list  = new List<Instruction>(count);
 
        for (int i = 0; i < count; i++)
        {
            var opCode  = (OpCode)br.ReadInt32();
            var name    = ReadNullableString(br);
            var operand = ReadOperand(br);
            var extra   = ReadExtra(br);
 
            list.Add(new Instruction(opCode, operand, name, extra));
        }
 
        return list;
    }
 
    private static string? ReadNullableString(BinaryReader br)
    {
        bool hasValue = br.ReadBoolean();
        return hasValue ? br.ReadString() : null;
    }
 
    private static object? ReadOperand(BinaryReader br)
    {
        byte tag = br.ReadByte();
        return tag switch
        {
            0x00 => null,
            0x01 => (object)br.ReadInt32(),
            0x02 => (object)br.ReadDouble(),
            0x03 => (object)br.ReadBoolean(),
            0x04 => (object)br.ReadString(),
            0x05 => (object)ReadValue(br),
            0x06 => (object)br.ReadChar(),
            _    => throw new InvalidOperationException($"SarPacker: неизвестный тег Operand: 0x{tag:X2}")
        };
    }
 
    private static object? ReadExtra(BinaryReader br)
    {
        byte tag = br.ReadByte();
        switch (tag)
        {
            case 0x00:
                return null;
 
            case 0x01:
                return br.ReadString();
 
            case 0x02:
                int count = br.ReadInt32();
                var list  = new List<string>(count);
                for (int i = 0; i < count; i++)
                    list.Add(br.ReadString());
                return list;
 
            default:
                throw new InvalidOperationException($"SarPacker: неизвестный тег Extra: 0x{tag:X2}");
        }
    }
 
    private static Value ReadValue(BinaryReader br)
    {
        var type = (SabakaType)br.ReadInt32();
        return type switch
        {
            SabakaType.Null   => Value.Null,
            SabakaType.Int    => Value.FromInt(br.ReadInt32()),
            SabakaType.Float  => Value.FromFloat(br.ReadDouble()),
            SabakaType.Bool   => Value.FromBool(br.ReadBoolean()),
            SabakaType.String => Value.FromString(br.ReadString()),
            SabakaType.Array  => ReadArrayValue(br),
            SabakaType.Object => Value.FromObject(ReadSabakaObject(br)),
            SabakaType.Char   => Value.FromChar(br.ReadChar()),
            _                 => throw new InvalidOperationException($"SarPacker: неизвестный SabakaType: {type}")
        };
    }
 
    private static Value ReadArrayValue(BinaryReader br)
    {
        int count = br.ReadInt32();
        var arr   = new List<Value>(count);
        for (int i = 0; i < count; i++)
            arr.Add(ReadValue(br));
        return Value.FromArray(arr);
    }
 
    private static SabakaObject ReadSabakaObject(BinaryReader br)
    {
        string className = br.ReadString();
        var obj          = new SabakaObject(className);
 
        int fieldCount = br.ReadInt32();
        for (int i = 0; i < fieldCount; i++)
        {
            string key = br.ReadString();
            obj.Fields[key] = ReadValue(br);
        }
 
        return obj;
    }
}