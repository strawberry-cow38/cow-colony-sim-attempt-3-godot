using Godot;
using CowColonySim.Sim.Biomes;
using CowColonySim.Sim.Grid;

namespace CowColonySim.Render;

public sealed partial class DayNightRenderer : Node3D
{
    [Export] public NodePath SunPath { get; set; } = "";
    [Export] public NodePath MoonPath { get; set; } = "";
    [Export] public NodePath WorldEnvPath { get; set; } = "";
    [Export] public float SunMaxEnergy { get; set; } = 1.4f;
    [Export] public float MoonMaxEnergy { get; set; } = 0.35f;
    [Export] public Color SunNoonColor { get; set; } = new(1.0f, 0.97f, 0.88f);
    [Export] public Color SunSetColor { get; set; } = new(1.0f, 0.55f, 0.30f);
    [Export] public Color MoonColor { get; set; } = new(0.72f, 0.80f, 1.0f);

    private SimHost? _sim;
    private DirectionalLight3D? _sun;
    private DirectionalLight3D? _moon;
    private WorldEnvironment? _worldEnv;
    private ShaderMaterial? _sky;

    public override void _Ready()
    {
        _sim = GetNode<SimHost>("/root/SimHost");
        _sun = GetNodeOrNull<DirectionalLight3D>(SunPath);
        _moon = GetNodeOrNull<DirectionalLight3D>(MoonPath);
        _worldEnv = GetNodeOrNull<WorldEnvironment>(WorldEnvPath);
        BuildEnvironment();
    }

    public override void _Process(double delta)
    {
        if (_sim == null) return;
        var frac = _sim.TimeOfDay.DayFraction;
        var sunAngle = (frac - 0.25f) * Mathf.Tau;
        var sunDir = new Vector3(Mathf.Cos(sunAngle), Mathf.Sin(sunAngle), 0.35f).Normalized();
        var moonDir = -sunDir;

        var sunHeight = sunDir.Y;
        var nightAmount = Mathf.Clamp(-sunHeight * 2.0f + 0.15f, 0f, 1f);
        var daylight = Mathf.Clamp(sunHeight, 0f, 1f);
        var sunsetAmount = Mathf.Pow(1f - Mathf.Abs(sunHeight), 6f) * (sunHeight > -0.1f ? 1f : 0f);

        if (_sun != null)
        {
            _sun.LookAtFromPosition(Vector3.Zero, -sunDir, Vector3.Up);
            _sun.LightEnergy = SunMaxEnergy * Mathf.Clamp(daylight + 0.05f, 0f, 1f);
            var col = SunNoonColor.Lerp(SunSetColor, sunsetAmount);
            _sun.LightColor = col;
            _sun.Visible = sunHeight > -0.05f;
        }
        if (_moon != null)
        {
            _moon.LookAtFromPosition(Vector3.Zero, -moonDir, Vector3.Up);
            _moon.LightEnergy = MoonMaxEnergy * nightAmount;
            _moon.LightColor = MoonColor;
            _moon.Visible = moonDir.Y > -0.05f;
        }

        if (_sky != null)
        {
            _sky.SetShaderParameter("sun_dir", sunDir);
            _sky.SetShaderParameter("moon_dir", moonDir);
            _sky.SetShaderParameter("night_amount", nightAmount);
            _sky.SetShaderParameter("sunset_amount", sunsetAmount);
        }

        if (_worldEnv?.Environment != null)
        {
            var env = _worldEnv.Environment;
            var ambientDay = new Color(0.55f, 0.70f, 0.95f);
            var ambientNight = new Color(0.10f, 0.14f, 0.25f);
            env.AmbientLightColor = ambientNight.Lerp(ambientDay, daylight);
            env.AmbientLightEnergy = 0.4f + 0.6f * daylight;

            // Fog = day/sunset/night phase × biome tint. Phase color tracks
            // horizon palette so fog and sky meet at the same band; biome
            // tint gives deserts a dusty warm haze, jungles a humid green,
            // plains read neutral. Tint multiplies onto phase so night still
            // dominates (desert night stays dark, not dusty orange).
            var fogDay = new Color(0.75f, 0.80f, 0.85f);
            var fogSunset = new Color(1.00f, 0.62f, 0.38f);
            var fogNight = new Color(0.08f, 0.10f, 0.18f);
            var fogPhase = fogDay.Lerp(fogSunset, sunsetAmount).Lerp(fogNight, nightAmount);

            var tint = BiomeFogTint(_sim.Overworld.Get(_sim.CurrentMapCoord).BiomeId);
            env.FogLightColor = new Color(
                fogPhase.R * tint.R,
                fogPhase.G * tint.G,
                fogPhase.B * tint.B,
                1f);
            env.FogLightEnergy = 0.7f + 0.3f * daylight;
        }
    }

    private static Color BiomeFogTint(byte biomeId) => biomeId switch
    {
        BiomeBuiltins.DesertId          => new Color(1.20f, 1.05f, 0.78f),
        BiomeBuiltins.SavannaId         => new Color(1.12f, 1.02f, 0.82f),
        BiomeBuiltins.JungleId          => new Color(0.82f, 1.02f, 0.88f),
        BiomeBuiltins.TemperateForestId => new Color(0.92f, 1.00f, 0.95f),
        BiomeBuiltins.TaigaId           => new Color(0.92f, 0.98f, 1.02f),
        BiomeBuiltins.TundraId          => new Color(0.96f, 0.99f, 1.04f),
        BiomeBuiltins.SnowId            => new Color(0.98f, 1.00f, 1.06f),
        BiomeBuiltins.StoneId           => new Color(0.95f, 0.95f, 0.98f),
        _                               => new Color(1.00f, 1.00f, 1.00f),
    };

    private void BuildEnvironment()
    {
        if (_worldEnv == null) return;
        var shader = GD.Load<Shader>("res://scripts/render/shaders/dream_sky.gdshader");
        _sky = new ShaderMaterial { Shader = shader };

        var sky = new Sky { SkyMaterial = _sky, RadianceSize = Sky.RadianceSizeEnum.Size128 };
        // Fog saturates at the 3×3 world's far axis edge so terrain
        // dissolves exactly where generation stops. Begin at the pocket
        // border so inside-pocket view stays crisp and neighbor cells fade
        // as they recede.
        var fogEnd = GridRenderer.FogEndMeters;
        var fogBegin = Cell.SizeTiles * TileCoord.TileW * 0.5f;
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = sky,
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightColor = new Color(0.55f, 0.70f, 0.95f),
            AmbientLightEnergy = 0.7f,
            AmbientLightSkyContribution = 0.4f,
            SsaoEnabled = false,
            GlowEnabled = false,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapExposure = 1.0f,
            // Depth fog on every terrain tier via Godot's lighting path.
            // FogSkyAffect = 0 keeps the sky shader (incl. below-horizon
            // brown floor plate) untouched so only ground fogs out.
            FogEnabled = true,
            FogMode = Godot.Environment.FogModeEnum.Depth,
            FogDepthBegin = fogBegin,
            FogDepthEnd = fogEnd,
            FogDepthCurve = 1.0f,
            FogLightColor = new Color(0.75f, 0.80f, 0.85f),
            FogLightEnergy = 1.0f,
            FogSkyAffect = 0f,
        };
        _worldEnv.Environment = env;
    }
}
