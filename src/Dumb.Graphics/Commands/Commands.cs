using Sia;
using Silk.NET.WebGPU;

namespace Dumb.Graphics;

public static unsafe class Commands
{
    public static Encoder CreateEncoder(GraphicsContext ctx, byte* label = null)
    {
        CommandEncoderDescriptor desc = new() { Label = label };
        var native = ctx.Command.CreateCommandEncoder(ctx.NativeDevice, &desc);
        return new Encoder(ctx, native);
    }

    public static RenderPassColorAttachment ColorAttachment(
        GraphicsContext ctx,
        Entity view,
        Color clearColor,
        LoadOp loadOp = LoadOp.Clear,
        StoreOp storeOp = StoreOp.Store)
    {
        return new RenderPassColorAttachment
        {
            View = (TextureView*)view.Get<TextureViewData>().NativePtr,
            DepthSlice = uint.MaxValue,
            ResolveTarget = null,
            LoadOp = loadOp,
            StoreOp = storeOp,
            ClearValue = clearColor
        };
    }

    public static RenderPassDescriptor RenderPass(RenderPassColorAttachment* colorAttachment)
    {
        return new RenderPassDescriptor
        {
            ColorAttachmentCount = 1,
            ColorAttachments = colorAttachment,
            DepthStencilAttachment = null,
            OcclusionQuerySet = null,
            TimestampWrites = null,
            Label = null
        };
    }

    public static RenderPassDescriptor RenderPass(
        ReadOnlySpan<RenderPassColorAttachment> colorAttachments,
        RenderPassDepthStencilAttachment* depthStencil = null)
    {
        fixed (RenderPassColorAttachment* ptr = colorAttachments)
        {
            return new RenderPassDescriptor
            {
                ColorAttachmentCount = (nuint)colorAttachments.Length,
                ColorAttachments = ptr,
                DepthStencilAttachment = depthStencil,
                OcclusionQuerySet = null,
                TimestampWrites = null,
                Label = null
            };
        }
    }

    public static RenderPassDepthStencilAttachment DepthStencilAttachment(
        GraphicsContext ctx,
        Entity view,
        LoadOp depthLoadOp = LoadOp.Clear,
        StoreOp depthStoreOp = StoreOp.Store,
        float depthClear = 1.0f,
        bool depthReadOnly = false)
    {
        return new RenderPassDepthStencilAttachment
        {
            View = (TextureView*)view.Get<TextureViewData>().NativePtr,
            DepthLoadOp = depthLoadOp,
            DepthStoreOp = depthStoreOp,
            DepthClearValue = depthClear,
            DepthReadOnly = depthReadOnly,
            StencilLoadOp = LoadOp.Undefined,
            StencilStoreOp = StoreOp.Undefined,
            StencilClearValue = 0,
            StencilReadOnly = true
        };
    }

    public static void Submit(GraphicsContext ctx, nint commandBuffer)
    {
        var cb = commandBuffer;
        ctx.Command.QueueSubmit(ctx.NativeQueue, 1, &cb);
    }

    public static void SubmitAndRelease(GraphicsContext ctx, nint commandBuffer)
    {
        Submit(ctx, commandBuffer);
        ctx.Command.CommandBufferRelease(commandBuffer);
    }

    public static void Submit(GraphicsContext ctx, ReadOnlySpan<nint> commandBuffers)
    {
        fixed (nint* ptr = commandBuffers)
            ctx.Command.QueueSubmit(ctx.NativeQueue, (nuint)commandBuffers.Length, ptr);
    }

    public static void ReleaseCommandBuffer(GraphicsContext ctx, nint commandBuffer)
    {
        ctx.Command.CommandBufferRelease(commandBuffer);
    }
}

public unsafe ref struct Encoder
{
    private readonly GraphicsContext _ctx;
    private nint _encoder;

    public Encoder(GraphicsContext ctx, nint encoder)
    {
        _ctx = ctx;
        _encoder = encoder;
    }

    public RenderPass BeginRenderPass(RenderPassDescriptor* descriptor)
    {
        ThrowIfFinished();
        var pass = _ctx.Command.CommandEncoderBeginRenderPass(_encoder, descriptor);
        return new RenderPass(_ctx, pass);
    }

    public ComputePass BeginComputePass(ComputePassDescriptor* descriptor)
    {
        ThrowIfFinished();
        var pass = _ctx.Command.CommandEncoderBeginComputePass(_encoder, descriptor);
        return new ComputePass(_ctx, pass);
    }

    public void CopyBufferToBuffer(
        Entity source, ulong sourceOffset,
        Entity destination, ulong destinationOffset, ulong size)
    {
        ThrowIfFinished();
        ref var src = ref source.Get<BufferData>();
        ref var dst = ref destination.Get<BufferData>();
        _ctx.Command.CopyBufferToBuffer(_encoder, src.NativePtr, sourceOffset, dst.NativePtr, destinationOffset, size);
    }

    public unsafe void CopyTextureToBuffer(
        Entity texture, Entity buffer, uint width, uint height,
        uint bytesPerRow, TextureAspect aspect = TextureAspect.All)
    {
        ThrowIfFinished();
        ref var tex = ref texture.Get<TextureData>();
        ref var buf = ref buffer.Get<BufferData>();

        ImageCopyTexture src = new()
        {
            Texture = (Texture*)tex.NativePtr,
            MipLevel = 0,
            Origin = new Origin3D { X = 0, Y = 0, Z = 0 },
            Aspect = aspect
        };

        ImageCopyBuffer dst = new()
        {
            Buffer = (Silk.NET.WebGPU.Buffer*)buf.NativePtr,
            Layout = new TextureDataLayout
            {
                Offset = 0,
                BytesPerRow = bytesPerRow,
                RowsPerImage = height
            }
        };

        Extent3D copySize = new()
        {
            Width = width,
            Height = height,
            DepthOrArrayLayers = 1
        };

        _ctx.Command.CommandEncoderCopyTextureToBuffer(_encoder, &src, &dst, &copySize);
    }

    public nint Finish()
    {
        ThrowIfFinished();
        var commandBuffer = _ctx.Command.CommandEncoderFinish(_encoder, null);
        _ctx.Command.CommandEncoderRelease(_encoder);
        _encoder = 0;
        return commandBuffer;
    }

    public void Dispose()
    {
        if (_encoder == 0)
            return;

        _ctx.Command.CommandEncoderRelease(_encoder);
        _encoder = 0;
    }

    private readonly void ThrowIfFinished()
    {
        if (_encoder == 0)
            throw new ObjectDisposedException(nameof(Encoder));
    }
}

