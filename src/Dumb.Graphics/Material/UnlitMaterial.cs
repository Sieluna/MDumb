using System.Numerics;
using System.Runtime.InteropServices;
using Sia;
using Silk.NET.WebGPU;

namespace Dumb.Graphics.Material;

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

        _cachedShader = Shaders.Wgsl(ctx, VertexShader + FragmentShader);
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

    private const string VertexShader = """
        struct CameraUniforms {
            view_projection: mat4x4f,
            view: mat4x4f,
            projection: mat4x4f,
            camera_position: vec3f,
            _pad0: f32,
            view_inverse: mat4x4f,
            projection_inverse: mat4x4f,
        }

        struct VSInput {
            @location(0) position: vec3f,
            @location(1) normal: vec3f,
            @location(2) color: vec4f,
        }

        struct VSOutput {
            @builtin(position) clip_position: vec4f,
            @location(0) color: vec3f,
        }

        @group(0) @binding(0) var<uniform> camera: CameraUniforms;
        @group(0) @binding(2) var<uniform> model: mat4x4f;

        @vertex
        fn vs_main(in: VSInput) -> VSOutput {
            let world_pos = (model * vec4f(in.position, 1.0)).xyz;

            var out: VSOutput;
            out.clip_position = camera.view_projection * vec4f(world_pos, 1.0);
            out.color = in.color.rgb;
            return out;
        }
        """;

    private const string FragmentShader = """
        struct MaterialUniforms {
            color: vec3f,
            _pad: f32,
        }

        @group(1) @binding(0) var<uniform> material: MaterialUniforms;

        @fragment
        fn fs_main(@location(0) color: vec3f) -> @location(0) vec4f {
            let final_color = color * material.color;
            return vec4f(final_color, 1.0);
        }
        """;
}
