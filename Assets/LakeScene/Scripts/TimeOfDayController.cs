using UnityEngine;

/// <summary>
/// 夕焼け → 夜 → 朝霧 → (夕焼け…) をプリセット補間でゆっくり切り替える。
/// Built-in RP / Quest 向けに、毎フレームの処理は値の補間のみ。
/// 研究実験などで環境を固定したい場合は autoPlay を OFF にし、startIndex か Jump() で固定する。
/// </summary>
[DisallowMultipleComponent]
public class TimeOfDayController : MonoBehaviour
{
    [Header("Presets (この順番で循環)")]
    public TimeOfDayPreset[] presets;

    [Header("Timing (seconds)")]
    public bool autoPlay = true;
    public bool loop = true;
    [Min(0f)] public float holdSeconds = 60f;
    [Min(1f)] public float transitionSeconds = 45f;
    public int startIndex = 0;

    [Header("References (空なら自動検索)")]
    public Light sun;
    public Transform lakeRoot;

    static readonly int TopId = Shader.PropertyToID("_TopColor");
    static readonly int MidId = Shader.PropertyToID("_MidColor");
    static readonly int HorizonId = Shader.PropertyToID("_HorizonColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    class Item
    {
        public Renderer r;
        public Color baseColor;
    }

    Item moon, moonRefl, glint, ripple, firefly, water, sakura, star;
    Item[] mountains = new Item[3];
    Item[] lanterns = new Item[2];

    MaterialPropertyBlock mpb;
    Material skyMat;
    int current;
    float phaseTime;
    bool ready;

    void Start()
    {
        if (presets == null || presets.Length == 0)
        {
            Debug.LogWarning("[TimeOfDay] presets が未設定です。");
            enabled = false;
            return;
        }

        if (sun == null) sun = RenderSettings.sun;
        if (lakeRoot == null)
        {
            var go = GameObject.Find("LakeScene_Night");
            if (go != null) lakeRoot = go.transform;
        }

        // スカイボックスはインスタンス化してアセット本体を書き換えない
        if (RenderSettings.skybox != null)
        {
            skyMat = new Material(RenderSettings.skybox);
            RenderSettings.skybox = skyMat;
        }

        mpb = new MaterialPropertyBlock();
        moon = Make("Mesh_Moon");
        moonRefl = Make("Mesh_MoonReflection");
        glint = Make("Mesh_Glint");
        ripple = Make("Mesh_Ripple");
        firefly = Make("Mesh_Firefly");
        water = Make("Mesh_Water");
        sakura = Make("Mesh_Sakura");
        star = Make("Mesh_Star");
        mountains[0] = Make("Mesh_Mountain1");
        mountains[1] = Make("Mesh_Mountain2");
        mountains[2] = Make("Mesh_Mountain3");
        lanterns[0] = Make("Mesh_LanternGlow");
        lanterns[1] = Make("Mesh_FloatLantern");

        current = Mathf.Clamp(startIndex, 0, presets.Length - 1);
        phaseTime = 0f;
        ready = true;
        Apply(presets[current], presets[current], 0f);
    }

    void OnDestroy()
    {
        if (skyMat != null) Destroy(skyMat);
    }

    void Update()
    {
        if (!ready || !autoPlay || presets.Length < 2) return;

        float cycle = holdSeconds + transitionSeconds;
        phaseTime += Time.deltaTime;

        bool isLast = current == presets.Length - 1;
        if (isLast && !loop)
        {
            // 最後のプリセットで固定
            phaseTime = Mathf.Min(phaseTime, holdSeconds);
            Apply(presets[current], presets[current], 0f);
            return;
        }

        while (phaseTime >= cycle)
        {
            phaseTime -= cycle;
            current = (current + 1) % presets.Length;
            isLast = current == presets.Length - 1;
            if (isLast && !loop) { phaseTime = 0f; break; }
        }

        int next = (current + 1) % presets.Length;
        float k = 0f;
        if (phaseTime > holdSeconds)
            k = Mathf.SmoothStep(0f, 1f, (phaseTime - holdSeconds) / transitionSeconds);

        Apply(presets[current], presets[next], k);
    }

    // ---------- Public API ----------

    /// <summary>指定プリセットへ即座に切り替え、そこから経過を再開する。</summary>
    public void Jump(int index)
    {
        if (!ready) return;
        current = Mathf.Clamp(index, 0, presets.Length - 1);
        phaseTime = 0f;
        Apply(presets[current], presets[current], 0f);
    }

    /// <summary>from → 次プリセット の途中状態 (0-1) をその場で表示する (確認・調整用)。</summary>
    public void Preview(int fromIndex, float blend01)
    {
        if (!ready) return;
        int a = Mathf.Clamp(fromIndex, 0, presets.Length - 1);
        int b = (a + 1) % presets.Length;
        Apply(presets[a], presets[b], Mathf.Clamp01(blend01));
    }

    public void SetAutoPlay(bool value) { autoPlay = value; }

    // ---------- Internals ----------

    Item Make(string objName)
    {
        if (lakeRoot == null) return null;
        var t = lakeRoot.Find(objName);
        if (t == null)
        {
            Debug.LogWarning("[TimeOfDay] 見つかりません: " + objName);
            return null;
        }
        var r = t.GetComponent<Renderer>();
        if (r == null || r.sharedMaterial == null) return null;
        return new Item { r = r, baseColor = r.sharedMaterial.GetColor(ColorId) };
    }

    void SetColor(Item it, Color c)
    {
        if (it == null) return;
        it.r.GetPropertyBlock(mpb);
        mpb.SetColor(ColorId, c);
        it.r.SetPropertyBlock(mpb);
    }

    // 不透明Unlitなので、見えなくする代わりに「背景色へ溶け込ませる」ことで自然にフェードさせる
    void SetFade(Item it, float vis, Color blendTarget)
    {
        if (it == null) return;
        bool show = vis > 0.01f;
        if (it.r.enabled != show) it.r.enabled = show;
        if (show) SetColor(it, Color.Lerp(blendTarget, it.baseColor, vis));
    }

    void SetGlow(Item it, float intensity)
    {
        if (it == null) return;
        Color c = it.baseColor * intensity;
        c.a = 1f;
        SetColor(it, c);
    }

    void Apply(TimeOfDayPreset a, TimeOfDayPreset b, float k)
    {
        // Sky
        Color top = Color.Lerp(a.skyTop, b.skyTop, k);
        Color mid = Color.Lerp(a.skyMid, b.skyMid, k);
        Color hor = Color.Lerp(a.skyHorizon, b.skyHorizon, k);
        if (skyMat != null)
        {
            skyMat.SetColor(TopId, top);
            skyMat.SetColor(MidId, mid);
            skyMat.SetColor(HorizonId, hor);
        }

        // Light
        if (sun != null)
        {
            sun.color = Color.Lerp(a.sunColor, b.sunColor, k);
            sun.intensity = Mathf.Lerp(a.sunIntensity, b.sunIntensity, k);
            sun.transform.rotation = Quaternion.Slerp(Quaternion.Euler(a.sunEuler), Quaternion.Euler(b.sunEuler), k);
        }
        RenderSettings.ambientLight = Color.Lerp(a.ambientColor, b.ambientColor, k);

        // Fog
        Color fog = Color.Lerp(a.fogColor, b.fogColor, k);
        RenderSettings.fogColor = fog;
        RenderSettings.fogDensity = Mathf.Lerp(a.fogDensity, b.fogDensity, k);

        // Surfaces
        Color waterC = Color.Lerp(a.water, b.water, k);
        SetColor(water, waterC);
        SetColor(ripple, Color.Lerp(a.ripple, b.ripple, k));
        SetColor(sakura, Color.Lerp(a.sakura, b.sakura, k));
        SetColor(mountains[0], Color.Lerp(a.mountainNear, b.mountainNear, k));
        SetColor(mountains[1], Color.Lerp(a.mountainMid, b.mountainMid, k));
        SetColor(mountains[2], Color.Lerp(a.mountainFar, b.mountainFar, k));

        // Celestial / glow
        SetFade(moon, Mathf.Lerp(a.moon, b.moon, k), mid);
        SetFade(star, Mathf.Lerp(a.stars, b.stars, k), Color.Lerp(mid, top, 0.5f));
        SetFade(moonRefl, Mathf.Lerp(a.moon, b.moon, k), waterC);
        SetFade(glint, Mathf.Lerp(a.moonGlint, b.moonGlint, k), waterC);
        SetFade(firefly, Mathf.Lerp(a.fireflies, b.fireflies, k), fog);

        float lan = Mathf.Lerp(a.lanterns, b.lanterns, k);
        SetGlow(lanterns[0], lan);
        SetGlow(lanterns[1], lan);
    }
}
