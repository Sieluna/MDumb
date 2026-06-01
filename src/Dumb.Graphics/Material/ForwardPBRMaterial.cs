using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sia;
using Silk.NET.WebGPU;

namespace Dumb.Graphics.Material;

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

        _cachedShader = Shaders.Wgsl(ctx, ForwardVertexShader + ForwardFragmentShader);
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

    private const string ForwardVertexShader = """
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
            @location(2) uv: vec2f,
            @location(3) tangent: vec4f,
            @location(4) color: vec4f,
        }

        struct VSOutput {
            @builtin(position) clip_position: vec4f,
            @location(0) world_pos: vec3f,
            @location(1) normal: vec3f,
            @location(2) uv: vec2f,
            @location(3) tangent: vec4f,
            @location(4) color: vec4f,
            @location(5) camera_pos: vec3f,
        }

        @group(0) @binding(0) var<uniform> camera: CameraUniforms;
        @group(0) @binding(2) var<uniform> model: mat4x4f;

        @vertex
        fn vs_main(in: VSInput) -> VSOutput {
            var out: VSOutput;
            let world = model * vec4f(in.position, 1.0);
            out.world_pos = world.xyz / world.w;
            out.clip_position = camera.view_projection * world;
            out.normal = normalize((model * vec4f(in.normal, 0.0)).xyz);
            out.uv = in.uv;
            out.tangent = vec4f(normalize((model * vec4f(in.tangent.xyz, 0.0)).xyz), in.tangent.w);
            out.color = in.color;
            out.camera_pos = camera.camera_position;
            return out;
        }
        """;

    private const string ForwardFragmentShader = """
        struct MaterialParams {
            base_color: vec3f,
            alpha: f32,
            roughness: f32,
            metallic: f32,
            occlusion: f32,
            _pad0: f32,
            emissive: vec3f,
            _pad1: f32,
        }

        @group(1) @binding(0) var<uniform> params: MaterialParams;
        @group(1) @binding(1) var base_color_tex: texture_2d<f32>;
        @group(1) @binding(2) var normal_tex: texture_2d<f32>;
        @group(1) @binding(3) var mro_tex: texture_2d<f32>;
        @group(1) @binding(4) var emissive_tex: texture_2d<f32>;
        @group(1) @binding(5) var mat_sampler: sampler;

        fn specular_brdf(N: vec3f, V: vec3f, L: vec3f, roughness: f32, metallic: f32, albedo: vec3f) -> vec3f {
            let H = normalize(L + V);
            let NdotL = max(dot(N, L), 0.0);
            let NdotV = max(dot(N, V), 0.001);
            let NdotH = max(dot(N, H), 0.0);
            let VdotH = max(dot(V, H), 0.0);

            let alpha = roughness * roughness;
            let alpha2 = alpha * alpha;

            let denom = NdotH * NdotH * (alpha2 - 1.0) + 1.0;
            let D = alpha2 / (3.14159265 * denom * denom);

            let k = (roughness + 1.0) * (roughness + 1.0) / 8.0;
            let G1 = NdotL / (NdotL * (1.0 - k) + k);
            let G2 = NdotV / (NdotV * (1.0 - k) + k);
            let G = G1 * G2;

            let F0 = mix(vec3f(0.04), albedo, metallic);
            let F = F0 + (1.0 - F0) * pow(1.0 - VdotH, 5.0);

            let specular = D * G * F / max(4.0 * NdotV * NdotL, 0.001);
            let diffuse = albedo * (1.0 - F0) * (1.0 - metallic) / 3.14159265;

            return (diffuse + specular) * NdotL;
        }

        @fragment
        fn fs_main(
            @location(0) world_pos: vec3f,
            @location(1) normal: vec3f,
            @location(2) uv: vec2f,
            @location(3) tangent: vec4f,
            @location(4) color: vec4f,
            @location(5) camera_pos: vec3f
        ) -> @location(0) vec4f {
            let base_color_sample = textureSample(base_color_tex, mat_sampler, uv);
            let mro_sample = textureSample(mro_tex, mat_sampler, uv);
            let emissive_sample = textureSample(emissive_tex, mat_sampler, uv);

            let albedo = params.base_color * base_color_sample.rgb * color.rgb;
            let alpha = params.alpha * base_color_sample.a * color.a;
            let roughness = params.roughness * mro_sample.r;
            let metallic = params.metallic;
            let occlusion = params.occlusion * mro_sample.g;
            let emissive = params.emissive * emissive_sample.rgb;

            let N = normalize(normal);
            let V = normalize(camera_pos - world_pos);

            var lit = vec3f(0.0);
            // Ambient
            lit += albedo * 0.03 * occlusion;

            // Directional light
            let light_dir = normalize(vec3f(0.3, -0.7, 0.6));
            let light_color = vec3f(1.0, 0.95, 0.85);
            let light_intensity = 2.0;
            lit += specular_brdf(N, V, light_dir, roughness, metallic, albedo)
                 * light_color * light_intensity;

            lit += emissive;
            return vec4f(lit, alpha);
        }
        """;
}
