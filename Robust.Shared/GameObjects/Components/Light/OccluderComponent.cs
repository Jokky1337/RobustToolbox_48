using Robust.Shared.ComponentTrees;
using Robust.Shared.GameStates;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.ViewVariables;
using System;

namespace Robust.Shared.GameObjects;

[RegisterComponent]
[NetworkedComponent()]
[Access(typeof(OccluderSystem))]
public sealed partial class OccluderComponent : Component, IComponentTreeEntry<OccluderComponent>
{
    [DataField("enabled")]
    public bool Enabled = true;

    [DataField("boundingBox")]
    public Box2 BoundingBox = new(-0.5f, -0.5f, 0.5f, 0.5f);

    /// <summary>
    ///     Structura: optional separate box for EYE/FOV occlusion (and sight raycasts). When null, FOV uses
    ///     <see cref="BoundingBox"/> as before. Decouples "blocks light" from "blocks view" for tall-wall
    ///     sprites whose art overhangs the tile: the FOV box covers the full art (so the vision mask does
    ///     not slice the sprite), while the light box stays tile-sized (so wall lamps mounted just outside
    ///     it are not swallowed).
    /// </summary>
    [DataField("fovBoundingBox")]
    public Box2? FovBoundingBox;

    /// <summary>Effective box for eye/FOV/sight purposes.</summary>
    [ViewVariables]
    public Box2 FovBox => FovBoundingBox ?? BoundingBox;

    public EntityUid? TreeUid { get; set; }
    public DynamicTree<ComponentTreeEntry<OccluderComponent>>? Tree { get; set; }

    public bool AddToTree => Enabled;
    public bool TreeUpdateQueued { get; set; } = false;

    [ViewVariables] public (EntityUid Grid, Vector2i Tile)? LastPosition;
    [ViewVariables] public OccluderDir Occluding;

    [Flags]
    public enum OccluderDir : byte
    {
        None = 0,
        North = 1,
        East = 1 << 1,
        South = 1 << 2,
        West = 1 << 3,
    }

    [NetSerializable, Serializable]
    public sealed class OccluderComponentState : ComponentState
    {
        public bool Enabled { get; }
        public Box2 BoundingBox { get; }
        public Box2? FovBoundingBox { get; }

        public OccluderComponentState(bool enabled, Box2 boundingBox, Box2? fovBoundingBox)
        {
            Enabled = enabled;
            BoundingBox = boundingBox;
            FovBoundingBox = fovBoundingBox;
        }
    }
}
