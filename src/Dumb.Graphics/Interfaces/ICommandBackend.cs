using Silk.NET.WebGPU;

namespace Dumb.Graphics.Interfaces;

public unsafe interface ICommandBackend
{
    public nint CreateCommandEncoder(nint device, CommandEncoderDescriptor* descriptor);

    // Render pass
    public nint CommandEncoderBeginRenderPass(nint encoder, RenderPassDescriptor* descriptor);
    public void RenderPassEncoderSetPipeline(nint pass, nint pipeline);
    public void RenderPassEncoderSetBindGroup(nint pass, uint groupIndex, nint group, nuint dynamicOffsetCount, uint* dynamicOffsets);
    public void RenderPassEncoderSetVertexBuffer(nint pass, uint slot, nint buffer, ulong offset, ulong size);
    public void RenderPassEncoderSetIndexBuffer(nint pass, nint buffer, IndexFormat format, ulong offset, ulong size);
    public void RenderPassEncoderSetViewport(nint pass, float x, float y, float w, float h, float minDepth, float maxDepth);
    public void RenderPassEncoderSetScissorRect(nint pass, uint x, uint y, uint w, uint h);
    public void RenderPassEncoderDraw(nint pass, uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance);
    public void RenderPassEncoderDrawIndexed(nint pass, uint indexCount, uint instanceCount, uint firstIndex, int baseVertex, uint firstInstance);
    public void RenderPassEncoderEnd(nint pass);

    // Compute pass
    public nint CommandEncoderBeginComputePass(nint encoder, ComputePassDescriptor* descriptor);
    public void ComputePassEncoderSetPipeline(nint pass, nint pipeline);
    public void ComputePassEncoderSetBindGroup(nint pass, uint groupIndex, nint group, nuint dynamicOffsetCount, uint* dynamicOffsets);
    public void ComputePassEncoderDispatchWorkgroups(nint pass, uint x, uint y, uint z);
    public void ComputePassEncoderEnd(nint pass);

    // Encoder finalization & queue
    public nint CommandEncoderFinish(nint encoder, CommandBufferDescriptor* descriptor);
    public void CommandEncoderRelease(nint encoder);
    public void CommandBufferRelease(nint commandBuffer);
    public void RenderPassEncoderRelease(nint pass);
    public void ComputePassEncoderRelease(nint pass);
    public void CopyBufferToBuffer(nint encoder, nint source, ulong sourceOffset, nint destination, ulong destinationOffset, ulong size);
    public void QueueSubmit(nint queue, nuint commandCount, nint* commands);
    public void QueueWriteBuffer(nint queue, nint buffer, ulong bufferOffset, void* data, nuint size);
    public void QueueWriteTexture(nint queue, ImageCopyTexture* destination, void* data, nuint dataSize,
        TextureDataLayout* dataLayout, Extent3D* writeSize);
    public void CommandEncoderCopyTextureToBuffer(nint encoder, ImageCopyTexture* source,
        ImageCopyBuffer* destination, Extent3D* copySize);
}
