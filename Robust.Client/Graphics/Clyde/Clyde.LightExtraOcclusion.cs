using System;
using System.Collections.Generic;
using System.Numerics;
using OpenToolkit.Graphics.OpenGL4;
using Robust.Client.ResourceManagement;
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
    private List<LightExtraOcclusionSprite>? _lightExtraOcclusionSprites;
    private List<float>? _lightExtraOcclusionTriangleVisibility;
    private Vertex2D[] _lightExtraOcclusionVertices = Array.Empty<Vertex2D>();
    private ExtraLightSpriteDraw[] _lightExtraOcclusionSpriteDraws = Array.Empty<ExtraLightSpriteDraw>();
    private ClydeHandle? _lightExtraOcclusionSpriteShaderHandle;
    private bool _lightExtraOcclusionSpriteShaderFailed;
    private bool _lightExtraOcclusionWarningShown;
    private bool _lightExtraOcclusionAllocationFailed;

    public bool LightExtraOcclusionSpritesSupported => _hasGLBlendMinMax && !_lightExtraOcclusionSpriteShaderFailed;

    public void SetLightExtraOcclusionProvider(LightExtraOcclusionProvider? provider)
    {
        _lightExtraOcclusionProvider = provider;
        _lightExtraOcclusionWarningShown = false;
        _lightExtraOcclusionAllocationFailed = false;
        _lightExtraOcclusionSpriteShaderFailed = false;
        if (provider != null)
            return;

        _lightExtraOcclusionTriangles = null;
        _lightExtraOcclusionSprites = null;
        _lightExtraOcclusionTriangleVisibility = null;
        _lightExtraOcclusionVertices = Array.Empty<Vertex2D>();
        _lightExtraOcclusionSpriteDraws = Array.Empty<ExtraLightSpriteDraw>();
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
        var sprites = _lightExtraOcclusionSprites ??= new List<LightExtraOcclusionSprite>();
        var visibility = _lightExtraOcclusionTriangleVisibility ??= new List<float>();
        triangles.Clear();
        sprites.Clear();
        visibility.Clear();
        var context = new LightExtraOcclusionContext(viewport, mapId, lightUid, lightPosition, radius);
        try
        {
            provider(in context, triangles, sprites, visibility);
        }
        catch (Exception e)
        {
            WarnExtraLightOcclusion($"provider failed: {e.Message}");
            if (triangles.Capacity > LightExtraOcclusionGeometry.MaxVertices)
                _lightExtraOcclusionTriangles = null;
            if (sprites.Capacity > LightExtraOcclusionGeometry.MaxSprites)
                _lightExtraOcclusionSprites = null;
            sprites.Clear();
            return null;
        }

        if (triangles.Count == 0 && sprites.Count == 0)
            return null;
        if (triangles.Count != 0 && !LightExtraOcclusionGeometry.IsTriangleCountValid(triangles.Count))
        {
            WarnExtraLightOcclusion($"expected complete triangles with at most {LightExtraOcclusionGeometry.MaxVertices} vertices");
            if (triangles.Capacity > LightExtraOcclusionGeometry.MaxVertices)
                _lightExtraOcclusionTriangles = null;
            if (visibility.Capacity > LightExtraOcclusionGeometry.MaxVertices)
                _lightExtraOcclusionTriangleVisibility = null;
            sprites.Clear();
            return null;
        }
        if (!LightExtraOcclusionGeometry.IsTriangleVisibilityCountValid(visibility.Count, triangles.Count))
        {
            WarnExtraLightOcclusion("expected no triangle visibility or one value per triangle vertex");
            if (visibility.Capacity > LightExtraOcclusionGeometry.MaxVertices)
                _lightExtraOcclusionTriangleVisibility = null;
            sprites.Clear();
            return null;
        }
        if (!LightExtraOcclusionGeometry.IsSpriteCountValid(sprites.Count))
        {
            WarnExtraLightOcclusion($"expected at most {LightExtraOcclusionGeometry.MaxSprites} silhouettes");
            _lightExtraOcclusionSprites = null;
            return null;
        }

        if (_lightExtraOcclusionVertices.Length < triangles.Count)
            _lightExtraOcclusionVertices = new Vertex2D[triangles.Count];

        // Graded triangles need the min-blended union; without blend-minmax they fall back to full occlusion.
        var gradedTriangles = visibility.Count != 0 && _hasGLBlendMinMax;
        for (var i = 0; i < triangles.Count; i++)
        {
            if (!LightExtraOcclusionGeometry.TryGetMaskPosition(triangles[i], lightPosition, radius, out var position))
            {
                WarnExtraLightOcclusion("non-finite geometry or light transform");
                sprites.Clear();
                return null;
            }
            var value = gradedTriangles ? LightExtraOcclusionGeometry.TriangleVisibility(visibility[i]) : 0f;
            _lightExtraOcclusionVertices[i] = new Vertex2D(position, Vector2.Zero, MaskVertexColor(value));
        }
        visibility.Clear();

        var spriteCount = 0;
        GLShaderProgram? spriteShader = null;
        if (sprites.Count != 0)
        {
            if (_hasGLBlendMinMax)
            {
                spriteShader = GetExtraLightSpriteShader();
                if (spriteShader != null)
                {
                    if (_lightExtraOcclusionSpriteDraws.Length < sprites.Count)
                        _lightExtraOcclusionSpriteDraws = new ExtraLightSpriteDraw[sprites.Count];
                    foreach (var sprite in sprites)
                    {
                        if (!TryPrepareExtraLightSprite(in sprite, lightPosition, radius, out var draw))
                        {
                            WarnExtraLightOcclusion("invalid silhouette bounds, transform, opacity, or disposed texture");
                            continue;
                        }
                        if (sprite.Opacity != 0f)
                            _lightExtraOcclusionSpriteDraws[spriteCount++] = draw;
                    }
                }
            }
            else
            {
                WarnExtraLightOcclusion("texture silhouettes require blend-minmax on GLES2");
            }
        }
        // Do not retain content-owned texture references between lamps or after allocation failure.
        sprites.Clear();

        if (triangles.Count == 0 && spriteCount == 0)
            return null;

        if (viewport.ExtraLightVisibilityTarget == null)
        {
            var boundTarget = _currentBoundRenderTarget;
            try
            {
                viewport.ExtraLightVisibilityTarget = CreateRenderTarget(
                    new Vector2i(LightExtraOcclusionGeometry.MaskSize, LightExtraOcclusionGeometry.MaskSize),
                    new RenderTargetFormatParameters(RenderTargetColorFormat.R8),
                    // Structura: filtered, so furniture shadow edges and graded wedges do not step at mask texels.
                    new TextureSampleParameters { Filter = true },
                    $"{viewport.Name}-extraLightVisibility");
            }
            catch (Exception e)
            {
                // Stop all extra passes until explicit registration; the factory's failure path
                // can leave partial GL allocations, so retrying for every lamp/frame is unsafe.
                _lightExtraOcclusionAllocationFailed = true;
                WarnExtraLightOcclusion($"scratch allocation failed: {e.Message}");
                Array.Clear(_lightExtraOcclusionSpriteDraws, 0, spriteCount);
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

            // Opaque black triangles overwrite white visibility, so overlaps form a union. Graded ones keep the
            // lowest visibility under min blending: the same union, with a soft tip.
            if (triangles.Count != 0)
            {
                if (gradedTriangles)
                {
                    // A batch drawn by the flush resets the equation after itself: drain the queue before Min.
                    FlushRenderQueue();
                    IsBlending = true;
                    GL.BlendEquation(BlendEquationMode.Min);
                    GL.BlendFunc(BlendingFactor.One, BlendingFactor.One);
                }
                ReadOnlySpan<Vertex2D> vertices = _lightExtraOcclusionVertices.AsSpan(0, triangles.Count);
                DrawPrimitives(DrawPrimitiveTopology.TriangleList, _stockTextureWhite.TextureId, in vertices);
            }
            FlushRenderQueue();
            if (gradedTriangles && triangles.Count != 0)
            {
                GL.BlendEquation(BlendEquationMode.FuncAdd);
                IsBlending = false;
            }

            if (spriteCount != 0)
            {
                // Immediate draws keep each opacity/UV uniform paired with its own quad.
                // min(visibility, 1 - alpha * opacity) is a coverage union, not repeated blending.
                IsStencilling = false;
                IsBlending = true;
                GL.BlendEquation(BlendEquationMode.Min);
                GL.BlendFunc(BlendingFactor.One, BlendingFactor.One);
                spriteShader!.Use();
                spriteShader.SetUniformTextureMaybe(UniIMainTexture, TextureUnit.Texture0);
                for (var i = 0; i < spriteCount; i++)
                {
                    ref var draw = ref _lightExtraOcclusionSpriteDraws[i];
                    SetupGlobalUniformsImmediate(spriteShader, draw.Texture);
                    SetTexture(TextureUnit.Texture0, draw.Texture.TextureId);
                    spriteShader.SetUniformMaybe("spriteUvRect", draw.UvRect);
                    spriteShader.SetUniformMaybe("spriteOpacity", draw.Opacity);
                    spriteShader.SetUniformMaybe("spriteFade", draw.Fade);
                    spriteShader.SetUniformMaybe("spriteTexel",
                        new Vector2(1f / draw.Texture.Width, 1f / draw.Texture.Height));
                    _drawQuad(draw.Bounds.BottomLeft, draw.Bounds.TopRight, draw.MaskTransform, spriteShader);
                }
            }
            FenceRenderTarget(target);
        }
        finally
        {
            GL.BlendEquation(BlendEquationMode.FuncAdd);
            ResetBlendFunc();
            PopRenderStateFull(state, clearStencil: false);
            _updateUniformConstants(_currentRenderTarget.Size);
            _currentMatrixModel = oldModel;
            Array.Clear(_lightExtraOcclusionSpriteDraws, 0, spriteCount);
        }

        return target.Texture;
    }

    /// <summary>
    /// The vertex colour that writes <paramref name="visibility"/> itself into the mask: a negative (unshaded) modulate
    /// makes the base shader ignore whatever light map is bound, and the base vertex shader linearizes the rest.
    /// </summary>
    private static Color MaskVertexColor(float visibility)
    {
        var encoded = -1f - Color.ToSrgb(new Color(visibility, visibility, visibility)).R;
        return new Color(encoded, encoded, encoded, -2f);
    }

    private GLShaderProgram? GetExtraLightSpriteShader()
    {
        if (_lightExtraOcclusionSpriteShaderFailed)
            return null;
        if (_lightExtraOcclusionSpriteShaderHandle == null)
        {
            try
            {
                var resource = _resourceCache.GetResource<ShaderSourceResource>("/Shaders/Internal/light-extra-occlusion-sprite.swsl");
                _lightExtraOcclusionSpriteShaderHandle = resource.ClydeHandle;
            }
            catch (Exception e)
            {
                _lightExtraOcclusionSpriteShaderFailed = true;
                WarnExtraLightOcclusion($"silhouette shader unavailable: {e.Message}");
                return null;
            }
        }
        return _loadedShaders[_lightExtraOcclusionSpriteShaderHandle.Value].Program;
    }

    private bool TryPrepareExtraLightSprite(in LightExtraOcclusionSprite sprite, Vector2 lightPosition, float radius,
        out ExtraLightSpriteDraw draw)
    {
        draw = default;
        if (!LightExtraOcclusionGeometry.TryGetSpriteMaskTransform(sprite.LocalBounds, sprite.WorldTransform,
                sprite.Opacity, lightPosition, radius, out var maskTransform))
            return false;

        // Match world texture drawing's atlas extraction and UV orientation without doing GPU work in the provider.
        var source = sprite.Texture is AtlasTexture atlas ? atlas.SourceTexture : sprite.Texture;
        if (source is not ClydeTexture texture || !_loadedTextures.ContainsKey(texture.TextureId) ||
            texture.Width <= 0 || texture.Height <= 0)
            return false;
        var clydeTexture = RenderHandle.ExtractTexture(sprite.Texture, null, out var region);
        if (!float.IsFinite(region.Left) || !float.IsFinite(region.Top) ||
            !float.IsFinite(region.Right) || !float.IsFinite(region.Bottom) ||
            region.Left < 0f || region.Top < 0f || region.Right > texture.Width || region.Bottom > texture.Height ||
            region.Left >= region.Right || region.Top >= region.Bottom)
            return false;
        // The fade is cosmetic: out-of-range values are clamped rather than rejecting the silhouette.
        if (!float.IsFinite(sprite.FeetFraction) || !float.IsFinite(sprite.TipAlpha) || !float.IsFinite(sprite.TipBlur))
            return false;
        var uv = RenderHandle.WorldTextureBoundsToUV(clydeTexture, region);
        draw = new ExtraLightSpriteDraw(clydeTexture, sprite.LocalBounds, maskTransform,
            new Vector4(uv.Left, uv.Bottom, uv.Right - uv.Left, uv.Top - uv.Bottom), sprite.Opacity,
            new Vector4(Math.Clamp(sprite.FeetFraction, 0f, 0.95f), Math.Clamp(sprite.TipAlpha, 0f, 1f),
                Math.Clamp(sprite.TipBlur, 0f, MaxExtraLightSpriteBlur), 0f));
        return true;
    }

    /// <summary>Texels; the shader's 13 taps stay a blur, not scattered copies, up to about this radius.</summary>
    private const float MaxExtraLightSpriteBlur = 8f;

    private readonly record struct ExtraLightSpriteDraw(
        ClydeTexture Texture,
        Box2 Bounds,
        Matrix3x2 MaskTransform,
        Vector4 UvRect,
        float Opacity,
        Vector4 Fade);

    private void WarnExtraLightOcclusion(string reason)
    {
        if (_lightExtraOcclusionWarningShown)
            return;
        _lightExtraOcclusionWarningShown = true;
        _sawmillOgl.Warning($"Extra light visibility ignored: {reason}");
    }
}
