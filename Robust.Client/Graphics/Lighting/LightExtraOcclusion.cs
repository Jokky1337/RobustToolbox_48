using System.Collections.Generic;
using System.Numerics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Robust.Client.Graphics;

/// <summary>
/// One source selected by Clyde's existing light tree for the current viewport.
/// Positions and radius are in world units, including the light's transformed offset.
/// </summary>
public readonly record struct LightExtraOcclusionContext(
    IClydeViewport Viewport,
    MapId MapId,
    EntityUid LightUid,
    Vector2 WorldPosition,
    float Radius);

/// <summary>
/// A texture-alpha silhouette projected onto the world plane for one light.
/// </summary>
/// <remarks>
/// <paramref name="LocalBounds"/> maps the whole texture using the same Y-up convention as world texture drawing.
/// <paramref name="WorldTransform"/> maps those local bounds to the final world-space shadow quad; the provider
/// computes the projection, including any height or sprite rotation. Only texture alpha is used.
/// <paramref name="Opacity"/> must be finite and between zero and one. Clyde does not own or dispose the texture;
/// it must stay alive for this synchronous render. Clyde textures and their single-level atlas textures are supported.
/// Overlaps take the maximum alpha coverage. GLES2 without blend-minmax ignores these silhouettes.
/// The optional fade softens the shadow away from its base: along the local Y from <paramref name="FeetFraction"/>
/// of the bounds to their top, coverage blurs up to <paramref name="TipBlur"/> texels and falls to
/// <paramref name="TipAlpha"/> of itself. The defaults (0, 1, 0) keep the silhouette uniformly sharp.
/// </remarks>
public readonly record struct LightExtraOcclusionSprite(
    Texture Texture,
    Box2 LocalBounds,
    Matrix3x2 WorldTransform,
    float Opacity,
    float FeetFraction = 0f,
    float TipAlpha = 1f,
    float TipBlur = 0f);

/// <summary>
/// Supplies extra occlusion for one light before its contribution is added to the light map.
/// Add complete triangles in world coordinates to <paramref name="worldTriangles"/> (at most 4096 vertices).
/// Add at most 64 projected texture-alpha silhouettes to <paramref name="worldSprites"/>.
/// Empty output preserves the normal light path. Invalid or non-finite output is ignored.
/// </summary>
/// <remarks>
/// Called synchronously during rendering, only for shadow-casting lights. This callback is CPU-only:
/// do not render, read back the GPU, retain any engine-owned list, or change the registered provider here.
/// Geometry need not depend on the eye; Clyde clips it to the square containing this light's radius.
/// This is a client rendering contract and does not alter FOV, server state, or replication.
/// <paramref name="worldTriangleVisibility"/> is optional: left empty, every triangle fully occludes; otherwise it
/// holds one visibility per vertex of <paramref name="worldTriangles"/> (0 occludes, 1 leaves the light), interpolated
/// across each triangle, and overlaps keep the lowest. GLES2 without blend-minmax ignores it and occludes fully.
/// </remarks>
public delegate void LightExtraOcclusionProvider(
    in LightExtraOcclusionContext context,
    List<Vector2> worldTriangles,
    List<LightExtraOcclusionSprite> worldSprites,
    List<float> worldTriangleVisibility);
