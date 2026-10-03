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
/// </remarks>
public readonly record struct LightExtraOcclusionSprite(
    Texture Texture,
    Box2 LocalBounds,
    Matrix3x2 WorldTransform,
    float Opacity);

/// <summary>
/// Supplies extra occlusion for one light before its contribution is added to the light map.
/// Add complete triangles in world coordinates to <paramref name="worldTriangles"/> (at most 4096 vertices).
/// Add at most 64 projected texture-alpha silhouettes to <paramref name="worldSprites"/>.
/// Empty output preserves the normal light path. Invalid or non-finite output is ignored.
/// </summary>
/// <remarks>
/// Called synchronously during rendering, only for shadow-casting lights. This callback is CPU-only:
/// do not render, read back the GPU, retain either engine-owned list, or change the registered provider here.
/// Geometry need not depend on the eye; Clyde clips it to the square containing this light's radius.
/// This is a client rendering contract and does not alter FOV, server state, or replication.
/// </remarks>
public delegate void LightExtraOcclusionProvider(
    in LightExtraOcclusionContext context,
    List<Vector2> worldTriangles,
    List<LightExtraOcclusionSprite> worldSprites);
