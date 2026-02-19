using SabakaLang.Exceptions;

namespace SabakaLang;

class Program
{
    static void Main(string[] args)
    {
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
                Console.WriteLine($"[Internal Error] {ex.Message}");
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
            Console.WriteLine($"[Internal Error] {ex.Message}");
        }
    }
}