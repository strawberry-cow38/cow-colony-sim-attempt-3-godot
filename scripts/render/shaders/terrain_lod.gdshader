shader_type spatial;
// L0/L1 CPU-mesh terrain. Replaces the StandardMaterial3D path so we can
// add a per-fragment IGN dither fade-out in the [tier1 - chunk, tier1]
// band. The same FRAGCOORD+TIME hash used by terrain_heightmap.gdshader's
// fade-in on G4 means every pixel in the overlap is drawn by exactly one
// tier — no holes, no overdraw, and the tier boundary no longer needs
// the +2 chunk L1 over-extension (it had been masking a visible seam).
render_mode specular_disabled;

// Orbit-gimbal world position, set every frame by GridRenderer and shared
// with terrain_heightmap.gdshader so both tiers fade relative to the same
// point (NOT the camera) — zoom/pitch don't shift the fade boundary.
global uniform vec3 gimbal_pos;

uniform sampler2D albedo_tex : filter_linear_mipmap_anisotropic, repeat_enable;

// end ≤ start disables the band. Meters from gimbal (cylindrical XZ).
uniform float fade_out_start_m = 0.0;
uniform float fade_out_end_m = 0.0;

// Hard discard past fog-end. Godot's depth fog handles the visual ramp;
// this matches MaxChunkDistance so no fragment draws past the cull edge.
uniform float fog_end_m = 1e9;

varying vec3 v_world_pos;
varying vec4 v_color;

// Interleaved Gradient Noise (Jimenez). Matches terrain_heightmap.gdshader
// so adjacent tiers using the same FRAGCOORD produce complementary masks.
float ign(vec2 p) {
	return fract(52.9829189 * fract(0.06711056 * p.x + 0.00583715 * p.y));
}

void vertex() {
	v_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_color = COLOR;
}

void fragment() {
	float d = distance(
		vec2(v_world_pos.x, v_world_pos.z),
		vec2(gimbal_pos.x, gimbal_pos.z));
	if (d > fog_end_m) discard;

	// Per-frame TIME offset so the dither pattern walks each frame —
	// reads as soft grain instead of a static pixel sieve. Same constants
	// as terrain_heightmap.gdshader so L1's fade-out hash aligns with
	// G4's fade-in hash in the shared crossfade band.
	vec2 dither_off = vec2(fract(TIME * 1.6180339887), fract(TIME * 2.7182818285)) * 64.0;
	float h = ign(FRAGCOORD.xy + dither_off);
	if (fade_out_end_m > fade_out_start_m) {
		float t = clamp((d - fade_out_start_m) / (fade_out_end_m - fade_out_start_m), 0.0, 1.0);
		// Inverted threshold from G4's fade-in (`t < h` discards there) so
		// the two tiers dither-complement: pixel kept on L1 → discarded on
		// G4 and vice versa.
		if (h <= t) discard;
	}

	vec4 tex = texture(albedo_tex, UV);
	ALBEDO = tex.rgb * v_color.rgb;
	ROUGHNESS = 0.95;
}
