using mmBackgammon.Game;
using mmEngine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;

namespace mmBackgammon;

// Pixel-to-world map for the backgammon table. The mmEngine layout is
// in pixels (board at (20, 30), 800 x 597); the 3D table keeps those
// numbers and scales them to world units, centred on the layout's
// centre (560, 350) so the table sits at the origin. Game logic and
// hit tests stay in pixels; only rendering and the mouse cross this
// map (mouse world -> px via ToPx, then the unchanged px hit tests).
public static class TableMap
{
    public const float Scale = 100f;
    public const float CenterPxX = 560f;
    public const float CenterPxY = 350f;

    public static Vector2 ToWorld(Vector2 px) =>
        new(px.X / Scale - CenterPxX / Scale, px.Y / Scale - CenterPxY / Scale);

    public static Vector2 ToPx(Vector2 world) =>
        new((world.X + CenterPxX / Scale) * Scale, (world.Y + CenterPxY / Scale) * Scale);

    // The whole table (layout 1120 x 700) in world units, for framing.
    public const float TableWidth = 11.2f;
    public const float TableHeight = 7.0f;
}

// The 3D backgammon table: felt ground, board photo, and the two
// 15-checker pools. Dice, outlines, and the dragged checker belong to
// the game scene (they animate); the pools are shared with the
// welcome scene's static board preview.
internal sealed class TableView
{
    private readonly BackgammonGame _app;
    private readonly Scene _scene;
    private readonly List<Entity> _entities = new();
    private readonly Entity[] _whitePool = new Entity[15];
    private readonly Entity[] _blackPool = new Entity[15];

    public TableView(BackgammonGame app, Scene scene)
    {
        _app = app;
        _scene = scene;
        var game = app.Game;

        // Felt ground far below the table pieces, big enough to cover
        // the framed view at any window aspect.
        var feltMat = app.TexturedTransparent(app.FeltTexture);
        Add(FlatQuads.QuadXZ(game, 60f, 60f, feltMat), new Vector3(0, -0.02f, 0));

        var boardMat = app.TexturedTransparent(app.Texture("Assets/Board/BG_Board.png"));
        var boardCenter = TableMap.ToWorld(new Vector2(
            BoardLayout.BoardX + BoardLayout.BoardWidth / 2f,
            BoardLayout.BoardY + BoardLayout.BoardHeight / 2f));
        Add(FlatQuads.QuadXZ(game, BoardLayout.BoardWidth / TableMap.Scale,
                BoardLayout.BoardHeight / TableMap.Scale, boardMat),
            new Vector3(boardCenter.X, 0f, boardCenter.Y));

        var whiteMat = app.TexturedTransparent(app.Texture("Assets/Checkers/WhiteChecker.png"));
        var blackMat = app.TexturedTransparent(app.Texture("Assets/Checkers/BlackChecker.png"));
        var whiteModel = FlatQuads.QuadXZ(game,
            BoardLayout.CheckerSize / TableMap.Scale, BoardLayout.CheckerSize / TableMap.Scale, whiteMat);
        var blackModel = FlatQuads.QuadXZ(game,
            BoardLayout.CheckerSize / TableMap.Scale, BoardLayout.CheckerSize / TableMap.Scale, blackMat);
        for (int i = 0; i < 15; i++)
        {
            _whitePool[i] = Add(whiteModel, new Vector3(0, 0.02f, 0));
            _blackPool[i] = Add(blackModel, new Vector3(0, 0.02f, 0));
        }
    }

    private Entity Add(Model model, Vector3 position)
    {
        var entity = FlatQuads.AddQuad(_scene, model, position);
        _entities.Add(entity);
        return entity;
    }

    // Removes every table entity (called from the scene's Exit).
    public void Remove()
    {
        foreach (var entity in _entities)
            _scene.Entities.Remove(entity);
        _entities.Clear();
    }

    // Positions the pools from a board state. dragFrom removes one
    // checker from its stack (it rides the cursor instead); pass null
    // for a static preview.
    public void Sync(Board board, int? dragFrom)
    {
        int wi = 0, bi = 0;
        for (int i = 0; i < 24; i++)
        {
            int white = board.White[i] - (dragFrom == i ? 1 : 0);
            for (int k = 0; k < white; k++)
                Place(_whitePool[wi++], BoardLayout.CheckerPosition(i, k, white));
            for (int k = 0; k < board.Black[i]; k++)
                Place(_blackPool[bi++], BoardLayout.CheckerPosition(i, k, board.Black[i]));
        }
        int whiteBar = board.WhiteBar - (dragFrom == -1 ? 1 : 0);
        for (int k = 0; k < whiteBar; k++)
            Place(_whitePool[wi++], BoardLayout.BarPosition(Player.White, k));
        for (int k = 0; k < board.BlackBar; k++)
            Place(_blackPool[bi++], BoardLayout.BarPosition(Player.Black, k));
        for (; wi < 15; wi++) SetVisible(_whitePool[wi], false);
        for (; bi < 15; bi++) SetVisible(_blackPool[bi], false);
    }

    private static void Place(Entity entity, Vector2 centerPx)
    {
        var w = TableMap.ToWorld(centerPx);
        entity.Transform.Position = new Vector3(w.X, 0.02f, w.Y);
        SetVisible(entity, true);
    }

    private static void SetVisible(Entity entity, bool visible)
    {
        var model = entity.Get<ModelComponent>();
        if (model != null)
            model.Enabled = visible;
    }
}
