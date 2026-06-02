using System.Numerics;
using System.Runtime.InteropServices;
using Sia;
using Silk.NET.WebGPU;

namespace Dumb.Graphics.Materials;

[StructLayout(LayoutKind.Sequential)]
public struct UnlitMaterialParameters
{
    public Vector3 Color;
    public float _pad;

    public static readonly UnlitMaterialParameters Default = new()
    {
        Color = Vector3.One
    };
}

public struct UnlitMaterial : IMaterial
{
    public UnlitMaterialParameters Parameters;

    private Entity? _cachedShader;

    public static string Name => "Unlit";

    public static Engine.Mesh.MeshDescriptor VertexDescriptor => new(
        [new Engine.Mesh.VertexStreamDescriptor([
            new Engine.Mesh.VertexElement(Engine.Mesh.MeshAttribute.Position, location: 0),
            new Engine.Mesh.VertexElement(Engine.Mesh.MeshAttribute.Normal, location: 1),
            new Engine.Mesh.VertexElement(Engine.Mesh.MeshAttribute.Color, location: 2)
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
            BindingLayout.UniformBuffer(0, ShaderStage.Fragment, 16)
        ]
    ];

    public static BindingLayout[][] BindGroupLayouts => s_bindGroupLayouts;

    public static BlendState? Blend => null;
    public static DepthStencilState? DepthStencil => new()
    {
        Format = TextureFormat.Depth32float,
        DepthWriteEnabled = true,
        DepthCompare = CompareFunction.Less,
        StencilFront = new StencilFaceState { Compare = CompareFunction.Always, FailOp = StencilOperation.Keep, DepthFailOp = StencilOperation.Keep, PassOp = StencilOperation.Keep },
        StencilBack = new StencilFaceState { Compare = CompareFunction.Always, FailOp = StencilOperation.Keep, DepthFailOp = StencilOperation.Keep, PassOp = StencilOperation.Keep }
    };

    public static TextureFormat[] ColorFormats => [TextureFormat.Rgba8Unorm];

    public Entity GetShader(GraphicsContext ctx)
    {
        if (_cachedShader is { Host: not null } s)
            return s;

        var result = ShaderLibrary.Preprocess(UnlitShader.Vertex + "\n" + UnlitShader.Fragment);
        _cachedShader = Shaders.Wgsl(ctx, result.CombinedSource);
        return _cachedShader!;
    }

    public Entity?[] CreateBindGroups(GraphicsContext ctx, Entity pipelineLayout)
    {
        var materialUniform = Buffers.Uniform(ctx, Parameters);

        ref var plData = ref pipelineLayout.Get<PipelineLayoutData>();
        var bgl1 = plData.BindGroupLayouts?[1]
            ?? throw new InvalidOperationException("Pipeline layout missing bind group layout 1.");

        var group1 = Pipelines.BindGroup(ctx, bgl1,
        [
            Binding.Uniform<UnlitMaterialParameters>(0, materialUniform)
        ]);

        return [null, group1];
    }
}
