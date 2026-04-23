shader_type sky;

uniform vec3 sun_dir = vec3(0.0, 1.0, 0.0);
uniform vec3 moon_dir = vec3(0.0, -1.0, 0.0);
uniform vec3 zenith_day : source_color = vec3(0.20, 0.45, 0.85);
uniform vec3 horizon_day : source_color = vec3(0.75, 0.88, 0.95);
uniform vec3 zenith_night : source_color = vec3(0.01, 0.02, 0.06);
uniform vec3 horizon_night : source_color = vec3(0.04, 0.05, 0.10);
uniform vec3 zenith_sunset : source_color = vec3(0.20, 0.10, 0.35);
uniform vec3 horizon_sunset : source_color = vec3(1.0, 0.45, 0.12);
uniform float sun_sharpness = 4000.0;
uniform float sun_energy = 40.0;
uniform vec3 sun_color : source_color = vec3(1.0, 0.9, 0.65);
uniform float moon_sharpness = 3000.0;
uniform float moon_energy = 2.0;
uniform vec3 moon_color : source_color = vec3(0.85, 0.90, 1.0);
uniform float night_amount = 0.0;
uniform float sunset_amount = 0.0;

float hash21(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float star_field(vec3 dir) {
    if (dir.y <= 0.0) return 0.0;
    vec3 ad = abs(dir);
    vec2 uv;
    if (ad.y >= ad.x && ad.y >= ad.z) {
        uv = dir.xz / dir.y;
    } else if (ad.x >= ad.z) {
        uv = dir.yz / ad.x;
    } else {
        uv = dir.xy / ad.z;
    }
    vec2 grid = uv * 180.0;
    vec2 cell = floor(grid);
    float r = hash21(cell);
    float bright = step(0.992, r) * pow(hash21(cell + 7.13), 6.0);
    float twinkle = 0.5 + 0.5 * sin(TIME * (0.5 + hash21(cell + 1.7) * 3.0) + hash21(cell) * 6.28);
    return bright * twinkle * 3.0;
}

void sky() {
    vec3 dir = EYEDIR;
    float up = clamp(dir.y, -1.0, 1.0);
    float up01 = clamp(up, 0.0, 1.0);

    vec3 zenith_ds = mix(zenith_day, zenith_sunset, sunset_amount);
    vec3 horizon_ds = mix(horizon_day, horizon_sunset, sunset_amount);
    vec3 zenith = mix(zenith_ds, zenith_night, night_amount);
    vec3 horizon = mix(horizon_ds, horizon_night, night_amount);

    // Ground plate bleeds slightly above the true horizon (up=0) so that
    // narrow sky strips between distant terrain tiers and the plate are
    // covered by the plate color instead of showing pale horizon blue.
    // Plate color = mid-sky band (horizon blended toward zenith) so the
    // seam to fogged ground reads as continuous sky instead of a hard
    // brown swatch. Tracks day/sunset/night automatically via horizon/zenith.
    float ground_blend = 1.0 - smoothstep(0.0, 0.05, up);
    vec3 sky_above = mix(horizon, zenith, pow(up01, 0.55));
    vec3 ground_col = mix(horizon, zenith, 0.25);
    vec3 sky_col = mix(sky_above, ground_col, ground_blend);

    float sun_d = max(dot(dir, normalize(sun_dir)), 0.0);
    vec3 sun = pow(sun_d, sun_sharpness) * sun_color * sun_energy * (1.0 - night_amount);

    float moon_d = max(dot(dir, normalize(moon_dir)), 0.0);
    vec3 moon = pow(moon_d, moon_sharpness) * moon_color * moon_energy * night_amount;

    float stars = star_field(dir) * night_amount;

    COLOR = sky_col + sun + moon + stars * vec3(1.0, 1.0, 0.95);
}
