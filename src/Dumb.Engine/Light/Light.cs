using System.Numerics;
using Sia;

namespace Dumb.Engine.Lighting;

public enum LightType
{
    Directional,
    Point,
    Spot
}

public partial record struct Light(
    [Sia] LightType Type = LightType.Directional,
    [Sia] Vector3 Color = default,
    [Sia] float Intensity = 1f,
    [Sia] Vector3 Direction = default,
    [Sia] float Range = 10f,
    [Sia] float InnerConeAngle = 0f,
    [Sia] float OuterConeAngle = 0f,
    [Sia] bool CastsShadows = false)
{
    public Light() : this(LightType.Directional, Vector3.One, 1f, Vector3.UnitZ, 10f, 0f, 0f, false) { }

    public static Light DirectionalLight(Vector3 color, float intensity, Vector3 direction)
        => new()
        {
            Type = LightType.Directional,
            Color = color,
            Intensity = intensity,
            Direction = Vector3.Normalize(direction),
            CastsShadows = true
        };

    public static Light PointLight(Vector3 color, float intensity, float range)
        => new()
        {
            Type = LightType.Point,
            Color = color,
            Intensity = intensity,
            Range = range
        };

    public static Light SpotLight(Vector3 color, float intensity, float range,
        float innerAngle, float outerAngle, Vector3 direction)
        => new()
        {
            Type = LightType.Spot,
            Color = color,
            Intensity = intensity,
            Range = range,
            InnerConeAngle = innerAngle,
            OuterConeAngle = outerAngle,
            Direction = Vector3.Normalize(direction)
        };
}
