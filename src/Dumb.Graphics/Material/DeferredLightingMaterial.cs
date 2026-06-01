using Sia;
using Silk.NET.WebGPU;
using Dumb.Graphics.Pipeline.Nodes;

namespace Dumb.Graphics.Material;

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
        var shader = Shaders.Wgsl(ctx, FullScreenVertexShader + LightingFragmentShader);

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

        _cachedShader = Shaders.Wgsl(ctx, FullScreenVertexShader + LightingFragmentShader);
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

    private const string FullScreenVertexShader = """
        @vertex
        fn vs_main(@builtin(vertex_index) vertex_index: u32) -> @builtin(position) vec4f {
            let x = f32(i32(vertex_index & 1u) * 4 - 1);
            let y = f32(i32(vertex_index >> 1u) * 4 - 1);
            return vec4f(x, y, 0.0, 1.0);
        }
        """;

    private const string LightingFragmentShader = """
        struct CameraUniforms {
            view_projection: mat4x4f,
            view: mat4x4f,
            projection: mat4x4f,
            camera_position: vec3f,
            _pad0: f32,
            view_inverse: mat4x4f,
            projection_inverse: mat4x4f,
        }

        struct LightData {
            color: vec3f,
            intensity: f32,
            direction: vec3f,
            range: f32,
            position: vec3f,
            light_type: u32,
            inner_cone: f32,
            outer_cone: f32,
            casts_shadows: u32,
        }

        struct ShadowUniforms {
            light_vp: mat4x4f,
            texel_size: vec2f,
            depth_bias: f32,
            normal_bias: f32,
        }

        @group(0) @binding(0) var<uniform> camera: CameraUniforms;
        @group(0) @binding(1) var<uniform> shadow_uniforms: array<ShadowUniforms, 4>;

        @group(1) @binding(0) var gbuffer_sampler: sampler;
        @group(1) @binding(1) var base_color_tex: texture_2d<f32>;
        @group(1) @binding(2) var normal_roughness_tex: texture_2d<f32>;
        @group(1) @binding(3) var pbr_tex: texture_2d<f32>;
        @group(1) @binding(4) var depth_tex: texture_depth_2d;
        @group(1) @binding(5) var shadow_sampler: sampler_comparison;
        @group(1) @binding(6) var shadow_map_0: texture_depth_2d;
        @group(1) @binding(7) var shadow_map_1: texture_depth_2d;
        @group(1) @binding(8) var shadow_map_2: texture_depth_2d;
        @group(1) @binding(9) var shadow_map_3: texture_depth_2d;

        @group(2) @binding(0) var<uniform> lights: array<LightData, 64>;

        const LIGHT_DIRECTIONAL = 0u;
        const LIGHT_POINT = 1u;
        const LIGHT_SPOT = 2u;

        fn octahedron_decode(v: vec2f) -> vec3f {
            var n = v * 2.0 - 1.0;
            let abs_sum = abs(n.x) + abs(n.y);
            var result: vec3f;
            if abs_sum <= 1.0 {
                result.z = 1.0 - abs_sum;
                result.x = n.x;
                result.y = n.y;
            } else {
                result.z = abs_sum - 1.0;
                result.x = select(abs(n.y) - 1.0, 1.0 - abs(n.y), n.x >= 0.0);
                result.y = select(abs(n.x) - 1.0, 1.0 - abs(n.x), n.y >= 0.0);
            }
            return normalize(result);
        }

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

        fn shadow_depth_sample(map_index: u32, uv: vec2f, z: f32) -> f32 {
            switch map_index {
                case 0u: { return textureSampleCompare(shadow_map_0, shadow_sampler, uv, z); }
                case 1u: { return textureSampleCompare(shadow_map_1, shadow_sampler, uv, z); }
                case 2u: { return textureSampleCompare(shadow_map_2, shadow_sampler, uv, z); }
                default: { return textureSampleCompare(shadow_map_3, shadow_sampler, uv, z); }
            }
        }

        fn shadow_tent_5x5(coord: vec4f, map_index: u32) -> f32 {
            // WebGPU viewport fb_y = h·(1 - ndc_y)/2  →  ndc_y = 1 - 2·uv.y
            let u = coord.x / coord.w * 0.5 + 0.5;
            let v = 0.5 - coord.y / coord.w * 0.5;
            let z = coord.z / coord.w;
            let ts = shadow_uniforms[map_index].texel_size;
            var sum = 0.0f;
            sum += 4.0 * shadow_depth_sample(map_index, vec2f(u, v), z);
            sum += 2.0 * shadow_depth_sample(map_index, vec2f(u - ts.x, v), z);
            sum += 2.0 * shadow_depth_sample(map_index, vec2f(u + ts.x, v), z);
            sum += 2.0 * shadow_depth_sample(map_index, vec2f(u, v - ts.y), z);
            sum += 2.0 * shadow_depth_sample(map_index, vec2f(u, v + ts.y), z);
            sum += 1.0 * shadow_depth_sample(map_index, vec2f(u - ts.x, v - ts.y), z);
            sum += 1.0 * shadow_depth_sample(map_index, vec2f(u + ts.x, v - ts.y), z);
            sum += 1.0 * shadow_depth_sample(map_index, vec2f(u - ts.x, v + ts.y), z);
            sum += 1.0 * shadow_depth_sample(map_index, vec2f(u + ts.x, v + ts.y), z);
            return sum / 16.0;
        }

        fn evaluate_directional(light: LightData, world_pos: vec3f, N: vec3f, V: vec3f, roughness: f32, metallic: f32, albedo: vec3f) -> vec3f {
            let L = normalize(-light.direction);
            let brdf = specular_brdf(N, V, L, roughness, metallic, albedo);
            var shadow_factor = 1.0f;

            let si = light.casts_shadows;
            if si > 0u {
                let shadow_idx = si - 1u;
                let su = shadow_uniforms[shadow_idx];
                let shadow_coord = su.light_vp * vec4f(world_pos, 1.0);
                let NdotL = max(dot(N, L), 0.0);
                let bias = su.depth_bias + su.normal_bias * (1.0 - NdotL);
                var biased = shadow_coord;
                biased.z -= bias;
                shadow_factor = shadow_tent_5x5(biased, shadow_idx);
            }

            return light.color * light.intensity * brdf * shadow_factor;
        }

        fn evaluate_point(light: LightData, world_pos: vec3f, N: vec3f, V: vec3f, roughness: f32, metallic: f32, albedo: vec3f) -> vec3f {
            let L_vec = light.position - world_pos;
            let dist = length(L_vec);
            if dist > light.range { return vec3f(0.0); }
            let attenuation = pow(max(1.0 - dist / light.range, 0.0), 2.0);
            let L = L_vec / dist;
            return light.color * light.intensity * attenuation * specular_brdf(N, V, L, roughness, metallic, albedo);
        }

        fn evaluate_spot(light: LightData, world_pos: vec3f, N: vec3f, V: vec3f, roughness: f32, metallic: f32, albedo: vec3f) -> vec3f {
            let L_vec = light.position - world_pos;
            let dist = length(L_vec);
            if dist > light.range { return vec3f(0.0); }
            let attenuation = pow(max(1.0 - dist / light.range, 0.0), 2.0);
            let L = L_vec / dist;
            let theta = dot(L, normalize(-light.direction));
            let epsilon = light.inner_cone - light.outer_cone;
            let spot_falloff = clamp((theta - light.outer_cone) / max(epsilon, 0.001), 0.0, 1.0);
            return light.color * light.intensity * attenuation * spot_falloff * specular_brdf(N, V, L, roughness, metallic, albedo);
        }

        @fragment
        fn fs_main(@builtin(position) screen_pos: vec4f) -> @location(0) vec4f {
            let uv = screen_pos.xy / vec2f(textureDimensions(base_color_tex, 0));
            let base_color_sample = textureSample(base_color_tex, gbuffer_sampler, uv);
            let nr_sample = textureSample(normal_roughness_tex, gbuffer_sampler, uv);
            let pbr_sample = textureSample(pbr_tex, gbuffer_sampler, uv);
            let depth = textureSample(depth_tex, gbuffer_sampler, uv);

            let base_color = base_color_sample.rgb;
            let normal = octahedron_decode(nr_sample.rg);
            let roughness = nr_sample.b;
            let metallic = pbr_sample.r;
            let occlusion = pbr_sample.g;
            let emissive = pbr_sample.b * base_color;

            // Reconstruct world position from depth.
            // WebGPU viewport: fb_y = h·(1 - ndc_y)/2  →  ndc_y = 1 - 2·uv.y (Y=0 at top).
            // X is the same as OpenGL; Z is already in [0,1] NDC.
            let clip = vec4f(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0, depth, 1.0);
            let view_space = camera.projection_inverse * clip;
            let view_pos = view_space.xyz / view_space.w;
            let world_pos = (camera.view_inverse * vec4f(view_pos, 1.0)).xyz;
            let V = normalize(camera.camera_position - world_pos);

            var color = vec3f(0.0);
            // Ambient term
            color += base_color * 0.03 * occlusion;

            for (var i = 0u; i < 64u; i++) {
                let light = lights[i];
                if light.intensity <= 0.0 { continue; }

                if light.light_type == LIGHT_DIRECTIONAL {
                    color += evaluate_directional(light, world_pos, normal, V, roughness, metallic, base_color);
                } else if light.light_type == LIGHT_POINT {
                    color += evaluate_point(light, world_pos, normal, V, roughness, metallic, base_color);
                } else if light.light_type == LIGHT_SPOT {
                    color += evaluate_spot(light, world_pos, normal, V, roughness, metallic, base_color);
                }
            }

            color += emissive;
            return vec4f(color, 1.0);
        }
        """;
}