public unsafe ref struct RenderPass
{
    private readonly GraphicsContext _ctx;
    private nint _pass;

    public RenderPass(GraphicsContext ctx, nint pass)
    {
        _ctx = ctx;
        _pass = pass;
    }

    public void SetPipeline(Entity pipeline)
    {
        ThrowIfEnded();
        ref var data = ref pipeline.Get<RenderPipelineData>();
        _ctx.Command.RenderPassEncoderSetPipeline(_pass, data.NativePtr);
    }

    public void SetBindGroup(uint groupIndex, Entity group, scoped ReadOnlySpan<uint> dynamicOffsets = default)
    {
        ThrowIfEnded();
        ref var data = ref group.Get<BindGroupData>();
        fixed (uint* offsets = dynamicOffsets)
            _ctx.Command.RenderPassEncoderSetBindGroup(_pass, groupIndex, data.NativePtr, (nuint)dynamicOffsets.Length, offsets);
    }

    public void SetVertexBuffer(uint slot, Entity buffer, ulong offset = 0, ulong size = unchecked((ulong)-1))
    {
        ThrowIfEnded();
        ref var data = ref buffer.Get<BufferData>();
        if (size == unchecked((ulong)-1)) size = data.Size - offset;
        _ctx.Command.RenderPassEncoderSetVertexBuffer(_pass, slot, data.NativePtr, offset, size);
    }

    public void SetIndexBuffer(Entity buffer, IndexFormat format, ulong offset = 0, ulong size = unchecked((ulong)-1))
    {
        ThrowIfEnded();
        ref var data = ref buffer.Get<BufferData>();
        if (size == unchecked((ulong)-1)) size = data.Size - offset;
        _ctx.Command.RenderPassEncoderSetIndexBuffer(_pass, data.NativePtr, format, offset, size);
    }

    public void SetViewport(float x, float y, float w, float h, float minDepth = 0, float maxDepth = 1)
    {
        ThrowIfEnded();
        _ctx.Command.RenderPassEncoderSetViewport(_pass, x, y, w, h, minDepth, maxDepth);
    }

    public void SetScissorRect(uint x, uint y, uint w, uint h)
    {
        ThrowIfEnded();
        _ctx.Command.RenderPassEncoderSetScissorRect(_pass, x, y, w, h);
    }

    public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
    {
        ThrowIfEnded();
        _ctx.Command.RenderPassEncoderDraw(_pass, vertexCount, instanceCount, firstVertex, firstInstance);
    }

    public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0)
    {
        ThrowIfEnded();
        _ctx.Command.RenderPassEncoderDrawIndexed(_pass, indexCount, instanceCount, firstIndex, baseVertex, firstInstance);
    }

    public void End()
    {
        ThrowIfEnded();
        _ctx.Command.RenderPassEncoderEnd(_pass);
        _ctx.Command.RenderPassEncoderRelease(_pass);
        _pass = 0;
    }

    public void Dispose()
    {
        if (_pass != 0)
            End();
    }

    private readonly void ThrowIfEnded()
    {
        if (_pass == 0)
            throw new ObjectDisposedException(nameof(RenderPass));
    }
}

public unsafe ref struct ComputePass
{
    private readonly GraphicsContext _ctx;
    private nint _pass;

    public ComputePass(GraphicsContext ctx, nint pass)
    {
        _ctx = ctx;
        _pass = pass;
    }

    public void SetPipeline(Entity pipeline)
    {
        ThrowIfEnded();
        ref var data = ref pipeline.Get<ComputePipelineData>();
        _ctx.Command.ComputePassEncoderSetPipeline(_pass, data.NativePtr);
    }

    public void SetBindGroup(uint groupIndex, Entity group, scoped ReadOnlySpan<uint> dynamicOffsets = default)
    {
        ThrowIfEnded();
        ref var data = ref group.Get<BindGroupData>();
        fixed (uint* offsets = dynamicOffsets)
            _ctx.Command.ComputePassEncoderSetBindGroup(_pass, groupIndex, data.NativePtr, (nuint)dynamicOffsets.Length, offsets);
    }

    public void Dispatch(uint x, uint y = 1, uint z = 1)
    {
        ThrowIfEnded();
        _ctx.Command.ComputePassEncoderDispatchWorkgroups(_pass, x, y, z);
    }

    public void End()
    {
        ThrowIfEnded();
        _ctx.Command.ComputePassEncoderEnd(_pass);
        _ctx.Command.ComputePassEncoderRelease(_pass);
        _pass = 0;
    }

    public void Dispose()
    {
        if (_pass != 0)
            End();
    }

    private readonly void ThrowIfEnded()
    {
        if (_pass == 0)
            throw new ObjectDisposedException(nameof(ComputePass));
    }
}
