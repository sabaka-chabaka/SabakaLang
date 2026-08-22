using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
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
    public void Pack(string srcDir, string destDir, bool toIl)
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

                if (toIl)
                {
                    ctx.Status("Transpiling to C#...");
                    var transpiler = new Transpiler.Transpiler();
                    var binderForIl = new Binder();
                    
                    var processedFiles = new HashSet<string>();
                    var syntaxTrees = new List<SyntaxTree>();
                    var fileAsts = new Dictionary<string, SabakaLang.Compiler.Parsing.ParseResult>();

                    void CollectAst(string filePath)
                    {
                        filePath = Path.GetFullPath(filePath);
                        if (fileAsts.ContainsKey(filePath)) return;

                        var content = File.ReadAllText(filePath);
                        var tokens = new Lexer(content).Tokenize();
                        var ast = new Parser(tokens).Parse();
                        fileAsts[filePath] = ast;

                        foreach (var stmt in ast.Statements)
                        {
                            if (stmt is SabakaLang.Compiler.AST.ImportStmt imp && imp.Path.Trim('\"').EndsWith(".sabaka"))
                            {
                                var relativePath = imp.Path.Trim('\"');
                                var importedPath = Path.Combine(Path.GetDirectoryName(filePath)!, relativePath);
                                CollectAst(importedPath);
                            }
                        }
                    }

                    CollectAst(mainFile);

                    // Collect all ASTs and Bind them
                    foreach (var entry in fileAsts)
                    {
                        Console.WriteLine($"Binding {entry.Key}...");
                        // We must bind to populate the global symbol table
                        binderForIl.Bind(entry.Value.Statements);
                    }

                    var allDecls = new StringBuilder();
                    var mainStmts = new StringBuilder();
                    var allImports = new StringBuilder();

                    // Transpile each file using the collected ASTs
                    foreach (var entry in fileAsts)
                    {
                        var parts = transpiler.TranspileAst(entry.Value, binderForIl);
                        var sb = new StringBuilder();
                        foreach (var importStmt in parts.importStmts)
                        {
                            sb.AppendLine($"using {importStmt.Path};");
                        }
                        allImports.AppendLine(sb.ToString());
                        allDecls.AppendLine(parts.Decls);
                        if (string.Equals(Path.GetFullPath(entry.Key), Path.GetFullPath(mainFile), StringComparison.OrdinalIgnoreCase))
                        {
                            mainStmts.Append(parts.Stmts);
                        }
                    }

                    var finalCsharp = $@"
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
{allImports}

public static class Program 
{{
    {allDecls}

    public static void Main() 
    {{
        {mainStmts}
    }}
}}";
                    syntaxTrees.Add(CSharpSyntaxTree.ParseText(finalCsharp));
                    
                    ctx.Status("Compiling to DLL");
                    
                    var references = new List<MetadataReference>
                    {
                        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                        MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                        MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll")),
                        MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "mscorlib.dll")),
                        MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                        MetadataReference.CreateFromFile(typeof(File).Assembly.Location)
                    };
                    
                    CSharpCompilation compilation = CSharpCompilation.Create(
                        "SabakaLangIL",
                        syntaxTrees: syntaxTrees,
                        references: references,
                        options: new CSharpCompilationOptions(OutputKind.ConsoleApplication)
                    );
                    
                    string outputPath = Path.Combine(destDir, "SabakaLangIL.dll");
                    EmitResult result = compilation.Emit(outputPath);
                    
                    if (result.Success)
                    {
                        // Generate runtimeconfig.json
                        var runtimeConfig = @"{
  ""runtimeOptions"": {
    ""tfm"": ""net10.0"",
    ""framework"": {
      ""name"": ""Microsoft.NETCore.App"",
      ""version"": ""10.0.0""
    }
  }
}";
                        File.WriteAllText(Path.Combine(destDir, "SabakaLangIL.runtimeconfig.json"), runtimeConfig);
                        Console.WriteLine($"Generated C# code:\n{finalCsharp}");
                        Console.WriteLine($"Successfully compiled to: {Path.GetFullPath(outputPath)}");
                    }
                    else
                    {
                        Console.WriteLine($"Generated C# code:\n{finalCsharp}");
                        Console.WriteLine("Compiling errors:");
                        foreach (Diagnostic diagnostic in result.Diagnostics.Where(d => d.IsWarningAsError || d.Severity == DiagnosticSeverity.Error))
                        {
                            Console.WriteLine($"- {diagnostic.GetMessage()}");
                        }
                    }
                }
                else
                {
                    ctx.Status("Compiling to bytecode...");
                    var compiler = new Compiler.Compiling.Compiler();
                    var result = compiler.Compile(parser.Statements, binder);

                    ctx.Status("Writing [green]app.sar[/]...");
                    BinaryWriterWorker.Pack(result.Code.ToList(), Path.Combine(destDir, "app.sar"));
                }
            
                AnsiConsole.MarkupLine("[bold green]Successfully packed![/]");
            });
    }
}

public static class BinaryWriterWorker
{
    public static byte[] Pack(List<Instruction> bytecode)
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8))
        {
            WriteBytecode(bw, bytecode);
        }
        return ms.ToArray();
    }

    public static void Pack(List<Instruction> bytecode, string path)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs, Encoding.UTF8);
        WriteBytecode(bw, bytecode);
    }

    private static void WriteBytecode(BinaryWriter bw, List<Instruction> bytecode)
    {
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

public static class BinaryReaderWorker
{
    public static List<Instruction> Read(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var br = new BinaryReader(ms, Encoding.UTF8);
        return ReadBytecode(br);
    }

    public static List<Instruction> Read(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        using var br = new BinaryReader(fs, Encoding.UTF8);
        return ReadBytecode(br);
    }

    private static List<Instruction> ReadBytecode(BinaryReader br)
    {
        int count = br.ReadInt32();
        var list = new List<Instruction>(count);

        for (int i = 0; i < count; i++)
        {
            var opCode = (OpCode)br.ReadInt32();
            var name = ReadNullableString(br);
            var operand = ReadOperand(br);
            var extra = ReadExtra(br);

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
            _ => throw new InvalidOperationException($"SarPacker: неизвестный тег Operand: 0x{tag:X2}")
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
                var list = new List<string>(count);
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
            SabakaType.Null => Value.Null,
            SabakaType.Int => Value.FromInt(br.ReadInt32()),
            SabakaType.Float => Value.FromFloat(br.ReadDouble()),
            SabakaType.Bool => Value.FromBool(br.ReadBoolean()),
            SabakaType.String => Value.FromString(br.ReadString()),
            SabakaType.Array => ReadArrayValue(br),
            SabakaType.Object => Value.FromObject(ReadSabakaObject(br)),
            SabakaType.Char => Value.FromChar(br.ReadChar()),
            _ => throw new InvalidOperationException($"SarPacker: неизвестный SabakaType: {type}")
        };
    }

    private static Value ReadArrayValue(BinaryReader br)
    {
        int count = br.ReadInt32();
        var arr = new List<Value>(count);
        for (int i = 0; i < count; i++)
            arr.Add(ReadValue(br));
        return Value.FromArray(arr);
    }

    private static SabakaObject ReadSabakaObject(BinaryReader br)
    {
        string className = br.ReadString();
        var obj = new SabakaObject(className);

        int fieldCount = br.ReadInt32();
        for (int i = 0; i < fieldCount; i++)
        {
            string key = br.ReadString();
            obj.Fields[key] = ReadValue(br);
        }

        return obj;
    }
}