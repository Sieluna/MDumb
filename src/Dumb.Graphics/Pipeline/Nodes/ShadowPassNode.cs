using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sia;
using Silk.NET.WebGPU;

namespace Dumb.Graphics.Pipeline.Nodes;

public sealed class ShadowPassNode : RenderNode
{
    public const int MaxShadowLights = 4;
    private const uint ShadowMapSize = 2048;
    private const float ShadowArea = 10f;           // orthographic half-extent (±10 world units)
    private const float LightDistance = 20f;        // light position = -lightDir * LightDistance

    private readonly GraphicsContext _ctx;
    private readonly PhaseQueueSystem _phaseQueue;
    private readonly TransformSyncSystem _transformSync;
    private readonly LightSyncSystem _lightSync;

    // Per-light resources
    private readonly Entity[] _shadowTextures = new Entity[MaxShadowLights];
    private readonly Entity[] _shadowViews = new Entity[MaxShadowLights];
    private readonly Entity[] _shadowUniforms = new Entity[MaxShadowLights];
    private Entity _combinedUniforms = null!;
    private int _activeCount;

    // Shared resources
    private Entity _sampler = null!;
    private Entity _shader = null!;
    private Entity _pipelineLayout = null!;
    private Entity _pipeline = null!;
    private Entity _frameBgl = null!;
    private Entity? _frameBg;
    private int _frameBgKey;

    private bool _initialized;

    public Entity Sampler => _sampler;
    public Entity ShadowTexture => _shadowTextures[0];
    public Entity CombinedShadowUniforms => _combinedUniforms;
    public int ActiveCount => _activeCount;

    public Entity GetShadowView(int index) => _shadowViews[index];
    public Entity GetShadowUniformBuffer(int index) => _shadowUniforms[index];

    public ShadowPassNode(
        GraphicsContext ctx,
        PhaseQueueSystem phaseQueue,
        TransformSyncSystem transformSync,
        LightSyncSystem lightSync)
    {
        _ctx = ctx;
        _phaseQueue = phaseQueue;
        _transformSync = transformSync;
        _lightSync = lightSync;
    }

    public override void DeclareResources() { }

    public override void Update(World world)
    {
        if (_initialized) return;

        _sampler = Samplers.ShadowComparison(_ctx);

        // Combined shadow uniforms buffer (4 × ShadowUniforms for deferred shader)
        _combinedUniforms = Buffers.Create(_ctx,
            (ulong)(MaxShadowLights * Unsafe.SizeOf<ShadowUniforms>()),
            BufferUsage.Uniform | BufferUsage.CopyDst);

        // Per-light shadow maps and uniform buffers
        for (var i = 0; i < MaxShadowLights; i++)
        {
            _shadowTextures[i] = Textures.Create2D(_ctx, ShadowMapSize, ShadowMapSize,
                TextureFormat.Depth32float,
                TextureUsage.RenderAttachment | TextureUsage.TextureBinding);
            _shadowViews[i] = Textures.CreateDepthView(_ctx, _shadowTextures[i]);
            _shadowUniforms[i] = Buffers.Create(_ctx,
                (ulong)Unsafe.SizeOf<ShadowUniforms>(),
                BufferUsage.Uniform | BufferUsage.CopyDst);
            Buffers.Write(_ctx, _shadowUniforms[i], ShadowUniforms.Default);
        }

        // Frame bind group layout: light VP (uniform) + model (dynamic offset)
        BindingLayout[] fbgl =
        [
            BindingLayout.UniformBuffer(0, ShaderStage.Vertex,
                (ulong)Unsafe.SizeOf<ShadowUniforms>()),
            BindingLayout.UniformBuffer(2, ShaderStage.Vertex, 64,
                hasDynamicOffset: true)
        ];
        _frameBgl = Pipelines.BindGroupLayout(_ctx, fbgl);

        _shader = Shaders.Wgsl(_ctx, ShadowShader.Source);
        _pipelineLayout = Pipelines.Layout(_ctx, [_frameBgl]);

        VertexBufferLayoutDescriptor[] vertLayouts =
        [
            new([new VertexAttributeLayout(0, VertexFormat.Float32x3, 0)],
                64, VertexStepMode.Vertex)
        ];

        _pipeline = Pipelines.RenderDepthOnly(_ctx, _shader, _pipelineLayout,
            TextureFormat.Depth32float, vertLayouts,
            vertexEntryPoint: "vs_main", fragmentEntryPoint: "fs_main");

        _initialized = true;
    }

