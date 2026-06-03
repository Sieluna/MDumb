using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Dumb.Engine.Cameras;
using Dumb.Engine.Lighting;
using Dumb.Engine.Mesh;
using Dumb.Engine.Transform;
using Dumb.Graphics;
using Dumb.Graphics.Config;
using Dumb.Graphics.Pipeline;
using Dumb.Graphics.Pipeline.Nodes;
using Dumb.Graphics.Materials;
using Dumb.Graphics.Resources;
using Dumb.Engine.Render;
using Sia;
using Silk.NET.WebGPU;
using RenderPipeline = Dumb.Graphics.Pipeline.RenderPipeline;
#if BROWSER
using System.Text;
using Dumb.Emscripten;
#else
using Dumb.Engine.Input;
using Dumb.Engine.Window;
#endif

namespace Dumb.Graphics.Deferred.Example;

public sealed class ExampleApp : IDisposable
{
    private const int NativeWidth = 1280;
    private const int NativeHeight = 720;

    private readonly GraphicsContext _graphics;
    private readonly World _engineWorld;

#if !BROWSER
    private Entity _window;
    private SystemStage _engineStage;
    private CameraController _cameraController;
#endif
    private GraphicsSurface _surface;

    private Entity _cameraEntity;
    private GBuffer _gbuffer;
    private RenderPipeline _pipeline;
    private ShadowPassNode _shadowNode;
    private DeferredLightingNode _deferredLightingNode;
    private ForwardPassNode _forwardNode;
    private GpuMeshRegistry _meshRegistry;

    private Entity _pbrMaterialEntity;
    private Entity _forwardMaterialEntity;
    private readonly List<Entity> _meshEntities = [];

    private int _frame;
    private bool _disposed;

#if BROWSER
    private static GCHandle s_browserHandle;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int BrowserAnimationFrame(double timeMs, void* userData)
    {
        var app = (ExampleApp)GCHandle.FromIntPtr((nint)userData).Target!;
        app.TickFrame((float)(timeMs / 1000.0));
        app._graphics.Tick();
        return 1;
    }
#endif

    public ExampleApp()
    {
        _engineWorld = new World();
        _graphics = new GraphicsContext();
        _graphics.AttachToParentWorld(_engineWorld);
    }

#if !BROWSER
    public bool ShouldClose => _window.Get<WindowState>().ShouldClose;
#endif

    public async Task RunAsync()
    {
        var adapterOptions = new RequestAdapterOptions
        {
            PowerPreference = PowerPreference.HighPerformance,
            ForceFallbackAdapter = false,
            BackendType = BackendType.Undefined,
            CompatibleSurface = null
        };
        var deviceDescriptor = new DeviceDescriptor
        {
            RequiredLimits = null,
            DefaultQueue = new QueueDescriptor { Label = null },
            DeviceLostCallback = default,
            DeviceLostUserdata = null
        };

#if BROWSER
        CreateBrowserSurface();
        _surface.Width = (uint)NativeWidth;
        _surface.Height = (uint)NativeHeight;
        GraphicsContext.SetCompatibleSurface(ref adapterOptions, _surface);
        await _graphics.InitializeAsync(adapterOptions, deviceDescriptor);
#else
        await _graphics.InitializeAsync(adapterOptions, deviceDescriptor);

        _window = _engineWorld.CreateWindow(new WindowDescriptor
        {
            Width = NativeWidth,
            Height = NativeHeight,
            Title = "Dumb.Graphics — Deferred Pipeline",
            Visible = true
        });

        _engineStage = SystemChain.Empty
            .Add<WindowSystem>()
            .Add<InputSystem>()
            .CreateStage(_engineWorld);

        CreateSurface();
        _surface.Format = _graphics.GetSurfacePreferredFormat(_surface);
        Console.WriteLine($"[Deferred] Surface format: {_surface.Format}");
        ConfigureSurface();

        _cameraController = CameraController.CreateFreeLook(new Vector3(0, 3, 0), 8f,
            MathF.PI / 4f, MathF.PI / 4f);
#endif

        SetupErrorCallback();

#if BROWSER
        _surface.Format = _graphics.GetSurfacePreferredFormat(_surface);
        Console.WriteLine($"[Deferred] Surface format: {_surface.Format}");
        _graphics.ConfigureSurface(_surface);
#endif

        CreatePipeline();
        CreateScene();

#if BROWSER
        StartBrowserAnimationLoop();
        Console.WriteLine("Dumb.Graphics browser deferred example started.");
        await new TaskCompletionSource().Task;
#else
        var clock = Stopwatch.StartNew();
        Console.WriteLine("Dumb.Graphics native deferred example started. Close the window to exit.");

        while (!_window.Get<WindowState>().ShouldClose)
        {
            _engineStage.Tick();

            ref var window = ref _window.Get<WindowState>();
            if (window.Width <= 0 || window.Height <= 0)
                continue;

            var width = (uint)window.FramebufferWidth;
            var height = (uint)window.FramebufferHeight;
            if (width != _surface.Width || height != _surface.Height)
            {
                ConfigureSurface();
                _gbuffer.Resize(width, height);
            }

            var elapsed = (float)clock.Elapsed.TotalSeconds;
            UpdateCamera(elapsed);
            TickFrame(elapsed);
            _graphics.Tick();
        }
#endif
    }

