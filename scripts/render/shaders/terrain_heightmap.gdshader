shader_type spatial;
// Stepped G8 patches put two coincident wall faces per cell boundary with
// opposite winding — neither side is reliably "front". Render both so we
// never see gaps when a cliff's far side is back-facing the camera.
render_mode cull_disabled, diffuse_lambert_wrap, specular_disabled, shadows_disabled;

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
uniform vec4 tint : source_color = vec4(1.0, 1.0, 1.0, 1.0);

varying vec3 v_world_pos;

float sample_height(vec2 uv_local) {
	// uv_local: [0..1] over patch.
	return texture(heightmap, uv_local).r * height_scale_m;
}

void vertex() {
	// Sample heightmap via the UV attribute from the mesh. Stepped patches put
	// multiple verts at the same XZ with different intended heights (wall top vs
	// bottom), so we can't derive the sample coord from position alone.
	vec2 uv_local = UV;
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

	// Atlas is 4×4 cells (see TileAtlas.cs); naive UV tiling sweeps across
	// grass → dirt → rock → sand every meter. Confine sampling to the grass
	// cell at (col=0, row=0) and repeat within it per-tile. textureGrad with
	// the unwrapped UV derivatives keeps mipmap selection sane across the
	// fract() boundary.
	const float inset = 8.0 / 2048.0;
	const float cell_span = 0.25 - 2.0 * inset;
	vec2 tile_uv = fract(UV);
	vec2 atlas_uv = vec2(inset) + tile_uv * cell_span;
	vec2 du = dFdx(UV) * cell_span;
	vec2 dv = dFdy(UV) * cell_span;
	vec4 tex = textureGrad(albedo_tex, atlas_uv, du, dv);
	ALBEDO = tex.rgb * tint.rgb;
	ROUGHNESS = 0.95;
}
