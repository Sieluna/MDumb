using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Dumb.Engine.Cameras;
using Dumb.Engine.Mesh;
using Dumb.Engine.Transform;
using Dumb.Graphics;
using Dumb.Graphics.Materials;
using Sia;
using Silk.NET.WebGPU;
#if BROWSER
using System.Text;
using Dumb.Emscripten;
#else
using Dumb.Engine.Input;
using Dumb.Engine.Window;
#endif

namespace Dumb.Unlit.Example;

public sealed class ExampleApp : IDisposable
{
    private const int NativeWidth = 1280;
    private const int NativeHeight = 720;

    private readonly GraphicsContext _graphics;
    private readonly World _engineWorld;

#if !BROWSER
    private Entity _window = null!;
    private SystemStage _engineStage = null!;
    private CameraController _cameraController;
#endif

    private Entity _cameraEntity = null!;
    private CameraSyncSystem _cameraSync = null!;
    private TransformSyncSystem _transformSync = null!;
    private SystemStage _syncStage = null!;

    private GraphicsSurface _surface;

    private Entity _unlitMaterial = null!;
    private Entity _frameBindGroup = null!;
    private Entity _depthTexture = null!;
    private Entity _depthView = null!;

    private readonly List<(Entity Mesh, Entity Entity)> _renderables = [];

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
            Title = "Dumb.Graphics — Unlit Material Test",
            Visible = true
        });

        _engineStage = SystemChain.Empty
            .Add<WindowSystem>()
            .Add<InputSystem>()
            .CreateStage(_engineWorld);

        CreateSurface();

        _surface.Format = _graphics.GetSurfacePreferredFormat(_surface);
        Console.WriteLine($"[Unlit] Surface format: {_surface.Format}");

        ConfigureSurface();

        _cameraController = CameraController.CreateFreeLook(new Vector3(0, 2, 0), 8f,
            MathF.PI / 4f, MathF.PI / 6f);
#endif

        SetupErrorCallback();

#if BROWSER
        _surface.Format = _graphics.GetSurfacePreferredFormat(_surface);
        Console.WriteLine($"[Unlit] Surface format: {_surface.Format}");
        _graphics.ConfigureSurface(_surface);
#endif

        _cameraEntity = _engineWorld.Create(HList.From(
            Camera.CreateFreeLook(new Vector3(0, 2, 0), 8f, MathF.PI / 4f, MathF.PI / 6f),
            new LocalTransform { Position = new Vector3(0, 3, -8) }));

        _cameraSync = new CameraSyncSystem(_graphics);
        _transformSync = new TransformSyncSystem(_graphics);

        _syncStage = SystemChain.Empty
            .Add(() => _cameraSync)
            .Add(() => _transformSync)
            .CreateStage(_graphics.World);

        CreateSceneResources();

#if BROWSER
        StartBrowserAnimationLoop();
        Console.WriteLine("Dumb.Graphics browser WebGPU unlit example started.");

        await new TaskCompletionSource().Task;
#else
        var clock = Stopwatch.StartNew();
        Console.WriteLine("Dumb.Graphics native unlit example started. Close the window to exit.");

        while (!_window.Get<WindowState>().ShouldClose)
        {
            _engineStage.Tick();

            ref var window = ref _window.Get<WindowState>();
            if (window.Width <= 0 || window.Height <= 0)
                continue;

            var width = (uint)window.FramebufferWidth;
            var height = (uint)window.FramebufferHeight;
            if (width != _surface.Width || height != _surface.Height)
                ConfigureSurface();

            var elapsed = (float)clock.Elapsed.TotalSeconds;
            UpdateCamera(elapsed);
            _syncStage.Tick();
            RenderFrame(elapsed);
            _graphics.Tick();
            _frame++;
        }
