using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Graphics;
using Stride.Input;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace mmEngine;

// A menu as one Panel holding its options: a title, a subtitle,
// one Button per option stacked vertically, and an optional footer
// line. The scene positions the panel once (usually centered in
// the real window); every option — button and label alike — is a
// child of the panel, so wherever the panel sits, the menu options
// go with it. The panel also owns hover, click, and key selection:
// the scene just supplies the options and reacts when one is
// chosen.
//
// An option with an Image gets an image button; without one it
// keeps the beveled look. Options menus (a current setting to
// show) use the beveled look and SelectedIndex: the selected
// option's face stays green, and SetOptionText updates a row
// whose text shows a value.
public sealed class MenuPanel
{
    public sealed class Option
    {
        public required string Text { get; init; }
        public required Keys Key { get; init; }
        public Texture? Image { get; init; }
        public required Action OnSelect { get; init; }
    }

    public Canvas Panel => _panel.Canvas;
    public float Width => _panel.Width;
    public float Height => _panel.Height;

    private readonly Panel _panel;
    private readonly List<Button> _buttons = new();
    private readonly List<Stride.UI.Controls.Button> _controls = new();
    private readonly List<Option> _options;
    private int _selectedIndex = -1;

    // The selected option's index (-1 = none). Setting it updates
    // the button faces; hover still wins over selection while the
    // mouse is over a button.
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            _selectedIndex = value;
            for (int i = 0; i < _buttons.Count; i++)
                _buttons[i].SetSelected(i == value);
        }
    }

    public MenuPanel(Game game, SpriteFont font, string title, string subtitle,
        IReadOnlyList<Option> options,
        float buttonHeight = 34f, float textSize = 20f, float titleSize = 44f,
        Color? imageTextColor = null, Color? imageHoverTint = null,
        string? footer = null, float footerSize = 15f, Color? footerColor = null,
        Color? subtitleColor = null)
    {
        _options = new List<Option>(options);
        // One width for all buttons, fitted to the longest label;
        // the panel itself fits whichever is widest: the buttons,
        // the title, the subtitle, or the footer.
        float buttonWidth = 0;
        foreach (var option in _options)
            buttonWidth = System.Math.Max(buttonWidth, 0.55f * textSize * option.Text.Length + 44f);
        float titleW = 0.55f * titleSize * title.Length;
        float subtitleW = 0.55f * 20f * subtitle.Length;
        float footerW = footer == null ? 0 : 0.55f * footerSize * footer.Length;
        float contentW = System.Math.Max(buttonWidth, System.Math.Max(titleW, System.Math.Max(subtitleW, footerW)));
        float pitch = buttonHeight + 4f;
        float padX = 24f, padTop = 14f, padBottom = 16f;
        float titleH = titleSize + 16f, subH = 30f;
        float footerH = footer == null ? 0 : footerSize + 10f;
        float width = contentW + 2f * padX;
        float height = padTop + titleH + 8f + subH + 12f + _options.Count * pitch
            + (footer == null ? 0 : 12f + footerH) + padBottom;

        _panel = new Panel(width, height, Color.White);
        _panel.Add(Ui.CenteredLabel(font, title, titleSize, Color.White, width, titleH),
            0, padTop + (titleH - Ui.TextHeight(titleSize)) / 2f);
        _panel.Add(Ui.CenteredLabel(font, subtitle, 20f, subtitleColor ?? new Color(128, 128, 128), width, subH),
            0, padTop + titleH + 8f + (subH - Ui.TextHeight(20f)) / 2f);
        float y = padTop + titleH + 8f + subH + 12f;
        for (int i = 0; i < _options.Count; i++)
        {
            var option = _options[i];
            Button button = option.Image != null
                ? Button.Image(game, option.Image, buttonWidth, buttonHeight,
                    font, option.Text, textSize,
                    textColor: imageTextColor ?? Button.DefaultImageTextColor,
                    hoverTint: imageHoverTint ?? Button.DefaultImageHoverTint)
                : Button.Beveled(game, buttonWidth, buttonHeight, 6f,
                    FlatButtons.BevelBase, FlatButtons.BevelHi, FlatButtons.BevelShadow,
                    FlatButtons.FaceIdle, font, option.Text, textSize, Color.White);
            // The kit panel is wrapped in a real Stride Button
            // control, which is what the UI system hit-tests —
            // the kit panel's own MouseOverState never fires, and
            // the hand-computed rect hit-test kept landing above
            // the button (the same hover bug Battleship had).
            var control = new Stride.UI.Controls.Button
            {
                Content = button.Panel,
                Width = buttonWidth,
                Height = buttonHeight,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            };
            _panel.Add(control, padX + (contentW - buttonWidth) / 2f, y);
            _buttons.Add(button);
            _controls.Add(control);
            y += pitch;
        }
        if (footer != null)
            _panel.Add(Ui.CenteredLabel(font, footer, footerSize,
                footerColor ?? new Color(128, 128, 128), width, footerH),
                0, y + 12f + (footerH - Ui.TextHeight(footerSize)) / 2f);
    }

    // Changes one option's label (for rows whose text shows a
    // value). The button keeps its constructed size.
    public void SetOptionText(int index, string text) => _buttons[index].SetText(text);

    // Positions the panel centered in the window, updates hover
    // faces, and invokes the chosen option's OnSelect on click or
    // keypress. Returns true when an option was chosen (the scene
    // usually returns right after, since the scene may be gone).
    public bool Update(InputManager input, Int2 win)
    {
        var pos = new Vector2((win.X - Width) / 2f, System.Math.Max(16f, (win.Y - Height) / 2f));
        _panel.Position(pos.X, pos.Y);
        // Each option is a real Stride Button control now; the UI
        // system hit-tests it, so hover and click use its
        // MouseOverState instead of a hand-computed rect.
        int hovered = -1;
        for (int i = 0; i < _options.Count; i++)
        {
            bool hit = _controls[i].MouseOverState != MouseOverState.MouseOverNone;
            if (hit)
                hovered = i;
            _buttons[i].SetHovered(hit);
        }
        if (input.IsMouseButtonPressed(MouseButton.Left) && hovered >= 0)
        {
            _options[hovered].OnSelect();
            return true;
        }
        for (int i = 0; i < _options.Count; i++)
        {
            if (input.IsKeyPressed(_options[i].Key))
            {
                _options[i].OnSelect();
                return true;
            }
        }
        return false;
    }
}