    public override void Execute(World world, RenderContext renderCtx)
    {
        if (!_initialized) return;

        _activeCount = 0;
        var lightCount = _lightSync.LightCount;
        if (lightCount == 0) return;

        // Store uniforms locally so we can write the combined buffer later
        Span<ShadowUniforms> uniformsArray = stackalloc ShadowUniforms[MaxShadowLights];
        uniformsArray.Fill(ShadowUniforms.Default);

        // Build shadow maps for shadow-casting directional lights (up to MaxShadowLights)
        for (var slot = 0; slot < lightCount && _activeCount < MaxShadowLights; slot++)
        {
            if (_lightSync.GetLightType(slot) != 0) continue;
            if (!_lightSync.GetLightCastsShadows(slot)) continue;

            var si = _activeCount;
            var lightDir = _lightSync.GetLightDirection(slot);
            var lightVP = ComputeDirectionalLightVP(lightDir);

            uniformsArray[si] = new ShadowUniforms
            {
                LightVP = lightVP,
                TexelSize = new Vector2(1f / ShadowMapSize, 1f / ShadowMapSize),
                DepthBias = 0.002f,
                NormalBias = 0.02f,
            };
            Buffers.Write(_ctx, _shadowUniforms[si], in uniformsArray[si]);

            // Update the light buffer: set shadow index (1-based) for this light
            Buffers.Write(_ctx, _lightSync.LightBuffer,
                (uint)(si + 1),
                (ulong)(slot * GPULight.Size + 56));

            var frameBg = GetOrCreateFrameBg(si);
            if (frameBg is null) continue;

            unsafe
            {
                var ds = Commands.DepthStencilAttachment(_ctx, _shadowViews[si],
                    LoadOp.Clear, StoreOp.Store, 1.0f);

                var renderDesc = new RenderPassDescriptor
                {
                    ColorAttachmentCount = 0,
                    ColorAttachments = null,
                    DepthStencilAttachment = &ds,
                    OcclusionQuerySet = null,
                    TimestampWrites = null,
                    Label = null
                };

                var encoder = Commands.CreateEncoder(_ctx);
                var pass = encoder.BeginRenderPass(&renderDesc);
                pass.SetViewport(0, 0, ShadowMapSize, ShadowMapSize, 0, 1);
                pass.SetScissorRect(0, 0, ShadowMapSize, ShadowMapSize);

                foreach (var binItems in _phaseQueue.OpaquePhase.Bins)
                {
                    foreach (var item in binItems)
                    {
                        pass.SetPipeline(_pipeline);
                        var modelOff = item.ModelOffset;
                        pass.SetBindGroup(0, frameBg, new ReadOnlySpan<uint>(ref modelOff));
                        ref var meshData = ref item.Mesh.Get<MeshResourceData>();
                        for (var j = 0; j < meshData.VertexBuffers.Length; j++)
                            pass.SetVertexBuffer((uint)j, meshData.VertexBuffers[j]);
                        pass.SetIndexBuffer(meshData.IndexBuffer, meshData.IndexFormat);
                        var subMesh = meshData.SubMeshes[item.SubMeshIndex];
                        pass.DrawIndexed(subMesh.IndexCount, 1,
                            subMesh.IndexStart, (int)subMesh.VertexStart, 0);
                    }
                }

                pass.End();
                var cb = encoder.Finish();
                renderCtx.AddCommandBuffer(cb);
            }

            _activeCount++;
        }

        // Zero out shadow indices for non-shadow-casting lights
        for (var slot = 0; slot < lightCount; slot++)
        {
            if (_lightSync.GetLightType(slot) != 0)
            {
                Buffers.Write(_ctx, _lightSync.LightBuffer,
                    0u, (ulong)(slot * GPULight.Size + 56));
                continue;
            }
            if (!_lightSync.GetLightCastsShadows(slot))
            {
                Buffers.Write(_ctx, _lightSync.LightBuffer,
                    0u, (ulong)(slot * GPULight.Size + 56));
            }
        }

        // Write combined uniforms buffer for deferred shader
        Buffers.Write(_ctx, _combinedUniforms, uniformsArray);
    }

    private Entity? GetOrCreateFrameBg(int shadowIndex)
    {
        if (_frameBgl.Host == null) return null;
        var modelBuffer = _transformSync.ModelBuffer;
        if (modelBuffer?.Host == null) return null;

        var shadowBuf = _shadowUniforms[shadowIndex];
        if (shadowBuf.Host == null) return null;

        var key = HashCode.Combine(shadowBuf.Id.Value, modelBuffer.Id.Value);
        if (_frameBg is not null && _frameBgKey == key)
            return _frameBg;

        if (_frameBg is { Host: not null } oldBg)
            Pipelines.ReleaseBindGroup(_ctx, oldBg);

        Binding[] bindings =
        [
            Binding.Uniform<ShadowUniforms>(0, shadowBuf),
            Binding.Buffer(2, modelBuffer, (nuint)Unsafe.SizeOf<Matrix4x4>()),
        ];

        _frameBg = Pipelines.BindGroup(_ctx, _frameBgl, bindings);
        _frameBgKey = key;
        return _frameBg;
    }

    private static Matrix4x4 ComputeDirectionalLightVP(Vector3 lightDir)
    {
        var lightPos = -lightDir * LightDistance;
        var up = Math.Abs(lightDir.Y) > 0.99f ? Vector3.UnitZ : Vector3.UnitY;
        var view = Matrix4x4.CreateLookAt(lightPos, lightPos + lightDir, up);
        var proj = Engine.Projection.OrthographicGPUSymmetric(ShadowArea, ShadowArea, 0.1f, ShadowArea * 4f);
        return view * proj;
    }

    public override void Dispose()
    {
        if (_frameBg is { Host: not null } fbg) Pipelines.ReleaseBindGroup(_ctx, fbg);
        if (_frameBgl is { Host: not null }) Pipelines.ReleaseBindGroupLayout(_ctx, _frameBgl);
        if (_pipeline is { Host: not null }) Pipelines.ReleaseRenderPipeline(_ctx, _pipeline);
        if (_pipelineLayout is { Host: not null }) Pipelines.ReleasePipelineLayout(_ctx, _pipelineLayout);
        if (_sampler is { Host: not null }) Samplers.Release(_ctx, _sampler);
        if (_shader is { Host: not null }) Shaders.Release(_ctx, _shader);

        if (_combinedUniforms.Host != null) Buffers.Release(_ctx, _combinedUniforms);
        for (var i = 0; i < MaxShadowLights; i++)
        {
            if (_shadowUniforms[i].Host != null) Buffers.Release(_ctx, _shadowUniforms[i]);
            if (_shadowViews[i].Host != null) Textures.ReleaseView(_ctx, _shadowViews[i]);
            if (_shadowTextures[i].Host != null) Textures.Release(_ctx, _shadowTextures[i]);
        }
    }
}
