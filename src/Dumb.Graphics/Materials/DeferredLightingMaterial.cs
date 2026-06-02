using Sia;
using Silk.NET.WebGPU;
using Dumb.Graphics.Pipeline.Nodes;

namespace Dumb.Graphics.Materials;

public struct DeferredLightingMaterial : IMaterial
{
    public Entity GBufferRT0;
    public Entity GBufferRT1;
    public Entity GBufferRT2;
    public Entity GBufferDepth;
    public Entity Sampler;
    public Entity CameraBuffer;
    public Entity LightBuffer;
    public Entity ShadowSampler;
    public Entity ShadowMap0;
    public Entity ShadowMap1;
    public Entity ShadowMap2;
    public Entity ShadowMap3;
    public Entity CombinedShadowUniforms;

    private Entity? _cachedShader;

    public static string Name => "DeferredLighting";

    public static Engine.Mesh.MeshDescriptor VertexDescriptor => new([]);

    private static readonly BindingLayout[][] s_bindGroupLayouts =
    [
        // Group 0: Frame (camera + shadow uniforms array)
        [
            BindingLayout.UniformBuffer(0, ShaderStage.Fragment, 336),
            BindingLayout.UniformBuffer(1, ShaderStage.Fragment,
                (ulong)(ShadowPassNode.MaxShadowLights * 80)),
        ],
        // Group 1: G-buffer textures + shadow sampler + shadow maps
        [
            BindingLayout.Sampler(0, ShaderStage.Fragment),
            BindingLayout.Texture(1, ShaderStage.Fragment),
            BindingLayout.Texture(2, ShaderStage.Fragment),
            BindingLayout.Texture(3, ShaderStage.Fragment),
            BindingLayout.Texture(4, ShaderStage.Fragment, TextureSampleType.Depth),
            BindingLayout.Sampler(5, ShaderStage.Fragment, SamplerBindingType.Comparison),
            BindingLayout.Texture(6, ShaderStage.Fragment, TextureSampleType.Depth),
            BindingLayout.Texture(7, ShaderStage.Fragment, TextureSampleType.Depth),
            BindingLayout.Texture(8, ShaderStage.Fragment, TextureSampleType.Depth),
            BindingLayout.Texture(9, ShaderStage.Fragment, TextureSampleType.Depth),
        ],
        // Group 2: Lights
        [
            BindingLayout.UniformBuffer(0, ShaderStage.Fragment, (ulong)(GPULight.MaxLights * GPULight.Size)),
        ]
    ];

    public static BindingLayout[][] BindGroupLayouts => s_bindGroupLayouts;

    public static TextureFormat[] ColorFormats => [TextureFormat.Rgba8Unorm];

    public static (Entity pipeline, Entity pipelineLayout) CreatePipeline(
        GraphicsContext ctx, TextureFormat surfaceFormat)
    {
        var result = ShaderLibrary.Preprocess(DeferredLightingShader.Vertex + "\n" + DeferredLightingFragmentShader.Source);
        var shader = Shaders.Wgsl(ctx, result.CombinedSource);

        var bgls = BindGroupLayouts;
        var bglEntities = new Entity[bgls.Length];
        for (var i = 0; i < bgls.Length; i++)
            bglEntities[i] = Pipelines.BindGroupLayout(ctx, bgls[i]);

        var pipelineLayout = Pipelines.Layout(ctx, bglEntities);

        var vertLayouts = Mesh.ToVertexBufferLayouts(
            VertexDescriptor.Streams);

        var pipeline = Pipelines.Render(ctx, shader, pipelineLayout,
            surfaceFormat, null, vertLayouts, blend: null);

        return (pipeline, pipelineLayout);
    }

    public Entity GetShader(GraphicsContext ctx)
    {
        if (_cachedShader is { Host: not null } s)
            return s;

        var result = ShaderLibrary.Preprocess(DeferredLightingShader.Vertex + "\n" + DeferredLightingFragmentShader.Source);
        _cachedShader = Shaders.Wgsl(ctx, result.CombinedSource);
        return _cachedShader!;
    }

    public Entity?[] CreateBindGroups(GraphicsContext ctx, Entity pipelineLayout)
    {
        ref var plData = ref pipelineLayout.Get<PipelineLayoutData>();
        var bgl1 = plData.BindGroupLayouts?[1]
            ?? throw new InvalidOperationException("Pipeline layout missing bind group layout 1.");
        var bgl2 = plData.BindGroupLayouts?[2]
            ?? throw new InvalidOperationException("Pipeline layout missing bind group layout 2.");

        var group1 = Pipelines.BindGroup(ctx, bgl1,
        [
            Binding.Sampler(0, Sampler),
            Binding.Texture(1, GBufferRT0),
            Binding.Texture(2, GBufferRT1),
            Binding.Texture(3, GBufferRT2),
            Binding.Texture(4, GBufferDepth),
            Binding.Sampler(5, ShadowSampler),
            Binding.Texture(6, ShadowMap0),
            Binding.Texture(7, ShadowMap1),
            Binding.Texture(8, ShadowMap2),
            Binding.Texture(9, ShadowMap3),
        ]);

        var group2 = Pipelines.BindGroup(ctx, bgl2,
        [
            Binding.Buffer(0, LightBuffer, (nuint)(GPULight.MaxLights * GPULight.Size)),
        ]);

        return [null, group1, group2];
    }
}
