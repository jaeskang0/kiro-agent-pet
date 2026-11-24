using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using KiroPet;

namespace KiroPet.Desktop;

public sealed partial class PetWindow : Window
{
    private readonly Canvas _canvas = new();
    private readonly Image _image = new() { Width = 132, Height = 143, Stretch = Stretch.Uniform };
    private readonly Border _bubble;
    private readonly Avalonia.Controls.Shapes.Path _tail = new()
    {
        Data = Geometry.Parse("M 1,0 L 9,8 L 17,0"),
        Stroke = Brush("#40342C"), StrokeThickness = 1,
        Width = 18, Height = 9, IsHitTestVisible = false
    };
    private readonly TextBlock _label = new() { FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Brush("#40342C") };
    private readonly TextBlock _detail = new() { FontSize = 10, Foreground = Brush("#6D6258"), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 140 };
    private readonly NativeReader _reader = new(PetPaths.Native, PetPaths.InData("native-sessions.json"), true);
    private readonly DispatcherTimer _status = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer _animation = new();
    private readonly DispatcherTimer _commands = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Dictionary<string, string> _ack = PetStore.Read("acknowledged.json", new Dictionary<string, string>());
    private PetSettings _settings = PetStore.Settings();
    private PetArtwork _art = null!;
    private NativeSnapshot _snapshot = new();
    private SessionSummary _summary = new();
    private string _state = "idle";
    private int _tick, _clip;
    private bool _hovered, _positionReady;
    private Point? _pressPoint;
    private NativeSession? _pressTarget;
    private bool _dragging, _openingAgent;
    private readonly Queue<string> _preview = new();
    private Window? _settingsWindow, _sessionsWindow;
    private WindowsHotkey? _hotkey;
    private long _sessionVersion = -1;
    private DateTime _nextIdePublish;
    private ListBox? _sessionList;
    private TextBlock? _sessionHealth;

