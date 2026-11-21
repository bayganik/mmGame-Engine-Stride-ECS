using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering.Sprites;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace mmSamples;

// Absolute-positioned UI builders (the Backgammon table's pattern):
// game elements live in a Canvas at fixed window coordinates.
internal static class Ui
{
    // Top-left of the element at (x, y) in canvas coordinates.
    public static T At<T>(T element, float x, float y) where T : UIElement
    {
        element.DependencyProperties.Set(Canvas.AbsolutePositionPropertyKey, new Vector3(x, y, 0f));
        return element;
    }

    public static ImageElement Img(Texture texture, float w, float h) =>
        new() { Source = new SpriteFromTexture { Texture = texture }, Width = w, Height = h };

    public static Border Box(float w, float h, Color border) =>
        new() { Width = w, Height = h, BorderColor = border, BorderThickness = new Thickness(2f, 2f, 2f, 2f) };
}
