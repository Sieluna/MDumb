using System.Numerics;
using System.Runtime.CompilerServices;

namespace Dumb.Engine.Transform;

public struct Affine3D : IEquatable<Affine3D>
{
    public float M11, M12, M13, M14;
    public float M21, M22, M23, M24;
    public float M31, M32, M33, M34;

    public static readonly Affine3D Identity = new()
    {
        M11 = 1, M22 = 1, M33 = 1
    };

    public readonly Vector3 Translation => new(M14, M24, M34);

    public static Affine3D FromMatrix4x4(Matrix4x4 m)
        => new()
        {
            M11 = m.M11, M12 = m.M12, M13 = m.M13, M14 = m.M41,
            M21 = m.M21, M22 = m.M22, M23 = m.M23, M24 = m.M42,
            M31 = m.M31, M32 = m.M32, M33 = m.M33, M34 = m.M43
        };

    public readonly Matrix4x4 ToMatrix4x4()
        => new(
            M11, M12, M13, 0,
            M21, M22, M23, 0,
            M31, M32, M33, 0,
            M14, M24, M34, 1
        );

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Affine3D operator *(Affine3D a, Affine3D b)
    {
        return new Affine3D
        {
            M11 = a.M11 * b.M11 + a.M12 * b.M21 + a.M13 * b.M31,
            M12 = a.M11 * b.M12 + a.M12 * b.M22 + a.M13 * b.M32,
            M13 = a.M11 * b.M13 + a.M12 * b.M23 + a.M13 * b.M33,
            M14 = a.M14 * b.M11 + a.M24 * b.M21 + a.M34 * b.M31 + b.M14,

            M21 = a.M21 * b.M11 + a.M22 * b.M21 + a.M23 * b.M31,
            M22 = a.M21 * b.M12 + a.M22 * b.M22 + a.M23 * b.M32,
            M23 = a.M21 * b.M13 + a.M22 * b.M23 + a.M23 * b.M33,
            M24 = a.M14 * b.M12 + a.M24 * b.M22 + a.M34 * b.M32 + b.M24,

            M31 = a.M31 * b.M11 + a.M32 * b.M21 + a.M33 * b.M31,
            M32 = a.M31 * b.M12 + a.M32 * b.M22 + a.M33 * b.M32,
            M33 = a.M31 * b.M13 + a.M32 * b.M23 + a.M33 * b.M33,
            M34 = a.M14 * b.M13 + a.M24 * b.M23 + a.M34 * b.M33 + b.M34
        };
    }

    public readonly Affine3D Inverse()
    {
        // Invert 3×3 via cofactors
        var (r11, r12, r13, r21, r22, r23, r31, r32, r33) = Invert3x3();

        // -t · R⁻¹ (row-vector: subtract translation then un-rotate)
        var nx = -(M14 * r11 + M24 * r21 + M34 * r31);
        var ny = -(M14 * r12 + M24 * r22 + M34 * r32);
        var nz = -(M14 * r13 + M24 * r23 + M34 * r33);

        return new Affine3D
        {
            M11 = r11, M12 = r12, M13 = r13, M14 = nx,
            M21 = r21, M22 = r22, M23 = r23, M24 = ny,
            M31 = r31, M32 = r32, M33 = r33, M34 = nz
        };
    }

