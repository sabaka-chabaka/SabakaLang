using System.Text.Json;

namespace SabakaLang.CLI.Project;

public static class ProjectManager
{
    public static void NewProject(string projectName)
    {
        Directory.CreateDirectory(projectName);
        
        Console.WriteLine($"Created project directory: {projectName}");
        
        string mainFile = Path.Combine(projectName, "main.sabaka");
        File.Create(mainFile);
        
        Console.Write($"Created project file: {mainFile}");
        
        Project project = new Project();
        
        project.Name = projectName;
        project.Entry = "main.sabaka";
        
        var json = JsonSerializer.Serialize(project);
        File.WriteAllText(mainFile, json);
        
        Console.WriteLine($"Serialized project file: {mainFile}");
    }
    
    public static void Run(string projectName)
    {
        var project = JsonSerializer.Deserialize<Project>(File.ReadAllText(projectName));
        
        Console.WriteLine($"Deserialized project file: {projectName}");

        if (project?.Entry != null)
        {
            SabakaRunner.Run(Path.Combine(projectName, project.Entry));
            Console.WriteLine($"Running project file: {projectName}");
        }
    }
    
    public static void Build(string projectName)
    {
        var project = JsonSerializer.Deserialize<Project>(File.ReadAllText(projectName));
        
        Console.WriteLine($"Deserialized project file: {projectName}");

        if (project?.Entry != null)
        {
            ToExe.Compiler.CompileToExe(Path.Combine(projectName, project.Entry));
            Console.WriteLine($"Compiled project file: {projectName}");
        }
    }
}