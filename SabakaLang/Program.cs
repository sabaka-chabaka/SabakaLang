using System.IO;
﻿using SabakaLang.Exceptions;

namespace SabakaLang;

class Program
{
    static void Main(string[] args)
    {
        // Add WindowsDesktop Runtime to PATH so native WPF dlls
        // (wpfgfx_cor3.dll, PresentationNative_cor3.dll etc.) are found
        // when a script imports SabakaUI.dll or similar WPF-based libraries.
        AddDesktopRuntimeToPath();
        if (args.Length > 0)
        {
            RunFile(args[0]);
            return;
        }

        RunRepl();
    }

    private static void RunRepl()
    {
        while (true)
        {
            Console.Write(">> ");
            var input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
                continue;

            if (input.Trim().ToLower() == "exit")
                break;

            try
            {
                SabakaRunner.Run(input);
            }
            catch (SabakaLangException ex)
            {
                Console.WriteLine($"[SabakaLang Error] {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Internal Error] {ex}");
            }
        }
    }

    private static void RunFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                Console.WriteLine($"File not found: {path}");
                return;
            }

            var source = File.ReadAllText(path);
            SabakaRunner.Run(source, path);
        }
        catch (SabakaLangException ex)
        {
            Console.WriteLine($"[SabakaLang Error] {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Internal Error] {ex}");
        }
    }

    // P/Invoke — must be called before any WPF native dll is needed
    [System.Runtime.InteropServices.DllImport("kernel32.dll",
        CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool AddDllDirectory(string lpPathName);

    [System.Runtime.InteropServices.DllImport("kernel32.dll",
        CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDefaultDllDirectories(uint directoryFlags);

    private const uint LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000;
    private const uint LOAD_LIBRARY_SEARCH_USER_DIRS    = 0x00000400;

    private static void AddDesktopRuntimeToPath()
    {
        string fxBase = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "dotnet", "shared", "Microsoft.WindowsDesktop.App");

        if (!Directory.Exists(fxBase)) return;

        string? fxDir = Directory.GetDirectories(fxBase)
            .OrderByDescending(d => d)
            .FirstOrDefault();

        if (fxDir == null) return;

        // Tell Windows loader to also search this directory for native dlls
        SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_DEFAULT_DIRS | LOAD_LIBRARY_SEARCH_USER_DIRS);
        AddDllDirectory(fxDir);
    }
}