    //Pipeline ──────────────────────────────────────────────────

    private void CreatePipeline()
    {
        _gbuffer = new GBuffer(_graphics, (uint)NativeWidth, (uint)NativeHeight);

        _cameraEntity = _engineWorld.Create(HList.From(
            Camera.CreateFreeLook(new Vector3(0, 3, 0), 8f, MathF.PI / 4f, MathF.PI / 4f),
            new LocalTransform { Position = new Vector3(0, 3, -8) }));

        var cameraSync = new CameraSyncSystem(_graphics);
        var transformSync = new TransformSyncSystem(_graphics);
        var lightSync = new LightSyncSystem(_graphics);
        _meshRegistry = new GpuMeshRegistry(_graphics);

        var settingsSystem = new RenderSettingsSystem(_graphics);

        var phaseQueue = new PhaseQueueSystem(_graphics, cameraSync, transformSync, _meshRegistry);
        var config = PipelineConfig.Default;

        _pipeline = new RenderPipeline(_graphics, config,
            cameraSync, transformSync, lightSync, settingsSystem, phaseQueue);

        _shadowNode = new ShadowPassNode(_graphics, phaseQueue, transformSync, lightSync);
        _deferredLightingNode = new DeferredLightingNode(
            _graphics, cameraSync, lightSync, _gbuffer, _surface.Format, _shadowNode);
        _forwardNode = new ForwardPassNode(_graphics, phaseQueue, _gbuffer);

        _pipeline.Graph.AddNode(_shadowNode);
        _pipeline.Graph.AddNode(new GBufferPassNode(_graphics, phaseQueue, _gbuffer));
        _pipeline.Graph.AddNode(_deferredLightingNode);
        _pipeline.Graph.AddNode(_forwardNode);

        var result = _pipeline.Graph.Compile();
        if (!result.Success)
            foreach (var err in result.Errors)
                Console.WriteLine($"[Graph Error] {err}");
        foreach (var warn in result.Warnings)
            Console.WriteLine($"[Graph Warning] {warn}");
    }

    //Scene ─────────────────────────────────────────────────────

