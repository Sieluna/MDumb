namespace Dumb.Graphics;

public static class GBufferShader
{
    public const string Vertex = @"
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
    @location(0) world_position: vec3f,
    @location(1) world_normal: vec3f,
    @location(2) uv: vec2f,
    @location(3) color: vec3f,
}

@group(0) @binding(0) var<uniform> camera: CameraUniforms;
@group(0) @binding(2) var<uniform> model: mat4x4f;

@vertex
fn vs_main(in: VSInput) -> VSOutput {
    var out: VSOutput;
    let world_pos = (model * vec4f(in.position, 1.0)).xyz;
    let world_norm = mat3x3f(model[0].xyz, model[1].xyz, model[2].xyz) * in.normal;
    out.world_position = world_pos;
    out.world_normal = normalize(world_norm);
    out.uv = in.uv;
    out.color = in.color.rgb;
    out.clip_position = camera.view_projection * vec4f(world_pos, 1.0);
    return out;
}
";

    public const string Fragment = @"
#import dumb::encoding

struct MaterialUniforms {
    base_color: vec3f,
    _pad0: f32,
    metallic: f32,
    roughness: f32,
    occlusion: f32,
    emissive: vec3f,
    _pad1: f32,
    _pad: f32,
    _padEnd: vec4f,
}

struct FSInput {
    @location(0) world_position: vec3f,
    @location(1) world_normal: vec3f,
    @location(2) uv: vec2f,
    @location(3) color: vec3f,
}

struct GBufferOutput {
    @location(0) base_color: vec4f,
    @location(1) normal_roughness: vec4f,
    @location(2) pbr: vec4f,
}

@group(1) @binding(0) var<uniform> material: MaterialUniforms;
@group(1) @binding(1) var base_color_tex: texture_2d<f32>;
@group(1) @binding(2) var normal_tex: texture_2d<f32>;
@group(1) @binding(3) var mro_tex: texture_2d<f32>;
@group(1) @binding(4) var emissive_tex: texture_2d<f32>;
@group(1) @binding(5) var tex_sampler: sampler;

@fragment
fn fs_main(in: FSInput) -> GBufferOutput {
    let base_color_sample = textureSample(base_color_tex, tex_sampler, in.uv).rgb;
    let mro_sample = textureSample(mro_tex, tex_sampler, in.uv);
    let emissive_sample = textureSample(emissive_tex, tex_sampler, in.uv).rgb;

    let base_color = base_color_sample * in.color * material.base_color;
    let metallic = mro_sample.r * material.metallic;
    let roughness = mro_sample.g * material.roughness;
    let occlusion = mro_sample.b * material.occlusion;
    let emissive = emissive_sample + material.emissive;

    let N = normalize(in.world_normal);
    let encoded_normal = octahedron_encode(N);

    var out: GBufferOutput;
    out.base_color = vec4f(base_color, 1.0);
    out.normal_roughness = vec4f(encoded_normal, roughness, 0.0);
    out.pbr = vec4f(metallic, occlusion, emissive.r, 1.0);
    return out;
}
";
}
