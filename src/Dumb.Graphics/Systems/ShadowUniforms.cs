using System.Numerics;
using System.Runtime.InteropServices;

namespace Dumb.Graphics;

[StructLayout(LayoutKind.Sequential)]
public struct ShadowUniforms
{
    public Matrix4x4 LightVP;
    public Vector2 TexelSize;
    public float DepthBias;
    public float NormalBias;

    public static readonly ShadowUniforms Default = new()
    {
        LightVP = Matrix4x4.Identity,
        TexelSize = Vector2.Zero, // zero disables shadow sampling
        DepthBias = 0.002f,
        NormalBias = 0.02f
    };
}
