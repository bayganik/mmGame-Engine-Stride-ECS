using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering.Sprites;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace mmEngine;

// A scrolling menu background: one image, cover-scaled, ping-
// ponging sideways (no seam: the image does not tile). The scene
// adds Image to its canvas first (behind the panel) and calls
// Update each frame.
public sealed class ScrollingBackground
{
    public ImageElement Image { get; }
    private readonly float _imgW;
    private readonly float _imgH;
    private float _offset;
    private float _dir = 1f;

    public ScrollingBackground(Texture texture)
    {
        _imgW = texture.Width;
        _imgH = texture.Height;
        Image = new ImageElement
        {
            Source = new SpriteFromTexture { Texture = texture },
            StretchType = StretchType.Fill,
        };
    }

    public void Update(float dt, Int2 win)
    {
        float scale = 1.35f * System.Math.Max(win.X / _imgW, win.Y / _imgH);
        scale = System.Math.Max(scale, 1.2f * win.X / _imgW);
        float imgW = _imgW * scale, imgH = _imgH * scale;
        float travel = imgW - win.X;
        _offset += _dir * 14f * dt;
        if (_offset >= travel)
        {
            _offset = travel;
            _dir = -1f;
        }
        else if (_offset <= 0)
        {
            _offset = 0;
            _dir = 1f;
        }
        Image.DependencyProperties.Set(Canvas.AbsolutePositionPropertyKey,
            new Vector3(-_offset, (win.Y - imgH) / 2f, 0f));
        Image.Width = imgW;
        Image.Height = imgH;
    }
}
