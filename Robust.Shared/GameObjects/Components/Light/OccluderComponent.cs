using Robust.Shared.ComponentTrees;
using Robust.Shared.GameStates;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.ViewVariables;
using System;
using System.Numerics;

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

    /// <summary>
    ///     Structura: rotation of BOTH boxes about <see cref="BoxOrigin"/> (entity-local). Lets an occluder stand
    ///     at an angle inside its tile (a swing-door leaf turning on its hinge) without rotating the entity.
    ///     Zero (the default) keeps every code path byte-for-byte stock. A rotated box never takes part in the
    ///     per-tile neighbour face culling: its edges do not lie on tile edges.
    /// </summary>
    [DataField("boxRotation")]
    public Angle BoxRotation;

    /// <summary>Structura: the point (entity-local) <see cref="BoxRotation"/> turns the boxes about.</summary>
    [DataField("boxOrigin")]
    public Vector2 BoxOrigin;

    /// <summary>
    ///     Structura: the boxes are placed by their own pivot transform (a rotation and/or a pivot is set), even at a
    ///     zero angle. Such a box takes no part in per-tile neighbour face culling — neither its own faces nor its
    ///     neighbours' faces towards it are culled: a leaf turning on its hinge no longer fills its tile, and a wall
    ///     next to it must keep the face it used to hide against that tile (otherwise sight and light leak through).
    /// </summary>
    public bool IsBoxTurned => BoxRotation.Theta != 0 || BoxOrigin != Vector2.Zero;

    /// <summary>Structura: a box as it actually stands, turned by <see cref="BoxRotation"/> about <see cref="BoxOrigin"/>.</summary>
    public Box2Rotated Turned(Box2 box) => new(box, BoxRotation, BoxOrigin);

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
        public Angle BoxRotation { get; }
        public Vector2 BoxOrigin { get; }

        public OccluderComponentState(bool enabled, Box2 boundingBox, Box2? fovBoundingBox, Angle boxRotation = default,
            Vector2 boxOrigin = default)
        {
            Enabled = enabled;
            BoundingBox = boundingBox;
            FovBoundingBox = fovBoundingBox;
            BoxRotation = boxRotation;
            BoxOrigin = boxOrigin;
        }
    }
}
