using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Dumb.Engine.Mesh;
using Sia;
using Silk.NET.WebGPU;
using Dumb.Graphics.Materials;
using Dumb.Graphics.Resources;
#if BROWSER
using System.Text;
using Dumb.Emscripten;
#else
using Dumb.Engine.Input;
using Dumb.Engine.Window;
#endif

namespace Dumb.Graphics.Example;

public struct FlatColorMaterial : IMaterial
{
    public Entity CameraBuffer;
    public Entity FrameBuffer;
    public Entity ComputeBuffer;

    private Entity? _cachedShader;

    public static string Name => "FlatColor";

    public static MeshDescriptor VertexDescriptor => new(
        [new VertexStreamDescriptor([
            new VertexElement(MeshAttribute.Position, location: 0),
            new VertexElement(MeshAttribute.Normal, location: 1),
            new VertexElement(MeshAttribute.Color, location: 2)
        ])],
        IndexFormat.Uint32);

    public static BindingLayout[][] BindGroupLayouts =>
    [
        [
            BindingLayout.UniformBuffer(0, ShaderStage.Vertex | ShaderStage.Fragment, 208),
            BindingLayout.UniformBuffer(1, ShaderStage.Vertex | ShaderStage.Fragment, 16),
            BindingLayout.StorageBuffer(2, ShaderStage.Fragment, 4 * sizeof(float), readOnly: true),
        ]
    ];

    public static TextureFormat[] ColorFormats => [TextureFormat.Rgba8Unorm];

    public Entity GetShader(GraphicsContext ctx)
    {
        if (_cachedShader is { Host: not null } s) return s;
        _cachedShader = Shaders.Wgsl(ctx, FlatColorShader);
        return _cachedShader!;
    }

    public Entity?[] CreateBindGroups(GraphicsContext ctx, Entity pipelineLayout)
    {
        ref var plData = ref pipelineLayout.Get<PipelineLayoutData>();
        var bgl0 = plData.BindGroupLayouts?[0]
            ?? throw new InvalidOperationException("Pipeline layout missing bind group layout 0.");

        var group0 = Pipelines.BindGroup(ctx, bgl0,
        [
            Binding.Uniform<CameraUniforms>(0, CameraBuffer),
            Binding.Uniform<Vector4>(1, FrameBuffer),
            Binding.Storage<float>(2, ComputeBuffer, 4)
        ]);
        return [group0];
    }

    private const string FlatColorShader = """
struct CameraUniforms {
    view_projection: mat4x4f,
    view: mat4x4f,
    projection: mat4x4f,
    camera_position: vec3f,
    _pad: f32,
}

struct FrameUniforms {
    time: f32,
    _pad0: f32,
    resolution: vec2f,
}

@group(0) @binding(0) var<uniform> camera: CameraUniforms;
@group(0) @binding(1) var<uniform> frame: FrameUniforms;
@group(0) @binding(2) var<storage, read> compute_data: array<f32>;

struct VertexInput {
    @location(0) position: vec3f,
    @location(1) normal: vec3f,
    @location(2) color: vec4f,
}

struct VertexOutput {
    @builtin(position) clip_position: vec4f,
    @location(0) color: vec3f,
    @location(1) uv: vec2f,
}

@vertex
fn vs_main(input: VertexInput) -> VertexOutput {
    var output: VertexOutput;
    output.clip_position = camera.view_projection * vec4f(input.position, 1.0);
    output.color = input.color.rgb;
    output.uv = input.position.xy * 0.5 + vec2f(0.5, 0.5);
    return output;
}

@fragment
fn fs_main(input: VertexOutput) -> @location(0) vec4f {
    let pulse = compute_data[0];
    let radial = distance(input.uv, vec2f(0.5, 0.5));
    let rings = sin(radial * 52.0 - frame.time * 5.0) * 0.5 + 0.5;
    let beam = pow(1.0 - abs(input.uv.x - input.uv.y), 3.0);
    let glow = smoothstep(0.68, 0.05, radial);
    let base = mix(vec3f(0.035, 0.055, 0.085), input.color, 0.72);
    let color = base * (0.62 + rings * 0.28) + vec3f(0.15, 0.26, 0.34) * pulse + vec3f(0.20, 0.42, 0.36) * beam * glow;
    return vec4f(color, 1.0);
}
""";
}

public sealed class GraphicsExampleApp : IDisposable
{
    private const uint OffscreenWidth = 512;
    private const uint OffscreenHeight = 512;
    private const int NativeWidth = 960;
    private const int NativeHeight = 640;
    private const TextureFormat ColorFormat = TextureFormat.Rgba8Unorm;
    private const TextureFormat DepthFormat = TextureFormat.Depth32float;

