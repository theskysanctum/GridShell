using System.Windows;
using System.Windows.Controls;
using GridShell.Core.Tiles;

namespace GridShell;

/// <summary>Picks the box-art-style template for Game tiles (full-bleed cover with the name
/// overlaid at the bottom) and the regular icon-above-name template for everything else.</summary>
public class GameTileTemplateSelector : DataTemplateSelector
{
    public DataTemplate? RegularTemplate { get; set; }
    public DataTemplate? GameTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container) =>
        item is Tile { Kind: TileKind.Game } ? GameTemplate : RegularTemplate;
}
