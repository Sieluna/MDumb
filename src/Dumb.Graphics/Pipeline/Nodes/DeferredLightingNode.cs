using System.Runtime.CompilerServices;
using Dumb.Graphics.Material;
using Sia;
using Silk.NET.WebGPU;

namespace Dumb.Graphics.Pipeline.Nodes;

public sealed class DeferredLightingNode : RenderNode
{
    private readonly GraphicsContext _ctx;
    private readonly CameraSyncSystem _cameraSync;
    private readonly LightSyncSystem _lightSync;
    private readonly GBuffer _gbuffer;
    private readonly TextureFormat _surfaceFormat;
    private readonly ShadowPassNode? _shadowNode;

    private Entity _sampler;
    private Entity _pipelineLayout;
    private Entity _pipeline;
    private Entity? _cachedGroup1;
    private Entity? _cachedGroup2;
    private Entity? _cachedFrameBg;
    private int _cachedCameraBufferId = -1;
    private int _cachedGbufferKey;
    private int _cachedShadowKey;
    private bool _materialCreated;

    // Dummy resources for no-shadow path
    private Entity _dummyShadowSampler;
    private Entity _dummyShadowUniforms;

    public Entity SwapchainView { get; set; }

    public DeferredLightingNode(
        GraphicsContext ctx,
        CameraSyncSystem cameraSync,
        LightSyncSystem lightSync,
        GBuffer gbuffer,
        TextureFormat surfaceFormat,
        ShadowPassNode? shadowNode = null)
    {
        _ctx = ctx;
        _cameraSync = cameraSync;
        _lightSync = lightSync;
        _gbuffer = gbuffer;
        _surfaceFormat = surfaceFormat;
        _shadowNode = shadowNode;
    }

    public override void DeclareResources()
    {
        Inputs.Add(new ResourceHandle(_gbuffer.RT0View, "GBuffer_BaseColor"));
        Inputs.Add(new ResourceHandle(_gbuffer.RT1View, "GBuffer_NormalRoughness"));
        Inputs.Add(new ResourceHandle(_gbuffer.RT2View, "GBuffer_PBR"));
        Inputs.Add(new ResourceHandle(_gbuffer.DepthView, "GBuffer_Depth"));
        Outputs.Add(new ResourceHandle(SwapchainView, "SwapchainOutput"));
    }

    public override void Update(World world)
    {
        var cameraBuffer = _cameraSync.FirstBuffer;
        if (cameraBuffer is not { } camBuf) return;

        if (!_materialCreated)
        {
            _sampler = Samplers.LinearClamp(_ctx);
            (_pipeline, _pipelineLayout) = DeferredLightingMaterial.CreatePipeline(_ctx, _surfaceFormat);
            _materialCreated = true;
        }

        EnsureDummyResources();

        var cameraId = camBuf.Id.Value;
        var gbufferKey = HashCode.Combine(
            _gbuffer.RT0View.Id.Value, _gbuffer.RT1View.Id.Value,
            _gbuffer.RT2View.Id.Value, _gbuffer.DepthView.Id.Value);
        var shadowKey = _shadowNode is { } sn
            ? HashCode.Combine(
                sn.GetShadowView(0).Id.Value,
                sn.GetShadowView(1).Id.Value,
                sn.GetShadowView(2).Id.Value,
                sn.GetShadowView(3).Id.Value,
                sn.CombinedShadowUniforms.Id.Value)
            : 0;

        if (_cachedCameraBufferId == cameraId
            && _cachedGbufferKey == gbufferKey
            && _cachedShadowKey == shadowKey)
            return;

        var matConfig = BuildMaterialConfig(camBuf);
        var bindGroups = matConfig.CreateBindGroups(_ctx, _pipelineLayout);
        var newGroup1 = bindGroups[1];
        var newGroup2 = bindGroups[2];

        ref var plData = ref _pipelineLayout.Get<PipelineLayoutData>();
        var frameBgl = plData.BindGroupLayouts?[0];
        Entity? newFrameBg = null;
        if (frameBgl?.Host != null)
        {
            var shadowBuf = _shadowNode?.CombinedShadowUniforms ?? _dummyShadowUniforms;
            newFrameBg = Pipelines.BindGroup(_ctx, frameBgl,
            [
                Binding.Buffer(0, camBuf, (nuint)Unsafe.SizeOf<CameraUniforms>()),
                Binding.Buffer(1, shadowBuf,
                    (nuint)(ShadowPassNode.MaxShadowLights * Unsafe.SizeOf<ShadowUniforms>())),
            ]);
        }

        // Release old only after new are created successfully.
        if (_cachedGroup1 is { } old1)
            Pipelines.ReleaseBindGroup(_ctx, old1);
        if (_cachedGroup2 is { } old2)
            Pipelines.ReleaseBindGroup(_ctx, old2);
        if (_cachedFrameBg is { } oldFg)
            Pipelines.ReleaseBindGroup(_ctx, oldFg);

        _cachedGroup1 = newGroup1;
        _cachedGroup2 = newGroup2;
        _cachedFrameBg = newFrameBg;

        _cachedCameraBufferId = cameraId;
        _cachedGbufferKey = gbufferKey;
        _cachedShadowKey = shadowKey;
    }