    private const string ComputeShader = """
@group(0) @binding(0) var<storage, read_write> values: array<f32>;

@compute @workgroup_size(1)
fn cs_main(@builtin(global_invocation_id) id: vec3u) {
    if (id.x == 0u) {
        values[0] = 0.42;
        values[1] = 0.78;
        values[2] = 1.0;
        values[3] = 8.0;
    }
}
""";

    private const string TextureShader = """
struct Uniforms {
    time: f32,
    pad: f32,
    target_size: vec2f,
}

@group(0) @binding(0) var render_sampler: sampler;
@group(0) @binding(1) var render_texture: texture_2d<f32>;
@group(0) @binding(2) var<uniform> u: Uniforms;

struct VertexOutput {
    @builtin(position) position: vec4f,
    @location(0) uv: vec2f,
}

@vertex
fn vs_main(@builtin(vertex_index) vertex_index: u32) -> VertexOutput {
    var output: VertexOutput;
    switch vertex_index {
        case 0u: {
            output.position = vec4f(-1.0, -1.0, 0.0, 1.0);
            output.uv = vec2f(0.0, 1.0);
        }
        case 1u: {
            output.position = vec4f(1.0, -1.0, 0.0, 1.0);
            output.uv = vec2f(1.0, 1.0);
        }
        case 2u: {
            output.position = vec4f(-1.0, 1.0, 0.0, 1.0);
            output.uv = vec2f(0.0, 0.0);
        }
        case 3u: {
            output.position = vec4f(-1.0, 1.0, 0.0, 1.0);
            output.uv = vec2f(0.0, 0.0);
        }
        case 4u: {
            output.position = vec4f(1.0, -1.0, 0.0, 1.0);
            output.uv = vec2f(1.0, 1.0);
        }
        default: {
            output.position = vec4f(1.0, 1.0, 0.0, 1.0);
            output.uv = vec2f(1.0, 0.0);
        }
    }
    return output;
}

@fragment
fn fs_main(input: VertexOutput) -> @location(0) vec4f {
    let texel = textureSample(render_texture, render_sampler, input.uv);
    let p = input.uv - vec2f(0.5, 0.5);
    let scan = 0.90 + 0.10 * sin(input.uv.y * u.target_size.y * 0.42);
    let vignette = smoothstep(0.82, 0.16, length(p));
    let grid_x = smoothstep(0.018, 0.0, abs(fract(input.uv.x * 12.0) - 0.5));
    let grid_y = smoothstep(0.018, 0.0, abs(fract(input.uv.y * 12.0) - 0.5));
    let grid = (grid_x + grid_y) * 0.035;
    let bloom = pow(max(max(texel.r, texel.g), texel.b), 2.0);
    let color = texel.rgb * scan * vignette + vec3f(0.018, 0.024, 0.034) + vec3f(0.16, 0.30, 0.34) * bloom + grid;
    return vec4f(color, 1.0);
}
""";

    private readonly GraphicsContext _graphics = new();
#if BROWSER
    private WGPUBrowser _wgpu = null!;
    private GraphicsSurface _surface;
#else
    private World _world = null!;
    private Entity _window;
    private SystemStage _windowStage = null!;
    private WebGPU _wgpu = null!;
    private GraphicsSurface _surface;
#endif

    private Entity _cameraBuffer;
    private Entity _frameBuffer;
    private Entity _computeBuffer;
    private Entity _mesh;
    private Entity _material;
    private Entity _computePipeline;
    private Entity _computeBindGroup;
    private Entity _texturePipeline;
    private Entity _textureBindGroup;
    private Entity _textureUniformBuffer;
    private RenderTexture _renderTarget;
    private Entity _depthTexture;
    private Entity _depthView;
    private bool _disposed;

#if BROWSER
    private static GCHandle s_browserHandle;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int BrowserAnimationFrame(double timeMs, void* userData)
    {
        var app = (GraphicsExampleApp)GCHandle.FromIntPtr((nint)userData).Target!;
        app.RenderFrame((float)(timeMs / 1000.0));
        app._graphics.Tick();
        return 1;
    }
#endif

