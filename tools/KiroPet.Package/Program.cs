using System.IO.Compression;
using System.Security.Cryptography;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: package <publish-root> <zip-path>");
    return 1;
}
string root = Path.GetFullPath(args[0]);
string output = Path.GetFullPath(args[1]);
string project = Directory.GetCurrentDirectory();
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
foreach (string component in new[] { "desktop", "cli" })
{
    string directory = Path.Combine(root, component);
    if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
    foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
    {
        if (file.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)) continue;
        string name = Path.GetRelativePath(directory, file).Replace('\\', '/');
        if (files.TryGetValue(name, out string? previous))
        {
            if (!SHA256.HashData(File.ReadAllBytes(previous)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(file))))
                throw new InvalidDataException("Published components have conflicting files: " + name);
        }
        else files.Add(name, file);
    }
}
foreach (string name in new[] { "README.md", "LICENSE", "THIRD-PARTY.md" }) files.Add(name, Path.Combine(project, name));
if (!files.ContainsKey("kiro-pet.exe") || !files.ContainsKey("KiroPet.Desktop.exe"))
    throw new InvalidDataException("Windows package requires both native executables.");
string temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
try
{
    using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
        foreach (var file in files) archive.CreateEntryFromFile(file.Value, file.Key, CompressionLevel.Optimal);
    File.Move(temporary, output, true);
    string checksum = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output))).ToLowerInvariant();
    File.WriteAllText(output + ".sha256", checksum + "  " + Path.GetFileName(output) + "\n");
    Console.WriteLine($"Package created: {output} ({files.Count} files)");
}
finally { if (File.Exists(temporary)) File.Delete(temporary); }
return 0;
