using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using KiroPet;

namespace KiroPet.Desktop;

public sealed partial class PetWindow
{
    private ContextMenu _menu = null!;
    private readonly PetMenuItem _petsMenu = new() { Header = "Change pet" };
    private readonly PetMenuItem _agentsMenu = new() { Header = "Agents" };
    private readonly PetMenuItem _actionsMenu = new() { Header = "Actions by status" };
    private readonly PetMenuItem _clickMenu = new() { Header = "Click reaction" };
    private readonly PetMenuItem _messagesMenu = new() { Header = "Messages" };
    private readonly PetMenuItem _previewMenu = new() { Header = "Preview animation" };
    private readonly PetMenuItem _healthMenu = new() { Header = "Connection health" };
    private readonly PetMenuItem _motionMenu = new() { Header = "Reduced motion", ToggleType = MenuItemToggleType.CheckBox };
    private readonly PetMenuItem _startupMenu = new() { Header = "Start at sign-in", ToggleType = MenuItemToggleType.CheckBox };
    private readonly PetMenuItem _speedMenu = new();
    private readonly PetMenuItem _durationMenu = new();
    private Slider _speedSlider = null!, _durationSlider = null!;
    private TextBlock _speedLabel = null!, _durationLabel = null!;
    private bool _refreshingMenu;

    private void BuildMenu()
    {
        _menu = new ContextMenu
        {
            Width = 320, MaxHeight = 650, Padding = new Thickness(6),
            FontSize = 13, Background = Brush("#F9F7F3"), BorderBrush = Brush("#D8C9B7")
        };
        var itemStyle = new Style(selector => selector.OfType<MenuItem>());
        itemStyle.Setters.Add(new Setter(MenuItem.MinHeightProperty, 30.0));
        itemStyle.Setters.Add(new Setter(MenuItem.PaddingProperty, new Thickness(10, 5)));
        _menu.Styles.Add(itemStyle);
        _motionMenu.StaysOpenOnClick = true;
        _motionMenu.Click += (_, _) =>
        {
            _settings.ReducedMotion = _motionMenu.IsChecked;
            ApplySettings();
            DrawFrame();
            _previewMenu.IsEnabled = !_settings.ReducedMotion;
        };
        _startupMenu.StaysOpenOnClick = true;
        _startupMenu.Click += (_, _) => Guard(() =>
        {
            try { Startup.Set(_startupMenu.IsChecked ? "on" : "off"); }
            finally { _startupMenu.IsChecked = Startup.Enabled; }
        });
        _menu.ItemsSource = new Control[]
        {
            _petsMenu, _agentsMenu, new Separator(),
            SliderItem(true), SliderItem(false),
            _actionsMenu, _clickMenu, _previewMenu, _messagesMenu,
            Item("Restore this pet's actions", () =>
            {
                _settings.Pets.Remove(_art.Pet.Key);
                _tick = _clip = 0; _preview.Clear();
                ApplySettings(); DrawFrame(); PopulateActions();
            }, true),
            _motionMenu, new Separator(),
            _startupMenu, Item("Reset position", () => RestorePosition(true)),
            _healthMenu, Item("Quit Kiro Agent Pet", Close)
        };
        _menu.Opened += (_, _) => RefreshMenu();
        ContextMenu = _menu;
        RefreshMenu();
    }

    private PetMenuItem Item(string name, Action action, bool stayOpen = false)
    {
        var item = new PetMenuItem { Header = name, StaysOpenOnClick = stayOpen };
        item.Click += (_, _) => Guard(action);
        return item;
    }

    private void ShowSettings()
    {
        Show();
        _menu.Open(this);
    }

    private void ApplySettings()
    {
        _animation.Interval = TimeSpan.FromMilliseconds(_settings.FrameMilliseconds);
        PetStore.Write("settings.json", _settings);
    }

