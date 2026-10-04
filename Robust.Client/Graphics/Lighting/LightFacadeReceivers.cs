using System;
using System.Collections.Generic;
using System.Numerics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Robust.Client.Graphics;

/// <summary>
/// A run of south-facing wall faces that a viewport sees, lit apart from the floor (Structura, plan §10.5).
/// </summary>
/// <remarks>
/// <paramref name="Bounds"/> is the world rectangle that stores the run's light: heights rise from its bottom edge at
/// <paramref name="MetresPerUnit"/> metres per world unit. <paramref name="FaceLine"/> is the world Y of the face itself,
/// where each light's attenuation, mask, wall occlusion and direction are evaluated (just outside it, so the wall's own
/// occluder does not shadow its outward side). <paramref name="ShadowLine"/> is where the face meets the floor as the
/// art draws it: floor shadows reaching that line climb the face. The face looks toward −Y.
/// </remarks>
public readonly record struct LightFacadeReceiver(Box2 Bounds, float FaceLine, float ShadowLine, float MetresPerUnit);

/// <summary>
/// Supplies the facade receivers of one viewport, once per frame, before its lights are drawn. Called synchronously
/// on the render thread: CPU only, no rendering, no retained engine lists.
/// </summary>
public delegate void LightFacadeReceiverProvider(IClydeViewport viewport, MapId mapId, Box2 worldBounds,
    List<LightFacadeReceiver> receivers);

/// <summary>The height above the floor a light shines from, metres.</summary>
public delegate float LightSourceHeightProvider(EntityUid light);

/// <summary>Validation and the CPU mirror of the facade light pass (light-facade.swsl), for tests and diagnostics.</summary>
public static class LightFacadeGeometry
{
    /// <summary>Receivers one viewport may register; the rest are ignored.</summary>
    public const int MaxReceivers = 1024;

    /// <summary>Just outside the face, so the wall's own occluder does not shadow its outward side.</summary>
    public const float FaceOutset = 0.02f;

    public static bool IsValid(in LightFacadeReceiver receiver)
    {
        var bounds = receiver.Bounds;
        return float.IsFinite(bounds.Left) && float.IsFinite(bounds.Bottom) && float.IsFinite(bounds.Right)
               && float.IsFinite(bounds.Top) && bounds.Width > 0f && bounds.Height > 0f
               && float.IsFinite(receiver.FaceLine) && float.IsFinite(receiver.ShadowLine)
               && float.IsFinite(receiver.MetresPerUnit)
               && receiver.MetresPerUnit > 0f;
    }

    /// <summary>Whether a light of this radius reaches any point of the receiver's face.</summary>
    public static bool Reaches(in LightFacadeReceiver receiver, Vector2 light, float radius)
    {
        var x = Math.Clamp(light.X, receiver.Bounds.Left, receiver.Bounds.Right);
        var dx = light.X - x;
        var dy = light.Y - receiver.FaceLine;
        return dx * dx + dy * dy < radius * radius;
    }

    /// <summary>
    /// How squarely a light falls on a south face at a point: 1 straight from the south, toward 0 grazing from the west
    /// or east, nothing from behind. <paramref name="wrap"/> softens the falloff toward grazing light.
    /// </summary>
    public static float Facing(Vector2 light, float lightHeight, Vector2 facePoint, float height, float wrap)
    {
        var toLight = new Vector3(light.X - facePoint.X, light.Y - facePoint.Y, lightHeight - height);
        var length = toLight.Length();
        if (!(length > 1e-4f))
            return 0f;
        var facing = -toLight.Y / length;
        return Math.Clamp((facing + wrap) / (1f + wrap), 0f, 1f);
    }

    /// <summary>
    /// The floor point where the ray from the light through a face point at <paramref name="height"/> lands, past the
    /// face: what the floor-plane extra visibility (furniture, silhouettes) says about that point.
    /// </summary>
    public static bool TryFloorPoint(Vector2 light, float lightHeight, Vector2 facePoint, float height, out Vector2 floor)
    {
        floor = default;
        if (!(lightHeight - height > 0.01f))
            return false;
        floor = light + (facePoint - light) * (lightHeight / (lightHeight - height));
        return true;
    }
}
