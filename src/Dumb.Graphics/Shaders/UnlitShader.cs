namespace Dumb.Graphics;

public static class UnlitShader
{
    public const string Vertex = """
        #import dumb::camera

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

    public const string Fragment = """
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