    private PetMenuItem SliderItem(bool speed)
    {
        var label = new TextBlock { FontWeight = FontWeight.SemiBold, FontSize = 13 };
        var slider = new Slider
        {
            Minimum = speed ? 0 : 5, Maximum = speed ? 1 : 120,
            SmallChange = speed ? 0.01 : 1, LargeChange = speed ? 0.1 : 10,
            Height = 44, MinHeight = 44,
            IsSnapToTickEnabled = !speed, TickFrequency = speed ? 0.01 : 1
        };
        AutomationProperties.SetName(slider, speed ? "Animation speed" : "Action duration");
        slider.PropertyChanged += (_, args) =>
        {
            if (_refreshingMenu || args.Property != Slider.ValueProperty) return;
            if (speed)
            {
                _settings.Speed = Math.Round(PetSettings.MinimumSpeed *
                    Math.Pow(PetSettings.MaximumSpeed / PetSettings.MinimumSpeed, slider.Value), 2);
            }
            else
            {
                _settings.Duration = Math.Round(slider.Value);
            }
            RefreshTimingLabels();
            ApplySettings();
        };
        var panel = new StackPanel { Width = 250, Spacing = 3, Margin = new Thickness(0, 4) };
        panel.Children.Add(label);
        panel.Children.Add(slider);
        if (speed)
        {
            var ends = new Grid();
            ends.Children.Add(new TextBlock
            {
                Text = "Slower", HorizontalAlignment = HorizontalAlignment.Left,
                FontSize = 11, Foreground = Brush("#6D6258")
            });
            ends.Children.Add(new TextBlock
            {
                Text = "Faster", HorizontalAlignment = HorizontalAlignment.Right,
                FontSize = 11, Foreground = Brush("#6D6258")
            });
            panel.Children.Add(ends);
        }
        else panel.Children.Add(new TextBlock
        {
            Text = "Finish each loop before switching actions",
            FontSize = 11, Foreground = Brush("#6D6258")
        });
        if (speed) { _speedSlider = slider; _speedLabel = label; }
        else { _durationSlider = slider; _durationLabel = label; }
        var parent = speed ? _speedMenu : _durationMenu;
        parent.Items.Add(PanelItem(panel));
        parent.PointerEntered += (_, _) =>
        {
            (speed ? _durationMenu : _speedMenu).IsSubMenuOpen = false;
            parent.IsSelected = true;
            parent.IsSubMenuOpen = true;
        };
        return parent;
    }

    private static PetMenuItem PanelItem(Control content) => new()
        {
            Header = content, StaysOpenOnClick = true, Padding = new Thickness(12, 8),
            Template = new FuncControlTemplate<MenuItem>((owner, _) => new Border
            {
                Padding = owner.Padding,
                Background = Brushes.Transparent,
                Child = new ContentPresenter { Content = owner.Header, HorizontalAlignment = HorizontalAlignment.Center }
            })
        };

    private void RefreshTimingLabels()
    {
        _speedLabel.Text = $"Animation speed: {_settings.Speed:0.00}×";
        _durationLabel.Text = $"Repeat each action: {_settings.Duration:0} seconds";
        _speedMenu.Header = _speedLabel.Text;
        _durationMenu.Header = _durationLabel.Text;
    }