    public async Task RunAsync()
    {
        RequestAdapterOptions adapterOptions = new()
        {
            PowerPreference = PowerPreference.HighPerformance,
            ForceFallbackAdapter = false,
            BackendType = BackendType.Undefined,
            CompatibleSurface = null
        };

        DeviceDescriptor deviceDescriptor = new()
        {
            RequiredLimits = null,
            DefaultQueue = new QueueDescriptor { Label = null },
            DeviceLostCallback = default,
            DeviceLostUserdata = null
        };

        _wgpu = _graphics.NativeApi;
#if BROWSER
        CreateBrowserSurface();
        GraphicsContext.SetCompatibleSurface(ref adapterOptions, _surface);
        await _graphics.InitializeAsync(adapterOptions, deviceDescriptor);

        _surface.Format = _graphics.GetSurfacePreferredFormat(_surface);
        _graphics.ConfigureSurface(_surface);

        CreateSceneResources(_surface.Format);

        s_browserHandle = GCHandle.Alloc(this);
        Console.WriteLine("Dumb.Graphics browser WebGPU canvas example started.");
        StartBrowserAnimationLoop();

        await new TaskCompletionSource().Task;
#else
        await _graphics.InitializeAsync(adapterOptions, deviceDescriptor);

        _world = new World();
        _window = _world.CreateWindow(new WindowDescriptor
        {
            Width = NativeWidth,
            Height = NativeHeight,
            Title = "Dumb.Graphics native WebGPU example"
        });
        _windowStage = SystemChain.Empty
            .Add<WindowSystem>()
            .Add<InputSystem>()
            .CreateStage(_world);

        CreateSurface();
        _surface.Format = _graphics.GetSurfacePreferredFormat(_surface);
        ConfigureSurface();
        CreateSceneResources(_surface.Format);
        RunNativeLoop();
#endif
    }

#if BROWSER
    private unsafe void StartBrowserAnimationLoop()
    {
        Emscripten.Emscripten.RequestAnimationFrameLoop(
            (delegate* unmanaged[Cdecl]<double, void*, int>)&BrowserAnimationFrame,
            (void*)GCHandle.ToIntPtr(s_browserHandle));
    }
#endif

    private unsafe void CreateSceneResources(TextureFormat presentFormat)
    {
        _cameraBuffer = Buffers.Uniform<CameraUniforms>(_graphics);
        _frameBuffer = Buffers.Uniform<Vector4>(_graphics);
        _computeBuffer = Buffers.Storage<float>(_graphics, 4, BufferUsage.CopySrc);

        var meshData = MeshData.FromVertices(TriangleVertices(), TriangleIndices());
        _mesh = Mesh.Create(_graphics, meshData);

        var materialDesc = new FlatColorMaterial
        {
            CameraBuffer = _cameraBuffer,
            FrameBuffer = _frameBuffer,
            ComputeBuffer = _computeBuffer
        };
        _material = Dumb.Graphics.Resources.Materials.Create(_graphics, materialDesc);

        _renderTarget = Textures.RenderTarget(_graphics, OffscreenWidth, OffscreenHeight, ColorFormat);
        _depthTexture = Textures.Create2D(_graphics, OffscreenWidth, OffscreenHeight, DepthFormat, TextureUsage.RenderAttachment);
        _depthView = Textures.CreateView2D(_graphics, _depthTexture, DepthFormat);

        var sampler = Samplers.LinearClamp(_graphics);

        var computeShader = Shaders.Wgsl(_graphics, ComputeShader);
        var textureShader = Shaders.Wgsl(_graphics, TextureShader);

        var computeLayout = Pipelines.BindGroupLayout(_graphics,
        [
            BindingLayout.StorageBuffer(0, ShaderStage.Compute, 4 * sizeof(float))
        ]);
        var computePipelineLayout = Pipelines.Layout(_graphics, [computeLayout]);
        _computeBindGroup = Pipelines.BindGroup(_graphics, computeLayout,
        [
            Binding.Storage<float>(0, _computeBuffer, 4)
        ]);
        _computePipeline = Pipelines.Compute(_graphics, computeShader, computePipelineLayout);

        _textureUniformBuffer = Buffers.Uniform<Vector4>(_graphics);

        var textureLayout = Pipelines.BindGroupLayout(_graphics,
        [
            BindingLayout.Sampler(0, ShaderStage.Fragment),
            BindingLayout.Texture(1, ShaderStage.Fragment),
            BindingLayout.UniformBuffer(2, ShaderStage.Fragment, 16)
        ]);
        var texturePipelineLayout = Pipelines.Layout(_graphics, [textureLayout]);
        _textureBindGroup = Pipelines.BindGroup(_graphics, textureLayout,
        [
            Binding.Sampler(0, sampler),
            Binding.Texture(1, _renderTarget.View),
            Binding.Uniform<Vector4>(2, _textureUniformBuffer)
        ]);
        _texturePipeline = Pipelines.Render(_graphics, textureShader, texturePipelineLayout, presentFormat);
    }

#if !BROWSER
    private void RunNativeLoop()
    {
        var clock = Stopwatch.StartNew();
        Console.WriteLine("Dumb.Graphics native example opened a GLFW WebGPU window. Close the window to exit.");

        ref var window = ref _window.Get<WindowState>();

        while (!window.ShouldClose)
        {
            _windowStage.Tick();

            if (window.Width <= 0 || window.Height <= 0)
                continue;

            var width = (uint)window.FramebufferWidth;
            var height = (uint)window.FramebufferHeight;
            if (width != _surface.Width || height != _surface.Height)
                ConfigureSurface();

            RenderFrame((float)clock.Elapsed.TotalSeconds);
            _graphics.Tick();
        }
    }

