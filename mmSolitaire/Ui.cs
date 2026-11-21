using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering.Sprites;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace mmSolitaire;

// Absolute-positioned UI builders (the Backgammon table's
// pattern). Images always fill their box: the felt background
// (1x1 into 1000x700) needs Fill, and the card sprites are
// aspect-exact so Fill is harmless for them.
internal static class Ui
{
    public static T At<T>(T element, float x, float y) where T : UIElement
    {
        element.DependencyProperties.Set(Canvas.AbsolutePositionPropertyKey, new Vector3(x, y, 0f));
        return element;
    }

    public static ImageElement Img(Texture texture, float w, float h) =>
        new() { Source = new SpriteFromTexture { Texture = texture }, Width = w, Height = h, StretchType = StretchType.Fill };

    public static ImageElement CardImage() =>
        new() { Width = 72, Height = 100, StretchType = StretchType.Fill };

    public static Border Box(float w, float h, Color border) =>
        new() { Width = w, Height = h, BorderColor = border, BorderThickness = new Thickness(2f, 2f, 2f, 2f) };
}
