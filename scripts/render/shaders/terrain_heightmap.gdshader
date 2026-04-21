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
// Per-cell TileKind byte (see Sim/Grid/Tile.cs). Nearest filter because the
// value is an enum — linear interpolation at cell borders would dream up
// nonexistent kinds. 1=Solid, 2=Floor(grass), 3=Water, 4=Sand.
uniform sampler2D kindmap : hint_default_black, filter_nearest, repeat_disable;
uniform sampler2D albedo_tex : filter_linear_mipmap_anisotropic, repeat_enable;
// Water tops sit half a tile below the raw column ceiling so shores read
// as a step above the waterline. Matches the L0 / L1 mesher offset.
uniform float water_top_drop_m = 0.375;
uniform vec3 water_tint = vec3(0.30, 0.50, 0.85);

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
// Hard clip past fog depth-end. Godot's built-in depth fog (with
// FogSkyAffect=0) handles the visual fog ramp across all tiers; this
// stays in place to discard G8 fragments past the cylindrical range so
// past-distance patches don't draw at all.
uniform float fog_end_m = 1e9;
// Tint for debug / palette.
// Darken to match the tone L0/G4 terrain lands at after StandardMaterial3D
// shading — raw grass texels are brighter than the lit near-field look.
uniform vec4 tint : source_color = vec4(0.7, 0.7, 0.7, 1.0);

varying vec3 v_local_pos;
varying vec3 v_world_pos;
varying vec2 v_uv_local;

float sample_height(vec2 uv_local) {
	// uv_local: [0..1] over patch.
	return texture(heightmap, uv_local).r * height_scale_m;
}

float sample_kind(vec2 uv_local) {
	// kindmap is R8 unorm storing TileKind byte; decode back to [0..255].
	return texture(kindmap, uv_local).r * 255.0;
}

// Interleaved Gradient Noise (Jimenez). Approximates blue-noise spectrum from
// just three float ops — no texture fetch — so neighboring pixels get values
// that are decorrelated rather than forming the bright grid patterns white-
// noise hashes produce. Good enough that it's what COD/UE ship for their
// screen-space dithers.
float ign(vec2 p) {
	return fract(52.9829189 * fract(0.06711056 * p.x + 0.00583715 * p.y));
}

void vertex() {
	// Sample heightmap via the UV attribute from the mesh. Stepped patches put
	// multiple verts at the same XZ with different intended heights (wall top vs
	// bottom), so we can't derive the sample coord from position alone.
	vec2 uv_local = UV;
	float h = sample_height(uv_local);
	// Water top sits half a tile below the raw ceiling so shores read as a step
	// above the waterline. Matches NaiveChunkMesher's topDrop path for L0/L1.
	float kind_byte = sample_kind(uv_local);
	float is_water = step(2.5, kind_byte) * step(kind_byte, 3.5);
	VERTEX.y += h - is_water * water_top_drop_m;
	v_local_pos = VERTEX;
	v_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_uv_local = uv_local;
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
	// Per-frame TIME offset so the dither pattern walks each frame. Between
	// flicker-fusion and the eye's temporal integration the noise reads as
	// soft grain instead of a static pixel sieve. Golden-ratio multipliers on
	// TIME keep the offset from ever repeating within a sane session length.
	vec2 dither_off = vec2(fract(TIME * 1.6180339887), fract(TIME * 2.7182818285)) * 64.0;
	float h = ign(FRAGCOORD.xy + dither_off);
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

	// Atlas: grass cell at (col=0,row=0), dirt cell 10 at (col=2,row=2),
	// sand cell 15 at (col=3,row=3). See TileAtlas.cs. Each cell is
	// 0.25×0.25 of the atlas; inset trims mipmap bleed from neighbours.
	const float inset = 8.0 / 2048.0;
	const float cell_span = 0.25 - 2.0 * inset;
	const vec2 grass_origin = vec2(inset);
	const vec2 dirt_origin = vec2(0.5 + inset, 0.5 + inset);
	const vec2 sand_origin = vec2(0.75 + inset, 0.75 + inset);

	// Kind per-fragment. filter_nearest snaps to the cell center, so interior
	// fragments land on a single stable byte; edges may straddle but the wall
	// texture branches hide it.
	float kind_byte = sample_kind(v_uv_local);
	bool is_water = kind_byte > 2.5 && kind_byte < 3.5;
	bool is_sand  = kind_byte > 3.5 && kind_byte < 4.5;

	// Top sample: grass by default; sand on beach columns.
	vec2 top_origin = is_sand ? sand_origin : grass_origin;
	vec2 top_uv = top_origin + fract(UV) * cell_span;
	vec2 top_du = dFdx(UV) * cell_span;
	vec2 top_dv = dFdy(UV) * cell_span;
	vec4 top_tex = textureGrad(albedo_tex, top_uv, top_du, top_dv);

	// Side sample: project onto whichever horizontal axis runs along the wall.
	// Compute both projections unconditionally so derivatives stay clean at
	// face boundaries where fragment groups might straddle both.
	vec2 side_origin = is_sand ? sand_origin : dirt_origin;
	vec2 wall_x_scaled = vec2(v_local_pos.x, v_local_pos.y) * (1.0 / 1.5);
	vec2 wall_z_scaled = vec2(v_local_pos.z, v_local_pos.y) * (1.0 / 1.5);
	vec2 side_uv_x = side_origin + fract(wall_x_scaled) * cell_span;
	vec2 side_uv_z = side_origin + fract(wall_z_scaled) * cell_span;
	vec4 side_x = textureGrad(albedo_tex, side_uv_x,
		dFdx(wall_x_scaled) * cell_span, dFdy(wall_x_scaled) * cell_span);
	vec4 side_z = textureGrad(albedo_tex, side_uv_z,
		dFdx(wall_z_scaled) * cell_span, dFdy(wall_z_scaled) * cell_span);
	// Wall normal along ±X → run UV along z; along ±Z → run UV along x.
	float pick_z = step(abs(world_n.z), abs(world_n.x));
	vec4 side = mix(side_x, side_z, pick_z);

	float top_factor = smoothstep(0.5, 0.9, world_n.y);
	vec4 tex = mix(side, top_tex, top_factor);

	vec3 albedo = tex.rgb;
	float roughness = 0.95;
	if (is_water) {
		// Water cells skip atlas sampling entirely — solid tint matches the
		// L0/L1 water material which rides cell 9 (white) × TintFor(Water).
		albedo = water_tint;
		roughness = 0.4;
	}

	ALBEDO = albedo * tint.rgb;
	ROUGHNESS = roughness;
}
