using System.Threading;
using Sia;
using Silk.NET.WebGPU;

namespace Dumb.Graphics;

public static unsafe class Samplers
{
    public static Entity LinearClamp(GraphicsContext ctx) =>
        Create(ctx, AddressMode.ClampToEdge, AddressMode.ClampToEdge, AddressMode.ClampToEdge,
            FilterMode.Linear, FilterMode.Linear, MipmapFilterMode.Linear);

    public static Entity ShadowComparison(GraphicsContext ctx)
    {
        SamplerDescriptor descriptor = new()
        {
            AddressModeU = AddressMode.ClampToEdge,
            AddressModeV = AddressMode.ClampToEdge,
            AddressModeW = AddressMode.ClampToEdge,
            MagFilter = FilterMode.Linear,
            MinFilter = FilterMode.Linear,
            MipmapFilter = MipmapFilterMode.Linear,
            LodMinClamp = 0,
            LodMaxClamp = 1,
            Compare = CompareFunction.LessEqual,
            MaxAnisotropy = 1,
            Label = null
        };
        return Create(ctx, descriptor);
    }

    public static Entity Create(
        GraphicsContext ctx,
        AddressMode addressModeU,
        AddressMode addressModeV,
        AddressMode addressModeW,
        FilterMode mag,
        FilterMode min,
        MipmapFilterMode mip)
    {
        SamplerDescriptor descriptor = new()
        {
            AddressModeU = addressModeU,
            AddressModeV = addressModeV,
            AddressModeW = addressModeW,
            MagFilter = mag,
            MinFilter = min,
            MipmapFilter = mip,
            LodMinClamp = 0,
            LodMaxClamp = 1,
            Compare = CompareFunction.Undefined,
            MaxAnisotropy = 1,
            Label = null
        };
        return Create(ctx, descriptor);
    }

    public static Entity Create(GraphicsContext ctx, SamplerDescriptor descriptor)
    {
        var native = ctx.Device.CreateSampler(ctx.NativeDevice, &descriptor);
        return ctx._samplers.Create(HList.From(new SamplerData
        {
            NativePtr = native,
            RefCount = 1
        }));
    }

    public static void Release(GraphicsContext ctx, Entity sampler)
    {
        ref var s = ref sampler.Get<SamplerData>();
        if (Interlocked.Decrement(ref s.RefCount) == 0)
        {
            ctx.Device.ReleaseSampler(s.NativePtr);
            sampler.Destroy();
        }
    }
}
