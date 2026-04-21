shader_type spatial;
// Stepped G8 patches put two coincident wall faces per cell boundary with
// opposite winding — neither side is reliably "front". Render both so we
// never see gaps when a cliff's far side is back-facing the camera.
// Default lambert (not lambert_wrap) so shadowed faces darken the same way
// StandardMaterial3D does on the L0/G4 terrain — keeps the tier boundary
// from flashing lighter where G8 takes over.
render_mode cull_disabled, specular_disabled, shadows_disabled;

// Heightmap tier (G4/G8/G16): single fixed-topology flat grid in local space
// is displaced along +Y by sampling an R16 (as sampler2D, red channel) heightmap.
// Normals are reconstructed in the fragment shader via screen-space derivatives so
// we don't need to ship per-vertex normals from the CPU path.

// Orbit-gimbal world position set every frame by GridRenderer. The coarse
// tier fades relative to this point — NOT the camera — so zoom/pitch don't
// change which fragments show up, and the fade window reads from the
// fragment's true world XY rather than the mesh-instance origin (which
// sits at the patch midpoint but still disagrees with cam→AABB at the
// edges).
global uniform vec3 gimbal_pos;

uniform sampler2D heightmap : hint_default_black, filter_linear, repeat_disable;
uniform sampler2D albedo_tex : filter_linear_mipmap_anisotropic, repeat_enable;

// Local-space footprint of this grid patch in meters (W × W).
uniform float patch_width_m = 384.0;
// Vertical scale: heightmap R channel (0..1) maps to [0, height_scale_m] meters.
uniform float height_scale_m = 192.0;
// Two dither bands. Fade-in: below fade_in_start_m every fragment discards
// (a nearer tier covers it); above fade_in_end_m every fragment keeps.
// Fade-out: mirror — above fade_out_end_m every fragment discards (a coarser
// tier covers it); below fade_out_start_m every fragment keeps. In the
// crossfade band between two tiers, this tier's fade-out and the coarser
// tier's fade-in share the same FRAGCOORD hash but invert the threshold,
// so every pixel is drawn by exactly one tier — no holes, no overdraw.
// Setting end ≤ start on either band disables it.
uniform float fade_in_start_m = 0.0;
uniform float fade_in_end_m = 0.0;
uniform float fade_out_start_m = 0.0;
uniform float fade_out_end_m = 0.0;
// Hard clip past fog depth-end. Distance fog already whites-out the horizon,
// but transparent-over-sky G8 fragments can still punch through when their
// dither keeps them; discarding past this distance stops those pixels.
uniform float fog_end_m = 1e9;
// Tint for debug / palette.
// Darken to match the tone L0/G4 terrain lands at after StandardMaterial3D
// shading — raw grass texels are brighter than the lit near-field look.
uniform vec4 tint : source_color = vec4(0.7, 0.7, 0.7, 1.0);

varying vec3 v_local_pos;
varying vec3 v_world_pos;

float sample_height(vec2 uv_local) {
	// uv_local: [0..1] over patch.
	return texture(heightmap, uv_local).r * height_scale_m;
}

float hash12(vec2 p) {
	return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

void vertex() {
	// Sample heightmap via the UV attribute from the mesh. Stepped patches put
	// multiple verts at the same XZ with different intended heights (wall top vs
	// bottom), so we can't derive the sample coord from position alone.
	vec2 uv_local = UV;
	VERTEX.y += sample_height(uv_local);
	v_local_pos = VERTEX;
	v_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	UV = VERTEX.xz * (1.0 / 1.5); // 1.5m per tile → 1 unit of UV = 1 tile
}

void fragment() {
	// Gimbal-based dither fades. Per-fragment so every part of a straddling
	// patch fades at its own distance. Same hash is shared with the adjacent
	// tier's fade band so the two tiers dither-complement (one discards where
	// the other keeps) in the overlap region.
	float d = distance(
		vec2(v_world_pos.x, v_world_pos.z),
		vec2(gimbal_pos.x, gimbal_pos.z));
	if (d > fog_end_m) discard;
	float h = hash12(FRAGCOORD.xy);
	if (fade_in_end_m > fade_in_start_m) {
		float t = clamp((d - fade_in_start_m) / (fade_in_end_m - fade_in_start_m), 0.0, 1.0);
		if (t < h) discard;
	}
	if (fade_out_end_m > fade_out_start_m) {
		float t = clamp((d - fade_out_start_m) / (fade_out_end_m - fade_out_start_m), 0.0, 1.0);
		if (h <= t) discard;
	}

	// View-space normal for Godot's lighting (VERTEX is view-space in fragment).
	vec3 dx = dFdx(VERTEX);
	vec3 dy = dFdy(VERTEX);
	NORMAL = normalize(cross(dy, dx));

	// World-space-adjacent normal (local differs from world by constant offset
	// on terrain, which vanishes under derivatives) to distinguish top faces
	// (grass) from walls (dirt).
	vec3 wdx = dFdx(v_local_pos);
	vec3 wdy = dFdy(v_local_pos);
	vec3 world_n = normalize(cross(wdy, wdx));

	// Atlas: grass cell at (col=0,row=0), dirt cell 10 at (col=2,row=2).
	// See TileAtlas.cs. Each cell is 0.25×0.25 of the atlas; inset trims
	// the mipmap bleed from neighbouring cells.
	const float inset = 8.0 / 2048.0;
	const float cell_span = 0.25 - 2.0 * inset;
	const vec2 grass_origin = vec2(inset);
	const vec2 dirt_origin = vec2(0.5 + inset, 0.5 + inset);

	// Top sample: tile the grass cell across xz in 1.5m units.
	vec2 grass_uv = grass_origin + fract(UV) * cell_span;
	vec2 grass_du = dFdx(UV) * cell_span;
	vec2 grass_dv = dFdy(UV) * cell_span;
	vec4 grass = textureGrad(albedo_tex, grass_uv, grass_du, grass_dv);

	// Side sample: project onto whichever horizontal axis runs along the wall.
	// Compute both projections unconditionally so derivatives stay clean at
	// face boundaries where fragment groups might straddle both.
	vec2 wall_x_scaled = vec2(v_local_pos.x, v_local_pos.y) * (1.0 / 1.5);
	vec2 wall_z_scaled = vec2(v_local_pos.z, v_local_pos.y) * (1.0 / 1.5);
	vec2 dirt_uv_x = dirt_origin + fract(wall_x_scaled) * cell_span;
	vec2 dirt_uv_z = dirt_origin + fract(wall_z_scaled) * cell_span;
	vec4 dirt_x = textureGrad(albedo_tex, dirt_uv_x,
		dFdx(wall_x_scaled) * cell_span, dFdy(wall_x_scaled) * cell_span);
	vec4 dirt_z = textureGrad(albedo_tex, dirt_uv_z,
		dFdx(wall_z_scaled) * cell_span, dFdy(wall_z_scaled) * cell_span);
	// Wall normal along ±X → run UV along z; along ±Z → run UV along x.
	float pick_z = step(abs(world_n.z), abs(world_n.x));
	vec4 dirt = mix(dirt_x, dirt_z, pick_z);

	float top_factor = smoothstep(0.5, 0.9, world_n.y);
	vec4 tex = mix(dirt, grass, top_factor);

	ALBEDO = tex.rgb * tint.rgb;
	ROUGHNESS = 0.95;
}
