using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;

namespace KiroPet.Desktop;

internal sealed class PetMenuItem : MenuItem
{
    protected override Type StyleKeyOverride => typeof(MenuItem);
    protected override AutomationPeer OnCreateAutomationPeer() => new MenuPeer(this);

    private sealed class MenuPeer(PetMenuItem item) : MenuItemAutomationPeer(item), IInvokeProvider
    {
        protected override IReadOnlyList<AutomationPeer>? GetChildrenCore()
        {
            var children = base.GetChildrenCore()?.ToList() ?? [];
            if (item.Header is Control header && CreatePeerForElement(header) is { } peer && !children.Contains(peer))
                children.Add(peer);
            return children;
        }

        public void Invoke() => Dispatcher.UIThread.Post(() =>
        {
            if (!item.IsEffectivelyEnabled) return;
            if (item.Items.Count > 0)
            {
                item.IsSelected = true;
                item.IsSubMenuOpen = true;
                return;
            }
            if (item.ToggleType == MenuItemToggleType.CheckBox) item.IsChecked = !item.IsChecked;
            else if (item.ToggleType == MenuItemToggleType.Radio) item.IsChecked = true;
            item.RaiseEvent(new RoutedEventArgs(ClickEvent));
            if (!item.StaysOpenOnClick)
                item.GetLogicalAncestors().OfType<ContextMenu>().FirstOrDefault()?.Close();
        });
    }
}
