using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Graphics;
using Stride.Input;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace mmEngine;

// A general options grid (Excel-like) that fits inside a Panel:
// one row per option, four cells per row — the option's name,
// its current value, the operation that changes it, and an
// additional operation. The programmer supplies the rows (any
// number) and the grid lays them out, draws the cell borders,
// and owns the clicks. It is built on the kit (Panel + Button +
// Label), like the MenuPanel.
//
// Each operation button is a kit Button (the beveled look) whose
// panel is the Content of a real Stride Button, so Stride's own
// hit-testing decides hover and click. The grid used to hit-test
// by hand (normalized mouse x window size, panel position +
// stored rect) and the hit zones came out displaced from the
// visuals by a column and more: hovering cell 4 lit the cell 3
// buttons, + never lit, Back lit way below it. Stride's Button
// hit-tests against the element's arranged bounds, in the UI's
// own coordinate space, so it cannot drift from what is drawn.
// (The same hand-computed pattern is what MenuPanel uses; it
// has not shown the symptom, so it stays as it is.)
//
// The first row is always a header: "Option" in cell 1 and
// "Value" in cell 2, on a lighter strip baked into the panel's
// background fill, so the columns read at a glance. An optional
// footer button (Back, Done, ...) sits centered under the rows,
// inside the same panel.
//
// The four cells, and what each row kind puts in them:
//   Cell 1 — the option's name, always.
//   Cell 2 — the value: info text (Info), a text field (Text),
//     or the current value (Bool, Number).
//   Cell 3 — the operation: Set (Text), the button that flips
//     the value to its other state (Bool), or - decrease
//     (Number). Info rows have no operation.
//   Cell 4 — the additional operation: + increase (Number
//     only); empty for the other kinds.
// The grid re-reads every value from the row each frame, so a
// value changed elsewhere still shows correctly — but it only
// writes a label's text when the text actually changed, so an
// idle grid does no layout work at all.
public sealed class OptionsGrid
{
    public enum Kind { Info, Text, Bool, Number }

    public sealed class Row
    {
        public required string Label { get; init; }
        public required Kind Kind { get; init; }

        // Cell 2, re-read every frame (Info, Bool, Number). Text
        // rows own their text in the edit box instead.
        public Func<string>? Value { get; init; }

        // Bool rows: the two state labels (On/Off, Yes/No,
        // True/False — the caller picks).
        public Func<bool>? GetBool { get; init; }
        public Action<bool>? SetBool { get; init; }
        public string TrueText { get; init; } = "On";
        public string FalseText { get; init; } = "Off";

        // Number rows: clamped to [Min, Max], moved by Step.
        public Func<int>? GetNumber { get; init; }
        public Action<int>? SetNumber { get; init; }
        public int Min { get; init; }
        public int Max { get; init; }
        public int Step { get; init; } = 1;

        // Text rows: the Set button calls SetText with the edit
        // box's text; the caller trims, defaults, and saves.
        public Func<string>? GetText { get; init; }
        public Action<string>? SetText { get; init; }
        public int MaxLength { get; init; } = 24;

        public static Row Info(string label, Func<string> value) =>
            new() { Label = label, Kind = Kind.Info, Value = value };

        public static Row Text(string label, Func<string> get, Action<string> set, int maxLength = 24) =>
            new() { Label = label, Kind = Kind.Text, GetText = get, SetText = set, MaxLength = maxLength };

        public static Row Bool(string label, Func<bool> get, Action<bool> set,
            string trueText = "On", string falseText = "Off") =>
            new()
            {
                Label = label, Kind = Kind.Bool, GetBool = get, SetBool = set,
                TrueText = trueText, FalseText = falseText,
                Value = () => get() ? trueText : falseText,
            };

        public static Row Number(string label, Func<int> get, Action<int> set, int min, int max, int step = 1) =>
            new()
            {
                Label = label, Kind = Kind.Number, GetNumber = get, SetNumber = set,
                Min = min, Max = max, Step = step, Value = () => get().ToString(),
            };
    }

    public Canvas Panel => _panel.Canvas;
    public float Width => _panel.Width;
    public float Height => _panel.Height;

    // The panel's top-left in window coordinates, set by Update
    // (the panel is centered every frame, like a MenuPanel).
    public Vector2 Position { get; private set; }