    private void EnsureDummyResources()
    {
        if (_shadowNode is not null) return;

        if (_dummyShadowSampler.Host == null)
            _dummyShadowSampler = Samplers.ShadowComparison(_ctx);
        if (_dummyShadowUniforms.Host == null)
        {
            var size = ShadowPassNode.MaxShadowLights * Unsafe.SizeOf<ShadowUniforms>();
            _dummyShadowUniforms = Buffers.Create(_ctx,
                (ulong)size, BufferUsage.Uniform | BufferUsage.CopyDst);
            Span<ShadowUniforms> defaults = stackalloc ShadowUniforms[ShadowPassNode.MaxShadowLights];
            defaults.Fill(ShadowUniforms.Default);
            Buffers.Write(_ctx, _dummyShadowUniforms, defaults);
        }
    }

    private DeferredLightingMaterial BuildMaterialConfig(Entity camBuf)
    {
        var sn = _shadowNode;
        return new DeferredLightingMaterial
        {
            GBufferRT0 = _gbuffer.RT0View,
            GBufferRT1 = _gbuffer.RT1View,
            GBufferRT2 = _gbuffer.RT2View,
            GBufferDepth = _gbuffer.DepthView,
            Sampler = _sampler,
            CameraBuffer = camBuf,
            LightBuffer = _lightSync.LightBuffer,
            ShadowSampler = sn?.Sampler ?? _dummyShadowSampler,
            ShadowMap0 = sn?.GetShadowView(0) ?? _gbuffer.DepthView,
            ShadowMap1 = sn?.GetShadowView(1) ?? _gbuffer.DepthView,
            ShadowMap2 = sn?.GetShadowView(2) ?? _gbuffer.DepthView,
            ShadowMap3 = sn?.GetShadowView(3) ?? _gbuffer.DepthView,
            CombinedShadowUniforms = sn?.CombinedShadowUniforms ?? _dummyShadowUniforms,
        };
    }

    public override void Execute(World world, RenderContext renderCtx)
    {
        if (!_materialCreated
            || _cachedGroup1 is null
            || _cachedGroup2 is null
            || _cachedFrameBg is null
            || SwapchainView.Host == null)
            return;

        var item = new PhaseItem(
            DrawEntity: default,
            Pipeline: _pipeline,
            PipelineLayout: _pipelineLayout,
            BindGroups: [_cachedFrameBg, _cachedGroup1, _cachedGroup2],
            Mesh: default!,
            SubMeshIndex: 0
        );

        unsafe
        {
            var colorAttachment = Commands.ColorAttachment(_ctx, SwapchainView,
                new Color { R = 0, G = 0, B = 0, A = 1 });

            var renderDesc = Commands.RenderPass(&colorAttachment);
            var encoder = Commands.CreateEncoder(_ctx);
            var pass = encoder.BeginRenderPass(&renderDesc);

            DrawCommand.DrawFullScreen(_ctx, ref pass, item);

            pass.End();
            var cb = encoder.Finish();
            renderCtx.AddCommandBuffer(cb);
        }
    }

    public override void Dispose()
    {
        if (_cachedFrameBg is { Host: not null } fbg) Pipelines.ReleaseBindGroup(_ctx, fbg);
        if (_cachedGroup1 is { Host: not null } g1) Pipelines.ReleaseBindGroup(_ctx, g1);
        if (_cachedGroup2 is { Host: not null } g2) Pipelines.ReleaseBindGroup(_ctx, g2);
        if (_pipeline is { Host: not null }) Pipelines.ReleaseRenderPipeline(_ctx, _pipeline);
        if (_pipelineLayout is { Host: not null }) Pipelines.ReleasePipelineLayout(_ctx, _pipelineLayout);
        if (_sampler is { Host: not null }) Samplers.Release(_ctx, _sampler);
        if (_dummyShadowSampler is { Host: not null }) Samplers.Release(_ctx, _dummyShadowSampler);
        if (_dummyShadowUniforms is { Host: not null }) Buffers.Release(_ctx, _dummyShadowUniforms);
    }
}
