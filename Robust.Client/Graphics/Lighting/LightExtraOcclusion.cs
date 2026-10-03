using System.Collections.Generic;
using System.Numerics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

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
/// Supplies opaque extra occlusion for one light before its contribution is added to the light map.
/// Add complete triangles in world coordinates to <paramref name="worldTriangles"/> (at most 4096 vertices).
/// Empty output preserves the normal light path. Invalid or non-finite output is ignored.
/// </summary>
/// <remarks>
/// Called synchronously during rendering, only for shadow-casting lights. This callback is CPU-only:
/// do not render, read back the GPU, retain the engine-owned list, or change the registered provider here.
/// Geometry need not depend on the eye; Clyde clips it to the square containing this light's radius.
/// This is a client rendering contract and does not alter FOV, server state, or replication.
/// </remarks>
public delegate void LightExtraOcclusionProvider(
    in LightExtraOcclusionContext context,
    List<Vector2> worldTriangles);