    private static readonly Color FillColor = new(20, 24, 36);
    private static readonly Color HeaderColor = new(40, 48, 72);
    private static readonly Color GridLineColor = new(128, 128, 128);

    // The bevel width of the grid's buttons.
    private const float Bevel = 6f;

    private readonly Panel _panel;
    private readonly List<Row> _rows;
    private readonly List<TextBlock?> _valueLabels = new();
    private readonly List<Button?> _opButtons = new();
    private readonly List<Button?> _op2Buttons = new();
    // The real Stride Buttons wrapping each kit button's panel:
    // their MouseOverState is the hover, their Click is the action.
    private readonly List<Stride.UI.Controls.Button?> _opSysButtons = new();
    private readonly List<Stride.UI.Controls.Button?> _op2SysButtons = new();
    private readonly List<Func<string>?> _opLabels = new();
    private readonly Button? _footerButton;
    private readonly Stride.UI.Controls.Button? _footerSysButton;
    private readonly Vector4 _footerRect;
    // Set by a wrapper button's Click handler; Update reports it
    // so the scene can return right after the action ran.
    private bool _actionInvoked;

    public OptionsGrid(Game game, SpriteFont font, IReadOnlyList<Row> rows,
        string? title = null,
        float labelWidth = 280f, float valueWidth = 170f, float opWidth = 100f,
        float rowHeight = 46f, float textSize = 20f, float titleSize = 44f,
        string? footerText = null, Action? footerAction = null)
    {
        _rows = new List<Row>(rows);
        float padX = 16f, padTop = 14f, padBottom = 14f;
        float titleH = title == null ? 0f : titleSize + 16f;
        float buttonH = 34f;
        float footerH = footerText == null ? 0f : 40f;
        float footerGap = footerText == null ? 0f : 16f;
        float width = padX * 2f + labelWidth + valueWidth + opWidth * 2f;
        float height = padTop + titleH + (title == null ? 0f : 8f)
            + rowHeight + _rows.Count * rowHeight
            + footerGap + footerH + padBottom;
        float labelX = padX;
        float valueX = padX + labelWidth;
        float opX = valueX + valueWidth;
        float op2X = opX + opWidth;

        // The header strip is baked into the panel's background
        // fill: a 1px-wide, panel-height texture whose rows under
        // the header are the lighter HeaderColor and the rest are
        // FillColor. A separate ImageElement for the strip sat on
        // top of the header labels in v22 (the "Option"/"Value"
        // text did not show), so the strip now lives in the
        // background itself and the header labels are plain
        // labels on it, exactly like the data rows.
        float headerY = padTop + titleH + (title == null ? 0f : 8f);
        int headerTop = (int)headerY;
        int headerBottom = (int)(headerY + rowHeight);
        int texH = (int)System.Math.Ceiling(height);
        var fillPixels = new Color[texH];
        for (int py = 0; py < texH; py++)
            fillPixels[py] = py >= headerTop && py < headerBottom ? HeaderColor : FillColor;
        var fill = Texture.New2D(game.GraphicsDevice, 1, texH, PixelFormat.R8G8B8A8_UNorm, fillPixels);
        _panel = new Panel(width, height, Color.White, 2f, fill);
        if (title != null)
            _panel.Add(Ui.CenteredLabel(font, title, titleSize, Color.White, width, titleH),
                0, padTop + (titleH - Ui.TextHeight(titleSize)) / 2f);

        float y = headerY;
        // The header row, always first: "Option" and "Value" on
        // the lighter strip, with the same cell borders as the rows.
        CellBorders(labelX, valueX, opX, op2X, labelWidth, valueWidth, opWidth, rowHeight, y);
        _panel.Add(CellLabel(font, "Option", labelWidth - 12f, rowHeight, textSize), labelX + 12f, y + (rowHeight - Ui.TextHeight(textSize)) / 2f);
        _panel.Add(CellLabel(font, "Value", valueWidth - 12f, rowHeight, textSize), valueX + 12f, y + (rowHeight - Ui.TextHeight(textSize)) / 2f);
        y += rowHeight;

        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            // The Excel cell borders: one outline per cell.
            CellBorders(labelX, valueX, opX, op2X, labelWidth, valueWidth, opWidth, rowHeight, y);
            // Cell 1: the option's name.
            _panel.Add(CellLabel(font, row.Label, labelWidth - 12f, rowHeight, textSize),
                labelX + 12f, y + (rowHeight - Ui.TextHeight(textSize)) / 2f);

            TextBlock? valueLabel = null;
            EditText? edit = null;
            Button? op = null;
            Button? op2 = null;
            Stride.UI.Controls.Button? opSys = null;
            Stride.UI.Controls.Button? op2Sys = null;
            Func<string>? opLabel = null;
            Action? opAction = null;
            Action? op2Action = null;
            var opRect = new Vector4(opX + 6f, y + (rowHeight - buttonH) / 2f, opWidth - 12f, buttonH);
            var op2Rect = new Vector4(op2X + 6f, y + (rowHeight - buttonH) / 2f, opWidth - 12f, buttonH);
            switch (row.Kind)
            {
                case Kind.Info:
                    valueLabel = CellLabel(font, row.Value!(), valueWidth - 12f, rowHeight, textSize);
                    _panel.Add(valueLabel, valueX + 12f, y + (rowHeight - Ui.TextHeight(textSize)) / 2f);
                    break;
                case Kind.Text:
                    edit = new EditText
                    {
                        Text = row.GetText!(),
                        Font = font,
                        TextSize = textSize,
                        TextColor = Color.White,
                        MaxLength = row.MaxLength,
                        Width = valueWidth - 12f,
                        Height = buttonH,
                    };
                    _panel.Add(edit, valueX + 6f, y + (rowHeight - buttonH) / 2f);
                    var editBox = edit;
                    (op, opSys) = MakeOpButton(game, font, "Set", opRect, textSize);
                    opAction = () =>
                    {
                        row.SetText!(editBox.Text);
                        editBox.Text = row.GetText!();
                    };
                    break;
                case Kind.Bool:
                    valueLabel = CellLabel(font, row.Value!(), valueWidth - 12f, rowHeight, textSize);
                    _panel.Add(valueLabel, valueX + 12f, y + (rowHeight - Ui.TextHeight(textSize)) / 2f);
                    // The button shows the value a click will set.
                    opLabel = () => row.GetBool!() ? row.FalseText : row.TrueText;
                    (op, opSys) = MakeOpButton(game, font, opLabel(), opRect, textSize);
                    opAction = () => row.SetBool!(!row.GetBool!());
                    break;
                case Kind.Number:
                    valueLabel = CellLabel(font, row.Value!(), valueWidth - 12f, rowHeight, textSize);
                    _panel.Add(valueLabel, valueX + 12f, y + (rowHeight - Ui.TextHeight(textSize)) / 2f);
                    (op, opSys) = MakeOpButton(game, font, "-", opRect, textSize);
                    opAction = () => row.SetNumber!(System.Math.Max(row.Min, row.GetNumber!() - row.Step));
                    (op2, op2Sys) = MakeOpButton(game, font, "+", op2Rect, textSize);
                    op2Action = () => row.SetNumber!(System.Math.Min(row.Max, row.GetNumber!() + row.Step));
                    break;
            }
            _valueLabels.Add(valueLabel);
            _opButtons.Add(op);
            _op2Buttons.Add(op2);
            _opSysButtons.Add(opSys);
            _op2SysButtons.Add(op2Sys);
            _opLabels.Add(opLabel);
            // The wrapper button's Click runs the row's action; the
            // action is only known here, after the switch.
            if (opSys != null)
            {
                var action = opAction;
                opSys.Click += (s, e) => { action?.Invoke(); _actionInvoked = true; };
            }
            if (op2Sys != null)
            {
                var action = op2Action;
                op2Sys.Click += (s, e) => { action?.Invoke(); _actionInvoked = true; };
            }
            y += rowHeight;
        }

