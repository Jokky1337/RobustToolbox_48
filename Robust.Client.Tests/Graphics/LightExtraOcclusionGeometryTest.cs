using System.Numerics;
using NUnit.Framework;
using Robust.Client.Graphics;

namespace Robust.Client.Tests.Graphics;

[TestFixture, TestOf(typeof(LightExtraOcclusionGeometry))]
internal sealed class LightExtraOcclusionGeometryTest
{
    [TestCase(0, false)]
    [TestCase(1, false)]
    [TestCase(3, true)]
    [TestCase(4095, true)]
    [TestCase(4096, false)]
    [TestCase(4098, false)]
    public void TriangleInputMustBeCompleteAndBounded(int count, bool valid)
    {
        Assert.That(LightExtraOcclusionGeometry.IsTriangleCountValid(count), Is.EqualTo(valid));
    }

    [Test]
    public void MaskCoversLightRadiusWithWorldSouthAtTextureBottom()
    {
        var light = new Vector2(10, -7);
        Assert.Multiple(() =>
        {
            AssertPosition(light, light, 4f, new Vector2(128, 128));
            // Raster screen Y points down, so the southern edge ends up in GL texture row V=0.
            AssertPosition(light + new Vector2(-4, -4), light, 4f, new Vector2(0, 256));
            AssertPosition(light + new Vector2(4, 4), light, 4f, new Vector2(256, 0));
        });
    }

    [TestCase(0f)]
    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.Epsilon)]
    public void InvalidOrUnrepresentableRadiusIsRejected(float radius)
    {
        Assert.That(LightExtraOcclusionGeometry.TryGetMaskPosition(Vector2.Zero, Vector2.Zero, radius, out _), Is.False);
    }

    [Test]
    public void NonFiniteWorldCoordinatesAndProjectionOverflowAreRejected()
    {
        Assert.Multiple(() =>
        {
            Assert.That(LightExtraOcclusionGeometry.TryGetMaskPosition(new Vector2(float.NaN, 0), Vector2.Zero, 4f, out _), Is.False);
            Assert.That(LightExtraOcclusionGeometry.TryGetMaskPosition(Vector2.Zero, new Vector2(0, float.PositiveInfinity), 4f, out _), Is.False);
            Assert.That(LightExtraOcclusionGeometry.TryGetMaskPosition(new Vector2(float.MaxValue), new Vector2(-float.MaxValue), 4f, out _), Is.False);
        });
    }

    private static void AssertPosition(Vector2 world, Vector2 light, float radius, Vector2 expected)
    {
        Assert.That(LightExtraOcclusionGeometry.TryGetMaskPosition(world, light, radius, out var actual), Is.True);
        Assert.That(actual, Is.EqualTo(expected));
    }
}
