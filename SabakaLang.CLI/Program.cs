using SabakaLang.CLI.Project;

namespace SabakaLang.CLI;

public class Program
{
    public static void Main(string[] args)
    {
        switch (args[0])
        {
            case "run":
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: sabaka run [project-name]");
                    return;
                }

                ProjectManager.Run(args[1]);
                break;
            }

            case "new":
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: sabaka run [project-name]");
                    return;
                }

                if (Directory.Exists(args[1]))
                {
                    Console.WriteLine("Directory already exists");
                    return;
                }
                
                ProjectManager.NewProject(args[1]);
                break;
            }

            case "build":
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: sabaka build [project-name]");
                    return;
                }

                if (!Directory.Exists(args[1]))
                {
                    Console.WriteLine("No project directory exists");
                    return;
                }
                
                ProjectManager.Build(args[1]);
                break;
            }
            
            default:
                Console.WriteLine("Usage: sabaka [command]");
                break;
        }
    }
}