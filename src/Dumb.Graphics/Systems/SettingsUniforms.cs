using System.Numerics;
using System.Runtime.InteropServices;

namespace Dumb.Graphics;

[StructLayout(LayoutKind.Sequential)]
public struct SettingsUniforms
{
    public float ShadowsMaxDistance;
    public uint CascadeCount;
    public float DepthBias;
    public float NormalBias;

    public float BloomIntensity;
    public float BloomThreshold;
    public float Exposure;

    private readonly float _align0;

    public Vector3 AmbientColor;
    public float _pad;

    public static SettingsUniforms Default => new()
    {
        ShadowsMaxDistance = 500f,
        CascadeCount = 4,
        DepthBias = 0.001f,
        NormalBias = 0.5f,
        BloomIntensity = 0f,
        BloomThreshold = 1f,
        Exposure = 1f,
        AmbientColor = new Vector3(0.03f, 0.03f, 0.03f),
    };
}
