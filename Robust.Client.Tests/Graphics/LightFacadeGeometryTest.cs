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
}
