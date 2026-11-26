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
        if (_sessionHealth != null) _sessionHealth.Text = string.Join("\n", ConnectionHealth);
    }

    private void SelectSession(string key)
    {
        if (_sessionList?.ItemsSource is IEnumerable<SessionRow> rows)
            _sessionList.SelectedItem = rows.FirstOrDefault(row => row.Session.Key == key);
    }

    private void ShowSessions()
    {
        if (_sessionsWindow != null) { _sessionsWindow.Activate(); return; }
        _sessionsWindow = Panel("Kiro Agent Pet sessions", 580, 440);
        var panel = new DockPanel { Margin = new Thickness(18) };
        _sessionHealth = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(_sessionHealth, Dock.Top);
        panel.Children.Add(_sessionHealth);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        Button? openChat = null;
        DockPanel.SetDock(buttons, Dock.Bottom);
        foreach (string name in new[] { "Open session", "Mark completion checked", "Refresh" })
        {
            var button = new Button { Content = name, Padding = new Thickness(9, 7) };
            if (name == "Open session") { openChat = button; button.IsEnabled = false; }
            button.Click += async (_, _) =>
            {
                if (name == "Refresh") { RefreshStatus(); RefreshSessions(); return; }
                if (_sessionList?.SelectedItem is not SessionRow row) return;
                if (name == "Open session") await OpenAgentAsync(row.Session);
                else MarkRead(row.Session);
            };
            buttons.Children.Add(button);
        }
        panel.Children.Add(buttons);
        _sessionList = new ListBox();
        _sessionList.SelectionChanged += (_, _) =>
        {
            if (openChat != null) openChat.IsEnabled = _sessionList?.SelectedItem is SessionRow row &&
                (row.Session.Source == "ide" || row.Session.Source == "cli" && OperatingSystem.IsWindows());
        };
        panel.Children.Add(_sessionList);
        _sessionsWindow.Content = panel;
        _sessionsWindow.Closed += (_, _) => { _sessionsWindow = null; _sessionList = null; _sessionHealth = null; };
        RefreshSessions();
        _sessionsWindow.Show(this);
    }
}