    private unsafe void CreateSurface()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new PlatformNotSupportedException("The native Dumb.Graphics example currently creates a WebGPU surface through GLFW Win32 handles.");

        var runtime = _window.Get<WindowRuntime>();
        var (hwnd, _, hInstance) = runtime.Native!.Win32!.Value;
        if (hwnd == 0)
            throw new InvalidOperationException("Failed to get the GLFW Win32 window handle.");

        var hwndDesc = new SurfaceDescriptorFromWindowsHWND
        {
            Chain = new ChainedStruct { Next = null, SType = SType.SurfaceDescriptorFromWindowsHwnd },
            Hinstance = (void*)hInstance,
            Hwnd = (void*)hwnd
        };

        var surfaceDesc = new SurfaceDescriptor { NextInChain = (ChainedStruct*)&hwndDesc, Label = null };

        _surface = _graphics.CreateSurfaceFromNative((nint)_wgpu.InstanceCreateSurface(
            (Instance*)_graphics.NativeInstanceHandle, &surfaceDesc));
        if (!_surface.IsValid)
            throw new InvalidOperationException("Failed to create the WebGPU surface.");
    }

    private void ConfigureSurface()
    {
        ref var window = ref _window.Get<WindowState>();
        _surface.Width = Math.Max(1u, (uint)window.FramebufferWidth);
        _surface.Height = Math.Max(1u, (uint)window.FramebufferHeight);

        _graphics.ConfigureSurface(_surface);
    }
#endif

#if BROWSER
    private unsafe void CreateBrowserSurface()
    {
        var selectorBytes = Encoding.UTF8.GetBytes("#canvas\0");
        var selectorHandle = GCHandle.Alloc(selectorBytes, GCHandleType.Pinned);

        var canvasDesc = new SurfaceDescriptorFromCanvasHTMLSelector
        {
            Chain = new ChainedStruct { Next = null, SType = SType.SurfaceDescriptorFromCanvasHtmlSelector },
            Selector = (byte*)selectorHandle.AddrOfPinnedObject()
        };

        var surfaceDesc = new SurfaceDescriptor { NextInChain = (ChainedStruct*)&canvasDesc, Label = null };

        _surface = _graphics.CreateSurfaceFromNative((nint)_wgpu.InstanceCreateSurface(
            (Instance*)_graphics.NativeInstanceHandle, surfaceDesc));
        selectorHandle.Free();

        if (!_surface.IsValid)
            throw new InvalidOperationException("Failed to create WebGPU surface from canvas.");

        _surface.Width = NativeWidth;
        _surface.Height = NativeHeight;
    }
