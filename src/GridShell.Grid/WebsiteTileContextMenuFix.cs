using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using GridShell.Core.Tiles;

namespace GridShell;

/// <summary>
/// ContextMenu is hosted in a separate popup window and does not reliably inherit the
/// DataContext of its placement target. Website tile menu items use that DataContext for
/// their CommandParameter, so refresh it whenever a website tile's menu is about to open.
/// </summary>
internal static class WebsiteTileContextMenuFix
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(FrameworkElement),
            FrameworkElement.ContextMenuOpeningEvent,
            new ContextMenuEventHandler(OnContextMenuOpening));
    }

    private static void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is FrameworkElement
            {
                DataContext: Tile { Kind: TileKind.Website } tile,
                ContextMenu: { } contextMenu,
            })
        {
            contextMenu.DataContext = tile;
        }
    }
}
