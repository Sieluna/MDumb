using Sia;
using Silk.NET.WebGPU;

namespace Dumb.Graphics.Resources;

public readonly record struct RenderTexture(
    Entity Texture,
    Entity View,
    uint Width,
    uint Height,
    TextureFormat Format);

public static unsafe class Textures
{
    public static RenderTexture RenderTarget(
        GraphicsContext ctx,
        uint width,
        uint height,
        TextureFormat format,
        TextureUsage usage = TextureUsage.RenderAttachment | TextureUsage.TextureBinding)
    {
        var texture = Create2D(ctx, width, height, format, usage);
        var view = CreateView2D(ctx, texture, format);
        return new RenderTexture(texture, view, width, height, format);
    }

    public static Entity Create2D(
        GraphicsContext ctx,
        uint width,
        uint height,
        TextureFormat format,
        TextureUsage usage,
        uint mipLevelCount = 1,
        uint sampleCount = 1)
    {
        TextureDescriptor descriptor = new()
        {
            Usage = usage,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D(width, height, 1),
            Format = format,
            MipLevelCount = mipLevelCount,
            SampleCount = sampleCount,
            ViewFormatCount = 0,
            ViewFormats = null,
            Label = null
        };
        return Create(ctx, descriptor);
    }

    public static Entity Create(GraphicsContext ctx, TextureDescriptor descriptor)
    {
        var native = ctx.Device.CreateTexture(ctx.NativeDevice, &descriptor);
        return ctx._textures.Create(HList.From(new TextureData
        {
            NativePtr = native,
            Size = descriptor.Size,
            Format = descriptor.Format,
            Usage = descriptor.Usage,
            MipLevelCount = descriptor.MipLevelCount,
            SampleCount = descriptor.SampleCount,
            RefCount = 1
        }));
    }

    public static Entity CreateView2D(
        GraphicsContext ctx,
        Entity texture,
        TextureFormat format,
        TextureAspect aspect = TextureAspect.All)
    {
        TextureViewDescriptor descriptor = new()
        {
            Format = format,
            Dimension = TextureViewDimension.Dimension2D,
            BaseMipLevel = 0,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = 1,
            Aspect = aspect,
            Label = null
        };
        return CreateView(ctx, texture, descriptor);
    }

    public static Entity CreateViewForNativeTexture(
        GraphicsContext ctx,
        nint texture,
        TextureFormat format,
        TextureAspect aspect = TextureAspect.All)
    {
        TextureViewDescriptor descriptor = new()
        {
            Format = format,
            Dimension = TextureViewDimension.Dimension2D,
            BaseMipLevel = 0,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = 1,
            Aspect = aspect,
            Label = null
        };

        var native = ctx.Device.CreateTextureView(texture, &descriptor);
        return ctx._textureViews.Create(HList.From(new TextureViewData
        {
            NativePtr = native,
            Texture = null!,
            RefCount = 1
        }));
    }

    public static Entity CreateDepthView(GraphicsContext ctx, Entity texture, TextureFormat format = TextureFormat.Depth32float)
    {
        TextureViewDescriptor descriptor = new()
        {
            Format = format,
            Dimension = TextureViewDimension.Dimension2D,
            BaseMipLevel = 0,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = 1,
            Aspect = TextureAspect.DepthOnly,
            Label = null
        };
        return CreateView(ctx, texture, descriptor);
    }

    public static Entity WrapNativeTextureView(GraphicsContext ctx, nint textureView)
    {
        return ctx._textureViews.Create(HList.From(new TextureViewData
        {
            NativePtr = textureView,
            Texture = null!,
            RefCount = 1
        }));
    }

    public static Entity CreateView(GraphicsContext ctx, Entity texture, TextureViewDescriptor descriptor)
    {
        ref var tex = ref texture.Get<TextureData>();
        var native = ctx.Device.CreateTextureView(tex.NativePtr, &descriptor);
        return ctx._textureViews.Create(HList.From(new TextureViewData
        {
            NativePtr = native,
            Texture = texture,
            RefCount = 1
        }));
    }

    public static void Release(GraphicsContext ctx, Entity texture)
    {
        ref var tex = ref texture.Get<TextureData>();
        if (Interlocked.Decrement(ref tex.RefCount) == 0)
        {
            ctx.Device.ReleaseTexture(tex.NativePtr);
            texture.Destroy();
        }
    }

    public static void ReleaseView(GraphicsContext ctx, Entity view)
    {
        ref var v = ref view.Get<TextureViewData>();
        if (Interlocked.Decrement(ref v.RefCount) == 0)
        {
            ctx.Device.ReleaseTextureView(v.NativePtr);
            view.Destroy();
        }
    }
}
