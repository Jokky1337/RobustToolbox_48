using System.Numerics;
using NUnit.Framework;
using Robust.Client.GameObjects;
using Robust.Client.Graphics.Clyde;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Robust.Client.Tests.Sprite;

/// <summary>
///     Structura (ADR-015): точка опоры для y-сортировки (<see cref="SpriteComponent.SortAnchor"/>).
///     Ключ точки обязан жить в тех же экранных координатах, что и стоковый низ рамки: спрайты с точкой и без неё
///     сортируются в одной плоскости.
/// </summary>
[TestFixture]
[TestOf(typeof(Clyde))]
public sealed class SortAnchorTest
{
    private static readonly Vector2 ViewScale = new(48f, -48f);

    private static IEnumerable<(Angle rotation, Vector2 scale, Vector2 anchor, Vector2 origin)> _poses =
    [
        (Angle.Zero, Vector2.One, new Vector2(0f, -0.48f), new Vector2(0f, 0f)),
        (Angle.Zero, Vector2.One, new Vector2(0.3f, -0.48f), new Vector2(-1.5f, 2.25f)),
        (Angle.FromDegrees(90), Vector2.One, new Vector2(0f, -0.48f), new Vector2(0.4f, -3f)),
        (Angle.FromDegrees(-90), Vector2.One, new Vector2(0f, -0.48f), new Vector2(0.4f, -3f)),
        (Angle.FromDegrees(7), new Vector2(1.05f, 0.84f), new Vector2(0f, -0.48f), new Vector2(2f, 1f)),
        (Angle.FromDegrees(33), new Vector2(0.5f, 2f), new Vector2(-0.2f, 0.1f), new Vector2(-7f, 5f)),
    ];

    private static SpriteComponent Sprite(Angle rotation, Vector2 scale, Vector2? anchor, int depth = 6, uint order = 0)
    {
        return new SpriteComponent
        {
            rotation = rotation,
            scale = scale,
            sortAnchor = anchor,
            drawDepth = depth,
            RenderOrder = order,
        };
    }

    /// <summary>
    ///     Ключ точки — это экранная Y самой точки после позы спрайта: то же, что даёт стоковое преобразование рамки
    ///     (<see cref="Clyde.TransformCenteredBox"/>) для рамки нулевого размера в этой точке.
    /// </summary>
    [Test]
    public void AnchorKeyIsTheTransformedPoint([ValueSource(nameof(_poses))]
        (Angle rotation, Vector2 scale, Vector2 anchor, Vector2 origin) pose)
    {
        var sprite = Sprite(pose.rotation, pose.scale, pose.anchor);
        var point = pose.anchor * pose.scale;
        var expected = Clyde.TransformCenteredBox(new Box2(point, point), (float) pose.rotation, pose.origin, ViewScale);

        var key = Clyde.SortKey(sprite, default, pose.origin, ViewScale);

        Assert.That(key, Is.EqualTo(expected.Top).Within(1e-4f));
    }

    /// <summary>Без точки ключ — низ рамки, ровно как в стоке.</summary>
    [Test]
    public void NoAnchorKeepsStockKey()
    {
        var sprite = Sprite(Angle.FromDegrees(33), new Vector2(0.5f, 2f), null);
        var box = Clyde.TransformCenteredBox(Box2.UnitCentered, 0.3f, new Vector2(1f, 2f), ViewScale);

        Assert.That(Clyde.SortKey(sprite, box, new Vector2(100f, 100f), ViewScale), Is.EqualTo(box.Top));
    }

    /// <summary>
    ///     Поза двигает точку: лёг (поворот ±90°) — ушла в центр, сел (сжатие по вертикали) — поднялась,
    ///     сдвиг спрайта двигает её вместе с картинкой.
    /// </summary>
    [Test]
    public void AnchorFollowsSpritePose()
    {
        var feet = new Vector2(0f, -0.48f);

        Assert.That(SpriteSystem.GetSortAnchorOffset(Sprite(Angle.Zero, Vector2.One, feet), feet).Y,
            Is.EqualTo(-0.48f).Within(1e-5f));
        Assert.That(SpriteSystem.GetSortAnchorOffset(Sprite(Angle.FromDegrees(90), Vector2.One, feet), feet).Y,
            Is.EqualTo(0f).Within(1e-5f));
        Assert.That(SpriteSystem.GetSortAnchorOffset(Sprite(Angle.FromDegrees(-90), Vector2.One, feet), feet).Y,
            Is.EqualTo(0f).Within(1e-5f));
        Assert.That(SpriteSystem.GetSortAnchorOffset(Sprite(Angle.Zero, new Vector2(1.05f, 0.84f), feet), feet).Y,
            Is.EqualTo(-0.48f * 0.84f).Within(1e-5f));

        // Sprite.Offset входит в начало координат спрайта: ключ поднятой на 0.2 тайла картинки — на 0.2 тайла севернее.
        var standing = Sprite(Angle.Zero, Vector2.One, feet);
        var raised = Clyde.SortKey(standing, default, new Vector2(0f, 0.2f), ViewScale);
        var ground = Clyde.SortKey(standing, default, Vector2.Zero, ViewScale);
        Assert.That(ground - raised, Is.EqualTo(0.2f * 48f).Within(1e-3f));
    }

