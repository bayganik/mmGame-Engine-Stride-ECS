using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering.Sprites;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace mmEngine;

// The mmEngine Panel: a bordered container that holds other UI
// elements. The scene positions the panel once; every child is
// placed relative to the panel, so wherever the panel sits, its
// contents go with it. The MenuPanel and the MessageBox are both
// built on this. The panel itself is transparent (the scene's
// background shows through) unless a fill texture is given.
public sealed class Panel
{
    public Canvas Canvas { get; }
    public float Width { get; }
    public float Height { get; }

    public Panel(float w, float h, Color borderColor, float borderThickness = 2f, Texture? fill = null)
    {
        Width = w;
        Height = h;
        Canvas = new Canvas { Width = w, Height = h };
        if (fill != null)
        {
            Canvas.Children.Add(new ImageElement
            {
                Source = new SpriteFromTexture { Texture = fill },
                Width = w,
                Height = h,
                StretchType = StretchType.Fill,
            });
        }
        Canvas.Children.Add(new Border
        {
            Width = w,
            Height = h,
            BorderColor = borderColor,
            BorderThickness = new Thickness(borderThickness, borderThickness, borderThickness, borderThickness),
        });
    }

    // Adds a child at (x, y) relative to the panel's top-left.
    public T Add<T>(T element, float x, float y) where T : UIElement
    {
        element.DependencyProperties.Set(Canvas.AbsolutePositionPropertyKey, new Vector3(x, y, 0f));
        Canvas.Children.Add(element);
        return element;
    }

    // Positions the whole panel in its parent canvas.
    public void Position(float x, float y) =>
        Canvas.DependencyProperties.Set(Canvas.AbsolutePositionPropertyKey, new Vector3(x, y, 0f));
}
