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

        string fileName = Path.GetFileNameWithoutExtension(sabakaFilePath);
        string currentDir = Directory.GetCurrentDirectory();
        string outputDir = Path.Combine(currentDir, fileName + "_build");
        string tempDir = Path.Combine(Path.GetTempPath(), "SabakaCompile_" + Guid.NewGuid().ToString());

        try
        {
            Directory.CreateDirectory(tempDir);
            
            // Собираем все исходники
            string allSource = CollectAllSources(sabakaFilePath);
            
            // Путь к сборке SabakaLang
            string sabakaLangAssemblyPath = typeof(SabakaRunner).Assembly.Location;
            
            // Создаем .csproj файл
            string csprojPath = Path.Combine(tempDir, $"{fileName}.csproj");
            string csprojContent = $@"
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
            
            File.WriteAllText(csprojPath, csprojContent);

            // Создаем Program.cs
            string programPath = Path.Combine(tempDir, "Program.cs");
            string programContent = $@"
using SabakaLang;

class Program
{{
    static void Main()
    {{
        string source = @""{EscapeString(allSource)}"";
        SabakaRunner.Run(source);
    }}
}}";
            
            File.WriteAllText(programPath, programContent);

            Console.WriteLine($"Compiling {sabakaFilePath} to EXE...");

            // Запускаем dotnet publish с указанием пути к проекту
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"publish \"{csprojPath}\" -c Release -o \"{outputDir}\"",
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
                Console.WriteLine($"Success! EXE in: {outputDir}");
                Console.WriteLine($"Executable: {Path.Combine(outputDir, fileName + ".exe")}");
            }
            else
            {
                Console.WriteLine("Compilation failed:");
                Console.WriteLine(output);
                Console.WriteLine(error);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    private static string CollectAllSources(string mainFile)
    {
        var allCode = new List<string>();
        var processed = new HashSet<string>();
        var queue = new Queue<string>();
        string mainDir = Path.GetDirectoryName(Path.GetFullPath(mainFile))!;
        
        queue.Enqueue(Path.GetFullPath(mainFile));
        
        while (queue.Count > 0)
        {
            string file = queue.Dequeue();
            
            if (processed.Contains(file)) continue;
            processed.Add(file);
            
            if (!File.Exists(file))
            {
                Console.WriteLine($"Warning: Import file not found: {file}");
                continue;
            }
            
            string content = File.ReadAllText(file);
            var lines = content.Split('\n');
            var filteredLines = new List<string>();
            
            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("import "))
                {
                    int start = trimmed.IndexOf('"') + 1;
                    int end = trimmed.LastIndexOf('"');
                    if (start > 0 && end > start)
                    {
                        string importFile = trimmed.Substring(start, end - start);
                        string importPath = Path.Combine(mainDir, importFile);
                        if (!importPath.EndsWith(".sabaka")) 
                            importPath += ".sabaka";
                        
                        if (File.Exists(importPath))
                            queue.Enqueue(importPath);
                        else
                            Console.WriteLine($"Warning: Import not found: {importFile}");
                    }
                }
                else
                {
                    filteredLines.Add(line);
                }
            }
            
            allCode.Add(string.Join("\n", filteredLines));
        }
        
        return string.Join("\n\n", allCode);
    }

    private static string EscapeString(string s)
    {
        return s.Replace("\"", "\"\"");
    }
}