    private void CreateScene()
    {
        // Lights
        _engineWorld.Create(HList.From(
            new Light(
                Type: LightType.Directional,
                Color: new Vector3(1.0f, 0.95f, 0.85f),
                Intensity: 2.0f,
                Direction: new Vector3(0.3f, -0.7f, 0.6f),
                CastsShadows: true),
            new LocalTransform()));

        _engineWorld.Create(HList.From(
            Light.PointLight(new Vector3(0.2f, 0.4f, 1.0f), 10.0f, 8.0f),
            new LocalTransform { Position = new Vector3(0, 2, 0) }));

        // Global default render volume — ECS components define settings
        _engineWorld.Create(HList.From(
            Engine.Render.RenderVolume.Global(priority: -100f),
            new Engine.Render.ShadowSettings(),
            new Engine.Render.PostProcessSettings()));

        // Default textures for PBR material
        var defaultWhite = CreateDefaultTextureView(255, 255, 255, 255);
        var defaultNormal = CreateDefaultTextureView(128, 128, 255, 255);
        var defaultMRO = CreateDefaultTextureView(0, 255, 255, 255);
        var defaultBlack = CreateDefaultTextureView(0, 0, 0, 255);

        // PBR material
        var mat = new PBRMaterial
        {
            Parameters = new PBRMaterialParameters
            {
                BaseColor = Vector3.One,
                Metallic = 0.0f,
                Roughness = 0.5f,
                Occlusion = 1.0f,
                Emissive = Vector3.Zero
            },
            BaseColorTexture = defaultWhite,
            NormalTexture = defaultNormal,
            MROTexture = defaultMRO,
            EmissiveTexture = defaultBlack,
            Sampler = Samplers.LinearClamp(_graphics)
        };
        _pbrMaterialEntity = Dumb.Graphics.Resources.Materials.Create(_graphics, mat);

        ref var matData = ref _pbrMaterialEntity.Get<MaterialResourceData>();
        ref var plData = ref matData.PipelineLayout.Get<PipelineLayoutData>();
        _pipeline.PhaseQueue.FrameBindGroupLayout = plData.BindGroupLayouts![0];

        // Forward transparent PBR material (must set surface format before creation)
        ForwardPBRMaterial.SurfaceFormat = _surface.Format;
        var fwdMat = new ForwardPBRMaterial
        {
            Parameters = new ForwardPBRParams
            {
                BaseColor = new Vector3(0.3f, 0.7f, 1.0f),
                Alpha = 0.4f,
                Roughness = 0.3f,
                Metallic = 0.1f,
                Occlusion = 1.0f,
                Emissive = Vector3.Zero
            },
            BaseColorTexture = defaultWhite,
            NormalTexture = defaultNormal,
            MROTexture = defaultMRO,
            EmissiveTexture = defaultBlack,
            Sampler = Samplers.LinearClamp(_graphics)
        };
        _forwardMaterialEntity = Dumb.Graphics.Resources.Materials.Create(_graphics, fwdMat);

        // Geometry — all opaque (floor + 5 boxes)
        AddFloor(new Vector3(-5, 0, -5), new Vector3(5, 0, -5),
            new Vector3(5, 0, 5), new Vector3(-5, 0, 5),
            new Vector3(0.3f, 0.3f, 0.35f), Vector3.UnitY);

        AddBox(new Vector3(-2, 1, 0), 1f, new Vector3(0.9f, 0.2f, 0.2f));
        AddBox(new Vector3(0, 1, 0), 1f, new Vector3(0.2f, 0.8f, 0.2f));
        AddBox(new Vector3(2, 1, 0), 1f, new Vector3(0.2f, 0.3f, 0.9f));
        AddBox(new Vector3(0, 2.5f, -2), 0.7f, new Vector3(0.9f, 0.7f, 0.2f));
        AddBox(new Vector3(0, 2.5f, 2), 0.7f, new Vector3(0.8f, 0.2f, 0.8f));

        foreach (var entity in _meshEntities)
            _meshRegistry.RegisterMaterial(entity, _pbrMaterialEntity);

        // Transparent box — offset to avoid clipping other geometry
        AddBox(new Vector3(1.5f, 1.5f, 1.5f), 1.2f, new Vector3(0.3f, 0.7f, 1.0f));
        _meshRegistry.RegisterMaterial(_meshEntities[^1], _forwardMaterialEntity);
    }

    //Per-frame ─────────────────────────────────────────────────

    public void TickFrame(float elapsed)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var frame = _graphics.BeginFrame(_surface);
        if (frame.View == null || frame.View.Host == null)
        {
#if !BROWSER
            ConfigureSurface();
#endif
            return;
        }

        _deferredLightingNode.SwapchainView = frame.View;
        _forwardNode.SwapchainView = frame.View;
        _pipeline.Tick();
        _frame++;
    }

