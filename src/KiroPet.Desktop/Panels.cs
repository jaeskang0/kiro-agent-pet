using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Layout;
using Avalonia.Media;
using KiroPet;

namespace KiroPet.Desktop;

public sealed partial class PetWindow
{
    private Window Panel(string title, double width, double height)
    {
        return new Window
        {
            Title = title, Width = width, Height = height, MinWidth = 420, MinHeight = 300,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true,
            Background = Brush("#F9F7F3"), FontSize = 13
        };
    }

    private static TextBlock Text(StackPanel panel, string text, bool heading = false)
    {
        var label = new TextBlock
        {
            Text = text, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 6), FontSize = heading ? 15 : 13,
            FontWeight = heading ? FontWeight.SemiBold : FontWeight.Normal
        };
        panel.Children.Add(label);
        return label;
    }

    private void Message(string title, string message)
    {
        var window = Panel(title, 450, 230);
        var panel = new StackPanel { Margin = new Thickness(20) };
        Text(panel, message);
        var close = new Button { Content = "Close", Margin = new Thickness(0, 15, 0, 0) };
        close.Click += (_, _) => window.Close();
        panel.Children.Add(close);
        window.Content = panel;
        window.Show(this);
    }

    private void ApplySettings()
    {
        _animation.Interval = TimeSpan.FromMilliseconds(180 / _settings.Speed);
        _tick = _clip = 0; _preview.Clear();
        PetStore.Write("settings.json", _settings);
        DrawFrame();
    }

    private void ShowSettings()
    {
        if (_settingsWindow != null) { _settingsWindow.Activate(); return; }
        _settingsWindow = Panel("Kiroling settings", 550, 720);
        var panel = new StackPanel { Margin = new Thickness(22) };
        _settingsWindow.Content = new ScrollViewer { Content = panel };
        Text(panel, $"Animation for {_art.Pet.DisplayName}", true);
        Text(panel, "Changes preview immediately and save automatically.");
        var speedLabel = Text(panel, $"Playback speed: {_settings.Speed:0.00}x");
        var speed = new Slider { Minimum = 0.1, Maximum = 2, Value = _settings.Speed };
        AutomationProperties.SetName(speed, "Playback speed");
        speed.PropertyChanged += (_, args) =>
        {
            if (args.Property != Slider.ValueProperty) return;
            _settings.Speed = Math.Round(speed.Value, 2);
            speedLabel.Text = $"Playback speed: {_settings.Speed:0.00}x";
            ApplySettings();
        };
        panel.Children.Add(speed);
        Text(panel, "0.10x is ten times slower than normal playback.");
        var durationLabel = Text(panel, $"Repeat an action for: {_settings.Duration:0} seconds");
        var duration = new Slider { Minimum = 5, Maximum = 120, Value = _settings.Duration };
        AutomationProperties.SetName(duration, "Action duration");
        duration.PropertyChanged += (_, args) =>
        {
            if (args.Property != Slider.ValueProperty) return;
            _settings.Duration = Math.Round(duration.Value);
            durationLabel.Text = $"Repeat an action for: {_settings.Duration:0} seconds";
            ApplySettings();
        };
        panel.Children.Add(duration);
        Text(panel, "Full animation loops finish before switching actions. New task states take effect immediately.");
        var motion = new CheckBox { Content = "Reduced motion (static character)", IsChecked = _settings.ReducedMotion, Margin = new Thickness(0, 12, 0, 10) };
        motion.Click += (_, _) => { _settings.ReducedMotion = motion.IsChecked == true; ApplySettings(); };
        panel.Children.Add(motion);
        var startup = new CheckBox { Content = "Start Kiroling at sign-in", IsChecked = Startup.Enabled, IsEnabled = OperatingSystem.IsWindows(), Margin = new Thickness(0, 0, 0, 10) };
        startup.Click += (_, _) => Guard(() => Startup.Set(startup.IsChecked == true ? "on" : "off"));
        panel.Children.Add(startup);
        Text(panel, "Actions by status", true);
        var captions = new Dictionary<string, string>
        {
            ["idle"] = "Idle", ["working"] = "Working", ["waiting"] = "Needs input",
            ["error"] = "Blocked / error", ["done"] = "Done (unread)"
        };
        foreach (var status in captions)
        {
            Text(panel, status.Value, true);
            var wrap = new WrapPanel();
            var boxes = new List<CheckBox>();
            string settingKey = status.Key == "waiting" ? "blocked" : status.Key;
            var defaults = status.Key switch
            {
                "working" => new[] { "read", "walk-right", "walk-left" },
                "waiting" => new[] { "surprise", "worry" },
                "error" => new[] { "worry", "surprise" },
                "done" => new[] { "jump", "excited" },
                _ => new[] { "idle", "eat", "sleep" }
            };
            string[] selected = _settings.Pets.TryGetValue(_art.Pet.Key, out var mapping) && mapping.TryGetValue(settingKey, out var saved) ? saved : defaults;
            foreach (string action in _art.Clips.Keys)
            {
                var box = new CheckBox { Content = action, IsChecked = selected.Contains(action), Margin = new Thickness(0, 3, 14, 7) };
                box.Click += (_, _) =>
                {
                    var actions = boxes.Where(item => item.IsChecked == true).Select(item => (string)item.Content!).ToArray();
                    if (actions.Length == 0) { box.IsChecked = true; return; }
                    if (!_settings.Pets.ContainsKey(_art.Pet.Key)) _settings.Pets[_art.Pet.Key] = new();
                    _settings.Pets[_art.Pet.Key][settingKey] = actions;
                    ApplySettings();
                    Preview([action]);
                };
                boxes.Add(box);
                wrap.Children.Add(box);
            }
            panel.Children.Add(wrap);
        }
        var reset = new Button { Content = "Restore defaults for this character", Padding = new Thickness(12, 8), Margin = new Thickness(0, 16, 0, 0) };
        reset.Click += (_, _) =>
        {
            _settings.Pets.Remove(_art.Pet.Key);
            _settings.Speed = 0.6; _settings.Duration = 14;
            ApplySettings();
            _settingsWindow?.Close();
            ShowSettings();
        };
        panel.Children.Add(reset);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show(this);
    }

    private sealed record SessionRow(NativeSession Session, string Text)
    {
        public override string ToString() => Text;
    }

    private void RefreshSessions()
    {
        if (_sessionList == null) return;
        string? selected = (_sessionList.SelectedItem as SessionRow)?.Session.Key;
        var rows = _snapshot.Sessions.OrderByDescending(session => SessionPolicy.PriorityOf(session, _ack))
            .ThenBy(session => session.Timestamp).Select(session =>
        {
            string state = _ack.TryGetValue(session.Key, out var result) && result == session.CompletionId ? "Checked" :
                session.Status switch { "waiting" => "Needs input", "recovering" => "Recovering status", _ => session.Status };
            if (session.Disconnected) state += " (closed)";
            return new SessionRow(session, $"{session.Title ?? session.SessionId}\n{Path.GetFileName(session.Workspace)} · {session.Source.ToUpperInvariant()} · {state}");
        }).ToArray();
        _sessionList.ItemsSource = rows;
        _sessionList.SelectedItem = rows.FirstOrDefault(row => row.Session.Key == selected);
        if (_sessionHealth != null) _sessionHealth.Text = string.Join("\n", _snapshot.Health);
    }

    private void ShowSessions()
    {
        if (_sessionsWindow != null) { _sessionsWindow.Activate(); return; }
        _sessionsWindow = Panel("Kiroling sessions", 580, 440);
        var panel = new DockPanel { Margin = new Thickness(18) };
        _sessionHealth = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(_sessionHealth, Dock.Top);
        panel.Children.Add(_sessionHealth);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        foreach (string name in new[] { "Open workspace", "Mark completion checked", "Refresh" })
        {
            var button = new Button { Content = name, Padding = new Thickness(9, 7) };
            button.Click += (_, _) =>
            {
                if (name == "Refresh") { RefreshStatus(); RefreshSessions(); return; }
                if (_sessionList?.SelectedItem is not SessionRow row) return;
                if (name == "Open workspace") OpenWorkspace(row.Session);
                else if (row.Session.Status is "done" or "error")
                {
                    _ack[row.Session.Key] = row.Session.CompletionId;
                    PetStore.Write("acknowledged.json", _ack);
                    RefreshStatus(); RefreshSessions();
                }
            };
            buttons.Children.Add(button);
        }
        panel.Children.Add(buttons);
        _sessionList = new ListBox();
        panel.Children.Add(_sessionList);
        _sessionsWindow.Content = panel;
        _sessionsWindow.Closed += (_, _) => { _sessionsWindow = null; _sessionList = null; _sessionHealth = null; };
        RefreshSessions();
        _sessionsWindow.Show(this);
    }
}
