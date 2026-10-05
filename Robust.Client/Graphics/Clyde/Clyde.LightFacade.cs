using System;
using System.Collections.Generic;
using System.Numerics;
using OpenToolkit.Graphics.OpenGL4;
using Robust.Client.GameObjects;
using Robust.Client.ResourceManagement;
using Robust.Shared.GameObjects;
using Robust.Shared.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using TKStencilOp = OpenToolkit.Graphics.OpenGL4.StencilOp;

namespace Robust.Client.Graphics.Clyde;

/// <summary>
/// Structura (plan §10.5, ADR-015): each light also lights the south faces of tall walls, evaluated for the face
/// instead of the floor, into a per-viewport facade light map that wall shaders read.
/// </summary>
internal sealed partial class Clyde
{
    private const string FacadeLightUniform = "STRUKTURA_FACADE_LIGHT";
    // The eye's polar FOV map for the shaders that read the facade light map, on unit 4.
    private const string FacadeFovUniform = "STRUKTURA_FACADE_FOV";
    // x: the facade light map holds this frame's light; yz: the eye; w: its field of view applies.
    private const string FacadeFlagUniform = "STRUKTURA_FACADE";

    private LightFacadeReceiverProvider? _lightFacadeProvider;
    private LightSourceHeightProvider? _lightFacadeHeights;
    private float _lightFacadeWrap;
    private readonly List<LightFacadeReceiver> _lightFacadeReceivers = new();
    private ClydeHandle? _lightFacadeShaderHandle;
    private bool _lightFacadeShaderFailed;
    private bool _lightFacadeAllocationFailed;
    private bool _lightFacadeWarningShown;

    public bool LightFacadeReceiversSupported => !_lightFacadeShaderFailed;

    public void SetLightFacadeReceivers(LightFacadeReceiverProvider? receivers, LightSourceHeightProvider? heights,
        float wrap)
    {
        _lightFacadeProvider = receivers;
        _lightFacadeHeights = heights;
        _lightFacadeWrap = float.IsFinite(wrap) ? Math.Clamp(wrap, 0f, 4f) : 0f;
        _lightFacadeWarningShown = false;
        _lightFacadeAllocationFailed = false;
        _lightFacadeShaderFailed = false;
        _lightFacadeReceivers.Clear();
        if (receivers != null)
            return;

        foreach (var viewportRef in _viewports.Values)
        {
            if (!viewportRef.TryGetTarget(out var viewport))
                continue;
            viewport.FacadeLightTarget?.Dispose();
            viewport.FacadeLightTarget = null;
            viewport.FacadeLightReady = false;
        }
    }

