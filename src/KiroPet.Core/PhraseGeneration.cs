#nullable enable
using System.Collections.Concurrent;

namespace KiroPet;

public enum PhraseGenerationStage { None, Queued, Generating, Ready, Failed }
public sealed record PhraseGenerationState(PhraseGenerationStage Stage, string Message);

public sealed class PhraseGeneration : IDisposable
{
    private readonly ConcurrentDictionary<string, PhraseGenerationState> _states = new();
    private readonly ConcurrentDictionary<string, byte> _pending = new();
    private readonly SemaphoreSlim _queue = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly Func<PersonalityRequest, CancellationToken, Task<PhrasePack>> _generate;
    private bool _disposed;
    public event Action<string, PhraseGenerationState>? Changed;

    public PhraseGeneration(Func<PersonalityRequest, CancellationToken, Task<PhrasePack>>? generate = null) =>
        _generate = generate ?? KiroPhraseGenerator.Generate;

    public bool IsPending(string key) => _pending.ContainsKey(key);
    public bool NeedsGeneration(string key) => !IsPending(key) && PhrasePacks.Load(key) == null;

    public PhraseGenerationState State(string key) => _states.GetValueOrDefault(key) ??
        (PhrasePacks.Load(key) != null
            ? new(PhraseGenerationStage.Ready, "Generated messages ready.")
            : new(PhraseGenerationStage.None, "Using built-in messages."));

    public bool Enqueue(PersonalityRequest request, bool force = false)
    {
        if (_disposed || (!force && PhrasePacks.Load(request.PetKey) != null) || !_pending.TryAdd(request.PetKey, 0)) return false;
        var token = _stop.Token;
        Set(request.PetKey, PhraseGenerationStage.Queued, "Messages queued; built-in messages remain available.");
        _ = Task.Run(() => Run(request, token));
        return true;
    }

    private async Task Run(PersonalityRequest request, CancellationToken token)
    {
        bool entered = false;
        try
        {
            await _queue.WaitAsync(token);
            entered = true;
            Set(request.PetKey, PhraseGenerationStage.Generating, "Generating personality with Kiro...");
            var pack = await _generate(request, token);
            token.ThrowIfCancellationRequested();
            PhrasePacks.Save(pack);
            Set(request.PetKey, PhraseGenerationStage.Ready, "Generated messages ready.");
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            Set(request.PetKey, PhraseGenerationStage.Failed, error.Message);
        }
        finally
        {
            _pending.TryRemove(request.PetKey, out _);
            if (entered) _queue.Release();
            if (!_disposed) Changed?.Invoke(request.PetKey, State(request.PetKey));
        }
    }

    private void Set(string key, PhraseGenerationStage stage, string message)
    {
        var state = new PhraseGenerationState(stage, message);
        _states[key] = state;
        if (!_disposed && stage is PhraseGenerationStage.Queued or PhraseGenerationStage.Generating) Changed?.Invoke(key, state);
    }

    public void Dispose()
    {
        _disposed = true;
        _stop.Cancel();
    }
}
