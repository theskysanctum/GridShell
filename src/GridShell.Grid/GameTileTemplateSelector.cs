using System.Windows;
using System.Windows.Controls;
using GridShell.Core.Tiles;

namespace GridShell;

/// <summary>Picks the box-art-style template for Game tiles and the regular icon-above-name
/// template for other tiles.</summary>
public class GameTileTemplateSelector : DataTemplateSelector
{
    public DataTemplate? RegularTemplate { get; set; }
    public DataTemplate? GameTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
    {
        if (item is not Tile tile)
        {
            return RegularTemplate;
        }

        if (tile.Kind == TileKind.Game)
        {
            return GameTemplate;
        }

        return RegularTemplate;
    }
}
