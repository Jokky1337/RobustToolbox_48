using System.Numerics;
using NUnit.Framework;
using Robust.Client.Graphics;
using Robust.Shared.Maths;

namespace Robust.Client.Tests.Graphics;

[TestFixture, TestOf(typeof(LightExtraOcclusionGeometry))]
internal sealed class LightExtraOcclusionGeometryTest
{
    [TestCase(0, false)]
    [TestCase(1, false)]
    [TestCase(3, true)]
    [TestCase(LightExtraOcclusionGeometry.MaxVertices - 3, true)]
    [TestCase(LightExtraOcclusionGeometry.MaxVertices, true)]
    [TestCase(LightExtraOcclusionGeometry.MaxVertices + 1, false)]
    [TestCase(LightExtraOcclusionGeometry.MaxVertices + 3, false)]
    public void TriangleInputMustBeCompleteAndBounded(int count, bool valid)
    {
        Assert.That(LightExtraOcclusionGeometry.IsTriangleCountValid(count), Is.EqualTo(valid));
    }

    [TestCase(0, 6, true)]
    [TestCase(6, 6, true)]
    [TestCase(3, 6, false)]
    [TestCase(9, 6, false)]
    public void TriangleVisibilityIsAbsentOrOnePerVertex(int visibility, int vertices, bool valid)
    {
        Assert.That(LightExtraOcclusionGeometry.IsTriangleVisibilityCountValid(visibility, vertices), Is.EqualTo(valid));
    }

    [TestCase(0.25f, 0.25f)]
    [TestCase(-1f, 0f)]
    [TestCase(2f, 1f)]
    [TestCase(float.NaN, 0f)]
    [TestCase(float.PositiveInfinity, 0f)]
    public void TriangleVisibilityIsClampedAndInvalidValuesOcclude(float value, float expected)
    {
        Assert.That(LightExtraOcclusionGeometry.TriangleVisibility(value), Is.EqualTo(expected));
    }

    [Test]
    public void MaskCoversLightRadiusWithWorldSouthAtTextureBottom()
    {
        var light = new Vector2(10, -7);
        Assert.Multiple(() =>
        {
            const float size = LightExtraOcclusionGeometry.MaskSize;
            AssertPosition(light, light, 4f, new Vector2(size / 2, size / 2));
            // Raster screen Y points down, so the southern edge ends up in GL texture row V=0.
            AssertPosition(light + new Vector2(-4, -4), light, 4f, new Vector2(0, size));
            AssertPosition(light + new Vector2(4, 4), light, 4f, new Vector2(size, 0));
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

    [TestCase(-1, false)]
    [TestCase(0, true)]
    [TestCase(LightExtraOcclusionGeometry.MaxSprites, true)]
    [TestCase(LightExtraOcclusionGeometry.MaxSprites + 1, false)]
    public void SilhouetteInputIsBounded(int count, bool valid)
    {
        Assert.That(LightExtraOcclusionGeometry.IsSpriteCountValid(count), Is.EqualTo(valid));
    }

    [Test]
    public void SilhouetteUsesCompleteWorldTransformAndSameProjectionAsTriangles()
    {
        var bounds = new Box2(-0.5f, -0.25f, 0.75f, 1.5f);
        var light = new Vector2(10, -7);
        // Include reflection, rotation, shear and translation, as projected content silhouettes do.
        var transform = new Matrix3x2(-1.1f, 0.3f, 0.6f, 0.2f, 11f, -6.2f);
        Assert.That(LightExtraOcclusionGeometry.TryGetSpriteMaskTransform(bounds, transform, 0.7f,
            light, 4f, out var mask), Is.True);

        foreach (var corner in new[] {bounds.BottomLeft, bounds.BottomRight, bounds.TopLeft, bounds.TopRight})
        {
            var world = Vector2.Transform(corner, transform);
            Assert.That(LightExtraOcclusionGeometry.TryGetMaskPosition(world, light, 4f, out var expected), Is.True);
            var actual = Vector2.Transform(corner, mask);
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(0.0001f));
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(0.0001f));
        }
    }

    [TestCase(-0.1f, false)]
    [TestCase(0f, true)]
    [TestCase(0.5f, true)]
    [TestCase(1f, true)]
    [TestCase(1.1f, false)]
    [TestCase(float.NaN, false)]
    [TestCase(float.PositiveInfinity, false)]
    public void SilhouetteOpacityMustBeFiniteCoverage(float opacity, bool valid)
    {
        Assert.That(LightExtraOcclusionGeometry.TryGetSpriteMaskTransform(new Box2(-1, -1, 1, 1),
            Matrix3x2.Identity, opacity, Vector2.Zero, 4f, out _), Is.EqualTo(valid));
    }

    [Test]
    public void InvalidSilhouetteBoundsAndProjectionAreRejected()
    {
        var bounds = new Box2(-1, -1, 1, 1);
        var transform = Matrix3x2.Identity;
        Assert.Multiple(() =>
        {
            Assert.That(LightExtraOcclusionGeometry.TryGetSpriteMaskTransform(new Box2(0, 0, 0, 1),
                transform, 1f, Vector2.Zero, 4f, out _), Is.False);
            Assert.That(LightExtraOcclusionGeometry.TryGetSpriteMaskTransform(new Box2(1, 0, -1, 1),
                transform, 1f, Vector2.Zero, 4f, out _), Is.False);
            Assert.That(LightExtraOcclusionGeometry.TryGetSpriteMaskTransform(new Box2(float.NaN, 0, 1, 1),
                transform, 1f, Vector2.Zero, 4f, out _), Is.False);
            Assert.That(LightExtraOcclusionGeometry.TryGetSpriteMaskTransform(new Box2(-float.MaxValue, 0, float.MaxValue, 1),
                transform, 1f, Vector2.Zero, 4f, out _), Is.False);
            Assert.That(LightExtraOcclusionGeometry.TryGetSpriteMaskTransform(bounds,
                new Matrix3x2(1, 0, float.PositiveInfinity, 1, 0, 0), 1f, Vector2.Zero, 4f, out _), Is.False);
            Assert.That(LightExtraOcclusionGeometry.TryGetSpriteMaskTransform(bounds,
                Matrix3x2.CreateTranslation(float.MaxValue, 0), 1f, Vector2.Zero, 4f, out _), Is.False);
            // Bounds-to-unit-quad coefficients overflow even when individual corners can remain finite.
            Assert.That(LightExtraOcclusionGeometry.TryGetSpriteMaskTransform(new Box2(-1, -1, 1, 1),
                Matrix3x2.CreateScale(float.MaxValue), 1f, Vector2.Zero, 128f, out _), Is.False);
        });
    }

    private static void AssertPosition(Vector2 world, Vector2 light, float radius, Vector2 expected)
    {
        Assert.That(LightExtraOcclusionGeometry.TryGetMaskPosition(world, light, radius, out var actual), Is.True);
        Assert.That(actual, Is.EqualTo(expected));
    }
}
