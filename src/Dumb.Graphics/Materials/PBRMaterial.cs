using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sia;
using Silk.NET.WebGPU;
using Dumb.Graphics.Resources;

namespace Dumb.Graphics.Materials;

[StructLayout(LayoutKind.Sequential, Size = 80)]
public struct PBRMaterialParameters
{
    public Vector3 BaseColor;           // offset 0,  WGSL: base_color: vec3f
    public float _pad0;                 // offset 12, WGSL: _pad0: f32
    public float Metallic;              // offset 16, WGSL: metallic: f32
    public float Roughness;             // offset 20, WGSL: roughness: f32
    public float Occlusion;             // offset 24, WGSL: occlusion: f32
    public float _wgpuAlign0;           // offset 28, WGSL implicit padding (vec3f align 16)
    public Vector3 Emissive;            // offset 32, WGSL: emissive: vec3f
    public float _pad1;                 // offset 44, WGSL: _pad1: f32
    public float _pad;                  // offset 48, WGSL: _pad: f32
    public float _wgpuAlign1;           // offset 52, WGSL implicit padding (vec4f align 16)
    public float _wgpuAlign2;           // offset 56
    public float _wgpuAlign3;           // offset 60
    public float _padEnd0;              // offset 64, WGSL: _padEnd: vec4f
    public float _padEnd1;              // offset 68
    public float _padEnd2;              // offset 72
    public float _padEnd3;              // offset 76

    public static readonly PBRMaterialParameters Default = new()
    {
        BaseColor = new Vector3(0.8f, 0.8f, 0.8f),
        Metallic = 0.0f,
        Roughness = 0.5f,
        Occlusion = 1.0f,
        Emissive = Vector3.Zero
    };
}

public struct PBRMaterial : IMaterial
{
    public PBRMaterialParameters Parameters;
    public Entity? BaseColorTexture;
    public Entity? NormalTexture;
    public Entity? MROTexture;
    public Entity? EmissiveTexture;
    public Entity? Sampler;

    private Entity? _cachedShader;

    public static string Name => "PBR";

    public static Engine.Mesh.MeshDescriptor VertexDescriptor => new(
        [new Engine.Mesh.VertexStreamDescriptor([
            new Engine.Mesh.VertexElement(Engine.Mesh.MeshAttribute.Position, location: 0),
            new Engine.Mesh.VertexElement(Engine.Mesh.MeshAttribute.Normal, location: 1),
            new Engine.Mesh.VertexElement(Engine.Mesh.MeshAttribute.UV0, location: 2),
            new Engine.Mesh.VertexElement(Engine.Mesh.MeshAttribute.Tangent, location: 3),
            new Engine.Mesh.VertexElement(Engine.Mesh.MeshAttribute.Color, location: 4)
        ])],
        IndexFormat.Uint32);

    private static readonly BindingLayout[][] s_bindGroupLayouts =
    [
        // Group 0: Frame (camera + model — provided by renderer)
        [
            BindingLayout.UniformBuffer(0, ShaderStage.Vertex, 336),
            BindingLayout.UniformBuffer(2, ShaderStage.Vertex, 64, hasDynamicOffset: true)
        ],
        // Group 1: Material
        [
            BindingLayout.UniformBuffer(0, ShaderStage.Fragment, (ulong)Unsafe.SizeOf<PBRMaterialParameters>()),
            BindingLayout.Texture(1, ShaderStage.Fragment),
            BindingLayout.Texture(2, ShaderStage.Fragment),
            BindingLayout.Texture(3, ShaderStage.Fragment),
            BindingLayout.Texture(4, ShaderStage.Fragment),
            BindingLayout.Sampler(5, ShaderStage.Fragment)
        ]
    ];

    public static BindingLayout[][] BindGroupLayouts => s_bindGroupLayouts;

    public static BlendState? Blend => null;

    public static TextureFormat[] ColorFormats =>
    [
        TextureFormat.Rgba8Unorm,
        TextureFormat.Rgba16float,
        TextureFormat.Rgba8Unorm
    ];

    public static DepthStencilState? DepthStencil => new()
    {
        Format = TextureFormat.Depth32float,
        DepthWriteEnabled = true,
        DepthCompare = CompareFunction.Less,
        StencilFront = new StencilFaceState { Compare = CompareFunction.Always, FailOp = StencilOperation.Keep, DepthFailOp = StencilOperation.Keep, PassOp = StencilOperation.Keep },
        StencilBack = new StencilFaceState { Compare = CompareFunction.Always, FailOp = StencilOperation.Keep, DepthFailOp = StencilOperation.Keep, PassOp = StencilOperation.Keep }
    };

    public Entity GetShader(GraphicsContext ctx)
    {
        if (_cachedShader is { Host: not null } s)
            return s;

        var result = ShaderLibrary.Preprocess(GBufferShader.Vertex + "\n" + GBufferShader.Fragment);
        _cachedShader = Shaders.Wgsl(ctx, result.CombinedSource);
        return _cachedShader!;
    }

    public Entity?[] CreateBindGroups(GraphicsContext ctx, Entity pipelineLayout)
    {
        var materialUniform = Buffers.Uniform(ctx, Parameters);

        var baseColorTex = BaseColorTexture ?? ctx._textures.First();
        var normalTex = NormalTexture ?? ctx._textures.First();
        var mroTex = MROTexture ?? ctx._textures.First();
        var emissiveTex = EmissiveTexture ?? ctx._textures.First();
        var sampler = Sampler ?? Samplers.LinearClamp(ctx);

        // Group 1: Material bindings
        ref var plData = ref pipelineLayout.Get<PipelineLayoutData>();
        var bgl1 = plData.BindGroupLayouts?[1]
            ?? throw new InvalidOperationException("Material requires bind group layout at index 1.");

        var group1 = Pipelines.BindGroup(ctx, bgl1,
        [
            Binding.Uniform<PBRMaterialParameters>(0, materialUniform),
            Binding.Texture(1, baseColorTex),
            Binding.Texture(2, normalTex),
            Binding.Texture(3, mroTex),
            Binding.Texture(4, emissiveTex),
            Binding.Sampler(5, sampler)
        ]);

        return [null, group1];
    }
}
