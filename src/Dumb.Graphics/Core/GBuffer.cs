using Sia;
using Silk.NET.WebGPU;
using Dumb.Graphics.Resources;

namespace Dumb.Graphics;

public sealed class GBuffer : IDisposable
{
    private readonly GraphicsContext _ctx;
    private RenderTexture _rt0; // albedo RGBA8Unorm
    private RenderTexture _rt1; // normal_roughness RGBA16Float
    private RenderTexture _rt2; // pbr RGBA8Unorm
    private Entity _depthTexture = null!;
    private Entity _depthView = null!;

    public uint Width { get; private set; }
    public uint Height { get; private set; }

    public Entity RT0View => _rt0.View;
    public Entity RT1View => _rt1.View;
    public Entity RT2View => _rt2.View;
    public Entity DepthView => _depthView;
    public Entity DepthTexture => _depthTexture;

    public Entity RT0Texture => _rt0.Texture;
    public Entity RT1Texture => _rt1.Texture;
    public Entity RT2Texture => _rt2.Texture;

    public ReadOnlySpan<RenderPassColorAttachment> ColorAttachments(
        GraphicsContext ctx)
    {
        var attachments = new RenderPassColorAttachment[3];
        attachments[0] = Commands.ColorAttachment(ctx, _rt0.View, new Color { R = 0, G = 0, B = 0, A = 1 });
        attachments[1] = Commands.ColorAttachment(ctx, _rt1.View, new Color { R = 0.5, G = 0.5, B = 0, A = 0 },
            loadOp: LoadOp.Clear);
        attachments[2] = Commands.ColorAttachment(ctx, _rt2.View, new Color { R = 0, G = 1, B = 0, A = 1 },
            loadOp: LoadOp.Clear);
        return attachments;
    }

    public RenderPassDepthStencilAttachment DepthAttachment(GraphicsContext ctx)
        => Commands.DepthStencilAttachment(ctx, _depthView);

    public GBuffer(GraphicsContext ctx, uint width, uint height)
    {
        _ctx = ctx;
        Width = width;
        Height = height;
        CreateTextures();
    }

    private void CreateTextures()
    {
        _rt0 = Textures.RenderTarget(_ctx, Width, Height, TextureFormat.Rgba8Unorm,
            TextureUsage.RenderAttachment | TextureUsage.TextureBinding | TextureUsage.CopySrc);
        _rt1 = Textures.RenderTarget(_ctx, Width, Height, TextureFormat.Rgba16float,
            TextureUsage.RenderAttachment | TextureUsage.TextureBinding | TextureUsage.CopySrc);
        _rt2 = Textures.RenderTarget(_ctx, Width, Height, TextureFormat.Rgba8Unorm,
            TextureUsage.RenderAttachment | TextureUsage.TextureBinding | TextureUsage.CopySrc);

        _depthTexture = Textures.Create2D(_ctx, Width, Height,
            TextureFormat.Depth32float,
            TextureUsage.RenderAttachment | TextureUsage.TextureBinding | TextureUsage.CopySrc);
        _depthView = Textures.CreateDepthView(_ctx, _depthTexture);
    }

    public void Resize(uint width, uint height)
    {
        if (width == Width && height == Height)
            return;

        ReleaseTextures();
        Width = width;
        Height = height;
        CreateTextures();
    }

    private void ReleaseTextures()
    {
        if (_depthView.Host != null) Textures.ReleaseView(_ctx, _depthView);
        if (_rt0.View.Host != null) Textures.ReleaseView(_ctx, _rt0.View);
        if (_rt1.View.Host != null) Textures.ReleaseView(_ctx, _rt1.View);
        if (_rt2.View.Host != null) Textures.ReleaseView(_ctx, _rt2.View);
        if (_rt0.Texture.Host != null) Textures.Release(_ctx, _rt0.Texture);
        if (_rt1.Texture.Host != null) Textures.Release(_ctx, _rt1.Texture);
        if (_rt2.Texture.Host != null) Textures.Release(_ctx, _rt2.Texture);
        if (_depthTexture.Host != null) Textures.Release(_ctx, _depthTexture);
    }

    public void Dispose()
    {
        ReleaseTextures();
    }
}
