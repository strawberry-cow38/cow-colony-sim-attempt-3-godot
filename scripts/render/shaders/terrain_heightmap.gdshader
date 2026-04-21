shader_type spatial;
render_mode cull_back, diffuse_lambert_wrap, specular_disabled, shadows_disabled;

// Heightmap tier (G4/G8/G16): single fixed-topology flat grid in local space
// is displaced along +Y by sampling an R16 (as sampler2D, red channel) heightmap.
// Normals are reconstructed in the fragment shader via screen-space derivatives so
// we don't need to ship per-vertex normals from the CPU path.

uniform sampler2D heightmap : hint_default_black, filter_linear, repeat_disable;
uniform sampler2D albedo_tex : filter_linear_mipmap_anisotropic, repeat_enable;

// Local-space footprint of this grid patch in meters (W × W).
uniform float patch_width_m = 384.0;
// Vertical scale: heightmap R channel (0..1) maps to [0, height_scale_m] meters.
uniform float height_scale_m = 192.0;
// Tint for debug / palette.
uniform vec4 tint : source_color = vec4(0.35, 0.65, 0.25, 1.0);

varying vec3 v_world_pos;

float sample_height(vec2 uv_local) {
	// uv_local: [0..1] over patch.
	return texture(heightmap, uv_local).r * height_scale_m;
}

void vertex() {
	// VERTEX.xz is already in local-space meters thanks to the mesh we upload.
	vec2 uv_local = clamp(VERTEX.xz / patch_width_m, vec2(0.0), vec2(1.0));
	VERTEX.y += sample_height(uv_local);
	UV = VERTEX.xz * (1.0 / 1.5); // 1.5m per tile → 1 unit of UV = 1 tile
}

void fragment() {
	// Screen-space derivatives reconstruct per-pixel view-space normal without
	// needing per-vertex normals. VERTEX is view-space in fragment() for
	// Godot 4 spatial shaders, so we don't multiply through VIEW_MATRIX.
	vec3 dx = dFdx(VERTEX);
	vec3 dy = dFdy(VERTEX);
	NORMAL = normalize(cross(dy, dx));
	vec4 tex = texture(albedo_tex, UV);
	ALBEDO = tex.rgb * tint.rgb;
	ROUGHNESS = 0.95;
}
