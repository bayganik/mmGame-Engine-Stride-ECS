using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Sprites;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace mmEngine;

// The mmEngine UI kit is four components: Button (this file),
// Label (Ui.cs), MessageBox (MessageBox.cs), Panel (Panel.cs).
// Menus are built from them (MenuPanel.cs); the older games'
// Stride Button controls (Ui.MenuOption/SmallButton) are legacy
// and not part of the kit.
//
// Buttons that sit in a menu panel implement this: the panel owns
// their position, the menu owns their hover state.
public interface IMenuButton
{
    Canvas Panel { get; }
    void SetHovered(bool hovered);
}

public static class FlatButtons
{
    // Standard heights on the table plane (table stacking for
    // reference: drag 0.15, highlights 0.2).
    public const float BaseHeight = 0.012f;
    public const float StripHeight = 0.02f;
    public const float FaceHeight = 0.03f;

    // Standard colors, shared by every button so they all match.
    public static readonly Color BevelBase = new(12, 30, 12);
    public static readonly Color BevelHi = new(150, 200, 150);
    public static readonly Color BevelShadow = new(30, 55, 30);
    public static readonly Color FaceIdle = new(46, 110, 46);
    public static readonly Color FaceHover = new(80, 170, 80);
    public static readonly Color FaceDisabled = new(110, 110, 110);
    public static readonly Color FaceSelected = new(120, 205, 120);

    // A face-sized quad model for one state (call once per state;
    // swap the button face's Model to change state).
    public static Model FaceModel(Game game, Vector2 sizePx, float bevelPx, float scale, Material mat)
    {
        float w = (sizePx.X - 2f * bevelPx) / scale;
        float h = (sizePx.Y - 2f * bevelPx) / scale;
        return FlatQuads.QuadXZ(game, w, h, mat);
    }

    // Builds a 3D beveled button on the table plane. centerPx/sizePx
    // are layout pixels (y-down); pxToWorld maps them to world XZ;
    // scale is layout px per world unit (100 in every table game).
    // The base peeks 4px around the button on each side.
    public static FlatButton3D Build3D(Scene scene, Game game, Func<Vector2, Vector2> pxToWorld,
        Vector2 centerPx, Vector2 sizePx, float bevelPx, float scale,
        Material baseMat, Material hiMat, Material shMat, Material faceMat)
    {
        float w = sizePx.X / scale, h = sizePx.Y / scale, bev = bevelPx / scale;
        float over = 4f / scale;
        var entities = new List<Entity>();
        Entity Add(Model model, Vector2 px, float y)
        {
            var world = pxToWorld(px);
            var entity = FlatQuads.AddQuad(scene, model, new Vector3(world.X, y, world.Y));
            entities.Add(entity);
            return entity;
        }
        float cx = centerPx.X, cy = centerPx.Y;
        Add(FlatQuads.QuadXZ(game, w + 2f * over, h + 2f * over, baseMat),
            new Vector2(cx, cy), BaseHeight);
        Add(FlatQuads.QuadXZ(game, w, bev, hiMat),
            new Vector2(cx, cy - h / 2f + bev / 2f), StripHeight);
        Add(FlatQuads.QuadXZ(game, w, bev, shMat),
            new Vector2(cx, cy + h / 2f - bev / 2f), StripHeight);
        Add(FlatQuads.QuadXZ(game, bev, h - 2f * bev, hiMat),
            new Vector2(cx - w / 2f + bev / 2f, cy), StripHeight);
        Add(FlatQuads.QuadXZ(game, bev, h - 2f * bev, shMat),
            new Vector2(cx + w / 2f - bev / 2f, cy), StripHeight);
        var face = Add(FlatQuads.QuadXZ(game, w - 2f * bev, h - 2f * bev, faceMat),
            new Vector2(cx, cy), FaceHeight);
        return new FlatButton3D { Entities = entities, Face = face };
    }
}

// A 3D beveled button: every quad it built (for scene cleanup)
// plus its face entity (swap the face's Model to change state).
public sealed class FlatButton3D
{
    public required IReadOnlyList<Entity> Entities { get; init; }
    public required Entity Face { get; init; }

    public void SetFace(Model model) => Face.Get<ModelComponent>()!.Model = model;
}

