namespace Dumb.Graphics;

public static class DeferredLightingShader
{
    public const string Vertex = """
        @vertex
        fn vs_main(@builtin(vertex_index) vertex_index: u32) -> @builtin(position) vec4f {
            let x = f32(i32(vertex_index & 1u) * 4 - 1);
            let y = f32(i32(vertex_index >> 1u) * 4 - 1);
            return vec4f(x, y, 0.0, 1.0);
        }
        """;
}
