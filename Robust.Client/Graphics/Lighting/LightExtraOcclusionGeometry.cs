using System;
using System.Numerics;
using Robust.Shared.Maths;

namespace Robust.Client.Graphics;

/// <summary>Validation and world-to-mask projection shared by the bounded extra-visibility pass.</summary>
internal static class LightExtraOcclusionGeometry
{
    // Structura: 4096 vertices (64 barrels) dropped every extra shadow of a lamp; the Clyde batch holds 65532, and a
    // lamp's triangles go in one DrawPrimitives call, so stay well inside it. A multiple of three.
    internal const int MaxVertices = 32766;
    internal const int MaxSprites = 128;
    // Structura: 256 texels across 2R left long furniture shadow edges stepped at large radii (25 cm at R = 32);
    // the mask is also sampled with filtering now.
    internal const int MaskSize = 512;

    internal static bool IsTriangleCountValid(int count)
    {
        return count >= 3 && count <= MaxVertices && count % 3 == 0;
    }

    internal static bool IsSpriteCountValid(int count)
    {
        return count >= 0 && count <= MaxSprites;
    }

    /// <summary>Per-vertex visibility is absent, or there is one value for every triangle vertex.</summary>
    internal static bool IsTriangleVisibilityCountValid(int visibilityCount, int vertexCount)
    {
        return visibilityCount == 0 || visibilityCount == vertexCount;
    }

    /// <summary>The mask value of one vertex: non-finite values occlude fully, others are clamped to [0, 1].</summary>
    internal static float TriangleVisibility(float visibility)
    {
        return float.IsFinite(visibility) ? Math.Clamp(visibility, 0f, 1f) : 0f;
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

    internal static bool TryGetSpriteMaskTransform(in Box2 bounds, in Matrix3x2 worldTransform, float opacity,
        Vector2 lightPosition, float radius, out Matrix3x2 maskTransform)
    {
        maskTransform = default;
        if (!float.IsFinite(opacity) || opacity < 0f || opacity > 1f ||
            !float.IsFinite(bounds.Left) || !float.IsFinite(bounds.Right) ||
            !float.IsFinite(bounds.Bottom) || !float.IsFinite(bounds.Top) ||
            bounds.Left >= bounds.Right || bounds.Bottom >= bounds.Top ||
            !float.IsFinite(bounds.Right - bounds.Left) || !float.IsFinite(bounds.Top - bounds.Bottom) ||
            !IsFinite(worldTransform) ||
            !TryGetMaskPosition(Vector2.Zero, lightPosition, radius, out var maskOrigin))
            return false;

        var scale = (MaskSize * 0.5f) / radius;
        maskTransform = worldTransform * new Matrix3x2(scale, 0f, 0f, -scale, maskOrigin.X, maskOrigin.Y);
        // _drawQuad composes these local bounds into the model matrix; reject overflow there too.
        var rectTransform = new Matrix3x2(bounds.Right - bounds.Left, 0f, 0f, bounds.Top - bounds.Bottom,
            bounds.Left, bounds.Bottom) * maskTransform;
        return IsFinite(maskTransform) && IsFinite(rectTransform) &&
               IsFinite(Vector2.Transform(bounds.BottomLeft, maskTransform)) &&
               IsFinite(Vector2.Transform(bounds.BottomRight, maskTransform)) &&
               IsFinite(Vector2.Transform(bounds.TopLeft, maskTransform)) &&
               IsFinite(Vector2.Transform(bounds.TopRight, maskTransform));
    }

    private static bool IsFinite(in Matrix3x2 matrix)
    {
        return float.IsFinite(matrix.M11) && float.IsFinite(matrix.M12) &&
               float.IsFinite(matrix.M21) && float.IsFinite(matrix.M22) &&
               float.IsFinite(matrix.M31) && float.IsFinite(matrix.M32);
    }

    private static bool IsFinite(Vector2 vector)
    {
        return float.IsFinite(vector.X) && float.IsFinite(vector.Y);
    }
}
