using System.Text;
using SabakaLang.Compiler;
using SabakaLang.Compiler.Compiling;
using SabakaLang.Compiler.Lexing;
using SabakaLang.Compiler.Parsing;
using SabakaLang.Compiler.Runtime;
using Spectre.Console;
using Binder = SabakaLang.Compiler.Binding.Binder;

namespace SabakaLang.Cli;

public class Packer
{
    public void Pack(string srcDir, string destDir)
    {
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .Start("Preparing to pack...", ctx => 
            {
                if (!Directory.Exists(srcDir))
                    throw new DirectoryNotFoundException($"Source dir not found: {srcDir}");
            
                if (!Directory.Exists(destDir))
                    Directory.CreateDirectory(destDir);

                string mainFile = Path.Combine(srcDir, "main.sabaka");

                ctx.Status("Reading and tokenizing...");
                var lexer = new Lexer(File.ReadAllText(mainFile)).Tokenize();

                ctx.Status("Parsing source code...");
                var parser = new Parser(lexer).Parse();

                ctx.Status("Binding nodes...");
                var binder = new Binder().Bind(parser.Statements);

                ctx.Status("Compiling to bytecode...");
                var compiler = new Compiler.Compiling.Compiler();
                var result = compiler.Compile(parser.Statements, binder);

                ctx.Status("Writing [green]app.sar[/]...");
                BinaryWriterWorker.Pack(result.Code.ToList(), Path.Combine(destDir, "app.sar"));
            
                AnsiConsole.MarkupLine("[bold green]Successfully packed![/]");
            });
    }
}

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
            WriteNullableString(bw, instr.Name);
            WriteOperand(bw, instr.Operand);
            WriteExtra(bw, instr.Extra);
        }
    }
 
    private static void WriteNullableString(BinaryWriter bw, string? s)
    {
        if (s is null)
        {
            bw.Write(false);
            return;
        }
        bw.Write(true);
        bw.Write(s);
    }
 
    private static void WriteOperand(BinaryWriter bw, object? operand)
    {
        switch (operand)
        {
            case null:
                bw.Write((byte)0x00);
                break;
 
            case int i:
                bw.Write((byte)0x01);
                bw.Write(i);
                break;
 
            case double d:
                bw.Write((byte)0x02);
                bw.Write(d);
                break;
 
            case bool b:
                bw.Write((byte)0x03);
                bw.Write(b);
                break;
 
            case string s:
                bw.Write((byte)0x04);
                bw.Write(s);
                break;
 
            case Value v:
                bw.Write((byte)0x05);
                WriteValue(bw, v);
                break;

            case char c:
                bw.Write((byte)0x06);
                bw.Write(c);
                break;
 
            default:
                throw new InvalidOperationException(
                    $"SarPacker: неизвестный тип Operand: {operand.GetType().FullName}");
        }
    }
 
    private static void WriteExtra(BinaryWriter bw, object? extra)
    {
        switch (extra)
        {
            case null:
                bw.Write((byte)0x00);
                break;
 
            case string s:
                bw.Write((byte)0x01);
                bw.Write(s);
                break;
 
            case List<string> list:
                bw.Write((byte)0x02);
                bw.Write(list.Count);
                foreach (var item in list)
                    bw.Write(item);
                break;
 
            default:
                throw new InvalidOperationException(
                    $"SarPacker: неизвестный тип Extra: {extra.GetType().FullName}");
        }
    }
 
    private static void WriteValue(BinaryWriter bw, Value v)
    {
        bw.Write((int)v.Type);
 
        switch (v.Type)
        {
            case SabakaType.Null:
                break;
 
            case SabakaType.Int:
                bw.Write(v.Int);
                break;
 
            case SabakaType.Float:
                bw.Write(v.Float);
                break;
 
            case SabakaType.Bool:
                bw.Write(v.Bool);
                break;
 
            case SabakaType.String:
                bw.Write(v.String);
                break;
 
            case SabakaType.Array:
                var arr = v.Array!;
                bw.Write(arr.Count);
                foreach (var item in arr)
                    WriteValue(bw, item);
                break;
 
            case SabakaType.Object:
                WriteSabakaObject(bw, v.Object!);
                break;

            case SabakaType.Char:
                bw.Write(v.Char);
                break;
 
            default:
                throw new InvalidOperationException($"SarPacker: неизвестный SabakaType: {v.Type}");
        }
    }
    
    private static void WriteSabakaObject(BinaryWriter bw, SabakaObject obj)
    {
        bw.Write(obj.ClassName);
        bw.Write(obj.Fields.Count);
        foreach (var kv in obj.Fields)
        {
            bw.Write(kv.Key);
            WriteValue(bw, kv.Value);
        }
    }
}