    private readonly (float r11, float r12, float r13, float r21, float r22, float r23, float r31, float r32, float r33) Invert3x3()
    {
        // Cofactor expansion: inv(A) = adj(A) / det(A)
        // A = | M11 M12 M13 |
        //     | M21 M22 M23 |
        //     | M31 M32 M33 |

        // Cofactors (transpose of adjugate)
        var c11 = M22 * M33 - M23 * M32;
        var c12 = M13 * M32 - M12 * M33;
        var c13 = M12 * M23 - M13 * M22;

        var c21 = M23 * M31 - M21 * M33;
        var c22 = M11 * M33 - M13 * M31;
        var c23 = M13 * M21 - M11 * M23;

        var c31 = M21 * M32 - M22 * M31;
        var c32 = M12 * M31 - M11 * M32;
        var c33 = M11 * M22 - M12 * M21;

        // det = M11*c11 + M12*c21 + M13*c31
        var det = M11 * c11 + M12 * c21 + M13 * c31;

        if (Math.Abs(det) < 1e-12f)
            throw new InvalidOperationException("Affine3D matrix is singular.");

        var invDet = 1f / det;
        return (
            c11 * invDet, c12 * invDet, c13 * invDet,
            c21 * invDet, c22 * invDet, c23 * invDet,
            c31 * invDet, c32 * invDet, c33 * invDet
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly Vector3 TransformPoint(Vector3 v)
        => new(
            v.X * M11 + v.Y * M21 + v.Z * M31 + M14,
            v.X * M12 + v.Y * M22 + v.Z * M32 + M24,
            v.X * M13 + v.Y * M23 + v.Z * M33 + M34
        );

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly Vector3 TransformDirection(Vector3 v)
        => new(
            v.X * M11 + v.Y * M21 + v.Z * M31,
            v.X * M12 + v.Y * M22 + v.Z * M32,
            v.X * M13 + v.Y * M23 + v.Z * M33
        );

    public static Affine3D FromTranslation(Vector3 t)
        => new() { M11 = 1, M22 = 1, M33 = 1, M14 = t.X, M24 = t.Y, M34 = t.Z };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Affine3D FromTRS(Vector3 t, Quaternion r, Vector3 s)
    {
        // Quaternion → rotation matrix (row-vector form)
        float xx = r.X * r.X, yy = r.Y * r.Y, zz = r.Z * r.Z;
        float xy = r.X * r.Y, xz = r.X * r.Z, yz = r.Y * r.Z;
        float wx = r.W * r.X, wy = r.W * r.Y, wz = r.W * r.Z;

        return new Affine3D
        {
            M11 = (1f - 2f * (yy + zz)) * s.X,
            M12 = (2f * (xy + wz)) * s.X,
            M13 = (2f * (xz - wy)) * s.X,
            M14 = t.X,

            M21 = (2f * (xy - wz)) * s.Y,
            M22 = (1f - 2f * (xx + zz)) * s.Y,
            M23 = (2f * (yz + wx)) * s.Y,
            M24 = t.Y,

            M31 = (2f * (xz + wy)) * s.Z,
            M32 = (2f * (yz - wx)) * s.Z,
            M33 = (1f - 2f * (xx + yy)) * s.Z,
            M34 = t.Z
        };
    }

    public static Affine3D FromRotation(Quaternion q)
    {
        float xx = q.X * q.X, yy = q.Y * q.Y, zz = q.Z * q.Z;
        float xy = q.X * q.Y, xz = q.X * q.Z, yz = q.Y * q.Z;
        float wx = q.W * q.X, wy = q.W * q.Y, wz = q.W * q.Z;

        return new Affine3D
        {
            M11 = 1f - 2f * (yy + zz),
            M12 = 2f * (xy + wz),
            M13 = 2f * (xz - wy),

            M21 = 2f * (xy - wz),
            M22 = 1f - 2f * (xx + zz),
            M23 = 2f * (yz + wx),

            M31 = 2f * (xz + wy),
            M32 = 2f * (yz - wx),
            M33 = 1f - 2f * (xx + yy)
        };
    }

    public static Affine3D FromScale(Vector3 s)
        => new() { M11 = s.X, M22 = s.Y, M33 = s.Z };

    public static Affine3D FromSRT(Vector3 scale, Quaternion rotation, Vector3 translation)
        => FromTRS(translation, rotation, scale);

    public static Affine3D FromTransform(Vector3 position, Quaternion rotation, Vector3 scale)
        => FromTRS(position, rotation, scale);

    public static Affine3D LookAt(Vector3 eye, Vector3 target, Vector3 up)
    {
        var m = Matrix4x4.CreateLookAtLeftHanded(eye, target, up);
        m.M11 = -m.M11; m.M21 = -m.M21; m.M31 = -m.M31; m.M41 = -m.M41;
        return FromMatrix4x4(m);
    }

    public bool Equals(Affine3D other)
        => M11 == other.M11 && M12 == other.M12 && M13 == other.M13 && M14 == other.M14
        && M21 == other.M21 && M22 == other.M22 && M23 == other.M23 && M24 == other.M24
        && M31 == other.M31 && M32 == other.M32 && M33 == other.M33 && M34 == other.M34;

    public override bool Equals(object? obj) => obj is Affine3D other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(M11); hash.Add(M12); hash.Add(M13); hash.Add(M14);
        hash.Add(M21); hash.Add(M22); hash.Add(M23); hash.Add(M24);
        hash.Add(M31); hash.Add(M32); hash.Add(M33); hash.Add(M34);
        return hash.ToHashCode();
    }

    public static bool operator ==(Affine3D left, Affine3D right) => left.Equals(right);

    public static bool operator !=(Affine3D left, Affine3D right) => !left.Equals(right);

    public override string ToString()
        => $"[{M11:F3} {M12:F3} {M13:F3} | {M14:F3}]\n"
         + $"[{M21:F3} {M22:F3} {M23:F3} | {M24:F3}]\n"
         + $"[{M31:F3} {M32:F3} {M33:F3} | {M34:F3}]";
}
