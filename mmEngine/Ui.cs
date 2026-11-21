using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace mmEngine;

// Small builders for the Stride UI used by menus and the HUD.
// The mmEngine UI kit is four components: Button (FlatButtons.cs),
// Label (this file), MessageBox (MessageBox.cs), Panel (Panel.cs).
// Ui.MenuOption/SmallButton below are the older Stride Button
// controls, kept for the older games; they are not part of the kit.
public static class Ui
{
    public static Entity Wrap(UIElement root, string name = "UI") =>
        new(name) { new UIComponent { Page = new UIPage { RootElement = root } } };

    public static TextBlock Label(SpriteFont font, string text, Color color, float textSize = 24,
        HorizontalAlignment alignment = HorizontalAlignment.Center) =>
        new()
        {
            Text = text,
            Font = font,
            TextSize = textSize,
            TextColor = color,
            HorizontalAlignment = alignment,
            Margin = new Thickness(0, 6, 0, 6),
        };

    // The default font's line box: a line of text at a given size
    // renders about 1.2x that tall (measured: a 20px line is ~24px).
    // Labels center themselves vertically by this, because a
    // Stride TextBlock draws its text from the top of its box.
    public static float TextHeight(float textSize) => textSize * 1.2f;

    // A label centered in a w x h box. The text is centered
    // horizontally by TextAlignment; vertically the box is exactly
    // the text's height, so the caller centers it in its h-tall
    // box by placing it at y + (h - TextHeight(textSize)) / 2.
    // (HorizontalAlignment/VerticalAlignment alone do NOT center
    // text in a Canvas — a Canvas gives a child exactly its own
    // size at its AbsolutePosition, so the alignment has nothing
    // to align within. That is why the old version, which relied
    // on them, rendered its text at the box's top-left.)
    public static TextBlock CenteredLabel(SpriteFont font, string text, float textSize, Color color, float w, float h) =>
        new()
        {
            Text = text,
            Font = font,
            TextSize = textSize,
            TextColor = color,
            Width = w,
            Height = TextHeight(textSize),
            TextAlignment = TextAlignment.Center,
        };

    // A label in a w x h box, left-, center- or right-aligned,
    // with an inset from the box edge when it is not centered.
    // CenteredLabel (above) is the centered case of this; it stays
    // for its existing callers. The box is positioned for a Canvas
    // parent, centered vertically in its h-tall box by the text
    // height (see TextHeight). The text is aligned horizontally
    // by TextAlignment.
    public static TextBlock AlignedLabel(SpriteFont font, string text, float textSize, Color color, float w, float h,
        HorizontalAlignment alignment, float inset = 10f)
    {
        float boxW = alignment == HorizontalAlignment.Center ? w : w - inset;
        float x = alignment == HorizontalAlignment.Left ? inset : 0f;
        var label = new TextBlock
        {
            Text = text,
            Font = font,
            TextSize = textSize,
            TextColor = color,
            Width = boxW,
            Height = TextHeight(textSize),
            TextAlignment = alignment switch
            {
                HorizontalAlignment.Left => TextAlignment.Left,
                HorizontalAlignment.Right => TextAlignment.Right,
                _ => TextAlignment.Center,
            },
        };
        label.DependencyProperties.Set(Canvas.AbsolutePositionPropertyKey,
            new Vector3(x, (h - TextHeight(textSize)) / 2f, 0f));
        return label;
    }

    // A greeting-menu row: a text label in a bare Stride Button
    // (no background/border of its own), turning red on hover.
    // textAlignment left-justifies the label inside the button
    // (mmSamples uses Left so the "[key]  label" rows all start
    // at the same left edge); it defaults to Center, as before.
    public static Stride.UI.Controls.Button MenuOption(SpriteFont font, string text, Action onClick, float textSize = 32,
        HorizontalAlignment textAlignment = HorizontalAlignment.Center)
    {
        var label = new TextBlock
        {
            Text = text,
            Font = font,
            TextSize = textSize,
            TextColor = Color.White,
            HorizontalAlignment = textAlignment,
        };
        var b = new Stride.UI.Controls.Button
        {
            Content = label,
            Padding = new Thickness(40, 10, 40, 10),
            Margin = new Thickness(0, 6, 0, 6),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        // the option turns red while the mouse hovers over it
        b.MouseOverStateChanged += (sender, args) =>
        {
            label.TextColor = b.MouseOverState == MouseOverState.MouseOverNone
                ? Color.White
                : new Color(255, 60, 60);
        };
        b.Click += (s, e) => onClick();
        return b;
    }

    public static Stride.UI.Controls.Button SmallButton(SpriteFont font, string text, Action onClick)
    {
        var label = new TextBlock
        {
            Text = text,
            Font = font,
            TextSize = 28,
            TextColor = Color.White,
        };
        var b = new Stride.UI.Controls.Button
        {
            Content = label,
            Padding = new Thickness(16, 6, 16, 6),
            Margin = new Thickness(12, 0, 12, 0),
        };
        b.MouseOverStateChanged += (sender, args) =>
        {
            label.TextColor = b.MouseOverState == MouseOverState.MouseOverNone
                ? Color.White
                : new Color(255, 60, 60);
        };
        b.Click += (s, e) => onClick();
        return b;
    }
}
