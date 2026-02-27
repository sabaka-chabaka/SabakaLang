using System.Text.Json;
using SabakaLang.RuntimeEnvironment.Models;

namespace SabakaLang.SarPacker;

public partial class Form1 : Form
{
    public Form1()
    {
        InitializeComponent();
    }
    
    private void BtnBuildOnClick(object? sender, EventArgs e)
    {
        rtbLog.AppendText("\n[LOG] Starting build...");
        
        DateTime start = DateTime.Now;

        string manifestPath;
        try
        {

            manifestPath = Path.Combine(txtSrc.Text, "manifest.json");
        }
        catch (Exception exception)
        {
            rtbLog.AppendText($"/n[ERROR] {exception.Message}");
            throw;
        }

        if (!File.Exists(manifestPath))
        {
            FailBuild(start);
            throw new Exception("manifest.json not found");
        }
        
        if (!Directory.Exists(txtSrc.Text))
        {
            FailBuild(start);
            throw new DirectoryNotFoundException(txtSrc.Text);
        }
        
        var manifestText = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<SarManifest>(manifestText);
        
        if (manifest == null)
            FailBuild(start);
        
        string entryPath = Path.Combine(txtSrc.Text, manifest.Entry);
        
        if (!File.Exists(entryPath))
        {
            FailBuild(start);
            throw new Exception($"{manifest.Entry} not found");
        }
        
        rtbLog.AppendText("\n[LOG] Validated files");
        
        rtbLog.AppendText("\n[LOG] Compiling...");
        
        DateTime startCompile = DateTime.Now;
        
        var lexer = new Lexer.Lexer(File.ReadAllText(entryPath));
        var parser = new Parser.Parser(lexer.Tokenize(false));
        var compiler = new Compiler.Compiler();
        
        var bytecode = compiler.Compile(parser.ParseProgram(), entryPath);
        
        rtbLog.AppendText($"\n[LOG] Compiled in {DateTime.Now - startCompile}");
        
        rtbLog.AppendText("\n[LOG] Writing SAR");
        
        // using var writer = new ResourceWriter(Path.Combine(txtOut.Text, $"{manifest.Name}.sar"));
        // writer.AddResource("Main", bytecode);
        // writer.Generate();
        // writer.Close();
        
        BinaryWriterWorker.Pack(bytecode,Path.Combine(txtOut.Text, $"{manifest.Name}.sar"));
        
        rtbLog.AppendText("\n[LOG] Successfully writed SAR");
        
        
        rtbLog.AppendText($"\n[LOG] BUILD COMPLETED IN {DateTime.Now - start}");
    }

    private void FailBuild(DateTime start)
    {
        rtbLog.AppendText($"\n[ERROR] BUILD FAILED");
    }
}