    private void RefreshMenu()
    {
        _refreshingMenu = true;
        try
        {
            _speedSlider.Value = Math.Log(_settings.Speed / PetSettings.MinimumSpeed) /
                Math.Log(PetSettings.MaximumSpeed / PetSettings.MinimumSpeed);
            _durationSlider.Value = _settings.Duration;
            RefreshTimingLabels();
            _motionMenu.IsChecked = _settings.ReducedMotion;
            _startupMenu.IsEnabled = OperatingSystem.IsWindows();
            _startupMenu.IsChecked = Startup.Enabled;
            _previewMenu.IsEnabled = !_settings.ReducedMotion;
            _petsMenu.Items.Clear();
            foreach (var pet in PetArtwork.Catalog())
            {
                var choice = Item(pet.DisplayName, () => ChangePet(pet.Key));
                choice.ToggleType = MenuItemToggleType.CheckBox;
                choice.IsChecked = pet.Key == _art.Pet.Key;
                _petsMenu.Items.Add(choice);
            }
            _petsMenu.Items.Add(new Separator());
            var zip = new PetMenuItem { Header = "Import ZIP..." };
            zip.Click += async (_, _) => await ImportPicker(false);
            var folder = new PetMenuItem { Header = "Import folder..." };
            folder.Click += async (_, _) => await ImportPicker(true);
            _petsMenu.Items.Add(zip); _petsMenu.Items.Add(folder);
            PopulateActions();
            PopulateMessages();
            PopulateAgents();
            _healthMenu.Items.Clear();
            foreach (string message in ConnectionHealth)
                _healthMenu.Items.Add(new PetMenuItem
                {
                    Header = new TextBlock { Text = message, MaxWidth = 280, TextWrapping = TextWrapping.Wrap },
                    IsEnabled = false
                });
        }
        finally { _refreshingMenu = false; }
    }

    private static string ActionName(string action) =>
        char.ToUpperInvariant(action[0]) + action[1..].Replace('-', ' ');

    private void PopulateMessages()
    {
        bool wasOpen = _messagesMenu.IsSubMenuOpen;
        _messagesMenu.Items.Clear();
        string key = _art.Pet.Key;
        var state = _phraseGeneration.State(key);
        _messagesMenu.Items.Add(new PetMenuItem
        {
            Header = new TextBlock { Text = state.Message, MaxWidth = 280, TextWrapping = TextWrapping.Wrap },
            IsEnabled = false
        });
        _messagesMenu.Items.Add(new Separator());
        var generated = Item("Use generated messages", () =>
        {
            _settings.GeneratedMessages[key] = true;
            ApplySettings();
            _speaker.Reload(key, true);
            UpdateBubble();
            PopulateMessages();
        }, true);
        generated.ToggleType = MenuItemToggleType.Radio;
        generated.IsChecked = _settings.UseGeneratedMessages(key);
        generated.IsEnabled = PhrasePacks.Load(key) != null;
        _messagesMenu.Items.Add(generated);
        var defaults = Item("Use default messages", () =>
        {
            _settings.GeneratedMessages[key] = false;
            ApplySettings();
            _speaker.Reload(key, false);
            UpdateBubble();
            PopulateMessages();
        }, true);
        defaults.ToggleType = MenuItemToggleType.Radio;
        defaults.IsChecked = !_settings.UseGeneratedMessages(key);
        _messagesMenu.Items.Add(defaults);
        var regenerate = Item("Regenerate messages", () => StartMessages(true), true);
        regenerate.IsEnabled = !_phraseGeneration.IsPending(key);
        _messagesMenu.Items.Add(regenerate);
        _messagesMenu.IsSubMenuOpen = wasOpen;
    }

