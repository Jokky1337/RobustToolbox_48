using System;
using System.Collections.Generic;
using System.Numerics;
using OpenToolkit.Graphics.OpenGL4;
using Robust.Shared.Enums;
using Robust.Shared.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Robust.Client.Graphics.Clyde;

internal sealed partial class Clyde
{
    private LightExtraOcclusionProvider? _lightExtraOcclusionProvider;
    private List<Vector2>? _lightExtraOcclusionTriangles;
    private Vertex2D[] _lightExtraOcclusionVertices = Array.Empty<Vertex2D>();
    private bool _lightExtraOcclusionWarningShown;
    private bool _lightExtraOcclusionAllocationFailed;

    public void SetLightExtraOcclusionProvider(LightExtraOcclusionProvider? provider)
    {
        _lightExtraOcclusionProvider = provider;
        _lightExtraOcclusionWarningShown = false;
        _lightExtraOcclusionAllocationFailed = false;
        if (provider != null)
            return;

        _lightExtraOcclusionTriangles = null;
        _lightExtraOcclusionVertices = Array.Empty<Vertex2D>();
        foreach (var viewportRef in _viewports.Values)
        {
            if (!viewportRef.TryGetTarget(out var viewport))
                continue;
            viewport.ExtraLightVisibilityTarget?.Dispose();
            viewport.ExtraLightVisibilityTarget = null;
        }
    }

    private ClydeTexture? PrepareExtraLightVisibility(Viewport viewport, MapId mapId, EntityUid lightUid,
        Vector2 lightPosition, float radius)
    {
        var provider = _lightExtraOcclusionProvider;
        if (provider == null || _lightExtraOcclusionAllocationFailed)
            return null;

        using var _ = _prof.Group("Extra Light Visibility");

        var triangles = _lightExtraOcclusionTriangles ??= new List<Vector2>();
        triangles.Clear();
        var context = new LightExtraOcclusionContext(viewport, mapId, lightUid, lightPosition, radius);
        try
        {
            provider(in context, triangles);
        }
        catch (Exception e)
        {
            WarnExtraLightOcclusion($"provider failed: {e.Message}");
            if (triangles.Capacity > LightExtraOcclusionGeometry.MaxVertices)
                _lightExtraOcclusionTriangles = null;
            return null;
        }

        if (triangles.Count == 0)
            return null;
        if (!LightExtraOcclusionGeometry.IsTriangleCountValid(triangles.Count))
        {
            WarnExtraLightOcclusion($"expected complete triangles with at most {LightExtraOcclusionGeometry.MaxVertices} vertices");
            if (triangles.Capacity > LightExtraOcclusionGeometry.MaxVertices)
                _lightExtraOcclusionTriangles = null;
            return null;
        }

        if (_lightExtraOcclusionVertices.Length < triangles.Count)
            _lightExtraOcclusionVertices = new Vertex2D[triangles.Count];

        for (var i = 0; i < triangles.Count; i++)
        {
            if (!LightExtraOcclusionGeometry.TryGetMaskPosition(triangles[i], lightPosition, radius, out var position))
            {
                WarnExtraLightOcclusion("non-finite geometry or light transform");
                return null;
            }
            _lightExtraOcclusionVertices[i] = new Vertex2D(position, Vector2.Zero, Color.Black);
        }

        if (viewport.ExtraLightVisibilityTarget == null)
        {
            var boundTarget = _currentBoundRenderTarget;
            try
            {
                viewport.ExtraLightVisibilityTarget = CreateRenderTarget(
                    new Vector2i(LightExtraOcclusionGeometry.MaskSize, LightExtraOcclusionGeometry.MaskSize),
                    new RenderTargetFormatParameters(RenderTargetColorFormat.R8),
                    TextureSampleParameters.Default,
                    $"{viewport.Name}-extraLightVisibility");
            }
            catch (Exception e)
            {
                // Stop all extra passes until explicit registration; the factory's failure path
                // can leave partial GL allocations, so retrying for every lamp/frame is unsafe.
                _lightExtraOcclusionAllocationFailed = true;
                WarnExtraLightOcclusion($"scratch allocation failed: {e.Message}");
                return null;
            }
            finally
            {
                // CreateRenderTarget temporarily binds an FBO; restore even if allocation failed.
                BindRenderTargetImmediate(boundTarget);
            }
        }

        var target = viewport.ExtraLightVisibilityTarget;
        var state = PushRenderStateFull();
        var oldModel = _currentMatrixModel;
        try
        {
            // This pass owns no stencil. Never use the usual RenderInRenderTarget restore here:
            // its stencil clear would erase the FOV stencil of the accumulating light target.
            BindRenderTargetFull(target);
            SetViewportImmediate(Box2i.FromDimensions(Vector2i.Zero, target.Size));
            _updateUniformConstants(target.Size);
            CalcScreenMatrices(target.Size, out var projection, out var view);
            SetProjViewFull(projection, view);
            _currentMatrixModel = Matrix3x2.Identity;
            _queuedShaderInstance = _defaultShader;
            SetScissorFull(null);
            IsStencilling = false;
            IsBlending = false;

            GLClearColor(Color.White);
            GL.Clear(ClearBufferMask.ColorBufferBit);
            CheckGlError();

            // Opaque black triangles overwrite white visibility, so overlaps form a union.
            ReadOnlySpan<Vertex2D> vertices = _lightExtraOcclusionVertices.AsSpan(0, triangles.Count);
            DrawPrimitives(DrawPrimitiveTopology.TriangleList, _stockTextureWhite.TextureId, in vertices);
            FlushRenderQueue();
            FenceRenderTarget(target);
        }
        finally
        {
            PopRenderStateFull(state, clearStencil: false);
            _updateUniformConstants(_currentRenderTarget.Size);
            _currentMatrixModel = oldModel;
        }

        return target.Texture;
    }

    private void WarnExtraLightOcclusion(string reason)
    {
        if (_lightExtraOcclusionWarningShown)
            return;
        _lightExtraOcclusionWarningShown = true;
        _sawmillOgl.Warning($"Extra light visibility ignored: {reason}");
    }
}
