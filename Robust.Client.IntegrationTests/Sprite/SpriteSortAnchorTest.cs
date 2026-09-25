using System.Numerics;
using System.Threading.Tasks;
using NUnit.Framework;
using Robust.Client;
using Robust.Client.GameObjects;
using Robust.Client.Graphics.Clyde;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Robust.UnitTesting.Client.Sprite;

/// <summary>
///     Structura (ADR-015): точка опоры для y-сортировки на настоящих спрайтах — разбор YAML, поза и то, что клик
///     (<see cref="SpriteSystem.TryGetWorldSortAnchor"/>) и отрисовка (<see cref="Clyde.SortKey"/>) считают один ключ.
/// </summary>
public sealed class SpriteSortAnchorTest : RobustIntegrationTest
{
    private static readonly string Prototypes = @"
- type: entity
  id: sortAnchorTestNone
  components:
  - type: Sprite
    sprite: debugRotation.rsi
    layers:
    - state: direction1

- type: entity
  id: sortAnchorTestFeet
  components:
  - type: Sprite
    sprite: debugRotation.rsi
    noRot: true
    sortAnchor: 0,-0.5
    layers:
    - state: direction1
";

    private static readonly Vector2 ViewScale = new(48f, -48f);

    [Test]
    public async Task TestSortAnchor()
    {
        var client = StartClient(new ClientIntegrationOptions {ExtraPrototypes = Prototypes});
        await client.WaitIdleAsync();
        var baseClient = client.Resolve<IBaseClient>();

        await client.WaitPost(() => baseClient.StartSinglePlayer());
        await client.WaitIdleAsync();

        var entMan = client.EntMan;
        var sys = client.System<SpriteSystem>();

        EntityUid noneUid = default;
        EntityUid feetUid = default;

        await client.WaitPost(() =>
        {
            noneUid = entMan.Spawn("sortAnchorTestNone");
            feetUid = entMan.Spawn("sortAnchorTestFeet");
        });

        var none = new Entity<SpriteComponent>(noneUid, entMan.GetComponent<SpriteComponent>(noneUid));
        var feet = new Entity<SpriteComponent>(feetUid, entMan.GetComponent<SpriteComponent>(feetUid));

        // YAML: поле необязательное, по умолчанию точки нет — всё как в стоке.
        Assert.That(none.Comp.SortAnchor, Is.Null);
        Assert.That(feet.Comp.SortAnchor, Is.EqualTo(new Vector2(0f, -0.5f)));
        Assert.That(sys.TryGetWorldSortAnchor(none, new Vector2(3f, 4f), Angle.Zero, Angle.Zero, out _), Is.False);

        var pos = new Vector2(3f, 4f);

        // Стоит: точка на полтайла южнее. noRot — поворот сущности точку не крутит.
        Assert.That(sys.TryGetWorldSortAnchor(feet, pos, Angle.FromDegrees(90), Angle.Zero, out var anchor), Is.True);
        Assert.That(anchor, Is.Approximately(new Vector2(3f, 3.5f)));

        // Лёг: спрайт повёрнут на 90° — точка ушла в центр по высоте.
        sys.SetRotation(feet.AsNullable(), Angle.FromDegrees(90));
        sys.TryGetWorldSortAnchor(feet, pos, Angle.Zero, Angle.Zero, out anchor);
        Assert.That(anchor.Y, Is.EqualTo(4f).Within(1e-5f));
        sys.SetRotation(feet.AsNullable(), Angle.Zero);

        // Сел: сжатие по вертикали и просадка спрайта поднимают точку вместе с картинкой.
        sys.SetScale(feet.AsNullable(), new Vector2(1f, 0.8f));
        sys.SetOffset(feet.AsNullable(), new Vector2(0f, 0.1f));
        sys.TryGetWorldSortAnchor(feet, pos, Angle.Zero, Angle.Zero, out anchor);
        Assert.That(anchor, Is.Approximately(new Vector2(3f, 4f + 0.1f - 0.4f)));

        // Поворот камеры: у noRot-спрайта сдвиг и точка остаются экранными, мировая точка поворачивается обратно.
        var eye = Angle.FromDegrees(90);
        sys.TryGetWorldSortAnchor(feet, pos, Angle.Zero, eye, out anchor);
        var expectedEyeY = eye.RotateVec(pos).Y + 0.1f - 0.4f;
        Assert.That(eye.RotateVec(anchor).Y, Is.EqualTo(expectedEyeY).Within(1e-5f));

        // Клик и отрисовка — один ключ: экранная Y мировой точки совпадает с Clyde.SortKey для того же кадра.
        foreach (var eyeRot in new[] {Angle.Zero, Angle.FromDegrees(30), Angle.FromDegrees(90)})
        {
            sys.TryGetWorldSortAnchor(feet, pos, Angle.FromDegrees(45), eyeRot, out anchor);
            var origin = eyeRot.RotateVec(pos + (-eyeRot).RotateVec(feet.Comp.Offset));
            var key = Clyde.SortKey(feet.Comp, default, origin, ViewScale);
            Assert.That(key, Is.EqualTo(eyeRot.RotateVec(anchor).Y * ViewScale.Y).Within(1e-3f), $"eye {eyeRot}");
        }

        // Спрайт с поворотом (без noRot): сдвиг крутится с сущностью, как в стоке, а сама точка — нет.
        feet.Comp.NoRotation = false;
        sys.SetOffset(feet.AsNullable(), new Vector2(0f, 0.2f));
        sys.SetScale(feet.AsNullable(), Vector2.One);
        sys.TryGetWorldSortAnchor(feet, pos, Angle.FromDegrees(90), Angle.Zero, out anchor);
        Assert.That(anchor, Is.Approximately(new Vector2(3f - 0.2f, 4f - 0.5f)));

        // Сеттер и копирование спрайта.
        sys.SetSortAnchor(none.AsNullable(), new Vector2(0.25f, -0.25f));
        Assert.That(none.Comp.SortAnchor, Is.EqualTo(new Vector2(0.25f, -0.25f)));
        sys.CopySprite(feet.AsNullable(), none.AsNullable());
        Assert.That(none.Comp.SortAnchor, Is.EqualTo(new Vector2(0f, -0.5f)));
        sys.SetSortAnchor(none.AsNullable(), null);
        Assert.That(none.Comp.SortAnchor, Is.Null);

        await client.WaitPost(() =>
        {
            entMan.DeleteEntity(noneUid);
            entMan.DeleteEntity(feetUid);
        });
    }
}
