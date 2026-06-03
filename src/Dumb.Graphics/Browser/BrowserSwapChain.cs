using Silk.NET.WebGPU;
using Dawn = Silk.NET.WebGPU.Extensions.Dawn;
using Dumb.Graphics.Interfaces;

namespace Dumb.Graphics.Browser;

public sealed unsafe class BrowserSwapChainBackend : ISwapChainBackend
{
    private readonly Emscripten.WGPUBrowser _wgpu;
    private Dawn.SwapChain* _swapChain;

    public BrowserSwapChainBackend(Emscripten.WGPUBrowser wgpu) => _wgpu = wgpu;

    public TextureFormat GetPreferredFormat(nint surface, nint adapter)
    {
        return _wgpu.SurfaceGetPreferredFormat((Surface*)surface, (Adapter*)adapter);
    }

    public void Configure(in GraphicsSurface surface, nint device,
        TextureUsage usage, PresentMode presentMode)
    {
        if (_swapChain != null)
            _wgpu.SwapChainRelease(_swapChain);

        var desc = new Dawn.SwapChainDescriptor
        {
            Usage = usage,
            Format = surface.Format,
            Width = surface.Width,
            Height = surface.Height,
            PresentMode = presentMode
        };
        _swapChain = _wgpu.DeviceCreateSwapChain((Device*)device, (Surface*)surface.Handle, desc);
    }

    public nint GetCurrentTextureView(in GraphicsSurface surface)
    {
        var view = _wgpu.SwapChainGetCurrentTextureView(_swapChain);
        return (nint)view;
    }

    public void Present(nint surface) { }

    public void Unconfigure(nint surface)
    {
        if (_swapChain == null) return;
        _wgpu.SwapChainRelease(_swapChain);
        _swapChain = null;
    }
}
