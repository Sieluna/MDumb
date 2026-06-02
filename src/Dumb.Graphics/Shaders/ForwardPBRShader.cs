namespace Dumb.Graphics;

public static class ForwardPBRShader
{
    public const string Vertex = """
        #import dumb::camera

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

    public const string Fragment = """
        #import dumb::brdf

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
            lit += albedo * 0.03 * occlusion;

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