#if !BROWSER
    private void UpdateCamera(float elapsed)
    {
        ref var input = ref _window.Get<WindowInput>();

        var lookDelta = _frame == 0
            ? Vector2.Zero
            : input.Mouse.NormalizedDelta;

        var move = Vector3.Zero;
        if (input.Keyboard[KeyCode.W].IsPressed) move.Z += 1;
        if (input.Keyboard[KeyCode.S].IsPressed) move.Z -= 1;
        if (input.Keyboard[KeyCode.A].IsPressed) move.X -= 1;
        if (input.Keyboard[KeyCode.D].IsPressed) move.X += 1;
        if (input.Keyboard[KeyCode.Q].IsPressed) move.Y -= 1;
        if (input.Keyboard[KeyCode.E].IsPressed) move.Y += 1;

        _cameraController.Update(_cameraEntity, 0.016f, lookDelta, 0, move, true);
    }
#endif

    //Geometry ──────────────────────────────────────────────────

    private void AddBox(Vector3 center, float size, Vector3 color)
    {
        var h = size / 2f;
        var verts = new List<MeshVertex>();
        var indices = new List<uint>();

        void Face(Vector3 origin, Vector3 right, Vector3 up, Vector3 normal)
        {
            uint b = (uint)verts.Count;
            verts.Add(new MeshVertex(origin, normal, color));
            verts.Add(new MeshVertex(origin + right, normal, color));
            verts.Add(new MeshVertex(origin + right + up, normal, color));
            verts.Add(new MeshVertex(origin + up, normal, color));
            indices.AddRange([b, b + 1, b + 2, b, b + 2, b + 3]);
        }

        Face(new Vector3(-h, -h,  h), new Vector3(size, 0, 0), new Vector3(0, size, 0),  Vector3.UnitZ);
        Face(new Vector3( h, -h, -h), new Vector3(-size, 0, 0), new Vector3(0, size, 0), -Vector3.UnitZ);
        Face(new Vector3(-h, -h, -h), new Vector3(0, 0, size), new Vector3(0, size, 0), -Vector3.UnitX);
        Face(new Vector3( h, -h,  h), new Vector3(0, 0, -size), new Vector3(0, size, 0),  Vector3.UnitX);
        Face(new Vector3(-h,  h,  h), new Vector3(size, 0, 0), new Vector3(0, 0, -size),  Vector3.UnitY);
        Face(new Vector3(-h, -h, -h), new Vector3(size, 0, 0), new Vector3(0, 0, size),  -Vector3.UnitY);

        AddMesh(MeshData.FromVertices(verts, indices, PBRMaterial.VertexDescriptor.Streams[0].Elements),
            center, Vector3.One);
    }

    private void AddFloor(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3,
        Vector3 color, Vector3 normal)
    {
        var center = (v0 + v1 + v2 + v3) / 4f;
        var data = MeshData.FromVertices(
        [
            new MeshVertex(v0, normal, color),
            new MeshVertex(v1, normal, color),
            new MeshVertex(v2, normal, color),
            new MeshVertex(v3, normal, color),
        ], [0u, 1, 2, 0, 2, 3],
        PBRMaterial.VertexDescriptor.Streams[0].Elements);
        AddMesh(data, center, Vector3.One);
    }

    private void AddMesh(MeshData data, Vector3 position, Vector3 scale)
    {
        var local = Affine3D.FromTRS(position, Quaternion.Identity, scale);
        var entity = _engineWorld.Create(HList.From(
            new VisibleEntity(),
            new LocalTransform { Position = position, Scale = scale },
            new GlobalTransform { Value = local }));

        _meshRegistry.RegisterMesh(entity, data);
        _meshEntities.Add(entity);
    }

    //Default texture ───────────────────────────────────────────

    private unsafe Entity CreateDefaultTextureView(byte r, byte g, byte b, byte a)
    {
        var texture = Textures.Create2D(_graphics, 1, 1, TextureFormat.Rgba8Unorm,
            TextureUsage.TextureBinding | TextureUsage.CopyDst);
        var view = Textures.CreateView2D(_graphics, texture, TextureFormat.Rgba8Unorm);

        byte* pixels = stackalloc byte[4];
        pixels[0] = r;
        pixels[1] = g;
        pixels[2] = b;
        pixels[3] = a;
        var destination = new ImageCopyTexture
        {
            Texture = (Texture*)texture.Get<TextureData>().NativePtr,
            MipLevel = 0,
            Origin = new Origin3D { X = 0, Y = 0, Z = 0 },
            Aspect = TextureAspect.All
        };
        var layout = new TextureDataLayout
        {
            Offset = 0,
            BytesPerRow = 4,
            RowsPerImage = 1
        };
        var size = new Extent3D { Width = 1, Height = 1, DepthOrArrayLayers = 1 };
        _graphics.Command.QueueWriteTexture(_graphics.NativeQueue, &destination, pixels, 4, &layout, &size);

        return view;
    }

    //Platform ──────────────────────────────────────────────────

