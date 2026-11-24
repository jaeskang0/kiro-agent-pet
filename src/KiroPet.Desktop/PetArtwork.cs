using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
using KiroPet;

namespace KiroPet.Desktop;

public sealed class PetDefinition
{
    public string Key { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int SpriteVersionNumber { get; set; } = 1;
    public int[][]? FrameColumns { get; set; }
    public int? HighestVisiblePixel { get; set; }
    public string SpritePath { get; set; } = "";
}

public sealed record AnimationClip(int Row, int[] Columns);

public sealed class PetArtwork : IDisposable
{
    public PetDefinition Pet { get; }
    public Bitmap[][] Frames { get; }
    public Dictionary<string, AnimationClip> Clips { get; } = new();
    public int Top { get; }

    public static List<PetDefinition> Catalog()
    {
        var result = new List<PetDefinition> { new() { Key = "spark", DisplayName = "Spark", SpritePath = "avares://KiroPet.Desktop/Assets/spark.png" } };
        string root = PetPaths.InData("pets");
        if (!Directory.Exists(root)) return result;
        foreach (string directory in Directory.EnumerateDirectories(root))
        {
            if (Path.GetFileName(directory).StartsWith('.')) continue;
            string metadata = Path.Combine(directory, "pet.json");
            try
            {
                var pet = JsonSerializer.Deserialize<PetDefinition>(File.ReadAllText(metadata), PetStore.Json);
                if (pet == null || pet.Key != Path.GetFileName(directory) || pet.SpriteVersionNumber is not (1 or 2)) continue;
                pet.SpritePath = Path.Combine(directory, "spritesheet.png");
                if (File.Exists(pet.SpritePath)) result.Add(pet);
            }
            catch (IOException) { }
            catch (JsonException) { }
        }
        return result;
    }

    public PetArtwork(PetDefinition pet)
    {
        Pet = pet;
        byte[] bytes;
        if (pet.SpritePath.StartsWith("avares:"))
        {
            using var asset = AssetLoader.Open(new Uri(pet.SpritePath));
            using var stream = new MemoryStream();
            asset.CopyTo(stream);
            bytes = stream.ToArray();
        }
        else bytes = File.ReadAllBytes(pet.SpritePath);
        using var sheet = Decode(bytes, pet.SpriteVersionNumber);
        if (sheet.ColorType is not (SKColorType.Bgra8888 or SKColorType.Rgba8888))
            throw new InvalidDataException("Sprite decoder returned an unsupported pixel format.");
        var pixels = new byte[sheet.ByteCount];
        Marshal.Copy(sheet.GetPixels(), pixels, 0, pixels.Length);
        int rows = sheet.Height / 208;
        Frames = new Bitmap[rows][];
        var available = new int[rows][];
        int highest = 208;
        for (int row = 0; row < rows; row++)
        {
            Frames[row] = new Bitmap[8];
            var columns = new List<int>();
            for (int column = 0; column < 8; column++)
            {
                int visible = 208;
                for (int y = 0; y < 208; y++)
                    for (int x = 0; x < 192; x++)
                        if (pixels[(row * 208 + y) * sheet.RowBytes + (column * 192 + x) * 4 + 3] > 16) visible = Math.Min(visible, y);
                if (visible < 208) { columns.Add(column); highest = Math.Min(highest, visible); }
                using var subset = new SKBitmap();
                if (!sheet.ExtractSubset(subset, new SKRectI(column * 192, row * 208, (column + 1) * 192, (row + 1) * 208)))
                    throw new InvalidDataException("Sprite frame could not be decoded.");
                using var image = SKImage.FromBitmap(subset);
                using var png = image.Encode(SKEncodedImageFormat.Png, 100);
                using var stream = new MemoryStream(png.ToArray());
                Frames[row][column] = new Bitmap(stream);
            }
            available[row] = columns.ToArray();
        }
        if (available[0].Length == 0) throw new InvalidDataException("The pet has no visible idle frames.");
        Top = highest;
        Add("idle", 0, [0,1,2,3,4,5,4,3,2,1]);
        Add("walk-right", 1, [0,1,2,3,4,5,6,7]);
        Add("walk-left", 2, [0,1,2,3,4,5,6,7]);
        Add("wave", 3, [0,1,2,3]);
        Add("jump", 4, [0,1,2,3,4,3,2,1]);
        Add("surprise", 5, [0,1,2,3,2,1]);
        Add("sleep", 5, [5,4,4,5]);
        Add("worry", 5, [3,6,7,6]);
        Add("eat", 6, [0,1,2,3,4,5]);
        Add("excited", 7, [0,1,2,3,4,5]);
        Add("read", 8, [0,1,2,3,4,5]);
        if (rows == 11) { Add("turn-right", 9, [0,1,2,3,4,5,6,7]); Add("turn-left", 10, [0,1,2,3,4,5,6,7]); }
        for (int row = 0; row < rows; row++)
        {
            var names = Clips.Where(pair => pair.Value.Row == row).Select(pair => pair.Key).ToArray();
            var unused = available[row].Except(names.SelectMany(name => Clips[name].Columns)).ToArray();
            if (unused.Length > 0 && names.Length > 0)
                Clips[names[0]] = Clips[names[0]] with { Columns = Clips[names[0]].Columns.Concat(unused).ToArray() };
        }
        void Add(string name, int row, int[] preferred)
        {
            int[] columns = preferred.Where(column => available[row].Contains(column)).ToArray();
            if (columns.Length == 0) columns = available[row];
            if (columns.Length == 0) { row = 0; columns = available[0]; }
            Clips[name] = new AnimationClip(row, columns);
        }
    }

