using System;
using System.Numerics;

namespace Robust.Client.Graphics;

/// <summary>Validation and world-to-mask projection shared by the bounded extra-visibility pass.</summary>
internal static class LightExtraOcclusionGeometry
{
    internal const int MaxVertices = 4096;
    internal const int MaskSize = 256;

    internal static bool IsTriangleCountValid(int count)
    {
        return count >= 3 && count <= MaxVertices && count % 3 == 0;
    }

    internal static bool TryGetMaskPosition(Vector2 worldPosition, Vector2 lightPosition, float radius,
        out Vector2 position)
    {
        position = default;
        if (!float.IsFinite(radius) || radius <= 0f ||
            !float.IsFinite(worldPosition.X) || !float.IsFinite(worldPosition.Y) ||
            !float.IsFinite(lightPosition.X) || !float.IsFinite(lightPosition.Y))
            return false;

        var scale = (MaskSize * 0.5f) / radius;
        // Screen-space Y points down. After rasterization, texture V=0 is world-space south.
        position = (worldPosition - lightPosition) * new Vector2(scale, -scale) + new Vector2(MaskSize * 0.5f);
        return float.IsFinite(position.X) && float.IsFinite(position.Y);
    }
}
