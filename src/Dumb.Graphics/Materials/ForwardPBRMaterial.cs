using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sia;
using Silk.NET.WebGPU;

namespace Dumb.Graphics.Materials;

[StructLayout(LayoutKind.Sequential, Size = 48)]
public struct ForwardPBRParams
{
    public Vector3 BaseColor;
    public float Alpha;
    public float Roughness;
    public float Metallic;
    public float Occlusion;
    public float _pad0;
    public Vector3 Emissive;
    public float _pad1;

    public static readonly ForwardPBRParams Default = new()
    {
        BaseColor = new Vector3(0.8f, 0.8f, 0.8f),
        Alpha = 1.0f,
        Roughness = 0.5f,
        Metallic = 0.0f,
        Occlusion = 1.0f,
        Emissive = Vector3.Zero
    };
}

public struct ForwardPBRMaterial : IMaterial
{
    public ForwardPBRParams Parameters;
    public Entity? BaseColorTexture;
    public Entity? NormalTexture;
    public Entity? MROTexture;
    public Entity? EmissiveTexture;
    public Entity? Sampler;

    private Entity? _cachedShader;

    public static string Name => "ForwardPBR";

    /// <summary>Must be set to the swapchain/surface format before calling Materials.Create.</summary>
    public static TextureFormat SurfaceFormat = TextureFormat.Rgba8Unorm;

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
        // Group 0: Frame (camera + model) — must match PBRMaterial for PhaseQueueSystem
        [
            BindingLayout.UniformBuffer(0, ShaderStage.Vertex, 336),
            BindingLayout.UniformBuffer(2, ShaderStage.Vertex, 64, hasDynamicOffset: true)
        ],
        // Group 1: Material
        [
            BindingLayout.UniformBuffer(0, ShaderStage.Fragment, (ulong)Unsafe.SizeOf<ForwardPBRParams>()),
            BindingLayout.Texture(1, ShaderStage.Fragment),
            BindingLayout.Texture(2, ShaderStage.Fragment),
            BindingLayout.Texture(3, ShaderStage.Fragment),
            BindingLayout.Texture(4, ShaderStage.Fragment),
            BindingLayout.Sampler(5, ShaderStage.Fragment)
        ]
    ];

    public static BindingLayout[][] BindGroupLayouts => s_bindGroupLayouts;

    public static BlendState? Blend => new()
    {
        Color = new BlendComponent
        {
            SrcFactor = BlendFactor.SrcAlpha,
            DstFactor = BlendFactor.OneMinusSrcAlpha,
            Operation = BlendOperation.Add
        },
        Alpha = new BlendComponent
        {
            SrcFactor = BlendFactor.One,
            DstFactor = BlendFactor.OneMinusSrcAlpha,
            Operation = BlendOperation.Add
        }
    };

    public static DepthStencilState? DepthStencil => new()
    {
        Format = TextureFormat.Depth32float,
        DepthWriteEnabled = false,
        DepthCompare = CompareFunction.Less,
        StencilFront = new StencilFaceState { Compare = CompareFunction.Always, FailOp = StencilOperation.Keep, DepthFailOp = StencilOperation.Keep, PassOp = StencilOperation.Keep },
        StencilBack = new StencilFaceState { Compare = CompareFunction.Always, FailOp = StencilOperation.Keep, DepthFailOp = StencilOperation.Keep, PassOp = StencilOperation.Keep }
    };

    public static TextureFormat[] ColorFormats => [SurfaceFormat];

    public Entity GetShader(GraphicsContext ctx)
    {
        if (_cachedShader is { Host: not null } s)
            return s;

        var result = ShaderLibrary.Preprocess(ForwardPBRShader.Vertex + "\n" + ForwardPBRShader.Fragment);
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

        ref var plData = ref pipelineLayout.Get<PipelineLayoutData>();
        var bgl1 = plData.BindGroupLayouts?[1]
            ?? throw new InvalidOperationException("Material requires bind group layout at index 1.");

        var group1 = Pipelines.BindGroup(ctx, bgl1,
        [
            Binding.Uniform<ForwardPBRParams>(0, materialUniform),
            Binding.Texture(1, baseColorTex),
            Binding.Texture(2, normalTex),
            Binding.Texture(3, mroTex),
            Binding.Texture(4, emissiveTex),
            Binding.Sampler(5, sampler),
        ]);

        return [null, group1];
    }
}
