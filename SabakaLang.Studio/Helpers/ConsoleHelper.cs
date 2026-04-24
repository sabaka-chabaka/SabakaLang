using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SabakaLang.Studio.Helpers;

public static class ConsoleHelper
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();
    
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    private const int STD_OUTPUT_HANDLE = -11;
    private const int STD_INPUT_HANDLE = -10;

    public static void Show()
    {
        if (AllocConsole())
        {
            var stdOutHandle = GetStdHandle(STD_OUTPUT_HANDLE);
            var safeFileHandleOut = new SafeFileHandle(stdOutHandle, true);
            var writer = new StreamWriter(new FileStream(safeFileHandleOut, FileAccess.Write)) { AutoFlush = true };
            
            Console.SetOut(writer);
            Console.SetError(writer);

            var stdInHandle = GetStdHandle(STD_INPUT_HANDLE);
            var safeFileHandleIn = new SafeFileHandle(stdInHandle, true);
            var reader = new StreamReader(new FileStream(safeFileHandleIn, FileAccess.Read));
            Console.SetIn(reader);
            
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
    }
    public static void Hide() => FreeConsole();
}