// The mmEngine Button: one component, two looks. Beveled (the
// default: dark base, light top/left strips, dark bottom/right
// strips, face brightens on hover) or image (a 9-sliced button
// image with a centered label and a hover tint). The label, when
// given, is a centered child of the panel, so it moves with the
// button wherever the panel sits.
public sealed class Button : IMenuButton
{
    public Canvas Panel { get; }
    private readonly ImageElement? _face; // beveled face; null for image buttons
    private readonly ImageElement? _tint; // image hover tint; null for beveled
    private readonly TextBlock? _label; // centered label, when one was given
    private readonly Color _labelColor; // the label's normal color (gray when disabled)
    private readonly Game _game;
    private readonly Dictionary<Color, Texture> _faceTextures = new();
    private Color _currentFace;
    private bool _hovered;
    private bool _selected;
    private bool _enabled = true;

    // Defaults for image buttons: dark text on the light button
    // image, warm tint on hover.
    public static readonly Color DefaultImageTextColor = new(35, 35, 35);
    public static readonly Color DefaultImageHoverTint = new(255, 240, 170, 100);

    private Button(Game game, Canvas panel, ImageElement? face, ImageElement? tint, TextBlock? label)
    {
        _game = game;
        Panel = panel;
        _face = face;
        _tint = tint;
        _label = label;
        _labelColor = label?.TextColor ?? Color.White;
    }

    // A button's own face colors (idle / hover / selected /
    // disabled). They start as the kit defaults, so every existing
    // caller is unchanged; a caller that needs to read brighter —
    // the piano's buttons on its light background, where the
    // defaults render near-black and the hover is invisible —
    // sets its own. ApplyState (below) uses these, so the sync's
    // SetHovered / SetEnabled keep working as before.
    private Color _faceIdle = FlatButtons.FaceIdle;
    private Color _faceHover = FlatButtons.FaceHover;
    private Color _faceSelected = FlatButtons.FaceSelected;
    private Color _faceDisabled = FlatButtons.FaceDisabled;

    public void SetFaceColors(Color idle, Color hover, Color? disabled = null)
    {
        _faceIdle = idle;
        _faceHover = hover;
        if (disabled.HasValue)
            _faceDisabled = disabled.Value;
        ApplyState();
    }

    // The label in its box: centered (the usual case) or
    // left/right-aligned with an inset from the box edge. The
    // label is exactly the text's height, so it is positioned at
    // the vertical center of the button; AlignedLabel positions
    // itself, CenteredLabel is placed here.
    private static TextBlock MakeLabel(SpriteFont font, string text, float textSize, Color color, float w, float h,
        HorizontalAlignment alignment)
    {
        if (alignment == HorizontalAlignment.Center)
        {
            var label = Ui.CenteredLabel(font, text, textSize, color, w, h);
            label.DependencyProperties.Set(Canvas.AbsolutePositionPropertyKey,
                new Vector3(0f, (h - Ui.TextHeight(textSize)) / 2f, 0f));
            return label;
        }
        return Ui.AlignedLabel(font, text, textSize, color, w, h, alignment);
    }

    // If a font is given, the label is a centered child of the
    // panel. (Callers that keep their own sibling label pass none.)
    public static Button Beveled(Game game, float w, float h, float bevel,
        Color baseColor, Color hiColor, Color shColor, Color faceColor,
        SpriteFont? font = null, string text = "", float textSize = 20f, Color? textColor = null,
        HorizontalAlignment textAlignment = HorizontalAlignment.Center)
    {
        var panel = new Canvas { Width = w, Height = h };
        ImageElement Rect(Color color, float x, float y, float rw, float rh)
        {
            var img = new ImageElement
            {
                Source = new SpriteFromTexture { Texture = Solid(game, color) },
                Width = rw,
                Height = rh,
                // A 1x1 texture in a non-square rect letterboxes
                // under the default Uniform stretch — Fill is what
                // makes the strip actually fill its rect.
                StretchType = StretchType.Fill,
            };
            img.DependencyProperties.Set(Canvas.AbsolutePositionPropertyKey, new Vector3(x, y, 0f));
            return img;
        }
        panel.Children.Add(Rect(baseColor, 0, 0, w, h));
        panel.Children.Add(Rect(hiColor, 0, 0, w, bevel));
        panel.Children.Add(Rect(shColor, 0, h - bevel, w, bevel));
        panel.Children.Add(Rect(hiColor, 0, bevel, bevel, h - 2f * bevel));
        panel.Children.Add(Rect(shColor, w - bevel, bevel, bevel, h - 2f * bevel));
        var face = Rect(faceColor, bevel, bevel, w - 2f * bevel, h - 2f * bevel);
        panel.Children.Add(face);
        TextBlock? label = null;
        if (font != null)
        {
            label = MakeLabel(font, text, textSize, textColor ?? Color.White, w, h, textAlignment);
            panel.Children.Add(label);
        }
        var button = new Button(game, panel, face, null, label);
        button._currentFace = faceColor;
        return button;
    }