    /// <summary>
    /// Asks content for this viewport's receivers and starts their light map from the light map's own base: the clear
    /// colour with the BeforeLighting overlays (Structura's ambient) on it, copied before FOV blackens what the eye
    /// cannot see — wall tiles among it. Called with the light render target bound; leaves it bound.
    /// </summary>
    private bool PrepareLightFacades(Viewport viewport, MapId mapId, Box2 worldAABB)
    {
        viewport.FacadeLightReady = false;
        _lightFacadeReceivers.Clear();
        var provider = _lightFacadeProvider;
        if (provider == null || _lightFacadeAllocationFailed || GetFacadeLightShader() == null)
            return false;

        try
        {
            provider(viewport, mapId, worldAABB, _lightFacadeReceivers);
        }
        catch (Exception e)
        {
            WarnLightFacade($"provider failed: {e.Message}");
            _lightFacadeReceivers.Clear();
            return false;
        }

        // Drop invalid entries and anything past the cap: content may not overflow the pass.
        var kept = 0;
        for (var i = 0; i < _lightFacadeReceivers.Count && kept < LightFacadeGeometry.MaxReceivers; i++)
        {
            var receiver = _lightFacadeReceivers[i];
            if (LightFacadeGeometry.IsValid(receiver))
                _lightFacadeReceivers[kept++] = receiver;
            else
                WarnLightFacade("invalid receiver bounds, face line or height scale");
        }
        _lightFacadeReceivers.RemoveRange(kept, _lightFacadeReceivers.Count - kept);
        if (kept == 0)
            return false;

        var lightTarget = viewport.LightRenderTarget;
        if (viewport.FacadeLightTarget == null)
        {
            try
            {
                var format = _hasGLFloatFramebuffers ? RenderTargetColorFormat.R11FG11FB10F : RenderTargetColorFormat.Rgba8;
                viewport.FacadeLightTarget = CreateRenderTarget(lightTarget.Size,
                    new RenderTargetFormatParameters(format),
                    new TextureSampleParameters { Filter = true },
                    $"{viewport.Name}-facadeLight");
            }
            catch (Exception e)
            {
                // Stop until explicit re-registration: a failed factory can leave partial GL allocations.
                _lightFacadeAllocationFailed = true;
                WarnLightFacade($"facade light map allocation failed: {e.Message}");
                BindRenderTargetImmediate(RtToLoaded(lightTarget));
                return false;
            }
        }

        // The bound light target is the read framebuffer; both have its size and colour format.
        var facadeTexture = _loadedTextures[viewport.FacadeLightTarget.Texture.TextureId].OpenGLObject;
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, facadeTexture.Handle);
        CheckGlError();
        GL.CopyTexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, 0, 0, lightTarget.Size.X, lightTarget.Size.Y);
        CheckGlError();
        viewport.FacadeLightReady = true;
        return true;
    }

    /// <summary>
    /// Adds one light to the facade light map of every receiver it reaches. Called right after its floor quad, with the
    /// light pass bound (mask on unit 0, shadow map on 1, this light's extra visibility on 2); restores that state.
    /// The eye's FOV map goes on unit 3, which the floor pass does not use.
    /// </summary>
    private void DrawLightFacades(Viewport viewport, GLShaderProgram lightShader, EntityUid lightUid,
        PointLightComponent component, Vector2 lightPos, Angle maskRotation, bool hasMask, bool hasExtraVisibility,
        int index)
    {
        if (!viewport.FacadeLightReady || viewport.FacadeLightTarget == null)
            return;

        var any = false;
        foreach (var receiver in _lightFacadeReceivers)
        {
            if (LightFacadeGeometry.Reaches(receiver, lightPos, component.Radius))
            {
                any = true;
                break;
            }
        }
        if (!any)
            return;

        var shader = GetFacadeLightShader();
        if (shader == null)
            return;

        var height = 2.5f;
        if (_lightFacadeHeights != null)
        {
            try
            {
                height = _lightFacadeHeights(lightUid);
            }
            catch (Exception e)
            {
                WarnLightFacade($"height provider failed: {e.Message}");
            }
        }
        if (!float.IsFinite(height) || height <= 0f)
            height = 2.5f;

        BindRenderTargetImmediate(RtToLoaded(viewport.FacadeLightTarget));
        IsStencilling = false;
        shader.Use();
        SetupGlobalUniformsImmediate(shader, ShadowTexture);
        shader.SetUniformTextureMaybe(UniIMainTexture, TextureUnit.Texture0);
        shader.SetUniformTextureMaybe("shadowMap", TextureUnit.Texture1);
        shader.SetUniformTextureMaybe("extraVisibilityMap", TextureUnit.Texture2);
        shader.SetUniformMaybe("extraVisibilityEnabled", hasExtraVisibility ? 1f : 0f);
        shader.SetUniformMaybe("lightColor", component.Color);
        shader.SetUniformMaybe("lightCenter", lightPos);
        shader.SetUniformMaybe("lightRange", component.Radius);
        shader.SetUniformMaybe("lightPower", component.Energy);
        shader.SetUniformMaybe("lightSoftness", _enableSoftShadows ? component.Softness : 0f);
        shader.SetUniformMaybe("lightFalloff", component.Falloff);
        shader.SetUniformMaybe("lightCurveFactor", component.CurveFactor);
        // index is the light's shadow map row (-1 without shadows), see DrawLightsAndFov.
        shader.SetUniformMaybe("lightIndex", index >= 0 ? (index + 0.5f) / ShadowTexture.Height : -1f);
        shader.SetUniformMaybe("lightHeight", height);
        var (sin, cos) = MathF.SinCos((float) maskRotation.Theta);
        shader.SetUniformMaybe("lightMask", new Vector4(cos, sin, hasMask ? 1f : 0f, 0f));
        // A face shows light only where the eye sees the floor in front of it: seen from behind (the outer side of a
        // room's wall) it would show the lamps and shadows of the room beyond. The FOV map is this viewport's.
        SetTexture(TextureUnit.Texture3, _structuraFovActive ? FovTexture : _stockTextureWhite);
        shader.SetUniformTextureMaybe("fovMap", TextureUnit.Texture3);
        shader.SetUniformMaybe("fovEye",
            new Vector4(_structuraFovEye.X, _structuraFovEye.Y, _structuraFovActive ? 1f : 0f, 0f));
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
        CheckGlError();

        foreach (var receiver in _lightFacadeReceivers)
        {
            if (!LightFacadeGeometry.Reaches(receiver, lightPos, component.Radius))
                continue;
            shader.SetUniformMaybe("facadeReceiver", new Vector4(receiver.Bounds.Bottom, receiver.FaceLine,
                receiver.MetresPerUnit, _lightFacadeWrap));
            shader.SetUniformMaybe("facadeShadowLine", receiver.ShadowLine);
            _drawQuad(receiver.Bounds.BottomLeft, receiver.Bounds.TopRight, Matrix3x2.Identity, shader);
        }

        // Back to the floor light pass, its FOV stencil untouched.
        BindRenderTargetImmediate(RtToLoaded(viewport.LightRenderTarget));
        lightShader.Use();
        SetupGlobalUniformsImmediate(lightShader, ShadowTexture);
        IsStencilling = true;
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
        GL.StencilFunc(StencilFunction.Equal, 0xFF, 0xFF);
        GL.StencilOp(TKStencilOp.Keep, TKStencilOp.Keep, TKStencilOp.Keep);
        CheckGlError();
    }

    private GLShaderProgram? GetFacadeLightShader()
    {
        if (_lightFacadeShaderFailed)
            return null;
        if (_lightFacadeShaderHandle == null)
        {
            try
            {
                var resource = _resourceCache.GetResource<ShaderSourceResource>("/Shaders/Internal/light-facade.swsl");
                _lightFacadeShaderHandle = resource.ClydeHandle;
            }
            catch (Exception e)
            {
                _lightFacadeShaderFailed = true;
                WarnLightFacade($"facade shader unavailable: {e.Message}");
                return null;
            }
        }
        return _loadedShaders[_lightFacadeShaderHandle.Value].Program;
    }

    private void WarnLightFacade(string reason)
    {
        if (_lightFacadeWarningShown)
            return;
        _lightFacadeWarningShown = true;
        _sawmillOgl.Warning($"Facade light pass: {reason}");
    }
}
