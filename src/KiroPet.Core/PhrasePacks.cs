#nullable enable
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KiroPet;

public sealed record PersonalityRequest(string PetKey, string Name, string Description, string Kind, string PortraitPath, string JobDirectory);

public sealed class PhrasePack
{
    public int SchemaVersion { get; set; } = 1;
    public string PetKey { get; set; } = "";
    public DateTimeOffset GeneratedAt { get; set; }
    public Dictionary<string, string[]> Phrases { get; set; } = new();
}

public static class PhrasePacks
{
    public static IReadOnlyList<string> Contexts { get; } = Array.AsReadOnly(new[]
    {
        "working", "waiting", "done", "error", "idleClick", "nextChat", "noOpenChats", "connectionIssue"
    });

    public static string JobsRoot => PetPaths.InData("phrase-jobs");

    public static bool IsJobWorkspace(string? workspace)
    {
        if (string.IsNullOrWhiteSpace(workspace)) return false;
        string full = Path.GetFullPath(workspace).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string root = Path.GetFullPath(JobsRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return full.Equals(root, comparison) || full.StartsWith(root + Path.DirectorySeparatorChar, comparison);
    }

    public static string CreateJobDirectory()
    {
        string directory = Path.Combine(JobsRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string FileFor(string petKey)
    {
        if (!Regex.IsMatch(petKey, "^[a-zA-Z0-9][a-zA-Z0-9_-]{0,127}$"))
            throw new InvalidDataException("Invalid pet identity for saved messages.");
        return PetPaths.InData(Path.Combine("personalities", petKey + ".json"));
    }

    public static PhrasePack ParseGenerated(string text, string petKey)
    {
        text = text.Trim();
        if (text.StartsWith("```json", StringComparison.Ordinal) && text.EndsWith("```", StringComparison.Ordinal))
            text = text[7..^3].Trim();
        if (text.Length > 32768) throw new InvalidDataException("Kiro returned too much message data.");
        var pack = JsonSerializer.Deserialize<PhrasePack>(text, PetStore.Json) ??
            throw new InvalidDataException("Kiro returned no message pack.");
        pack.PetKey = petKey;
        pack.GeneratedAt = DateTimeOffset.UtcNow;
        Validate(pack, petKey);
        return pack;
    }

    public static void Validate(PhrasePack pack, string petKey)
    {
        if (pack.SchemaVersion != 1 || pack.PetKey != petKey || pack.Phrases == null)
            throw new InvalidDataException("Unsupported message pack.");
        foreach (string context in Contexts)
        {
            if (!pack.Phrases.TryGetValue(context, out var lines) || lines == null || lines.Length is < 2 or > 8)
                throw new InvalidDataException($"Message pack needs 2–8 phrases for {context}.");
            var cleaned = lines.Select(line => line?.Trim() ?? "").ToArray();
            if (cleaned.Any(line => line.Length is < 1 or > 32 || line.Any(char.IsControl)) ||
                cleaned.Distinct(StringComparer.Ordinal).Count() != cleaned.Length)
                throw new InvalidDataException($"Messages for {context} must be distinct, single-line phrases of at most 32 characters.");
            pack.Phrases[context] = cleaned;
        }
    }

    public static PhrasePack? Load(string petKey)
    {
        string file = FileFor(petKey);
        if (!File.Exists(file)) return null;
        try
        {
            if (new FileInfo(file).Length > 100000) return null;
            var pack = JsonSerializer.Deserialize<PhrasePack>(File.ReadAllText(file), PetStore.Json);
            if (pack == null) return null;
            Validate(pack, petKey);
            return pack;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return null;
        }
    }

    public static void Save(PhrasePack pack)
    {
        Validate(pack, pack.PetKey);
        PetStore.WriteFile(FileFor(pack.PetKey), pack);
    }

    public static IReadOnlyList<string> Defaults(string context) => context switch
    {
        "working" => ["I'm on it!", "Thinking it through...", "Keeping busy!"],
        "waiting" => ["A little help?", "I need you!", "Could you take a look?"],
        "done" => ["All done! Take a peek.", "Ready for your review!", "Finished! Your turn."],
        "error" => ["Oops! Please take a look.", "Something went wrong.", "Could use a little help."],
        "idleClick" => ["Let's check in!", "Back for a peek?", "Hello again!"],
        "nextChat" => ["Who's next?", "Let's check another!", "One more peek!"],
        "noOpenChats" => ["Ready when you are.", "All quiet here.", "I'll be right here."],
        _ => ["I can't see Kiro yet.", "Could you check Kiro?", "The connection needs help."]
    };
}

public sealed class PhraseSpeaker
{
    private readonly Random _random;
    private readonly Dictionary<string, string> _last = new();
    private PhrasePack? _pack;
    private string _petKey = "", _eventKey = "", _line = "";

    public PhraseSpeaker(Random? random = null) => _random = random ?? Random.Shared;

    public void Reload(string petKey, bool generated)
    {
        _petKey = petKey;
        _pack = generated ? PhrasePacks.Load(petKey) : null;
        Reset();
    }

    public void Reset() => _eventKey = "";

    public string Say(string context, string attentionKey)
    {
        string key = context + "\0" + attentionKey;
        if (_eventKey == key) return _line;
        IReadOnlyList<string> options = _pack?.Phrases.GetValueOrDefault(context) ?? PhrasePacks.Defaults(context);
        string historyKey = _petKey + "\0" + context;
        _last.TryGetValue(historyKey, out string? previous);
        var choices = options.Where(line => line != previous).ToArray();
        _line = choices.Length == 0 ? options[0] : choices[_random.Next(choices.Length)];
        _last[historyKey] = _line;
        _eventKey = key;
        return _line;
    }
}
