using System.Numerics;
using System.Runtime.CompilerServices;
using Sia;

namespace Dumb.Engine.Cameras;

public partial record struct Camera(
    [Sia] Vector3 Position,
    [Sia] Quaternion Rotation,
    [Sia] float Fov = MathF.PI / 3f,
    [Sia] float NearPlane = 0.1f,
    [Sia] float FarPlane = 1000f,
    [Sia] float AspectRatio = 16f / 9f)
{
    public Camera() : this(Vector3.Zero, Quaternion.Identity) { }

    public readonly Vector3 Forward => Vector3.Transform(Vector3.UnitZ, Rotation);
    public readonly Vector3 Right => Vector3.Transform(Vector3.UnitX, Rotation);
    public readonly Vector3 Up => Vector3.Transform(Vector3.UnitY, Rotation);

    public readonly Matrix4x4 ViewMatrix
    {
        get
        {
            var m = Matrix4x4.CreateLookAtLeftHanded(Position, Position + Forward, Up);
            // C# Vector3.Cross is right-hand rule; left-handed convention
            // requires negating the right vector to avoid X-axis mirroring.
            m.M11 = -m.M11; m.M21 = -m.M21; m.M31 = -m.M31; m.M41 = -m.M41;
            return m;
        }
    }

    public readonly Matrix4x4 ProjectionMatrix =>
        Matrix4x4.CreatePerspectiveFieldOfViewLeftHanded(Fov, AspectRatio, NearPlane, FarPlane);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetYawPitch(float yaw, float pitch)
    {
        Rotation = Quaternion.CreateFromYawPitchRoll(yaw, pitch, 0);
    }

    public static Camera CreateFreeLook(Vector3 target, float distance, float yaw, float pitch)
    {
        var camera = new Camera { Fov = MathF.PI / 3f, NearPlane = 0.1f, FarPlane = 1000f };
        camera.SetYawPitch(yaw, pitch);
        camera.Position = target - camera.Forward * distance;
        return camera;
    }

    public static Camera CreateFirstPerson(Vector3 position, float yaw, float pitch)
    {
        var camera = new Camera { Position = position, Fov = MathF.PI / 3f, NearPlane = 0.1f, FarPlane = 1000f };
        camera.SetYawPitch(yaw, pitch);
        return camera;
    }

    public static Camera CreateThirdPerson(Vector3 target, Vector3 offset)
    {
        var camera = new Camera
        {
            Fov = MathF.PI / 3f, NearPlane = 0.1f, FarPlane = 1000f,
            Position = target + offset,
            Rotation = Quaternion.CreateFromYawPitchRoll(
                MathF.Atan2(offset.X, offset.Z), -MathF.Asin(offset.Y / offset.Length()), 0)
        };
        return camera;
    }
}
