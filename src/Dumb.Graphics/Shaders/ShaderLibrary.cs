namespace Dumb.Graphics;

using Dumb.Engine.Wgsl;

public static class ShaderLibrary
{
    // Camera uniform struct shared by all shaders that read frame data.
    // Layout: 7 fields = 336 bytes (matches CameraUniforms in CameraSyncSystem).
    public const string Camera = """
        #define_import_path dumb::camera

        struct CameraUniforms {
            view_projection: mat4x4f,
            view: mat4x4f,
            projection: mat4x4f,
            camera_position: vec3f,
            _pad0: f32,
            view_inverse: mat4x4f,
            projection_inverse: mat4x4f,
        }
        """;

    // Cook-Torrance specular BRDF with Smith GGX.
    public const string Brdf = """
        #define_import_path dumb::brdf

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
        """;

    // Octahedron normal encoding / decoding.
    public const string Encoding = """
        #define_import_path dumb::encoding

        fn octahedron_encode(v: vec3f) -> vec2f {
            let l1norm = abs(v.x) + abs(v.y) + abs(v.z);
            var result = v.xy * (1.0 / l1norm);
            if v.z < 0.0 {
                let old_x = result.x;
                result.x = (1.0 - abs(result.y)) * sign(old_x);
                result.y = (1.0 - abs(old_x)) * sign(result.y);
            }
            return result * 0.5 + 0.5;
        }

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
        """;

    private static readonly Dictionary<string, string> s_modules = new()
    {
        ["dumb::camera"] = Camera,
        ["dumb::brdf"] = Brdf,
        ["dumb::encoding"] = Encoding,
    };

    public static string? Resolve(string importPath, string _) =>
        s_modules.TryGetValue(importPath, out var source) ? source : null;

    // Convenience: preprocess a shader source against the library.
    public static ProcessResult Preprocess(string source,
        IReadOnlyDictionary<string, string>? shaderDefs = null) =>
        WgslPreprocessor.Process(source, shaderDefs, Resolve);
}
