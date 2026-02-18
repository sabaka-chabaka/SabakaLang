using System.Diagnostics;
using SabakaLang;

namespace SabakaLang.ToExe;

public static class Compiler
{
    public static void CompileToExe(string sabakaFilePath)
    {
        if (!File.Exists(sabakaFilePath))
        {
            Console.WriteLine($"Error: File not found: {sabakaFilePath}");
            return;
        }

        string source = File.ReadAllText(sabakaFilePath);
        string fileName = Path.GetFileNameWithoutExtension(sabakaFilePath);
        string currentDir = Directory.GetCurrentDirectory();
        string outputDir = Path.Combine(currentDir, fileName + "_build");
        string tempDir = Path.Combine(Path.GetTempPath(), "SabakaCompile_" + Guid.NewGuid().ToString());

        try
        {
            Directory.CreateDirectory(tempDir);

            string sabakaLangAssemblyPath = typeof(SabakaRunner).Assembly.Location;
            if (string.IsNullOrEmpty(sabakaLangAssemblyPath) || !File.Exists(sabakaLangAssemblyPath))
            {
                Console.WriteLine("Error: Could not find SabakaLang assembly.");
                return;
            }

            // Create .csproj
            string csproj = $@"
<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include=""SabakaLang"">
      <HintPath>{sabakaLangAssemblyPath}</HintPath>
    </Reference>
  </ItemGroup>
</Project>";
            File.WriteAllText(Path.Combine(tempDir, $"{fileName}.csproj"), csproj);

            // Create Program.cs
            string escapedSource = source.Replace("\"", "\"\"");
            string programCs = $@"
using SabakaLang;

class Program
{{
    static void Main()
    {{
        string source = @""{escapedSource}"";
        try {{
            SabakaRunner.Run(source);
        }} catch (Exception ex) {{
            Console.WriteLine($""[Runtime Error] {{ex.Message}}"");
        }}
    }}
}}";
            File.WriteAllText(Path.Combine(tempDir, "Program.cs"), programCs);

            Console.WriteLine($"Compiling {sabakaFilePath} to EXE...");

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"publish -c Release -o \"{outputDir}\"",
                WorkingDirectory = tempDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                Console.WriteLine("Error: Failed to start dotnet process.");
                return;
            }

            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                Console.WriteLine($"Successfully compiled! Output directory: {outputDir}");
                Console.WriteLine($"Executable: {Path.Combine(outputDir, fileName + ".exe")}");
            }
            else
            {
                Console.WriteLine("Compilation failed.");
                Console.WriteLine(output);
                Console.WriteLine(error);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred during compilation: {ex.Message}");
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
            catch { /* Ignore cleanup errors */ }
        }
    }
}