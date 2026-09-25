using System.Numerics;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Robust.Client.GameObjects;

// Structura (ADR-015): точка опоры для y-сортировки, см. SpriteComponent.SortAnchor.
// Ключ считают двое — Clyde при отрисовке и контентный ClickableSystem при выборе верхней сущности под курсором;
// оба берут смещение точки отсюда, чтобы клик попадал в того, кто нарисован сверху.
public sealed partial class SpriteSystem
{
    public void SetSortAnchor(Entity<SpriteComponent?> sprite, Vector2? value)
    {
        if (!_query.Resolve(sprite.Owner, ref sprite.Comp))
            return;

        sprite.Comp.sortAnchor = value;
    }

    /// <summary>
    ///     Точка опоры относительно начала координат спрайта (позиция сущности плюс <see cref="SpriteComponent.Offset"/>)
    ///     в экранных осях: повёрнута и отмасштабирована позой спрайта — <see cref="SpriteComponent.Rotation"/> и
    ///     <see cref="SpriteComponent.Scale"/>, как слои в <see cref="SpriteComponent.LocalMatrix"/>. Поворот сущности и
    ///     камеры не участвует. Тайлы, +Y к северу.
    /// </summary>
    public static Vector2 GetSortAnchorOffset(SpriteComponent sprite, Vector2 anchor)
    {
        return sprite.Rotation.RotateVec(anchor * sprite.Scale);
    }

    /// <summary>
    ///     Мировая точка опоры спрайта при заданных позе сущности и повороте камеры. Её Y в осях камеры — ключ, по
    ///     которому Clyde сортирует спрайт при отрисовке. false — точки нет, ключом служит низ рамки
    ///     (<see cref="CalculateBounds"/>).
    /// </summary>
    public bool TryGetWorldSortAnchor(
        Entity<SpriteComponent> sprite,
        Vector2 worldPos,
        Angle worldRot,
        Angle eyeRot,
        out Vector2 worldAnchor)
    {
        if (sprite.Comp.sortAnchor is not { } anchor)
        {
            worldAnchor = default;
            return false;
        }

        // Начало координат спрайта — тем же правилом, что в CalculateBounds и Clyde.ProcessSprites.
        var origin = worldPos + (sprite.Comp.NoRotation
            ? (-eyeRot).RotateVec(sprite.Comp.Offset)
            : worldRot.RotateVec(sprite.Comp.Offset));

        // Смещение точки задано в экранных осях, в мировые его переводит обратный поворот камеры.
        worldAnchor = origin + (-eyeRot).RotateVec(GetSortAnchorOffset(sprite.Comp, anchor));
        return true;
    }
}
