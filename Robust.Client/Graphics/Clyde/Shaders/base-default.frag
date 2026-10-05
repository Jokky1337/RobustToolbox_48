// UV coordinates in texture-space. I.e., (0,0) is the corner of the texture currently being used to draw.
// When drawing a sprite from a texture atlas, (0,0) is the corner of the atlas, not the specific sprite being drawn.
varying highp vec2 UV;

// UV coordinates in quad-space. I.e., when drawing a sprite from a texture atlas (0,0) is the corner of the sprite
// currently being drawn.
varying highp vec2 UV2;

// TBH I'm not sure what this is for. I think it is scree  UV coordiantes, i.e., FRAGCOORD.xy * SCREEN_PIXEL_SIZE ?
// TODO CLYDE Is this still needed?
varying highp vec2 Pos;

// Vertex colour modulation. Note that negative values imply that the LIGHTMAP should be ignored. This is used to avoid
// having to set the texture to a white/blank texture for sprites that have no light shading applied.
varying highp vec4 VtxModulate;

// The current light map. Unless disabled, this is automatically sampled to create the LIGHT vector, which is then used
// to modulate the output colour.
// TODO CLYDE consistent shader variable naming
uniform sampler2D lightMap;

// Structura: half a step of an 8-bit sRGB target, added in sRGB space so it is the same step at every level. Dark
// light gradients otherwise band into rings, because neighbouring dark sRGB codes differ by 5-8% of brightness.
// Interleaved gradient noise: a fixed screen pattern, no flicker.
highp vec3 zStructuraDither8(highp vec3 linearColor, highp vec2 fragCoord)
{
    highp float n = fract(52.9829189 * fract(dot(fragCoord, vec2(0.06711056, 0.00583715))));
    highp vec3 s = zToSrgb(vec4(clamp(linearColor, 0.0, 1.0), 1.0)).rgb;
    // Darkness stays black: below a couple of steps there is no banding to hide, and noise would only turn the
    // unseen dark into grain.
    highp float amount = smoothstep(0.5 / 255.0, 2.0 / 255.0, max(s.r, max(s.g, s.b)));
    s = clamp(s + (n - 0.5) * amount / 255.0, 0.0, 1.0);
    return zFromSrgb(vec4(s, 1.0)).rgb;
}

// Structura: lit colour past the knee rolls off toward 1 instead of clipping per channel (which turned a warm lamp's
// pool into a flat yellow-white plate). Scaled by the brightest channel, so the hue survives; very bright light
// whitens a little, as film and eyes do. Below the knee the colour is untouched.
highp vec3 zStructuraShoulder(highp vec3 c, highp float knee)
{
    highp float m = max(c.r, max(c.g, c.b));
    if (m <= knee)
        return c;
    highp float range = 1.0 - knee;
    highp float mapped = knee + range * (1.0 - exp(-(m - knee) / range));
    highp vec3 scaled = c * (mapped / m);
    return mix(scaled, vec3(mapped), smoothstep(1.0, 4.0, m) * 0.6);
}

// [SHADER_HEADER_CODE]

void main()
{
    highp vec4 FRAGCOORD = gl_FragCoord;

    // The output colour. This should get set by the shader code block.
    // This will get modified by the LIGHT and MODULATE vectors.
    lowp vec4 COLOR;

    // The light colour, usually sampled from the LIGHTMAP
    lowp vec4 LIGHT;

    // Colour modulation vector.
    highp vec4 MODULATE;

    // Sample the texture outside of the branch / with uniform control flow.
    LIGHT = texture2D(lightMap, Pos);

    if (VtxModulate.x < 0.0)
    {
        // Negative VtxModulate implies unshaded/no lighting.
        MODULATE = -1.0 - VtxModulate;
        LIGHT = vec4(1.0);
    }
    else
    {
        MODULATE = VtxModulate;
    }

    // TODO CLYDE consistent shader variable naming
    // Requires breaking changes.
    lowp vec3 lightSample = LIGHT.xyz;

    // [SHADER_CODE]

    LIGHT.xyz = lightSample;

    highp vec4 zStructuraOut = COLOR * MODULATE * LIGHT;
    // Structura: lit world drawing only (sprites, tiles, lit content shaders); UI and unshaded draws stay exact.
    if (VtxModulate.x >= 0.0)
    {
        if (STRUCTURA_TONEMAP_KNEE < 0.999)
            zStructuraOut.rgb = zStructuraShoulder(zStructuraOut.rgb, STRUCTURA_TONEMAP_KNEE);
        if (STRUCTURA_DITHER > 0.5)
            zStructuraOut.rgb = zStructuraDither8(zStructuraOut.rgb, FRAGCOORD.xy);
    }

    gl_FragColor = zAdjustResult(zStructuraOut);
}
