using System.Numerics;
using Sia;

namespace Dumb.Engine.Render;

[Flags]
public enum ShadowSettingsOverride : byte
{
    None = 0,
    MaxDistance = 1 << 0,
    CascadeCount = 1 << 1,
    DepthBias = 1 << 2,
    NormalBias = 1 << 3,
}

[Flags]
public enum PostProcessOverride : byte
{
    None = 0,
    BloomIntensity = 1 << 0,
    BloomThreshold = 1 << 1,
    Exposure = 1 << 2,
    AmbientColor = 1 << 3,
}

public partial record struct RenderVolume(
    [Sia] Vector3 BoundsMin = default,
    [Sia] Vector3 BoundsMax = default,
    [Sia] float Priority = 0f,
    [Sia] float BlendDistance = 0f,
    [Sia] float Weight = 1f,
    [Sia] bool IsGlobal = false,
    [Sia] ShadowSettingsOverride ShadowMask = ShadowSettingsOverride.MaxDistance
        | ShadowSettingsOverride.CascadeCount
        | ShadowSettingsOverride.DepthBias
        | ShadowSettingsOverride.NormalBias,
    [Sia] PostProcessOverride PostMask = PostProcessOverride.BloomIntensity
        | PostProcessOverride.BloomThreshold
        | PostProcessOverride.Exposure
        | PostProcessOverride.AmbientColor)
{
    public RenderVolume() : this(default, default, 0f, 0f, 1f, false,
        ShadowSettingsOverride.MaxDistance | ShadowSettingsOverride.CascadeCount
            | ShadowSettingsOverride.DepthBias | ShadowSettingsOverride.NormalBias,
        PostProcessOverride.BloomIntensity | PostProcessOverride.BloomThreshold
            | PostProcessOverride.Exposure | PostProcessOverride.AmbientColor) { }

    /// <summary>Create a volume that affects the entire scene.</summary>
    public static RenderVolume Global(float priority = 0f, float weight = 1f)
    {
        return new RenderVolume
        {
            BoundsMin = Vector3.Zero,
            BoundsMax = Vector3.Zero,
            Priority = priority,
            BlendDistance = 0f,
            Weight = Math.Clamp(weight, 0f, 1f),
            IsGlobal = true
        };
    }

    /// <summary>Create a local volume with a bounding box.</summary>
    public static RenderVolume Local(Vector3 min, Vector3 max,
        float priority = 0f, float blendDistance = 0f, float weight = 1f)
    {
        return new RenderVolume
        {
            BoundsMin = min,
            BoundsMax = max,
            Priority = priority,
            BlendDistance = blendDistance,
            Weight = Math.Clamp(weight, 0f, 1f),
            IsGlobal = false
        };
    }

    /// <summary>Squared distance from a point to the bounding box (0 if inside).</summary>
    public readonly float SqrDistance(Vector3 point)
    {
        var dx = Math.Max(0f, Math.Max(BoundsMin.X - point.X, point.X - BoundsMax.X));
        var dy = Math.Max(0f, Math.Max(BoundsMin.Y - point.Y, point.Y - BoundsMax.Y));
        var dz = Math.Max(0f, Math.Max(BoundsMin.Z - point.Z, point.Z - BoundsMax.Z));
        return dx * dx + dy * dy + dz * dz;
    }
}
