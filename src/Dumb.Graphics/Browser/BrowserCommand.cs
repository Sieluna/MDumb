using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;
using Dumb.Graphics.Interfaces;

namespace Dumb.Graphics.Browser;

public sealed unsafe class BrowserCommandBackend : ICommandBackend
{
    private readonly Emscripten.WGPUBrowser _wgpu;

    public BrowserCommandBackend(Emscripten.WGPUBrowser wgpu) => _wgpu = wgpu;

    public nint CreateCommandEncoder(nint device, CommandEncoderDescriptor* descriptor) =>
        (nint)_wgpu.DeviceCreateCommandEncoder((Device*)device, descriptor);

    public nint CommandEncoderBeginRenderPass(nint encoder, RenderPassDescriptor* descriptor) =>
        (nint)_wgpu.CommandEncoderBeginRenderPass((CommandEncoder*)encoder, descriptor);

    public void RenderPassEncoderSetPipeline(nint pass, nint pipeline) =>
        _wgpu.RenderPassEncoderSetPipeline((RenderPassEncoder*)pass, (RenderPipeline*)pipeline);

    public void RenderPassEncoderSetBindGroup(nint pass, uint groupIndex, nint group, nuint dynamicOffsetCount, uint* dynamicOffsets) =>
        _wgpu.RenderPassEncoderSetBindGroup((RenderPassEncoder*)pass, groupIndex, (BindGroup*)group, dynamicOffsetCount, dynamicOffsets);

    public void RenderPassEncoderSetVertexBuffer(nint pass, uint slot, nint buffer, ulong offset, ulong size) =>
        _wgpu.RenderPassEncoderSetVertexBuffer((RenderPassEncoder*)pass, slot, (WgpuBuffer*)buffer, offset, size);

    public void RenderPassEncoderSetIndexBuffer(nint pass, nint buffer, IndexFormat format, ulong offset, ulong size) =>
        _wgpu.RenderPassEncoderSetIndexBuffer((RenderPassEncoder*)pass, (WgpuBuffer*)buffer, format, offset, size);

    public void RenderPassEncoderSetViewport(nint pass, float x, float y, float w, float h, float minDepth, float maxDepth) =>
        _wgpu.RenderPassEncoderSetViewport((RenderPassEncoder*)pass, x, y, w, h, minDepth, maxDepth);

    public void RenderPassEncoderSetScissorRect(nint pass, uint x, uint y, uint w, uint h) =>
        _wgpu.RenderPassEncoderSetScissorRect((RenderPassEncoder*)pass, x, y, w, h);

    public void RenderPassEncoderDraw(nint pass, uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance) =>
        _wgpu.RenderPassEncoderDraw((RenderPassEncoder*)pass, vertexCount, instanceCount, firstVertex, firstInstance);

    public void RenderPassEncoderDrawIndexed(nint pass, uint indexCount, uint instanceCount, uint firstIndex, int baseVertex, uint firstInstance) =>
        _wgpu.RenderPassEncoderDrawIndexed((RenderPassEncoder*)pass, indexCount, instanceCount, firstIndex, baseVertex, firstInstance);

    public void RenderPassEncoderEnd(nint pass) =>
        _wgpu.RenderPassEncoderEnd((RenderPassEncoder*)pass);

    public nint CommandEncoderBeginComputePass(nint encoder, ComputePassDescriptor* descriptor) =>
        (nint)_wgpu.CommandEncoderBeginComputePass((CommandEncoder*)encoder, descriptor);

    public void ComputePassEncoderSetPipeline(nint pass, nint pipeline) =>
        _wgpu.ComputePassEncoderSetPipeline((ComputePassEncoder*)pass, (ComputePipeline*)pipeline);

    public void ComputePassEncoderSetBindGroup(nint pass, uint groupIndex, nint group, nuint dynamicOffsetCount, uint* dynamicOffsets) =>
        _wgpu.ComputePassEncoderSetBindGroup((ComputePassEncoder*)pass, groupIndex, (BindGroup*)group, dynamicOffsetCount, dynamicOffsets);

    public void ComputePassEncoderDispatchWorkgroups(nint pass, uint x, uint y, uint z) =>
        _wgpu.ComputePassEncoderDispatchWorkgroups((ComputePassEncoder*)pass, x, y, z);

    public void ComputePassEncoderEnd(nint pass) =>
        _wgpu.ComputePassEncoderEnd((ComputePassEncoder*)pass);

    public nint CommandEncoderFinish(nint encoder, CommandBufferDescriptor* descriptor) =>
        (nint)_wgpu.CommandEncoderFinish((CommandEncoder*)encoder, descriptor);

    public void CommandEncoderRelease(nint encoder) =>
        _wgpu.CommandEncoderRelease((CommandEncoder*)encoder);

    public void CommandBufferRelease(nint commandBuffer) =>
        _wgpu.CommandBufferRelease((CommandBuffer*)commandBuffer);

    public void RenderPassEncoderRelease(nint pass) =>
        _wgpu.RenderPassEncoderRelease((RenderPassEncoder*)pass);

    public void ComputePassEncoderRelease(nint pass) =>
        _wgpu.ComputePassEncoderRelease((ComputePassEncoder*)pass);

    public void CopyBufferToBuffer(nint encoder, nint source, ulong sourceOffset, nint destination, ulong destinationOffset, ulong size) =>
        _wgpu.CommandEncoderCopyBufferToBuffer((CommandEncoder*)encoder, (WgpuBuffer*)source, sourceOffset, (WgpuBuffer*)destination, destinationOffset, size);

    public void QueueSubmit(nint queue, nuint commandCount, nint* commands) =>
        _wgpu.QueueSubmit((Queue*)queue, commandCount, (CommandBuffer**)commands);

    public void QueueWriteBuffer(nint queue, nint buffer, ulong bufferOffset, void* data, nuint size) =>
        _wgpu.QueueWriteBuffer((Queue*)queue, (WgpuBuffer*)buffer, bufferOffset, data, size);

    public void QueueWriteTexture(nint queue, ImageCopyTexture* destination, void* data, nuint dataSize,
        TextureDataLayout* dataLayout, Extent3D* writeSize) =>
        _wgpu.QueueWriteTexture((Queue*)queue, destination, data, dataSize, dataLayout, writeSize);

    public void CommandEncoderCopyTextureToBuffer(nint encoder, ImageCopyTexture* source,
        ImageCopyBuffer* destination, Extent3D* copySize) =>
        _wgpu.CommandEncoderCopyTextureToBuffer((CommandEncoder*)encoder, source, destination, copySize);
}