#if !BROWSER
    private unsafe void CreateSurface()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new PlatformNotSupportedException("Requires GLFW Win32 handles.");

        var runtime = _window.Get<WindowRuntime>();
        var (hwnd, _, hInstance) = runtime.Native!.Win32!.Value;
        if (hwnd == 0) throw new InvalidOperationException("Failed to get GLFW Win32 window handle.");

        var hwndDesc = new SurfaceDescriptorFromWindowsHWND
        {
            Chain = new ChainedStruct { Next = null, SType = SType.SurfaceDescriptorFromWindowsHwnd },
            Hinstance = (void*)hInstance,
            Hwnd = (void*)hwnd
        };
        var surfaceDesc = new SurfaceDescriptor { NextInChain = (ChainedStruct*)&hwndDesc, Label = null };

        _surface = _graphics.CreateSurfaceFromNative((nint)_graphics.NativeApi.InstanceCreateSurface(
            (Instance*)_graphics.NativeInstanceHandle, &surfaceDesc));
        if (!_surface.IsValid) throw new InvalidOperationException("Failed to create WebGPU surface.");
    }
#endif

#if BROWSER
    private unsafe void CreateBrowserSurface()
    {
        var selectorBytes = Encoding.UTF8.GetBytes("#canvas\0");
        var selectorHandle = GCHandle.Alloc(selectorBytes, GCHandleType.Pinned);
        try
        {
            var canvasDesc = new SurfaceDescriptorFromCanvasHTMLSelector
            {
                Chain = new ChainedStruct { Next = null, SType = SType.SurfaceDescriptorFromCanvasHtmlSelector },
                Selector = (byte*)selectorHandle.AddrOfPinnedObject()
            };
            var surfaceDesc = new SurfaceDescriptor { NextInChain = (ChainedStruct*)&canvasDesc, Label = null };

            _surface = _graphics.CreateSurfaceFromNative((nint)_graphics.NativeApi.InstanceCreateSurface(
                (Instance*)_graphics.NativeInstanceHandle, surfaceDesc));

            if (!_surface.IsValid) throw new InvalidOperationException("Failed to create WebGPU surface from canvas.");
        }
        finally
        {
            selectorHandle.Free();
        }
    }
#endif

#if !BROWSER
    private void ConfigureSurface()
    {
        ref var window = ref _window.Get<WindowState>();
        _surface.Width = Math.Max(1u, (uint)window.FramebufferWidth);
        _surface.Height = Math.Max(1u, (uint)window.FramebufferHeight);
        _graphics.ConfigureSurface(_surface);
    }
#endif

#if BROWSER
    private unsafe void StartBrowserAnimationLoop()
    {
        s_browserHandle = GCHandle.Alloc(this);
        Emscripten.Emscripten.RequestAnimationFrameLoop(
            (delegate* unmanaged[Cdecl]<double, void*, int>)&BrowserAnimationFrame,
            (void*)GCHandle.ToIntPtr(s_browserHandle));
    }
#endif

    //Error callback ────────────────────────────────────────────

    private unsafe void SetupErrorCallback()
    {
        _graphics.NativeApi.DeviceSetUncapturedErrorCallback(
            (Device*)_graphics.NativeDeviceHandle,
            (delegate* unmanaged[Cdecl]<ErrorType, byte*, void*, void>)&WgpuErrorCallback,
            null);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void WgpuErrorCallback(ErrorType type, byte* message, void* userdata)
    {
        var msg = message != null ? Marshal.PtrToStringUTF8((nint)message) : "";
        Console.WriteLine($"[WGPU {type}] {msg}");
    }

    //Dispose ───────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _pipeline?.Dispose();
        _gbuffer.Dispose();
        _surface.Dispose();

#if !BROWSER
        _engineStage?.Dispose();
#endif
        _engineWorld.Dispose();
        _graphics.Dispose();
    }
}
