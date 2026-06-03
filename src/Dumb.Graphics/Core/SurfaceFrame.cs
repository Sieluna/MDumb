using Sia;
using Dumb.Graphics.Resources;

namespace Dumb.Graphics;

public struct SurfaceFrame : IDisposable
{
    private readonly GraphicsContext _ctx;
    private readonly nint _surface;
    private bool _disposed;

    public Entity View { get; }

    public SurfaceFrame(GraphicsContext ctx, nint surface, Entity view)
    {
        _ctx = ctx;
        _surface = surface;
        View = view;
        _disposed = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (View != null && View.Host != null)
        {
            _ctx.PresentSurface(_surface);
            Textures.ReleaseView(_ctx, View);
        }
    }
}
