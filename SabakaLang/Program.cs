using SabakaLang.Exceptions;

namespace SabakaLang;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("SabakaLang v0.1");
        Console.WriteLine("Type 'exit' to quit");
        Console.WriteLine();

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
                var result = SabakaRunner.Run(input);
                Console.WriteLine(result);
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
            var result = SabakaRunner.Run(source);
            Console.WriteLine(result);
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