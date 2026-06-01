using Silk.NET.WebGPU;

namespace Dumb.Graphics.Interfaces;

public unsafe interface IDeviceBackend
{
    // Instance & adapter
    public nint CreateInstance(InstanceDescriptor* descriptor);
    public nint GetProcAddress(nint device, byte* procName);
    public void InstanceProcessEvents(nint instance);
    public void InstanceRequestAdapter(nint instance, RequestAdapterOptions* options,
        delegate* unmanaged[Cdecl]<RequestAdapterStatus, nint, byte*, void*, void> callback, void* userdata);
    public void AdapterGetProperties(nint adapter, AdapterProperties* properties);
    public void AdapterRequestDevice(nint adapter, DeviceDescriptor* descriptor,
        delegate* unmanaged[Cdecl]<RequestDeviceStatus, nint, byte*, void*, void> callback, void* userdata);
    public nint DeviceGetQueue(nint device);

    // Error scopes
    public void DevicePushErrorScope(nint device, ErrorFilter filter);
    public void DevicePopErrorScope(nint device,
        delegate* unmanaged[Cdecl]<ErrorType, byte*, void*, void> callback, void* userdata);
    public void DeviceSetUncapturedErrorCallback(nint device,
        delegate* unmanaged[Cdecl]<ErrorType, byte*, void*, void> callback, void* userdata);

    // Resources
    public nint CreateBuffer(nint device, BufferDescriptor* descriptor);
    public nint CreateTexture(nint device, TextureDescriptor* descriptor);
    public nint CreateTextureView(nint texture, TextureViewDescriptor* descriptor);
    public nint CreateSampler(nint device, SamplerDescriptor* descriptor);
    public nint CreateShaderModule(nint device, ShaderModuleDescriptor* descriptor);
    public nint CreateBindGroupLayout(nint device, BindGroupLayoutDescriptor* descriptor);
    public nint CreateBindGroup(nint device, BindGroupDescriptor* descriptor);
    public nint CreatePipelineLayout(nint device, PipelineLayoutDescriptor* descriptor);
    public nint CreateRenderPipeline(nint device, RenderPipelineDescriptor* descriptor);
    public nint CreateComputePipeline(nint device, ComputePipelineDescriptor* descriptor);

    public void ReleaseInstance(nint instance);
    public void ReleaseAdapter(nint adapter);
    public void ReleaseDevice(nint device);
    public void ReleaseBuffer(nint buffer);
    public void ReleaseTexture(nint texture);
    public void ReleaseTextureView(nint textureView);
    public void ReleaseSampler(nint sampler);
    public void ReleaseShaderModule(nint shaderModule);
    public void ReleaseBindGroupLayout(nint bindGroupLayout);
    public void ReleaseBindGroup(nint bindGroup);
    public void ReleasePipelineLayout(nint pipelineLayout);
    public void ReleaseRenderPipeline(nint renderPipeline);
    public void ReleaseComputePipeline(nint computePipeline);

    // Buffer mapping (for readback)
    public void BufferMapAsync(nint buffer, MapMode mode, nuint offset, nuint size,
        delegate* unmanaged[Cdecl]<BufferMapAsyncStatus, void*, void> callback, void* userdata);
    public void* BufferGetConstMappedRange(nint buffer, nuint offset, nuint size);
}
