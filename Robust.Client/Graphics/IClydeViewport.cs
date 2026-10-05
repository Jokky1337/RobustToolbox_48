using System;
using System.Numerics;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Robust.Client.Graphics
{
    /// <summary>
    ///     A viewport is an API for rendering a section of the game map centered around an eye,
    ///     complete with lighting, FOV and grid rendering.
    /// </summary>
    [NotContentImplementable]
    public interface IClydeViewport : IDisposable
    {
        /// <summary>
        /// A unique ID for this viewport. No other viewport with this ID can ever exist in the app lifetime.
        /// </summary>
        long Id { get; }

        /// <summary>
        ///     The render target that is rendered to when rendering this viewport.
        /// </summary>
        IRenderTexture RenderTarget { get; }
        IRenderTexture LightRenderTarget { get; }

        /// <summary>
        ///     Structura: the soft FOV mask of the frame being drawn, its alpha how hidden a pixel is (1 = hidden), or
        ///     null when the soft path (<c>light.fov_blur_mult</c> &gt; 0) did not run. Ready for
        ///     <see cref="Robust.Shared.Enums.OverlaySpace.WorldSpace"/> overlays, after the FOV is applied.
        /// </summary>
        IRenderTexture? FovMaskRenderTarget { get; }

        /// <summary>
        ///     Structura: where the light comes from (<c>light.direction_map</c>), at a quarter of the viewport size and
        ///     covering the same area as <see cref="LightRenderTarget"/>, or null when it was not drawn this frame.
        ///     Per pixel, summed over the lamps reaching it (wall shadows and masks applied): RG = brightness-weighted
        ///     unit vector toward the lamp, B = brightness, A = brightness-weighted distance (world units). RG/B is the
        ///     mean direction (its length tells how one-sided the light is), A/B the mean distance. Not gated by the eye's
        ///     FOV (the light map is): a consumer must weigh it by the light map or the FOV mask, or it shows lamps the
        ///     eye cannot see. Null without float framebuffers.
        /// </summary>
        IRenderTexture? LightDirectionRenderTarget { get; }

        /// <summary>
        ///     Structura: the light map of the wall faces (facade receivers), the same size and area as
        ///     <see cref="LightRenderTarget"/>, or null when this frame has none. Ready for
        ///     <see cref="Robust.Shared.Enums.OverlaySpace.AfterLighting"/> overlays that grade light as a whole (eye
        ///     adaptation), so the faces of tall walls change with the floor.
        /// </summary>
        IRenderTexture? FacadeLightRenderTarget { get; }

        IEye? Eye { get; set; }
        Vector2i Size { get; }

        /// <summary>
        /// Raised when the viewport indicates that any cached rendering resources (e.g. render targets)
        /// should be purged.
        /// </summary>
        /// <remarks>
        /// This event is raised if the viewport is disposed (manually or via finalization).
        /// However, code should expect this event to be raised at any time, even if the viewport is not disposed fully.
        /// </remarks>
        event Action<ClearCachedViewportResourcesEvent> ClearCachedResources;

        /// <summary>
        /// Color to clear the render target to before rendering. If null, no clearing will happen.
        /// </summary>
        Color? ClearColor { get; set; }

        /// <summary>
        /// On frames where Eye is null or in nullspace, whether the viewport may clear.
        /// </summary>
        bool ClearWhenMissingEye { get; set; }

        /// <summary>
        ///     This is, effectively, a multiplier to the eye's zoom.
        /// </summary>
        Vector2 RenderScale { get; set; }

        /// <summary>
        ///     If true, <see cref="Render"/> will be automatically called at the start of the frame.
        /// </summary>
        bool AutomaticRender { get; set; }

        /// <summary>
        ///     Render the state of the world in this viewport, updating the texture inside the render target.
        /// </summary>
        void Render();

        /// <summary>
        ///     Converts a point in the viewport's screen to world coordinates.
        /// </summary>
        MapCoordinates LocalToWorld(Vector2 point);

        /// <summary>
        ///     Matrix equivalent of <see cref="LocalToWorld(Vector2)"/>.
        /// </summary>
        Matrix3x2 GetWorldToLocalMatrix();

        /// <summary>
        ///     Converts a point in world-space to the viewport's screen coordinates.
        /// </summary>
        Vector2 WorldToLocal(Vector2 point);

        /// <summary>
        ///     Draw below screen-space overlays for this viewport in UI space.
        /// </summary>
        /// <param name="handle">The drawing handle to draw with.</param>
        /// <param name="control">The control rendering.</param>
        /// <param name="viewportBounds">
        ///     Absolute screen-space bounds to draw the control at.
        ///     Not relative to the current transform of <see cref="handle"/>.
        /// </param>
        public void RenderScreenOverlaysBelow(
            IRenderHandle handle,
            IViewportControl control,
            in UIBox2i viewportBounds);

        /// <summary>
        ///     Draw above screen-space overlays for this viewport in UI space.
        /// </summary>
        /// <param name="handle">The drawing handle to draw with.</param>
        /// <param name="control">The control rendering.</param>
        /// <param name="viewportBounds">
        ///     Absolute screen-space bounds to draw the control at.
        ///     Not relative to the current transform of <see cref="handle"/>.
        /// </param>
        public void RenderScreenOverlaysAbove(
            IRenderHandle handle,
            IViewportControl control,
            in UIBox2i viewportBounds);
    }

    public struct ClearCachedViewportResourcesEvent
    {
        /// <summary>
        /// The <see cref="IClydeViewport.Id"/> of the viewport.
        /// </summary>
        public readonly long ViewportId;

        /// <summary>
        /// The viewport itself. This is not available if the viewport was disposed.
        /// </summary>
        public readonly IClydeViewport? Viewport;

        internal ClearCachedViewportResourcesEvent(long viewportId, IClydeViewport? viewport)
        {
            ViewportId = viewportId;
            Viewport = viewport;
        }
    }
}
