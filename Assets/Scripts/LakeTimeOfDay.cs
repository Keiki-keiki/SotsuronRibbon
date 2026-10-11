using UnityEngine;

/// <summary>
/// 夕方 → 夜 → (霧 or 晴 を抽選) → 夕方… をループ補間する。
/// 空(Procedural Skybox: 色・大気・太陽サイズ)・星・環境光・フォグ・Directional Light を制御。
/// </summary>
public class LakeTimeOfDay : MonoBehaviour
{
    [System.Serializable]
    public class Phase
    {
        public string name;
        public Color skyTint = Color.gray;
        public Color ground = Color.gray;
        public float atmosphere = 1f;
        public float sunSize = 0.04f;
        public float exposure = 1f;
        public Color ambient = Color.gray;
        public Color fogColor = Color.gray;
        public float fogDensity = 0.01f;
        public Color lightColor = Color.white;
        public float lightIntensity = 1f;
        public Vector2 sunAngle = new Vector2(30, -30);
        [Range(0, 1)] public float stars = 0f;
    }

    public Light sun;
    [Tooltip("1フェーズ(夕方/夜/朝)の秒数")]
    public float phaseSeconds = 60f;
    [Range(0f, 0.9f)] public float hold = 0.4f;
    [Tooltip("朝が晴れになる確率(0.5=霧と晴れが1/2)")]
    [Range(0f, 1f)] public float clearChance = 0.5f;
    public Material starMaterial; // 未設定なら Sprites/Default

    public Phase sunsetP = new Phase { name = "Sunset",
        skyTint = new Color(1.0f, 0.50f, 0.35f), ground = new Color(0.35f, 0.25f, 0.25f),
        atmosphere = 1.6f, sunSize = 0.07f, exposure = 1.1f,
        ambient = new Color(0.55f, 0.35f, 0.35f),
        fogColor = new Color(0.95f, 0.55f, 0.45f), fogDensity = 0.015f,
        lightColor = new Color(1.0f, 0.55f, 0.30f), lightIntensity = 1.1f,
        sunAngle = new Vector2(6, -60), stars = 0f };
    public Phase nightP = new Phase { name = "Night",
        skyTint = new Color(0.08f, 0.10f, 0.25f), ground = new Color(0.05f, 0.06f, 0.12f),
        atmosphere = 0.4f, sunSize = 0.0f, exposure = 0.35f,
        ambient = new Color(0.10f, 0.12f, 0.22f),
        fogColor = new Color(0.12f, 0.16f, 0.30f), fogDensity = 0.012f,
        lightColor = new Color(0.55f, 0.65f, 1.0f), lightIntensity = 0.35f,
        sunAngle = new Vector2(-40, 120), stars = 1f };
    public Phase mistP = new Phase { name = "MorningMist",
        skyTint = new Color(0.80f, 0.88f, 0.92f), ground = new Color(0.65f, 0.70f, 0.72f),
        atmosphere = 1.8f, sunSize = 0.02f, exposure = 0.95f,
        ambient = new Color(0.60f, 0.66f, 0.70f),
        fogColor = new Color(0.85f, 0.90f, 0.92f), fogDensity = 0.03f,
        lightColor = new Color(1.0f, 0.95f, 0.85f), lightIntensity = 0.8f,
        sunAngle = new Vector2(12, 70), stars = 0f };
    public Phase clearP = new Phase { name = "MorningClear",
        skyTint = new Color(0.45f, 0.60f, 0.85f), ground = new Color(0.45f, 0.48f, 0.50f),
        atmosphere = 1.0f, sunSize = 0.05f, exposure = 1.3f,
        ambient = new Color(0.62f, 0.70f, 0.82f),
        fogColor = new Color(0.72f, 0.82f, 0.92f), fogDensity = 0.004f,
        lightColor = new Color(1.0f, 0.96f, 0.88f), lightIntensity = 1.25f,
        sunAngle = new Vector2(32, 70), stars = 0f };