    public PetWindow()
    {
        Title = "Kiroling";
        AutomationProperties.SetName(_image, "Kiroling pet");
        Width = 176; Height = 222;
        CanResize = false; ShowInTaskbar = false; Topmost = true;
        SystemDecorations = SystemDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Content = _canvas;
        Canvas.SetLeft(_image, 22); Canvas.SetTop(_image, 73);
        _canvas.Children.Add(_image);
        var texts = new StackPanel { Spacing = 3 };
        texts.Children.Add(_label); texts.Children.Add(_detail);
        _bubble = new Border
        {
            Child = texts, CornerRadius = new CornerRadius(17),
            BorderBrush = Brush("#40342C"), BorderThickness = new Thickness(1.25),
            Padding = new Thickness(12, 7, 12, 8), MinWidth = 142, MaxWidth = 168
        };
        AutomationProperties.SetName(_bubble, "Agent status");
        Canvas.SetLeft(_bubble, 4);
        _canvas.Children.Add(_bubble);
        Canvas.SetLeft(_tail, 78);
        Canvas.SetTop(_tail, 48);
        _canvas.Children.Add(_tail);
        _canvas.LayoutUpdated += (_, _) =>
        {
            double y = Canvas.GetTop(_bubble) + _bubble.Bounds.Height - 2;
            if (!double.IsNaN(y) && Math.Abs(Canvas.GetTop(_tail) - y) > 0.5) Canvas.SetTop(_tail, y);
            double x = 4 + _bubble.Bounds.Width / 2 - 9;
            if (!double.IsNaN(x) && Math.Abs(Canvas.GetLeft(_tail) - x) > 0.5) Canvas.SetLeft(_tail, x);
        };
        _canvas.PointerEntered += (_, _) => { _hovered = true; UpdateBubble(); };
        _canvas.PointerExited += (_, _) => { _hovered = false; UpdateBubble(); };
        PointerPressed += (_, args) =>
        {
            if (!args.GetCurrentPoint(this).Properties.IsLeftButtonPressed || args.ClickCount > 1) return;
            _pressPoint = args.GetPosition(this);
            _pressTarget = _summary.Target;
            _dragging = false;
            args.Pointer.Capture(this);
            args.Handled = true;
        };
        PointerMoved += (_, args) =>
        {
            if (_pressPoint is not { } pressed || !args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            var delta = args.GetPosition(this) - pressed;
            if (!_dragging && delta.X * delta.X + delta.Y * delta.Y < 36) return;
            if (!_dragging) { _dragging = true; Preview(["wave"]); }
            double scale = Screens.ScreenFromPoint(Position)?.Scaling ?? 1;
            Position = new PixelPoint(Position.X + (int)Math.Round(delta.X * scale), Position.Y + (int)Math.Round(delta.Y * scale));
            args.Handled = true;
        };
        PointerReleased += (_, args) =>
        {
            if (args.InitialPressMouseButton != MouseButton.Left || _pressPoint == null) return;
            bool clicked = !_dragging;
            var target = _pressTarget;
            _pressPoint = null; _pressTarget = null; _dragging = false;
            args.Pointer.Capture(null);
            if (clicked) _ = OpenAgentAsync(target);
            args.Handled = true;
        };
        PointerCaptureLost += (_, _) => { _pressPoint = null; _pressTarget = null; _dragging = false; };
        PositionChanged += (_, _) => { if (_positionReady) SavePosition(); };
        _animation.Tick += (_, _) => DrawFrame();
        _status.Tick += (_, _) => RefreshStatus();
        _commands.Tick += async (_, _) => await ReceiveCommands();
        Closed += (_, _) =>
        {
            _positionReady = false;
            Screens.Changed -= OnScreensChanged;
            _status.Stop(); _animation.Stop(); _commands.Stop();
            _hotkey?.Dispose();
            _reader.Dispose(); _art.Dispose();
            _settingsWindow?.Close(); _sessionsWindow?.Close();
            string running = PetPaths.InData("running.json");
            if (File.Exists(running)) File.Delete(running);
        };
        Opened += (_, _) =>
        {
            RestorePosition();
            _positionReady = true;
            SavePosition();
            Screens.Changed += OnScreensChanged;
            _hotkey = new WindowsHotkey(() => Dispatcher.UIThread.Post(() => { Show(); ShowSessions(); }));
        };
        ChangePet(PetStore.Read("selected-pet.json", new Selection()).Key, false);
        BuildMenu();
        RefreshStatus();
        _animation.Start(); _status.Start(); _commands.Start();
        using var process = Process.GetCurrentProcess();
        PetStore.Write("running.json", new RunningPet
        {
            Pid = Environment.ProcessId, Started = process.StartTime.ToUniversalTime().Ticks.ToString(), Engine = "avalonia"
        });
    }

    private static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));

    private void ChangePet(string name, bool remember = true)
    {
        var matches = PetArtwork.Catalog().Where(pet => pet.Key == name || pet.DisplayName == name || pet.Key.StartsWith(name + "-", StringComparison.OrdinalIgnoreCase)).ToArray();
        var pet = matches.Length == 1 ? matches[0] : !remember ? PetArtwork.Catalog()[0] : throw new InvalidDataException("Pet name is missing or ambiguous. Use kiro-pet pets for exact keys.");
        var graphics = new PetArtwork(pet);
        _art?.Dispose();
        _art = graphics;
        if (_settingsWindow != null) _settingsWindow.Close();
        _tick = _clip = 0; _preview.Clear();
        _animation.Interval = TimeSpan.FromMilliseconds(180 / _settings.Speed);
        Canvas.SetTop(_bubble, Math.Max(8, 73 + _art.Top * 132.0 / 192 - 64));
        DrawFrame();
        if (remember) PetStore.Write("selected-pet.json", new Selection { Key = pet.Key, DisplayName = pet.DisplayName });
    }

    private string[] Playlist()
    {
        string settingKey = _state == "waiting" ? "blocked" : _state;
        if (_settings.Pets.TryGetValue(_art.Pet.Key, out var mapping) && mapping.TryGetValue(settingKey, out var custom))
        {
            string[] available = custom.Where(_art.Clips.ContainsKey).ToArray();
            if (available.Length > 0) return available;
        }
        string[] result = _state switch
        {
            "working" => ["read", "walk-right", "walk-left"],
            "waiting" => ["surprise", "worry"],
            "error" => ["worry", "surprise"],
            "done" => ["jump", "excited"],
            _ => ["idle", "eat", "sleep"]
        };
        if (_art.Pet.SpriteVersionNumber == 2 && _state is "working" or "idle") result = [.. result, "turn-right", "turn-left"];
        return result;
    }

    private void DrawFrame()
    {
        string[] playlist = Playlist();
        string name = _preview.Count > 0 ? _preview.Peek() : playlist[_clip % playlist.Length];
        var clip = _art.Clips[name];
        _image.Source = _art.Frames[clip.Row][clip.Columns[_tick % clip.Columns.Length]];
        if (_settings.ReducedMotion) return;
        _tick++;
        int loops = _preview.Count > 0 ? 1 : Math.Max(1, (int)Math.Ceiling(_settings.Duration * 1000 / (clip.Columns.Length * 180 / _settings.Speed)));
        if (_tick < clip.Columns.Length * loops) return;
        _tick = 0;
        if (_preview.Count > 0) _preview.Dequeue();
        else _clip = (_clip + 1) % playlist.Length;
    }

    private void Preview(IEnumerable<string> names)
    {
        _preview.Clear();
        foreach (string name in names) if (_art.Clips.ContainsKey(name)) _preview.Enqueue(name);
        _tick = 0;
        DrawFrame();
    }

    private void RefreshStatus()
    {
        _snapshot = _reader.Poll();
        if (_snapshot.Version != _sessionVersion || DateTime.UtcNow >= _nextIdePublish)
        {
            IdeBridge.Publish(_snapshot.Sessions);
            _nextIdePublish = DateTime.UtcNow.AddSeconds(1);
        }
        bool checkedResult = false;
        foreach (var viewed in IdeBridge.TakeViewed())
        {
            if (SessionPolicy.MarkViewed(_snapshot.Sessions, _ack, viewed))
                checkedResult = true;
        }
        if (checkedResult) PetStore.Write("acknowledged.json", _ack);
        _summary = SessionPolicy.Summarize(_snapshot.Sessions, _ack);
        if (_state != _summary.Status)
        {
            _state = _summary.Status; _tick = _clip = 0; _preview.Clear(); DrawFrame();
        }
        UpdateBubble();
        ToolTip.SetTip(_image, string.Join("\n", _snapshot.Health));
        if (_snapshot.Version != _sessionVersion || checkedResult)
        {
            _sessionVersion = _snapshot.Version;
            RefreshSessions();
        }
    }

    private void UpdateBubble()
    {
        string text = _state switch { "working" => "I'm working...", "waiting" => "I need you!", "error" => "Something went wrong", "done" => "Done! Take a look", _ => "Taking a little break" };
        int count = _summary.Counts.GetValueOrDefault(_state);
        _label.Text = text + (count > 1 ? $" ({count})" : "");
        var target = _summary.Target;
        _detail.Text = target == null ? "" : $"{target.Title ?? target.SessionId} · {Path.GetFileName(target.Workspace)}";
        int working = _summary.Counts.GetValueOrDefault("working");
        if (_state != "working" && working > 0) _detail.Text += $" · {working} working";
        _detail.IsVisible = !string.IsNullOrEmpty(_detail.Text);
        _bubble.Background = Brush(_state switch { "working" => "#E8F2FF", "waiting" or "error" => "#FFECEC", "done" => "#F0FADB", _ => "#FFF7DB" });
        _tail.Fill = _bubble.Background;
        _bubble.IsVisible = _state != "idle" || _hovered;
        _tail.IsVisible = _bubble.IsVisible;
    }

    private void BuildMenu()
    {
        var change = new MenuItem { Header = "Change pet" };
        void Populate()
        {
            var items = new List<MenuItem>();
            foreach (var pet in PetArtwork.Catalog())
            {
                var choice = new MenuItem { Header = pet.Key == _art.Pet.Key ? "✓ " + pet.DisplayName : pet.DisplayName };
                choice.Click += (_, _) => Guard(() => ChangePet(pet.Key));
                items.Add(choice);
            }
            change.ItemsSource = items;
        }
        Populate();
        var menu = new ContextMenu();
        menu.Opened += (_, _) => Populate();
        var enableIde = new MenuItem { Header = "Enable Kiro IDE chat opening..." };
        enableIde.Click += async (_, _) =>
        {
            try { Message("Kiro IDE navigation", await IdeBridge.Install()); }
            catch (Exception error) { Message("Kiro IDE navigation", error.Message); }
        };
        menu.ItemsSource = new Control[]
        {
            change,
            Item("Import ZIP...", async () => await ImportPicker(false)),
            Item("Import folder...", async () => await ImportPicker(true)),
            enableIde,
            Item("Settings...", ShowSettings),
            Item("Sessions...", ShowSessions),
            Item("Connection health...", () => Message("Connection health", string.Join("\n", _snapshot.Health))),
            Item("Play all animations", () => Preview(_art.Clips.Keys)),
            Item("Quit Kiroling", Close)
        };
        ContextMenu = menu;
    }

    private MenuItem Item(string name, Action action)
    {
        var item = new MenuItem { Header = name };
        item.Click += (_, _) => Guard(action);
        return item;
    }

    private async Task ImportPicker(bool folder)
    {
        try
        {
            string? path = folder
                ? (await StorageProvider.OpenFolderPickerAsync(new() { Title = "Choose pet folder", AllowMultiple = false })).FirstOrDefault()?.Path.LocalPath
                : (await StorageProvider.OpenFilePickerAsync(new() { Title = "Choose Codex pet ZIP", AllowMultiple = false })).FirstOrDefault()?.Path.LocalPath;
            if (path != null) ChangePet(PetArtwork.Import(path).Key);
        }
        catch (Exception error) { Message("Import pet", error.Message); }
    }

    private async Task ReceiveCommands()
    {
        string directory = PetPaths.InData("commands");
        if (!Directory.Exists(directory)) return;
        foreach (string file in Directory.EnumerateFiles(directory, "*.json"))
        {
            bool stop = false;
            PetReply reply;
            try
            {
                var request = JsonSerializer.Deserialize<PetCommand>(File.ReadAllText(file), PetStore.Json) ?? throw new InvalidDataException("Invalid command.");
                switch (request.Command)
                {
                    case "stop": stop = true; break;
                    case "hide": Hide(); break;
                    case "show": Show(); Activate(); break;
                    case "settings": ShowSettings(); break;
                    case "sessions": ShowSessions(); break;
                    case "select": ChangePet(request.Value); break;
                    case "import": ChangePet(PetArtwork.Import(request.Value).Key); break;
                    default: throw new ArgumentException("Unknown pet command.");
                }
                reply = new PetReply(true, "Kiroling: " + request.Command);
            }
            catch (Exception error) { reply = new PetReply(false, "", error.Message); }
            PetStore.WriteFile(Path.Combine(directory, Path.GetFileNameWithoutExtension(file) + ".reply"), reply);
            File.Delete(file);
            if (stop) { Close(); return; }
        }
        await Task.CompletedTask;
    }

    private void Guard(Action action) { try { action(); } catch (Exception error) { Message("Kiroling", error.Message); } }

    private void MarkRead(NativeSession? session)
    {
        if (session == null || !SessionPolicy.MarkRead(_snapshot.Sessions, _ack, session.Key, session.CompletionId)) return;
        PetStore.Write("acknowledged.json", _ack);
        RefreshStatus();
        RefreshSessions();
    }

    private async Task OpenAgentAsync(NativeSession? session)
    {
        if (_openingAgent) return;
        if (session?.Source == "cli")
        {
            ShowSessions();
            SelectSession(session.Key);
            return;
        }
        if (session?.Source == "ide")
        {
            _openingAgent = true;
            try
            {
                var result = await IdeBridge.Open(session);
                if (result.Opened) MarkRead(session);
                else Message("Open Kiro chat", result.Message);
            }
            catch (Exception error) { Message("Open Kiro chat", error.Message); }
            finally { _openingAgent = false; }
            return;
        }
        Guard(() =>
        {
            var start = new ProcessStartInfo { UseShellExecute = true };
            if (OperatingSystem.IsWindows())
                start.FileName = Environment.GetEnvironmentVariable("KIRO_EDITOR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Kiro", "Kiro.exe");
            else if (OperatingSystem.IsMacOS()) { start.FileName = "/usr/bin/open"; start.ArgumentList.Add("-a"); start.ArgumentList.Add("Kiro"); start.ArgumentList.Add("--args"); }
            else start.FileName = "kiro";
            Process.Start(start);
        });
    }

    private void RestorePosition()
    {
        var saved = PetStore.Read<Dictionary<string, double>>("position.json", new());
        var displays = Screens.All.Select(screen => new DisplayArea(
            screen.WorkingArea.X, screen.WorkingArea.Y, screen.WorkingArea.Width, screen.WorkingArea.Height,
            screen.Scaling, screen.IsPrimary)).ToArray();
        var restored = PetPosition.Restore(
            saved.TryGetValue("left", out var x) ? x : null,
            saved.TryGetValue("top", out var y) ? y : null, Width, Height, displays);
        Position = new PixelPoint(restored.X, restored.Y);
    }

    private void OnScreensChanged(object? sender, EventArgs args) =>
        Dispatcher.UIThread.Post(() => { if (_positionReady) RestorePosition(); });

    private void SavePosition()
    {
        double scale = Screens.ScreenFromPoint(Position)?.Scaling ?? 1;
        PetStore.Write("position.json", new { left = Position.X / scale, top = Position.Y / scale });
    }
}
