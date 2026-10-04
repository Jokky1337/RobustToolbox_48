using System;
using System.Numerics;
using NUnit.Framework;
using Robust.Client.Graphics;
using Robust.Shared.Maths;

namespace Robust.Client.Tests.Graphics;

[TestFixture, TestOf(typeof(LightFacadeGeometry))]
internal sealed class LightFacadeGeometryTest
{
    // A run of wall tiles x 0..3 storing heights from y 0, its face line 1/3 tile south of it, 1.85 m tall.
    private static readonly LightFacadeReceiver Run = new(new Box2(0f, 0f, 3f, 1f), 0f, -1f / 3f, 1.85f);

    [Test]
    public void OnlyFiniteRunsWithPositiveSizeAndScaleAreAccepted()
    {
        Assert.Multiple(() =>
        {
            Assert.That(LightFacadeGeometry.IsValid(Run), Is.True);
            Assert.That(LightFacadeGeometry.IsValid(Run with { Bounds = new Box2(0f, 0f, 0f, 1f) }), Is.False);
            Assert.That(LightFacadeGeometry.IsValid(Run with { Bounds = new Box2(0f, float.NaN, 3f, 1f) }), Is.False);
            Assert.That(LightFacadeGeometry.IsValid(Run with { FaceLine = float.PositiveInfinity }), Is.False);
            Assert.That(LightFacadeGeometry.IsValid(Run with { ShadowLine = float.NaN }), Is.False);
            Assert.That(LightFacadeGeometry.IsValid(Run with { MetresPerUnit = 0f }), Is.False);
            Assert.That(LightFacadeGeometry.IsValid(Run with { MetresPerUnit = -1f }), Is.False);
        });
    }

    [Test]
    public void ALightReachesTheRunByItsNearestFacePoint()
    {
        Assert.Multiple(() =>
        {
            Assert.That(LightFacadeGeometry.Reaches(Run, new Vector2(1.5f, -2.9f), 3f), Is.True, "In front of it.");
            Assert.That(LightFacadeGeometry.Reaches(Run, new Vector2(1.5f, -4f), 3f), Is.False, "Too far in front.");
            Assert.That(LightFacadeGeometry.Reaches(Run, new Vector2(5f, 0f), 2.5f), Is.True,
                "Beside its east end, measured from that end.");
            Assert.That(LightFacadeGeometry.Reaches(Run, new Vector2(6f, 0f), 2.5f), Is.False);
        });
    }

    [Test]
    public void TheFaceTakesLightStraightFromTheSouth_GrazingFromTheSides_NoneFromBehind()
    {
        var point = new Vector2(1.5f, -1f / 3f);
        // At the light's own height the direction is horizontal: the pure angle to the face.
        Assert.Multiple(() =>
        {
            Assert.That(LightFacadeGeometry.Facing(point + new Vector2(0f, -2f), 1f, point, 1f, 0f),
                Is.EqualTo(1f).Within(1e-5f), "Due south.");
            Assert.That(LightFacadeGeometry.Facing(point + new Vector2(0f, 2f), 1f, point, 1f, 0f), Is.Zero,
                "Due north: the light sees the wall's back.");
            Assert.That(LightFacadeGeometry.Facing(point + new Vector2(-2f, 0f), 1f, point, 1f, 0f), Is.Zero,
                "Exactly along the face from the west.");
            Assert.That(LightFacadeGeometry.Facing(point + new Vector2(2f, -2f), 1f, point, 1f, 0f),
                Is.EqualTo(MathF.Sqrt(0.5f)).Within(1e-5f), "From the south-east, at 45°.");
            Assert.That(LightFacadeGeometry.Facing(point + new Vector2(-2f, 0f), 1f, point, 1f, 0.5f),
                Is.EqualTo(0.5f / 1.5f).Within(1e-5f), "Wrap lets grazing light reach the face softly.");
        });
        // A ceiling lamp lights the face's foot at a steeper angle than its top.
        var foot = LightFacadeGeometry.Facing(point + new Vector2(0f, -1f), 2.5f, point, 0f, 0f);
        var top = LightFacadeGeometry.Facing(point + new Vector2(0f, -1f), 2.5f, point, 1.8f, 0f);
        Assert.That(top, Is.GreaterThan(foot));
    }

    [Test]
    public void TheRayThroughAFacePointLandsOnTheFloorPastIt_TheHigherTheFarther()
    {
        var light = new Vector2(1.5f, -3f);
        var point = new Vector2(1.5f, -1f / 3f);
        Assert.Multiple(() =>
        {
            Assert.That(LightFacadeGeometry.TryFloorPoint(light, 2.5f, point, 0f, out var foot), Is.True);
            Assert.That(Vector2.Distance(foot, point), Is.LessThan(1e-5f), "At the foot of the face it is the face line.");
            Assert.That(LightFacadeGeometry.TryFloorPoint(light, 2.5f, point, 1.25f, out var half), Is.True);
            // Half the light's height doubles the distance from the light: H / (H - z) = 2.
            Assert.That(Vector2.Distance(half, light), Is.EqualTo(2f * Vector2.Distance(point, light)).Within(1e-4f));
            Assert.That(LightFacadeGeometry.TryFloorPoint(light, 2.5f, point, 2.5f, out _), Is.False,
                "Level with the light the ray never comes down.");
        });
    }

    [Test]
    public void ALampOnTheWallLightsItAsASconce_ALampOutInTheRoomOrBehindDoesNot()
    {
        Assert.Multiple(() =>
        {
            Assert.That(LightFacadeGeometry.Sconce(-0.12f, 0f), Is.EqualTo(1f), "A wall lamp, 0.12 before its face.");
            Assert.That(LightFacadeGeometry.Sconce(-3f, 0f), Is.Zero, "A ceiling lamp out in the room.");
            Assert.That(LightFacadeGeometry.Sconce(0.5f, 0f), Is.Zero, "A lamp behind the wall.");
            var fading = LightFacadeGeometry.Sconce(-0.375f, 0f);
            Assert.That(fading, Is.GreaterThan(0f).And.LessThan(1f), "It fades out, it does not switch.");
        });
    }

    [Test]
    public void ALampOnTheWallItselfCastsTowardTheFace_NeverBackIntoTheRoom()
    {
        // The face line at y 0, the drawn contact line a third of a tile below it.
        const float shadowLine = -1f / 3f;
        // A ceiling lamp out in the room judges shadows at the contact line, as drawn.
        Assert.That(LightFacadeGeometry.ShadowBase(-3f, shadowLine),
            Is.EqualTo(shadowLine - LightFacadeGeometry.FaceOutset).Within(1e-6f));
        // A lamp on the wall stands 0.12 before the face, past the contact line: every face point's ray lands beyond it,
        // toward the wall, however high the point — not back in the room, among the floor shadows of its furniture.
        var lamp = new Vector2(0f, -0.12f);
        var baseY = LightFacadeGeometry.ShadowBase(lamp.Y, shadowLine);
        Assert.That(baseY, Is.GreaterThan(lamp.Y));
        foreach (var height in new[] { 0.1f, 0.9f, 1.8f })
        {
            Assert.That(LightFacadeGeometry.TryFloorPoint(lamp, 2f, new Vector2(-2f, baseY), height, out var floor), Is.True);
            Assert.That(floor.Y, Is.GreaterThan(lamp.Y), $"At {height} m.");
        }
    }
}
