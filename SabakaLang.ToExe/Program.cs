using SabakaLang.ToExe;

if (args.Length == 0)
{
    Console.WriteLine("Usage: SabakaLang.ToExe <file.sabaka>");
    return;
}

string filePath = args[0];
if (!filePath.EndsWith(".sabaka"))
{
    Console.WriteLine("Error: Input file must have .sabaka extension.");
    return;
}

Compiler.CompileToExe(filePath);