    private void PopulateActions()
    {
        _actionsMenu.Items.Clear();
        _clickMenu.Items.Clear();
        _previewMenu.Items.Clear();
        var clickChoices = new Dictionary<string, PetMenuItem>();
        string clickReaction = _settings.ClickReaction(_art.Pet.Key, _art.Clips.Keys);
        foreach (string action in _art.Clips.Keys)
        {
            _previewMenu.Items.Add(Item(ActionName(action), () => Preview([action]), true));
            var choice = new PetMenuItem
            {
                Header = ActionName(action), ToggleType = MenuItemToggleType.Radio,
                IsChecked = clickReaction == action, StaysOpenOnClick = true
            };
            choice.Click += (_, _) =>
            {
                if (!_settings.Pets.TryGetValue(_art.Pet.Key, out var mapping))
                    _settings.Pets[_art.Pet.Key] = mapping = new();
                mapping["click"] = [action];
                foreach (var item in clickChoices) item.Value.IsChecked = item.Key == action;
                ApplySettings();
                if (!_settings.ReducedMotion) Preview([action]);
            };
            clickChoices.Add(action, choice);
            _clickMenu.Items.Add(choice);
        }
        foreach (var (status, caption) in new[]
        {
            ("working", "Working"), ("waiting", "Needs input"), ("done", "Done (unread)"),
            ("error", "Blocked / error"), ("idle", "Idle")
        })
        {
            var group = new PetMenuItem { Header = caption };
            var choices = new Dictionary<string, PetMenuItem>();
            string[] selected = Playlist(status);
            foreach (string action in _art.Clips.Keys)
            {
                var choice = new PetMenuItem
                {
                    Header = ActionName(action), ToggleType = MenuItemToggleType.CheckBox,
                    IsChecked = selected.Contains(action), StaysOpenOnClick = true
                };
                choice.Click += (_, _) =>
                {
                    var actions = choices.Where(pair => pair.Value.IsChecked).Select(pair => pair.Key).ToArray();
                    if (actions.Length == 0) { choice.IsChecked = true; return; }
                    if (!_settings.Pets.TryGetValue(_art.Pet.Key, out var mapping))
                        _settings.Pets[_art.Pet.Key] = mapping = new();
                    mapping[status == "waiting" ? "blocked" : status] = actions;
                    ApplySettings();
                    if (choice.IsChecked && !_settings.ReducedMotion) Preview([action]);
                };
                choices.Add(action, choice);
                group.Items.Add(choice);
            }
            _actionsMenu.Items.Add(group);
        }
    }

    private void PopulateAgents()
    {
        _agentsMenu.Items.Clear();
        var history = new PetMenuItem { Header = "History" };
        foreach (var session in _snapshot.Sessions
            .OrderByDescending(session => SessionPolicy.PriorityOf(session, _ack))
            .ThenByDescending(session => session.Timestamp))
        {
            bool checkedResult = _ack.GetValueOrDefault(session.Key) == session.CompletionId &&
                !string.IsNullOrEmpty(session.CompletionId);
            string status = checkedResult ? "Checked" : session.Status switch
            {
                "waiting" => "Needs input", "done" => "Done", "working" => "Working",
                "error" => "Error", "recovering" => "Recovering", _ => "Idle"
            };
            string source = session.Source == "ide" ? "IDE" : "CLI";
            string label = $"{session.Title ?? session.SessionId} · {source} · {status}";
            var row = new PetMenuItem
            {
                Header = new TextBlock { Text = label, MaxWidth = 340, TextTrimming = TextTrimming.CharacterEllipsis }
            };
            AutomationProperties.SetName(row, label);
            ToolTip.SetTip(row, $"{label}\n{session.Workspace}");
            var open = new PetMenuItem { Header = "Open chat" };
            open.Click += async (_, _) => await OpenAgentAsync(session);
            row.Items.Add(open);
            if (session.Status is "done" or "error" && !checkedResult)
            {
                row.Items.Add(Item("Mark result checked", () =>
                {
                    MarkRead(session);
                    row.IsSubMenuOpen = false;
                    PopulateAgents();
                }, true));
            }
            if (checkedResult || (session.Closed || session.Disconnected) && session.Status is not ("done" or "error"))
                history.Items.Add(row);
            else _agentsMenu.Items.Add(row);
        }
        if (_agentsMenu.Items.Count == 0)
            _agentsMenu.Items.Add(new PetMenuItem { Header = "No agents need attention", IsEnabled = false });
        if (history.Items.Count > 0) { _agentsMenu.Items.Add(new Separator()); _agentsMenu.Items.Add(history); }
        _agentsMenu.Items.Add(new Separator());
        _agentsMenu.Items.Add(Item("Open session list...", ShowSessions));
    }
}
