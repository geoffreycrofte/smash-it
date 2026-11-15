using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace SmashIt.App.Controls;

/// <summary>A Grid showing the horizontal resize cursor (↔), for the before/after split.</summary>
public sealed partial class ResizeCursorGrid : Grid
{
    public ResizeCursorGrid() => ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
}