    // The face is 9-sliced — the source image's left/right caps
    // keep their width and only the middle stretches — so a small
    // button image scales to any width without fattening its
    // borders. Hovering shows a translucent tint over the image
    // (below the label), which is how the image changes color.
    public static Button Image(Game game, Texture image, float w, float h,
        SpriteFont font, string text, float textSize = 20f,
        HorizontalAlignment textAlignment = HorizontalAlignment.Center,
        Color? textColor = null, Color? hoverTint = null)
    {
        var panel = new Canvas { Width = w, Height = h };
        ImageElement Slice(RectangleF region, float x, float rw)
        {
            var img = new ImageElement
            {
                Source = new RegionSprite(image, region),
                Width = rw,
                Height = h,
                StretchType = StretchType.Fill,
            };
            img.DependencyProperties.Set(Canvas.AbsolutePositionPropertyKey, new Vector3(x, 0f, 0f));
            return img;
        }
        // End caps keep the corner curve of the source image.
        float cap = 12f;
        float iw = image.Width, ih = image.Height;
        panel.Children.Add(Slice(new RectangleF(0, 0, cap, ih), 0, cap));
        panel.Children.Add(Slice(new RectangleF(cap, 0, iw - 2f * cap, ih), cap, w - 2f * cap));
        panel.Children.Add(Slice(new RectangleF(iw - cap, 0, cap, ih), w - cap, cap));
        var tint = new ImageElement
        {
            Source = new SpriteFromTexture
            {
                Texture = Solid(game, hoverTint ?? DefaultImageHoverTint),
            },
            Width = w,
            Height = h,
            StretchType = StretchType.Fill,
            Visibility = Visibility.Collapsed,
        };
        panel.Children.Add(tint);
        var label = MakeLabel(font, text, textSize,
            textColor ?? DefaultImageTextColor, w, h, textAlignment);
        panel.Children.Add(label);
        return new Button(game, panel, null, tint, label);
    }

    private static Texture Solid(Game game, Color color) =>
        Texture.New2D(game.GraphicsDevice, 1, 1, PixelFormat.R8G8B8A8_UNorm, new[] { color });

    // Beveled buttons only; a no-op on image buttons. Face textures
    // are cached per color, so calling this every frame is free
    // once the state settles.
    public void SetFaceColor(Color color)
    {
        if (_face == null || color == _currentFace)
            return;
        if (!_faceTextures.TryGetValue(color, out var tex))
        {
            tex = Solid(_game, color);
            _faceTextures[color] = tex;
        }
        _face.Source = new SpriteFromTexture { Texture = tex };
        _currentFace = color;
    }

    public void SetHovered(bool hovered)
    {
        _hovered = hovered;
        ApplyState();
    }

    // Marks the button enabled or disabled. A disabled button
    // shows the gray face (beveled) or a gray label with no hover
    // tint (image), and stays that way until it is enabled again.
    // Clicks are the scene's business — this is only the look.
    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        ApplyState();
    }

    // Marks the button as the selected one (an options menu's
    // current setting). Hover still wins over selection while the
    // mouse is over the button.
    public void SetSelected(bool selected)
    {
        _selected = selected;
        ApplyState();
    }

    private void ApplyState()
    {
        if (_tint != null)
        {
            _tint.Visibility = _enabled && (_hovered || _selected) ? Visibility.Visible : Visibility.Collapsed;
            if (_label != null)
                _label.TextColor = _enabled ? _labelColor : new Color(128, 128, 128);
        }
        else
            SetFaceColor(!_enabled ? _faceDisabled : _hovered ? _faceHover : _selected ? _faceSelected : _faceIdle);
    }

    // Changes the centered label (for options whose text shows a
    // value, such as "Ghost piece: On"). No-op when the button was
    // built without a label; the button keeps its constructed size.
    public void SetText(string text)
    {
        // Only write when it changed: setting TextBlock.Text
        // invalidates the layout, so an idle button must not
        // re-set the same text every frame.
        if (_label != null && _label.Text != text)
            _label.Text = text;
    }
}

// An ISpriteProvider for one pixel region of a texture (the same
// shape as the card-sheet provider in the Solitaire shell).
internal sealed class RegionSprite : ISpriteProvider
{
    private readonly Sprite _sprite;
    public RegionSprite(Texture texture, RectangleF region) =>
        _sprite = new Sprite(texture) { Region = region };
    public int SpritesCount => 1;
    public Sprite GetSprite() => _sprite;
}
