#include "includes/core.slang"

[[vk::push_constant]]
PushConstants Push;

struct PushConstants
{
    uint lightsStart;
    uint lightsCount;
    uint resolution;
    uint lightmapGroup;

    float4 *pixels;
};

[shader("compute")]
[numthreads(8, 8, 1)]
void main(uint3 threadID: SV_DispatchThreadID)
{
    uint2 coords = threadID.xy;

    if (any(coords.xy >= Push.resolution))
    {
        return;
    }

    float4 *pixels = Push.pixels;

    uint compactedIndex = GetCompactedIndex((float2)coords, Push.lightmapGroup, false);
    uint linearIndex = coords.y * Push.resolution.x + coords.x;

    if (compactedIndex == uint::maxValue)
    {
        pixels[linearIndex] = 0;
        return;
    }

    float4 visibility = CompactedVisibility[compactedIndex];
    float3 positionWS = visibility.xyz;

    uint lightsStart = Push.lightsStart;
    uint lightsCount = Push.lightsCount;

    float4 occlusionChannels = float4(0.0, 0.0, 0.0, 0.0);

    for (uint lightIndex = lightsStart; lightIndex < lightsCount; lightIndex++)
    {
        Light light = Lights[lightIndex];

        uint channel = light.OcclusionChannel();
        if (channel > 3)
        {
            continue;
        }

        if (ShadowMaskVisibility(light, positionWS))
        {
            occlusionChannels[channel] = 1.0;
        }
    }

    pixels[linearIndex] = occlusionChannels;
}
