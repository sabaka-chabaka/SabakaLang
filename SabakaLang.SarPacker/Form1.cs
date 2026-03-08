using System.IO.Compression;
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
        try   { Build(start); }
        catch (Exception ex)
        {
            rtbLog.AppendText($"\n[ERROR] {ex.Message}");
            rtbLog.AppendText($"\n[ERROR] BUILD FAILED in {DateTime.Now - start}");
        }
    }

    private void Build(DateTime start)
    {
        string srcDir = txtSrc.Text.Trim();
        string outDir = txtOut.Text.Trim();

        if (!Directory.Exists(srcDir))
            throw new DirectoryNotFoundException($"Source dir not found: {srcDir}");
        if (!Directory.Exists(outDir))
            Directory.CreateDirectory(outDir);

        string manifestPath = Path.Combine(srcDir, "manifest.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("manifest.json not found in source dir");

        // manifest.json содержит dlls как List<string> (имена файлов) — читаем через InputManifest
        var inputManifest = JsonSerializer.Deserialize<InputManifest>(File.ReadAllText(manifestPath))
                            ?? throw new InvalidOperationException("Invalid manifest.json");
        // Превращаем в SarManifest без dlls — они будут заполнены компилятором
        var manifest = new SarManifest
        {
            Name      = inputManifest.Name,
            Version   = inputManifest.Version,
            Entry     = inputManifest.Entry,
            SreTarget = inputManifest.SreTarget,
            Dlls      = new List<SarDllEntry>()
        };

        string entryPath = Path.Combine(srcDir, manifest.Entry);
        if (!File.Exists(entryPath))
            throw new FileNotFoundException($"Entry file not found: {manifest.Entry}");

        rtbLog.AppendText("\n[LOG] Validated manifest");

        // ── Компиляция ────────────────────────────────────────────────────────
        rtbLog.AppendText("\n[LOG] Compiling...");
        DateTime startCompile = DateTime.Now;

        var lexer    = new Lexer.Lexer(File.ReadAllText(entryPath));
        var parser   = new Parser.Parser(lexer.Tokenize(false));
        var compiler = new Compiler.Compiler();
        var bytecode = compiler.Compile(parser.ParseProgram(), entryPath);

        rtbLog.AppendText($"\n[LOG] Compiled {bytecode.Count} instructions in {DateTime.Now - startCompile:ss\\.fff}s");

        // ── DLL-зависимости — берём из inputManifest.Dlls (там есть alias) ─────
        // Ищем DLL рядом с exe компилятора (там же где Compiler их грузит)
        string compilerDir = System.IO.Path.GetDirectoryName(
            System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";

        var dllEntries = inputManifest.Dlls.Select(d =>
        {
            // Ищем файл: сначала рядом с exe, потом в srcDir
            string dllPath = System.IO.Path.Combine(compilerDir, d.File);
            if (!File.Exists(dllPath))
                dllPath = System.IO.Path.Combine(srcDir, d.File);
            return (Entry: new SarDllEntry
            {
                Path  = "dlls/" + d.File,
                Alias = d.Alias
            }, SrcPath: dllPath);
        }).ToList();

        if (dllEntries.Count > 0)
        {
            var names = dllEntries.Select(x =>
                x.Entry.Alias != null ? x.Entry.Path + " as " + x.Entry.Alias : x.Entry.Path);
            rtbLog.AppendText("\n[LOG] Found " + dllEntries.Count + " DLL(s): " + string.Join(", ", names));
        }

        // ── Манифест с dlls ───────────────────────────────────────────────────
        var finalManifest = new SarManifest
        {
            Name      = manifest.Name,
            Version   = manifest.Version,
            Entry     = manifest.Entry,
            SreTarget = manifest.SreTarget,
            Dlls      = dllEntries.Select(x => x.Entry).ToList()
        };

        // ── Временные файлы ───────────────────────────────────────────────────
        string outPath   = Path.Combine(outDir, $"{manifest.Name}.sar");
        string writePath = Path.Combine(outDir, $"{manifest.Name}_{Guid.NewGuid():N}.tmp");
        string tmpBytecode = Path.GetTempFileName();

        try
        {
            BinaryWriterWorker.Pack(bytecode, tmpBytecode);
            rtbLog.AppendText($"\n[LOG] Bytecode size: {new FileInfo(tmpBytecode).Length} bytes");

            using (var zip = ZipFile.Open(writePath, ZipArchiveMode.Create))
            {
                // manifest.json
                var me = zip.CreateEntry("manifest.json", CompressionLevel.SmallestSize);
                using (var sw = new StreamWriter(me.Open()))
                    sw.Write(JsonSerializer.Serialize(finalManifest,
                        new JsonSerializerOptions { WriteIndented = true }));

                // bytecode
                zip.CreateEntryFromFile(tmpBytecode, manifest.Entry, CompressionLevel.SmallestSize);

                // DLLs — ReadAllBytes чтобы не конфликтовать с Assembly.LoadFrom
                foreach (var (dllEntry, srcPath) in dllEntries)
                {
                    if (!File.Exists(srcPath))
                    {
                        rtbLog.AppendText("\n[WARN] DLL not found, skipping: " + srcPath);
                        continue;
                    }
                    byte[] bytes = File.ReadAllBytes(srcPath);
                    var ze = zip.CreateEntry(dllEntry.Path, CompressionLevel.SmallestSize);
                    using (var s = ze.Open())
                        s.Write(bytes, 0, bytes.Length);
                    string label = dllEntry.Alias != null
                        ? dllEntry.Path + " (alias=" + dllEntry.Alias + ")"
                        : dllEntry.Path;
                    rtbLog.AppendText("\n[LOG] Packed DLL: " + label);
                }

                // Ресурсы из srcDir (не манифест, не entry, не .sabaka, не outDir)
                string outDirFull = Path.GetFullPath(outDir);
                string srcDirFull = Path.GetFullPath(srcDir);
                foreach (var file in Directory.GetFiles(srcDir, "*", SearchOption.AllDirectories))
                {
                    string fileFull = Path.GetFullPath(file);
                    if (fileFull.StartsWith(outDirFull + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase)) continue;

                    string rel = Path.GetRelativePath(srcDirFull, fileFull).Replace("\\", "/");
                    if (rel.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)) continue;
                    if (rel.Equals(manifest.Entry,  StringComparison.OrdinalIgnoreCase)) continue;
                    if (rel.EndsWith(".sabaka",  StringComparison.OrdinalIgnoreCase)) continue;
                    if (rel.EndsWith(".sabakac", StringComparison.OrdinalIgnoreCase)) continue;

                    byte[] bytes = File.ReadAllBytes(fileFull);
                    var ze = zip.CreateEntry("assets/" + rel, CompressionLevel.SmallestSize);
                    using (var s = ze.Open())
                        s.Write(bytes, 0, bytes.Length);
                    rtbLog.AppendText($"\n[LOG] Packed asset: assets/{rel}");
                }
            } // zip Disposed — файл закрыт до File.Move

            if (File.Exists(outPath)) File.Delete(outPath);
            File.Move(writePath, outPath);

            long sarSize = new FileInfo(outPath).Length;
            rtbLog.AppendText($"\n[LOG] SAR written: {outPath} ({sarSize} bytes)");
        }
        finally
        {
            try { if (File.Exists(tmpBytecode)) File.Delete(tmpBytecode); } catch { }
            try { if (File.Exists(writePath))   File.Delete(writePath);   } catch { }
        }

        rtbLog.AppendText($"\n[LOG] BUILD COMPLETED in {DateTime.Now - start:ss\\.fff}s");
    }
}