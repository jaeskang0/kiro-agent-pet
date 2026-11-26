namespace KiroPet;

public static class SpriteActions
{
    public static IReadOnlyList<string> Standard { get; } = Array.AsReadOnly(new[]
    {
        "idle", "running-right", "running-left", "waving", "jumping",
        "failed", "waiting", "running", "review"
    });

    public static string Normalize(string action) => action switch
    {
        "walk-right" => "running-right",
        "walk-left" => "running-left",
        "wave" => "waving",
        "jump" => "jumping",
        "surprise" or "sleep" or "worry" => "failed",
        "eat" => "waiting",
        "excited" => "running",
        "read" => "review",
        _ => action
    };
}
