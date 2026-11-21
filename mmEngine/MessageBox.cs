using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Graphics;
using Stride.Input;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace mmEngine;

// The mmEngine MessageBox: information with an OK button and an
// optional CANCEL button. The scene adds Panel to its canvas and
// calls Update each frame; Update centers the box, handles hover,
// click, and keys (Enter = OK, Esc = CANCEL, or OK when there is
// no CANCEL button), invokes the chosen callback, and returns true
// once the box is dismissed. While the box is up, the scene should
// not process other input: check the box first in Update and
// return while it is showing. The box hides its own panel on
// dismiss; the scene removes it from its canvas when Update
// returns true.
public sealed class MessageBox
{
    public enum Outcome { None, Ok, Cancel }

    public Canvas Panel => _panel.Canvas;
    public Outcome Result { get; private set; } = Outcome.None;

    private readonly Panel _panel;
    private readonly Button _ok;
    private readonly Button? _cancel;
    private readonly Stride.UI.Controls.Button _okControl;
    private readonly Stride.UI.Controls.Button? _cancelControl;
    private readonly Action _onOk;
    private readonly Action? _onCancel;
    private Vector2 _pos;

    private const float ButtonWidth = 120f;
    private const float ButtonHeight = 40f;

    public MessageBox(Game game, SpriteFont font, string title, string message,
        bool cancel, Action onOk, Action? onCancel = null,
        Texture? buttonImage = null, float textSize = 20f, float titleSize = 28f,
        string okLabel = "OK", string cancelLabel = "CANCEL")
    {
        _onOk = onOk;
        _onCancel = onCancel;
        var lines = Wrap(message, 560f, textSize);
        float lineW = 0;
        foreach (var line in lines)
            lineW = System.Math.Max(lineW, 0.55f * textSize * line.Length);
        float padX = 24f, padTop = 16f, padBottom = 16f;
        float titleH = titleSize + 14f, lineH = textSize + 6f;
        float width = System.Math.Max(320f, System.Math.Min(600f, lineW + 2f * padX));
        float height = padTop + titleH + 10f + lines.Count * lineH + 16f + ButtonHeight + padBottom;
        var fill = Texture.New2D(game.GraphicsDevice, 1, 1, PixelFormat.R8G8B8A8_UNorm,
            new[] { new Color(20, 24, 36) });
        _panel = new Panel(width, height, Color.White, 2f, fill);
        _panel.Add(Ui.CenteredLabel(font, title, titleSize, Color.White, width, titleH),
            0, padTop + (titleH - Ui.TextHeight(titleSize)) / 2f);
        float y = padTop + titleH + 10f;
        foreach (var line in lines)
        {
            _panel.Add(LineLabel(font, line, textSize, width - 2f * padX), padX, y);
            y += lineH;
        }
        float buttonsY = y + 16f;
        if (cancel)
        {
            float total = 2f * ButtonWidth + 16f;
            float x = (width - total) / 2f;
            _ok = MakeButton(game, font, okLabel, buttonImage, textSize);
            _okControl = MakeControl(_ok);
            _panel.Add(_okControl, x, buttonsY);
            _cancel = MakeButton(game, font, cancelLabel, buttonImage, textSize);
            _cancelControl = MakeControl(_cancel);
            _panel.Add(_cancelControl, x + ButtonWidth + 16f, buttonsY);
        }
        else
        {
            _ok = MakeButton(game, font, okLabel, buttonImage, textSize);
            _okControl = MakeControl(_ok);
            _panel.Add(_okControl, (width - ButtonWidth) / 2f, buttonsY);
        }
    }

    public bool Update(InputManager input, Int2 win)
    {
        if (Result != Outcome.None)
            return true;
        _pos = new Vector2((win.X - _panel.Width) / 2f, System.Math.Max(16f, (win.Y - _panel.Height) / 2f));
        _panel.Position(_pos.X, _pos.Y);
        // OK/CANCEL are real Stride Button controls now; the UI
        // system hit-tests them, so hover and click use their
        // MouseOverState instead of a hand-computed rect.
        bool okHit = _okControl.MouseOverState != MouseOverState.MouseOverNone;
        _ok.SetHovered(okHit);
        bool cancelHit = _cancelControl != null && _cancelControl.MouseOverState != MouseOverState.MouseOverNone;
        _cancel?.SetHovered(cancelHit);
        if (input.IsMouseButtonPressed(MouseButton.Left))
        {
            if (okHit)
            {
                Dismiss(Outcome.Ok);
                return true;
            }
            if (cancelHit)
            {
                Dismiss(Outcome.Cancel);
                return true;
            }
        }
        if (input.IsKeyPressed(Keys.Enter))
        {
            Dismiss(Outcome.Ok);
            return true;
        }
        if (input.IsKeyPressed(Keys.Escape))
        {
            Dismiss(_cancel != null ? Outcome.Cancel : Outcome.Ok);
            return true;
        }
        return false;
    }

    private void Dismiss(Outcome outcome)
    {
        Result = outcome;
        _panel.Canvas.Visibility = Visibility.Collapsed;
        if (outcome == Outcome.Ok)
            _onOk();
        else
            _onCancel?.Invoke();
    }

    private static Button MakeButton(Game game, SpriteFont font, string text, Texture? image, float textSize) =>
        image != null
            ? Button.Image(game, image, ButtonWidth, ButtonHeight, font, text, textSize)
            : Button.Beveled(game, ButtonWidth, ButtonHeight, 6f,
                FlatButtons.BevelBase, FlatButtons.BevelHi, FlatButtons.BevelShadow,
                FlatButtons.FaceIdle, font, text, textSize, Color.White);

    // Wraps a kit button's panel in a real Stride Button control,
    // which is what the UI system hit-tests. The kit panel is
    // only the look.
    private static Stride.UI.Controls.Button MakeControl(Button button) =>
        new()
        {
            Content = button.Panel,
            Width = ButtonWidth,
            Height = ButtonHeight,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

    private static TextBlock LineLabel(SpriteFont font, string text, float textSize, float w) =>
        new()
        {
            Text = text,
            Font = font,
            TextSize = textSize,
            TextColor = Color.White,
            Width = w,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

    private static List<string> Wrap(string text, float maxWidth, float textSize)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Split('\n'))
        {
            string line = "";
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string next = line.Length == 0 ? word : line + " " + word;
                if (0.55f * textSize * next.Length <= maxWidth || line.Length == 0)
                    line = next;
                else
                {
                    lines.Add(line);
                    line = word;
                }
            }
            lines.Add(line);
        }
        return lines;
    }
}