#endif

    private void RenderFrame(float time)
    {
        var view = Matrix4x4.CreateRotationZ((float)Math.Sin(time * 0.3) * 0.15f);
        var proj = Matrix4x4.CreateOrthographicOffCenterLeftHanded(-1, 1, -1, 1, -1, 1);
        Matrix4x4.Invert(view, out var viewInv);
        Matrix4x4.Invert(proj, out var projInv);
        var cameraUniforms = new CameraUniforms(view * proj, view, proj, Vector3.Zero, viewInv, projInv);
        Buffers.Write(_graphics, _cameraBuffer, cameraUniforms);

        var frameInfo = new Vector4(time, 0, OffscreenWidth, OffscreenHeight);
        Buffers.Write(_graphics, _frameBuffer, frameInfo);
        Buffers.Write(_graphics, _textureUniformBuffer, frameInfo);

        using var frame = _graphics.BeginFrame(_surface);
        if (frame.View.Host == null)
        {
#if !BROWSER
            ConfigureSurface();
#endif
            return;
        }

        RecordAndSubmit(frame.View, _surface.Width, _surface.Height);
    }

    private unsafe void RecordAndSubmit(
        Entity presentTargetView,
        uint presentTargetWidth,
        uint presentTargetHeight)
    {
        using var encoder = Commands.CreateEncoder(_graphics);

        ComputePassDescriptor computePassDescriptor = new()
        {
            TimestampWrites = null,
            Label = null
        };
        using (var computePass = encoder.BeginComputePass(&computePassDescriptor))
        {
            computePass.SetPipeline(_computePipeline);
            computePass.SetBindGroup(0, _computeBindGroup);
            computePass.Dispatch(1);
        }

        DrawTrianglePass(encoder);
        DrawTexturePass(encoder, presentTargetView, presentTargetWidth, presentTargetHeight);

        var commandBuffer = encoder.Finish();
        Commands.SubmitAndRelease(_graphics, commandBuffer);
    }

    private unsafe void DrawTrianglePass(Encoder encoder)
    {
        var colorAttachment = Commands.ColorAttachment(
            _graphics,
            _renderTarget.View,
            new Color { R = 0.06, G = 0.08, B = 0.11, A = 1.0 });
        var depthAttachment = Commands.DepthStencilAttachment(_graphics, _depthView);

        var colorAttachments = new[] { colorAttachment };
        var descriptor = Commands.RenderPass(colorAttachments, &depthAttachment);
        using var pass = encoder.BeginRenderPass(&descriptor);
        pass.SetViewport(0, 0, _renderTarget.Width, _renderTarget.Height);
        pass.SetScissorRect(0, 0, _renderTarget.Width, _renderTarget.Height);

        ref var matData = ref _material.Get<MaterialResourceData>();
        pass.SetPipeline(matData.Pipeline);
        if (matData.BindGroups[0] is { Host: not null } bg0)
            pass.SetBindGroup(0, bg0);

        Mesh.Draw(pass, _mesh);
    }

    private unsafe void DrawTexturePass(
        Encoder encoder,
        Entity targetView,
        uint targetWidth,
        uint targetHeight)
    {
        var attachment = Commands.ColorAttachment(
            _graphics,
            targetView,
            new Color { R = 0.02, G = 0.025, B = 0.035, A = 1.0 });
        var descriptor = Commands.RenderPass(&attachment);

        using var pass = encoder.BeginRenderPass(&descriptor);
        pass.SetViewport(0, 0, targetWidth, targetHeight);
        pass.SetScissorRect(0, 0, targetWidth, targetHeight);
        pass.SetPipeline(_texturePipeline);
        pass.SetBindGroup(0, _textureBindGroup);
        pass.Draw(6);
    }

    private static MeshVertex[] TriangleVertices() =>
    [
        new(new Vector3(-0.86f, -0.70f, 0), new Vector3(0, 0, 1), new Vector3(0.95f, 0.25f, 0.18f)),
        new(new Vector3(0.76f, -0.62f, 0), new Vector3(0, 0, 1), new Vector3(0.08f, 0.72f, 0.96f)),
        new(new Vector3(-0.06f, 0.86f, 0), new Vector3(0, 0, 1), new Vector3(0.98f, 0.76f, 0.18f)),
        new(new Vector3(-0.58f, -0.12f, 0), new Vector3(0, 0, 1), new Vector3(0.22f, 0.88f, 0.72f)),
        new(new Vector3(0.22f, -0.20f, 0), new Vector3(0, 0, 1), new Vector3(0.72f, 0.34f, 0.96f)),
        new(new Vector3(-0.18f, 0.52f, 0), new Vector3(0, 0, 1), new Vector3(0.96f, 0.92f, 0.48f)),
        new(new Vector3(0.16f, -0.76f, 0), new Vector3(0, 0, 1), new Vector3(0.98f, 0.44f, 0.28f)),
        new(new Vector3(0.92f, 0.04f, 0), new Vector3(0, 0, 1), new Vector3(0.24f, 0.86f, 0.96f)),
        new(new Vector3(0.42f, 0.74f, 0), new Vector3(0, 0, 1), new Vector3(0.96f, 0.88f, 0.24f))
    ];

    private static uint[] TriangleIndices() => [0, 1, 2, 3, 4, 5, 6, 7, 8, 1, 7, 4];

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _surface.Dispose();
#if !BROWSER
        _windowStage?.Dispose();
        _world?.Dispose();
#endif
        _graphics.Dispose();
    }
}
