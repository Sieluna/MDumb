using System.Runtime.InteropServices;
using Sia;
using Silk.NET.WebGPU;

namespace Dumb.Graphics.Resources;

public static unsafe class Buffers
{
    public static Entity Create(GraphicsContext ctx, ulong size, BufferUsage usage)
    {
        BufferDescriptor descriptor = new()
        {
            Usage = usage,
            Size = size,
            MappedAtCreation = false,
            Label = null
        };
        return Create(ctx, descriptor);
    }

    public static Entity Create(GraphicsContext ctx, BufferDescriptor descriptor)
    {
        var native = ctx.Device.CreateBuffer(ctx.NativeDevice, &descriptor);
        return ctx._buffers.Create(HList.From(new BufferData
        {
            NativePtr = native,
            Size = descriptor.Size,
            Usage = descriptor.Usage,
            RefCount = 1
        }));
    }

    public static void Write(GraphicsContext ctx, Entity buffer, ulong offset, ReadOnlySpan<byte> data)
    {
        ref var buf = ref buffer.Get<BufferData>();
        fixed (byte* ptr = data)
            ctx.Command.QueueWriteBuffer(ctx.NativeQueue, buf.NativePtr, offset, ptr, (nuint)data.Length);
    }

    public static void Write<T>(GraphicsContext ctx, Entity buffer, in T value, ulong offset = 0)
        where T : unmanaged
    {
        fixed (T* ptr = &value)
            Write(ctx, buffer, offset, new ReadOnlySpan<byte>(ptr, sizeof(T)));
    }

    public static void Write<T>(GraphicsContext ctx, Entity buffer, ReadOnlySpan<T> values, ulong offset = 0)
        where T : unmanaged
    {
        Write(ctx, buffer, offset, MemoryMarshal.AsBytes(values));
    }

    public static Entity Uniform<T>(GraphicsContext ctx)
        where T : unmanaged
    {
        return Create(ctx, (ulong)sizeof(T), BufferUsage.Uniform | BufferUsage.CopyDst);
    }

    public static Entity Uniform<T>(GraphicsContext ctx, in T value)
        where T : unmanaged
    {
        var buffer = Uniform<T>(ctx);
        Write(ctx, buffer, value);
        return buffer;
    }

    public static Entity Storage<T>(
        GraphicsContext ctx,
        int elementCount,
        BufferUsage extraUsage = BufferUsage.None)
        where T : unmanaged
    {
        return Create(ctx, (ulong)(sizeof(T) * elementCount), BufferUsage.Storage | BufferUsage.CopyDst | extraUsage);
    }

    public static Entity Vertex<T>(GraphicsContext ctx, ReadOnlySpan<T> values)
        where T : unmanaged
    {
        var buffer = Create(ctx, (ulong)(sizeof(T) * values.Length), BufferUsage.Vertex | BufferUsage.CopyDst);
        Write(ctx, buffer, values);
        return buffer;
    }

    public static Entity Index<T>(GraphicsContext ctx, ReadOnlySpan<T> values)
        where T : unmanaged
    {
        var buffer = Create(ctx, (ulong)(sizeof(T) * values.Length), BufferUsage.Index | BufferUsage.CopyDst);
        Write(ctx, buffer, values);
        return buffer;
    }

    public static void Retain(GraphicsContext ctx, Entity buffer)
    {
        ref var buf = ref buffer.Get<BufferData>();
        Interlocked.Increment(ref buf.RefCount);
    }

    public static void Release(GraphicsContext ctx, Entity buffer)
    {
        ref var buf = ref buffer.Get<BufferData>();
        if (Interlocked.Decrement(ref buf.RefCount) == 0)
        {
            ctx.Device.ReleaseBuffer(buf.NativePtr);
            buffer.Destroy();
        }
    }

    public static Entity CreateStaging(GraphicsContext ctx, ulong size)
    {
        return Create(ctx, size, BufferUsage.CopyDst | BufferUsage.MapRead);
    }

    public static unsafe void MapAsync(
        GraphicsContext ctx, Entity buffer, MapMode mode, nuint offset, nuint size,
        delegate* unmanaged[Cdecl]<BufferMapAsyncStatus, void*, void> callback, void* userdata)
    {
        ref var buf = ref buffer.Get<BufferData>();
        ctx.Device.BufferMapAsync(buf.NativePtr, mode, offset, size, callback, userdata);
    }

    public static unsafe byte[] ReadMapped(GraphicsContext ctx, Entity buffer, nuint size)
    {
        ref var buf = ref buffer.Get<BufferData>();
        var mapped = ctx.Device.BufferGetConstMappedRange(buf.NativePtr, 0, size);
        if (mapped == null)
            throw new InvalidOperationException("Buffer is not mapped");
        var byteCount = checked((int)size);
        var result = new byte[byteCount];
        fixed (byte* dst = result)
            new ReadOnlySpan<byte>(mapped, byteCount).CopyTo(new Span<byte>(dst, byteCount));
        return result;
    }
}