#endif
    }

    public void TickFrame(float elapsed)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _syncStage.Tick();
        RenderFrame(elapsed);
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

        var canvasDesc = new SurfaceDescriptorFromCanvasHTMLSelector
        {
            Chain = new ChainedStruct { Next = null, SType = SType.SurfaceDescriptorFromCanvasHtmlSelector },
            Selector = (byte*)selectorHandle.AddrOfPinnedObject()
        };
        var surfaceDesc = new SurfaceDescriptor { NextInChain = (ChainedStruct*)&canvasDesc, Label = null };

        _surface = _graphics.CreateSurfaceFromNative((nint)_graphics.NativeApi.InstanceCreateSurface(
            (Instance*)_graphics.NativeInstanceHandle, surfaceDesc));
        selectorHandle.Free();

        if (!_surface.IsValid) throw new InvalidOperationException("Failed to create WebGPU surface from canvas.");
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

    private void CreateSceneResources()
    {
        AddQuad(new Vector3(-5, 0, -5), new Vector3(5, 0, -5),
            new Vector3(5, 0, 5), new Vector3(-5, 0, 5),
            new Vector3(0.3f, 0.3f, 0.35f), Vector3.UnitY);

        AddBox(new Vector3(-2, 1, 0), 1f, new Vector3(0.9f, 0.2f, 0.2f));
        AddBox(new Vector3(0, 1, 0), 1f, new Vector3(0.2f, 0.8f, 0.2f));
        AddBox(new Vector3(2, 1, 0), 1f, new Vector3(0.2f, 0.3f, 0.9f));
        AddBox(new Vector3(0, 2.5f, -2), 0.7f, new Vector3(0.9f, 0.7f, 0.2f));
        AddBox(new Vector3(0, 2.5f, 2), 0.7f, new Vector3(0.8f, 0.2f, 0.8f));

        _depthTexture = Textures.Create2D(_graphics, NativeWidth, NativeHeight,
            TextureFormat.Depth32float, TextureUsage.RenderAttachment);
        _depthView = Textures.CreateDepthView(_graphics, _depthTexture);

        _syncStage.Tick();

        var unlitMat = new UnlitMaterial { Parameters = UnlitMaterialParameters.Default };
        var shader = unlitMat.GetShader(_graphics);
        var bglEntities = new Entity[UnlitMaterial.BindGroupLayouts.Length];
        for (var i = 0; i < bglEntities.Length; i++)
            bglEntities[i] = Pipelines.BindGroupLayout(_graphics, UnlitMaterial.BindGroupLayouts[i]);
        var layout = Pipelines.Layout(_graphics, bglEntities);
        var vertLayouts = Dumb.Graphics.Mesh.ToVertexBufferLayouts(UnlitMaterial.VertexDescriptor.Streams);
        var pipeline = Pipelines.Render(_graphics, shader, layout,
            _surface.Format, TextureFormat.Depth32float, vertLayouts, UnlitMaterial.Blend);
        var bindGroups = unlitMat.CreateBindGroups(_graphics, layout);
        _unlitMaterial = _graphics.CreateMaterialResource(pipeline, layout, bindGroups);

        var cameraBuffer = _cameraSync.GetUniformBuffer(_cameraEntity);
        ref var matData = ref _unlitMaterial.Get<MaterialResourceData>();
        var bgl0 = matData.PipelineLayout.Get<PipelineLayoutData>().BindGroupLayouts![0];
        _frameBindGroup = Pipelines.BindGroup(_graphics, bgl0,
        [
            Binding.Uniform<CameraUniforms>(0, cameraBuffer),
            Binding.Uniform<Matrix4x4>(1, _transformSync.ModelBuffer)
        ]);
    }

    private unsafe void RenderFrame(float time)
    {
        using var frame = _graphics.BeginFrame(_surface);
        if (frame.View.Host == null)
        {
#if !BROWSER
            ConfigureSurface();
#endif
            return;
        }

        using var encoder = Commands.CreateEncoder(_graphics);

        var ca = Commands.ColorAttachment(_graphics, frame.View,
            new Color { R = 0.06, G = 0.08, B = 0.11, A = 1.0 });
        var ds = Commands.DepthStencilAttachment(_graphics, _depthView);
        RenderPassDescriptor renderDesc = new()
        {
            ColorAttachmentCount = 1,
            ColorAttachments = &ca,
            DepthStencilAttachment = &ds,
        };

        {
            using var pass = encoder.BeginRenderPass(&renderDesc);
            pass.SetViewport(0, 0, _surface.Width, _surface.Height);
            pass.SetScissorRect(0, 0, _surface.Width, _surface.Height);

            ref var matData = ref _unlitMaterial.Get<MaterialResourceData>();
            pass.SetPipeline(matData.Pipeline);

            if (matData.BindGroups[1] is { Host: not null } matBg1)
                pass.SetBindGroup(1, matBg1);

            foreach (var (gpuMesh, entity) in _renderables)
            {
                if (!_transformSync.TryGetOffset(entity, out var offset))
                    continue;
                var dynOffset = (uint)offset;
                pass.SetBindGroup(0, _frameBindGroup, new ReadOnlySpan<uint>(&dynOffset, 1));
                Dumb.Graphics.Mesh.Draw(pass, gpuMesh);
            }
        }

        var cmd = encoder.Finish();
        Commands.SubmitAndRelease(_graphics, cmd);
    }

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

        Face(new Vector3(-h, -h,  h), new Vector3(size, 0, 0), new Vector3(0, size, 0), Vector3.UnitZ);
        Face(new Vector3( h, -h, -h), new Vector3(-size, 0, 0), new Vector3(0, size, 0), -Vector3.UnitZ);
        Face(new Vector3(-h, -h, -h), new Vector3(0, 0, size), new Vector3(0, size, 0), -Vector3.UnitX);
        Face(new Vector3( h, -h,  h), new Vector3(0, 0, -size), new Vector3(0, size, 0), Vector3.UnitX);
        Face(new Vector3(-h,  h,  h), new Vector3(size, 0, 0), new Vector3(0, 0, -size), Vector3.UnitY);
        Face(new Vector3(-h, -h, -h), new Vector3(size, 0, 0), new Vector3(0, 0, size), -Vector3.UnitY);

        AddMesh(MeshData.FromVertices(verts, indices, UnlitMaterial.VertexDescriptor.Streams[0].Elements), center, Vector3.One);
    }

    private void AddQuad(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3,
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
        UnlitMaterial.VertexDescriptor.Streams[0].Elements);
        AddMesh(data, center, Vector3.One);
    }

    private void AddMesh(MeshData data, Vector3 position, Vector3 scale)
    {
        var gpuMesh = Dumb.Graphics.Mesh.Create(_graphics, data);
        var local = Affine3D.FromTRS(position, Quaternion.Identity, scale);
        var entity = _engineWorld.Create(HList.From(
            new LocalTransform { Position = position, Scale = scale },
            new GlobalTransform { Value = local }));
        _renderables.Add((gpuMesh, entity));
    }

#if BROWSER
    private unsafe void StartBrowserAnimationLoop()
    {
        s_browserHandle = GCHandle.Alloc(this);
        Emscripten.Emscripten.RequestAnimationFrameLoop(
            (delegate* unmanaged[Cdecl]<double, void*, int>)&BrowserAnimationFrame,
            (void*)GCHandle.ToIntPtr(s_browserHandle));
    }
#endif

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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _surface.Dispose();

#if !BROWSER
        _engineStage?.Dispose();
#endif
        _syncStage?.Dispose();
        _engineWorld.Dispose();
        _graphics.Dispose();
    }
}