    int slot;           // 0=夕方 1=夜 2=朝
    float t;
    Phase morning;
    ParticleSystem starPS;
    Material starMat;

    Phase Get(int s) => s == 0 ? sunsetP : s == 1 ? nightP : morning;

    void Start()
    {
        if (sun == null) sun = RenderSettings.sun;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        PickMorning();
        MakeStars();
        Apply(Get(0), Get(1), 0f);
    }

    void PickMorning() => morning = Random.value < clearChance ? clearP : mistP;

    void Update()
    {
        t += Time.deltaTime / Mathf.Max(1f, phaseSeconds);
        if (t >= 1f)
        {
            t -= 1f;
            slot = (slot + 1) % 3;
            if (slot == 1) PickMorning(); // 夜に入る時点で朝の種類を決定
        }
        Apply(Get(slot), Get((slot + 1) % 3), t);
    }

    void LateUpdate()
    {
        if (starPS != null && Camera.main != null)
            starPS.transform.position = Camera.main.transform.position;
    }

    void Apply(Phase a, Phase b, float local)
    {
        float k = Mathf.Clamp01((local - hold) / (1f - hold));
        k = k * k * (3f - 2f * k);

        var sky = RenderSettings.skybox;
        if (sky != null)
        {
            Set(sky, "_SkyTint", Color.Lerp(a.skyTint, b.skyTint, k));
            Set(sky, "_GroundColor", Color.Lerp(a.ground, b.ground, k));
            Set(sky, "_AtmosphereThickness", Mathf.Lerp(a.atmosphere, b.atmosphere, k));
            Set(sky, "_SunSize", Mathf.Lerp(a.sunSize, b.sunSize, k));
            Set(sky, "_Exposure", Mathf.Lerp(a.exposure, b.exposure, k));
        }

        RenderSettings.ambientLight = Color.Lerp(a.ambient, b.ambient, k);
        RenderSettings.fogColor = Color.Lerp(a.fogColor, b.fogColor, k);
        RenderSettings.fogDensity = Mathf.Lerp(a.fogDensity, b.fogDensity, k);

        if (sun != null)
        {
            sun.color = Color.Lerp(a.lightColor, b.lightColor, k);
            sun.intensity = Mathf.Lerp(a.lightIntensity, b.lightIntensity, k);
            Vector2 ang = Vector2.Lerp(a.sunAngle, b.sunAngle, k);
            sun.transform.rotation = Quaternion.Euler(ang.x, ang.y, 0f);
        }

        if (starMat != null)
            starMat.color = new Color(1, 1, 1, Mathf.Lerp(a.stars, b.stars, k));

        DynamicGI.UpdateEnvironment();
    }

    static void Set(Material m, string p, Color c) { if (m.HasProperty(p)) m.SetColor(p, c); }
    static void Set(Material m, string p, float v) { if (m.HasProperty(p)) m.SetFloat(p, v); }

    void MakeStars()
    {
        var go = new GameObject("Stars");
        go.transform.SetParent(transform, false);
        starPS = go.AddComponent<ParticleSystem>();
        var main = starPS.main;
        main.loop = false; main.playOnAwake = false;
        main.startLifetime = 1e6f; main.maxParticles = 600;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        var em = starPS.emission; em.enabled = false;
        var sh = starPS.shape; sh.enabled = false;

        var r = go.GetComponent<ParticleSystemRenderer>();
        starMat = starMaterial != null ? new Material(starMaterial) : new Material(Shader.Find("Sprites/Default"));
        r.material = starMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        for (int i = 0; i < 500; i++)
        {
            Vector3 v = Random.onUnitSphere; v.y = Mathf.Abs(v.y) + 0.03f;
            var p = new ParticleSystem.EmitParams
            {
                position = v.normalized * 200f,
                startSize = Random.Range(0.4f, 1.2f),
                startLifetime = 1e6f,
                startColor = Color.white
            };
            starPS.Emit(p, 1);
        }
    }
}
