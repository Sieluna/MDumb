using Silk.NET.WebGPU;

namespace Dumb.Graphics;

public struct GraphicsSurface : IDisposable
{
    public nint Handle;
    public GraphicsContext? Context;

    public TextureFormat Format;
    public uint Width;
    public uint Height;

    public readonly bool IsValid => Handle != 0;

    public void Dispose()
    {
        if (Context != null && Handle != 0)
            Context.DestroySurface(ref this);
    }
}