    private static SKBitmap Decode(byte[] bytes, int version)
    {
        using var stream = new SKMemoryStream(bytes);
        using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("Sprite image is not supported.");
        int height = version == 2 ? 2288 : 1872;
        if (codec.Info.Width != 1536 || codec.Info.Height != height)
            throw new InvalidDataException($"Sprite version {version} requires a 1536 × {height} image.");
        return SKBitmap.Decode(bytes) ?? throw new InvalidDataException("Sprite image could not be decoded.");
    }

    public static PetDefinition Import(string path)
    {
        byte[] manifestBytes, sheetBytes;
        if (File.Exists(path))
        {
            if (new FileInfo(path).Length > 50_000_000) throw new InvalidDataException("Pet ZIP exceeds 50 MB.");
            using var archive = ZipFile.OpenRead(path);
            var manifests = archive.Entries.Where(entry => Path.GetFileName(entry.FullName) == "pet.json" && entry.FullName.Count(c => c == '/') <= 1).ToArray();
            if (manifests.Length != 1) throw new InvalidDataException("Expected one pet.json at the ZIP root or one folder below it.");
            ValidatePath(manifests[0].FullName);
            manifestBytes = Read(manifests[0], 65536);
            using var manifest = JsonDocument.Parse(manifestBytes);
            string relative = manifest.RootElement.GetProperty("spritesheetPath").GetString() ?? "";
            ValidatePath(relative);
            string prefix = manifests[0].FullName[..^"pet.json".Length];
            var sheet = archive.GetEntry(prefix + relative) ?? throw new InvalidDataException("Sprite sheet is missing.");
            sheetBytes = Read(sheet, 20_000_000);
        }
        else
        {
            string directory = Path.GetFullPath(path);
            string metadata = Path.Combine(directory, "pet.json");
            if (!File.Exists(metadata))
            {
                var children = Directory.EnumerateDirectories(directory).Where(child => File.Exists(Path.Combine(child, "pet.json"))).ToArray();
                if (children.Length != 1) throw new InvalidDataException("Choose the folder containing pet.json.");
                directory = children[0];
                metadata = Path.Combine(directory, "pet.json");
            }
            if (new FileInfo(metadata).Length > 65536) throw new InvalidDataException("Pet manifest is too large.");
            manifestBytes = File.ReadAllBytes(metadata);
            using var manifest = JsonDocument.Parse(manifestBytes);
            string relative = manifest.RootElement.GetProperty("spritesheetPath").GetString() ?? "";
            ValidatePath(relative);
            string sheet = Path.Combine(directory, relative);
            if (new FileInfo(sheet).Length > 20_000_000) throw new InvalidDataException("Sprite sheet is too large.");
            sheetBytes = File.ReadAllBytes(sheet);
        }
        using var data = JsonDocument.Parse(manifestBytes);
        string id = data.RootElement.GetProperty("id").GetString() ?? "";
        string name = data.RootElement.GetProperty("displayName").GetString() ?? "";
        int version = data.RootElement.TryGetProperty("spriteVersionNumber", out var number) ? number.GetInt32() : 1;
        if (!Regex.IsMatch(id, "^[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}$") || string.IsNullOrWhiteSpace(name) || name.Length > 64 || version is not (1 or 2))
            throw new InvalidDataException("Pet manifest has an invalid identity, name, or sprite version.");
        using var bitmap = Decode(sheetBytes, version);
        string key = id + "-" + Convert.ToHexString(SHA256.HashData(sheetBytes))[..10].ToLowerInvariant();
        string root = PetPaths.InData("pets");
        Directory.CreateDirectory(root);
        string destination = Path.Combine(root, key);
        if (!Directory.Exists(destination))
        {
            string stage = Path.Combine(root, ".import-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            try
            {
                using var image = SKImage.FromBitmap(bitmap);
                using var png = image.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(Path.Combine(stage, "spritesheet.png"), png.ToArray());
                PetStore.WriteFile(Path.Combine(stage, "pet.json"), new PetDefinition { Key = key, DisplayName = name.Trim(), SpriteVersionNumber = version });
                Directory.Move(stage, destination);
            }
            finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
        }
        return Catalog().Single(pet => pet.Key == key);
    }

    private static byte[] Read(ZipArchiveEntry entry, long limit)
    {
        if (entry.Length > limit) throw new InvalidDataException("Pet archive entry is too large.");
        using var source = entry.Open();
        using var target = new MemoryStream();
        source.CopyTo(target);
        if (target.Length > limit) throw new InvalidDataException("Pet archive entry is too large.");
        return target.ToArray();
    }

    private static void ValidatePath(string path)
    {
        if (path.Length == 0 || path.Contains('\\') || path.Contains(':') || path.Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidDataException("Sprite path must stay inside the pet folder.");
    }

    public void Dispose()
    {
        foreach (var row in Frames) foreach (var frame in row) frame.Dispose();
    }
}
