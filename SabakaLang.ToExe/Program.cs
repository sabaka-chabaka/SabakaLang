using SabakaLang.ToExe;

Console.ForegroundColor = ConsoleColor.Yellow;
Console.Error.WriteLine("[WARN] THIS UTILITY IS DEPRECATED. USE SABAKALANG SAR PACKER UTILITY INSTEAD.");
Console.ResetColor();

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