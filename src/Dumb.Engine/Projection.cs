using System.Numerics;

namespace Dumb.Engine;

public static class Projection
{
    /// <summary>
    /// WebGPU perspective projection.
    /// Maps view-space z ∈ [-far, -near] to NDC z ∈ [0, 1].
    /// </summary>
    public static Matrix4x4 PerspectiveGPU(float fovY, float aspect, float near, float far)
    {
        float h = 1f / MathF.Tan(fovY * 0.5f);
        float w = h / aspect;
        float invNF = 1f / (near - far); // negative

        var m = new Matrix4x4
        {
            M11 = w,                   // col[0].x
            M22 = h,                   // col[1].y
            M33 = far * invNF,         // col[2].z = f / (n - f)
            M34 = -1f,                 // col[2].w = -1 → clip.w = -view.z > 0
            M43 = near * far * invNF   // col[3].z = n·f / (n - f)
        };
        return m;
    }

    /// <summary>
    /// WebGPU orthographic projection.
    /// Maps view-space z ∈ [-far, -near] to NDC z ∈ [0, 1].
    /// </summary>
    public static Matrix4x4 OrthographicGPU(
        float left, float right, float bottom, float top,
        float near, float far)
    {
        float rl = 1f / (right - left);
        float tb = 1f / (top - bottom);
        float invNF = 1f / (near - far); // negative

        var m = Matrix4x4.Identity;
        m.M11 = 2f * rl;               // col[0].x = 2 / (r - l)
        m.M22 = 2f * tb;               // col[1].y = 2 / (t - b)
        m.M33 = invNF;                  // col[2].z = 1 / (n - f)
        m.M34 = near * invNF;           // col[3].z = n / (n - f)
        m.M14 = -(right + left) * rl;   // col[3].x = -(r + l) / (r - l)
        m.M24 = -(top + bottom) * tb;   // col[3].y = -(t + b) / (t - b)

        return m;
    }

    /// <summary>
    /// Symmetric WebGPU orthographic projection (frustum centered on view axis).
    /// Equivalent to OrthographicGPU(-w, w, -h, h, near, far) with the
    /// symmetry simplifications baked in.
    /// </summary>
    public static Matrix4x4 OrthographicGPUSymmetric(
        float halfWidth, float halfHeight, float near, float far)
    {
        float invNF = 1f / (near - far); // negative

        var m = Matrix4x4.Identity;
        m.M11 = 1f / halfWidth;     // col[0].x
        m.M22 = 1f / halfHeight;    // col[1].y
        m.M33 = invNF;              // col[2].z
        m.M34 = near * invNF;       // col[3].z

        return m;
    }
}
