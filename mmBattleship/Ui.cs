using Stride.Core.Mathematics;
using Stride.UI;
using Stride.UI.Panels;

namespace mmBattleship;

// Absolute-positioned UI helper (each game has its own Ui.cs;
// mmEngine.Ui only has Wrap/Label/MenuOption/SmallButton).
internal static class Ui
{
    public static T At<T>(T element, float x, float y) where T : UIElement
    {
        element.DependencyProperties.Set(Canvas.AbsolutePositionPropertyKey, new Vector3(x, y, 0f));
        return element;
    }
}