    private static Clyde.SpriteData Data(SpriteComponent sprite, float key, int uid)
    {
        return new Clyde.SpriteData
        {
            Uid = new EntityUid(uid),
            Sprite = sprite,
            SortKey = key,
        };
    }

    /// <summary>
    ///     Порядок сравнения: слой отрисовки, затем RenderOrder (подсветка перетаскивания и выбора цели), затем ключ
    ///     y-сортировки, затем uid. Точка опоры меняет только третий шаг.
    /// </summary>
    [Test]
    public void ComparerOrder()
    {
        var mobs = Sprite(Angle.Zero, Vector2.One, Vector2.Zero, depth: 6);
        var overMobs = Sprite(Angle.Zero, Vector2.One, Vector2.Zero, depth: 7);
        var dragged = Sprite(Angle.Zero, Vector2.One, Vector2.Zero, depth: 6, order: 5);

        // Слой старше ключа.
        Assert.That(Clyde.SpriteDrawingOrderComparer.Compare(Data(overMobs, 0f, 1), Data(mobs, 100f, 2)), Is.GreaterThan(0));

        // RenderOrder старше ключа.
        Assert.That(Clyde.SpriteDrawingOrderComparer.Compare(Data(dragged, 0f, 1), Data(mobs, 100f, 2)), Is.GreaterThan(0));

        // Внутри слоя — южнее рисуется позже.
        Assert.That(Clyde.SpriteDrawingOrderComparer.Compare(Data(mobs, 30f, 1), Data(mobs, 20f, 2)), Is.GreaterThan(0));
        Assert.That(Clyde.SpriteDrawingOrderComparer.Compare(Data(mobs, 20f, 1), Data(mobs, 30f, 2)), Is.LessThan(0));

        // Равные ключи — по uid.
        Assert.That(Clyde.SpriteDrawingOrderComparer.Compare(Data(mobs, 20f, 3), Data(mobs, 20f, 2)), Is.GreaterThan(0));
    }

    /// <summary>
    ///     Точка опоры переставляет пару, которую сток ставит по рамке. Бортик ванны: кадр 48 px, по рамке ключ —
    ///     низ кадра, +24 px; по линии передней кромки — +44 px. Персонаж со ступнями на +30 px по рамке рисуется
    ///     поверх бортика, по линии — под ним.
    /// </summary>
    [Test]
    public void AnchorReordersAgainstStockBounds()
    {
        var feet = new Vector2(0f, -23f / 48f);
        var rimLine = new Vector2(0f, -44f / 48f);
        var frame = Box2.UnitCentered; // кадр 48×48 px = тайл

        // Шагнувший из ванны: центр на 7 px южнее центра ванны, ступни на +30 px.
        var bather = Sprite(Angle.Zero, Vector2.One, feet);
        var batherOrigin = new Vector2(0f, -7f / 48f);
        var batherKey = Clyde.SortKey(bather, Clyde.TransformCenteredBox(frame, 0f, batherOrigin, ViewScale), batherOrigin, ViewScale);
        Assert.That(batherKey, Is.EqualTo(30f).Within(1e-3f));

        var rimBox = Clyde.TransformCenteredBox(frame, 0f, Vector2.Zero, ViewScale);
        var stockRim = Sprite(Angle.Zero, Vector2.One, null);
        var anchoredRim = Sprite(Angle.Zero, Vector2.One, rimLine);

        var stockOrder = Clyde.SpriteDrawingOrderComparer.Compare(
            Data(stockRim, Clyde.SortKey(stockRim, rimBox, Vector2.Zero, ViewScale), 1),
            Data(bather, batherKey, 2));
        var anchoredOrder = Clyde.SpriteDrawingOrderComparer.Compare(
            Data(anchoredRim, Clyde.SortKey(anchoredRim, rimBox, Vector2.Zero, ViewScale), 1),
            Data(bather, batherKey, 2));

        Assert.That(stockOrder, Is.LessThan(0), "по рамке бортик (+24) рисуется раньше ступней на +30");
        Assert.That(anchoredOrder, Is.GreaterThan(0), "по линии бортик (+44) рисуется позже ступней на +30");
    }
}