        if (footerText != null)
        {
            float footerW = 160f;
            _footerRect = new Vector4((width - footerW) / 2f, y + footerGap, footerW, footerH);
            _footerButton = Button.Beveled(game, footerW, footerH, Bevel,
                FlatButtons.BevelBase, FlatButtons.BevelHi, FlatButtons.BevelShadow,
                FlatButtons.FaceIdle, font, footerText, 22f, Color.White);
            _footerSysButton = new Stride.UI.Controls.Button
            {
                Content = _footerButton.Panel,
                Padding = new Thickness(0, 0, 0, 0),
                Width = footerW,
                Height = footerH,
            };
            _footerSysButton.Click += (s, e) => { footerAction?.Invoke(); _actionInvoked = true; };
            _panel.Add(_footerSysButton, _footerRect.X, _footerRect.Y);
        }
    }

    private void CellBorders(float labelX, float valueX, float opX, float op2X,
        float labelWidth, float valueWidth, float opWidth, float rowHeight, float y)
    {
        _panel.Add(new Border { Width = labelWidth, Height = rowHeight, BorderColor = GridLineColor, BorderThickness = new Thickness(1, 1, 1, 1) }, labelX, y);
        _panel.Add(new Border { Width = valueWidth, Height = rowHeight, BorderColor = GridLineColor, BorderThickness = new Thickness(1, 1, 1, 1) }, valueX, y);
        _panel.Add(new Border { Width = opWidth, Height = rowHeight, BorderColor = GridLineColor, BorderThickness = new Thickness(1, 1, 1, 1) }, opX, y);
        _panel.Add(new Border { Width = opWidth, Height = rowHeight, BorderColor = GridLineColor, BorderThickness = new Thickness(1, 1, 1, 1) }, op2X, y);
    }

    // A left-aligned label in a cell, exactly the text's height
    // (the caller centers it vertically in the row).
    private static TextBlock CellLabel(SpriteFont font, string text, float w, float h, float textSize) =>
        new()
        {
            Text = text,
            Font = font,
            TextSize = textSize,
            TextColor = Color.White,
            Width = w,
            Height = Ui.TextHeight(textSize),
            HorizontalAlignment = HorizontalAlignment.Left,
            TextAlignment = TextAlignment.Left,
        };

    // Creates the beveled button the grid shows and wraps its panel
    // in a real Stride Button, so Stride's own hit-testing decides
    // hover and click (see the class comment for why the hand
    // computed hit test had to go). Returns the kit button (the
    // look) and the Stride wrapper (the hit test); the caller
    // wires the wrapper's Click to the row's action.
    private (Button Visual, Stride.UI.Controls.Button Sys) MakeOpButton(Game game, SpriteFont font, string text, Vector4 rect, float textSize)
    {
        var button = Button.Beveled(game, rect.Z, rect.W, Bevel,
            FlatButtons.BevelBase, FlatButtons.BevelHi, FlatButtons.BevelShadow,
            FlatButtons.FaceIdle, font, text, textSize, Color.White);
        var sys = new Stride.UI.Controls.Button
        {
            Content = button.Panel,
            Padding = new Thickness(0, 0, 0, 0),
            Width = rect.Z,
            Height = rect.W,
        };
        _panel.Add(sys, rect.X, rect.Y);
        return (button, sys);
    }

    // Positions the panel centered in the window, refreshes the
    // value and button labels (only when they changed), and drives
    // each button's hover from its wrapper Stride Button's
    // MouseOverState. The wrapper's Click runs the action; Update
    // returns true when a click ran one (the scene usually returns
    // right after, since the scene may be gone). The text edit
    // boxes handle their own input. No hand-computed mouse hit
    // test lives here any more (see the class comment).
    public bool Update(InputManager input, Int2 win)
    {
        Position = new Vector2((win.X - Width) / 2f, System.Math.Max(16f, (win.Y - Height) / 2f));
        _panel.Position(Position.X, Position.Y);
        for (int i = 0; i < _rows.Count; i++)
        {
            if (_valueLabels[i] != null)
            {
                string value = _rows[i].Value!();
                if (_valueLabels[i]!.Text != value)
                    _valueLabels[i]!.Text = value;
            }
            if (_opLabels[i] != null && _opButtons[i] != null)
                _opButtons[i]!.SetText(_opLabels[i]!());
            if (_opButtons[i] != null && _opSysButtons[i] != null)
                _opButtons[i]!.SetHovered(_opSysButtons[i]!.MouseOverState != MouseOverState.MouseOverNone);
            if (_op2Buttons[i] != null && _op2SysButtons[i] != null)
                _op2Buttons[i]!.SetHovered(_op2SysButtons[i]!.MouseOverState != MouseOverState.MouseOverNone);
        }
        if (_footerButton != null && _footerSysButton != null)
            _footerButton.SetHovered(_footerSysButton.MouseOverState != MouseOverState.MouseOverNone);
        if (_actionInvoked)
        {
            _actionInvoked = false;
            return true;
        }
        return false;
    }
}
