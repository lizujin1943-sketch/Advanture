#ifndef HOOK_ISLAND_SKY_INCLUDED
#define HOOK_ISLAND_SKY_INCLUDED

// One directional sky for the skybox and water. In particular, an orthographic
// water view must not stretch a perspective-rendered sky across the sea.
float4 _SkyHorizonColor, _SkyMiddleColor, _SkyZenithColor, _HorizonGlowColor;
float4 _CloudColor, _CloudShadowColor;
float _CloudCoverage, _CloudSoftness, _CloudScale, _CloudSpeed, _SunGlow;

float IslandSkyHash(float2 p)
{
    float3 q = frac(float3(p.xyx) * .1031);
    q += dot(q, q.yzx + 33.33);
    return frac((q.x + q.y) * q.z);
}

float IslandCloudUnion(float a, float b)
{
    float h = max(.16 - abs(a - b), 0) / .16;
    return min(a, b) - h * h * .04;
}

float4 IslandClouds(float3 direction, float time, float blur, float coverage)
{
    // Irregular groups on a sky dome retain their rounded volume at the horizon.
    // Each has its own height and size; there are no latitude bands or longitude seam.
    float windSine, windCosine;
    // The sky and its reflection share one slow drift: about 1.44 degrees/minute
    // at the default speed. Rotate continuously without wrapping or respawning clouds.
    sincos(time * _CloudSpeed * .0012, windSine, windCosine);
    direction.xz = float2(direction.x * windCosine + direction.z * windSine,
                         -direction.x * windSine + direction.z * windCosine);
    float footprint = max(length(ddx(direction)), length(ddy(direction)));
    float alpha = 0;
    float3 tint = 0;
    [unroll] for (int n = 0; n < 24; n++)
    {
        float seed = IslandSkyHash(float2(n + 2, 7));
        float height = .13 + .76 * frac(n * .618034 + .19);
        float azimuth = n * 2.399963;
        float sine, cosine;
        sincos(azimuth, sine, cosine);
        float radius = sqrt(1 - height * height);
        float3 centre = float3(sine * radius, height, cosine * radius);
        float facing = dot(direction, centre);
        [branch] if (facing > .75 && seed < coverage)
        {
            float3 right = float3(cosine, 0, -sine);
            float3 up = cross(centre, right);
            float2 size = float2(lerp(.1, .19, seed), lerp(.065, .12, IslandSkyHash(float2(n, 19))));
            size *= .6 / max(_CloudScale, .2);
            float2 q = float2(dot(direction, right), dot(direction, up)) / facing / size;
            // All four sides are curved, including the underside seen in reflection.
            float d = length((q - float2(-.55, -.07)) / float2(.57, .64)) - 1;
            d = IslandCloudUnion(d, length((q - float2(.05, .25)) / float2(.75, .75)) - 1);
            d = IslandCloudUnion(d, length((q - float2(.65, -.03)) / float2(.52, .56)) - 1);
            d = IslandCloudUnion(d, length((q - float2(-.04, -.42)) / float2(.73, .42)) - 1);
            // Water uses an angular filter: soften reflected clouds and conserve their
            // brightness instead of producing high-contrast flecks at every small wave.
            float edge = max(_CloudSoftness, min(2, (footprint + blur) / size.y));
            float cloud = (1 - smoothstep(-edge, edge, d)) * saturate(coverage * 10);
            cloud *= size.x * size.y / (size.x * size.y + blur * blur);
            if (cloud > alpha)
            {
                alpha = cloud;
                float shade = .06 + .17 * (1 - smoothstep(-.6, .65, q.y));
                tint = lerp(_CloudColor.rgb, _CloudShadowColor.rgb, shade);
            }
        }
    }
    alpha *= smoothstep(.035, .12, direction.y);
    tint = lerp(_SkyHorizonColor.rgb, tint, smoothstep(.035, .24, direction.y));
    return float4(tint * alpha, alpha);
}

float3 IslandSampleSky(float3 direction, float time, float blur,
                      float3 cloudDirection, float coverage, float cloudStrength)
{
    direction = normalize(direction);
    float height = saturate(direction.y);
    float3 sky = lerp(_SkyHorizonColor.rgb, _SkyMiddleColor.rgb, smoothstep(0, .3, height));
    sky = lerp(sky, _SkyZenithColor.rgb, smoothstep(.2, 1, height));
    float warmBand = smoothstep(0, .055, height) * (1 - smoothstep(.07, .23, height));
    sky = lerp(sky, _HorizonGlowColor.rgb, warmBand * .6);
    float alignment = saturate(dot(direction, normalize(_WorldSpaceLightPos0.xyz)));
    sky += float3(1, .87, .61) * (pow(alignment, 24) * .045 + pow(alignment, 280) * .16) * _SunGlow;
    float4 cloud = IslandClouds(normalize(cloudDirection), time, blur, coverage);
    // Boost the existing soft mask without growing it or over-compositing its core.
    cloud *= min(cloudStrength, 1 / max(cloud.a, .001));
    return sky * (1 - cloud.a) + cloud.rgb;
}

float3 IslandSampleSky(float3 direction, float time, float blur)
{
    return IslandSampleSky(direction, time, blur, direction, _CloudCoverage, 1);
}

#endif
