namespace Dumb.Graphics;

public static class ShadowShader
{
    public const string Source = """
        struct VSInput {
            @location(0) position: vec3f,
        }

        struct VSOutput {
            @builtin(position) clip_position: vec4f,
        }

        @group(0) @binding(0) var<uniform> light_vp: mat4x4f;
        @group(0) @binding(2) var<uniform> model: mat4x4f;

        @vertex
        fn vs_main(in: VSInput) -> VSOutput {
            var out: VSOutput;
            out.clip_position = light_vp * model * vec4f(in.position, 1.0);
            return out;
        }

        @fragment
        fn fs_main() -> @location(0) vec4f {
            return vec4f(0.0);
        }
        """;
}
