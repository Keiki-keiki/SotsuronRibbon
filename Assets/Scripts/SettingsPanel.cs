using UnityEngine;

/// <summary>
/// 設定画面。棒の先端(tip)でスライダーをなぞって BGM音量・効果音音量・画面の明るさ を調整する。
/// 値は PlayerPrefs に保存。画面の明るさは、カメラ前に置いた黒い半透明の板で暗くする（明るさ30〜100%）。
/// </summary>
public class SettingsPanel
{
    class Slider
    {
        public string label, key;
        public float value, z;
        public Transform handle;
        public TextMesh text;
    }

    const float Width = 0.4f;        // スライダーの長さ(m)
    const float GrabHalfZ = 0.045f;  // 上下の判定幅
    const float DepthTol = 0.06f;    // 奥行きの判定幅
    const float MinBright = 0.3f;    // 明るさの下限

    readonly GameObject root;
    readonly Slider[] sliders;
    readonly AudioSource bgm, sfx;
    Renderer overlay;
    Material overlayMat;

    public SettingsPanel(Vector3 center, Quaternion rot, AudioSource bgm, AudioSource sfx,
                         float charSize, Vector3 backLocal, string backText)
    {
        this.bgm = bgm; this.sfx = sfx;
        root = new GameObject("SettingsPanel");
        root.transform.SetPositionAndRotation(center, rot);

        sliders = new[]
        {
            new Slider { label = "BGM", key = "Set_Bgm2", value = 0.5f, z = 0.12f },
            new Slider { label = "効果音", key = "Set_Sfx", value = 1f, z = 0f },
            new Slider { label = "画面の明るさ", key = "Set_Bright", value = 1f, z = -0.12f },
        };

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        foreach (var s in sliders)
        {
            s.value = PlayerPrefs.GetFloat(s.key, s.value);
            Prim(PrimitiveType.Cube, new Vector3(0f, 0f, s.z), new Vector3(Width, 0.01f, 0.01f), new Color(0.6f, 0.6f, 0.6f));
            s.handle = Prim(PrimitiveType.Sphere, new Vector3(0f, 0f, s.z), Vector3.one * 0.05f, Color.white);
            s.text = Label(font, new Vector3(0f, 0f, s.z + 0.055f), charSize * 1.2f);
            Refresh(s);
        }
        if (!string.IsNullOrEmpty(backText)) Label(font, backLocal + new Vector3(0.2f, 0f, 0f), charSize).text = backText;
        Apply();
    }

    Transform Prim(PrimitiveType type, Vector3 localPos, Vector3 scale, Color c)
    {
        GameObject g = GameObject.CreatePrimitive(type);
        Object.Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(root.transform, false);
        g.transform.localPosition = localPos;
        g.transform.localScale = scale;
        Renderer r = g.GetComponent<Renderer>();
        r.material = new Material(Shader.Find("Sprites/Default"));
        r.material.color = c;
        return g.transform;
    }

    TextMesh Label(Font font, Vector3 localPos, float charSize)
    {
        GameObject g = new GameObject("SettingsLabel");
        g.transform.SetParent(root.transform, false);
        g.transform.localPosition = localPos;
        Vector3 d = Vector3.back;
        if (Camera.main != null) d = Vector3.ProjectOnPlane(Camera.main.transform.position - g.transform.position, Vector3.up);
        if (d.sqrMagnitude < 0.0001f) d = Vector3.back;
        g.transform.rotation = Quaternion.LookRotation(-d.normalized, Vector3.up);
        TextMesh tm = g.AddComponent<TextMesh>();
        tm.font = font; tm.fontSize = 64; tm.characterSize = charSize;
        tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center; tm.color = Color.white;
        g.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        return tm;
    }

    void Refresh(Slider s)
    {
        s.handle.localPosition = new Vector3((s.value - 0.5f) * Width, 0f, s.z);
        s.text.text = s.label + " " + Mathf.RoundToInt(s.value * 100f) + "%";
    }

    public void SetActive(bool active) { root.SetActive(active); }

    /// <summary>毎フレーム呼ぶ。先端がスライダーの帯に入っていれば、その横位置を値にする</summary>
    public void Tick(Transform tip)
    {
        if (tip == null) return;
        Vector3 l = Quaternion.Inverse(root.transform.rotation) * (tip.position - root.transform.position);
        if (Mathf.Abs(l.y) > DepthTol || Mathf.Abs(l.x) > Width * 0.5f + 0.05f) return;
        foreach (var s in sliders)
        {
            if (Mathf.Abs(l.z - s.z) > GrabHalfZ) continue;
            s.value = Mathf.Clamp01((l.x + Width * 0.5f) / Width);
            Refresh(s);
            Apply();
        }
    }

    public void Save()
    {
        foreach (var s in sliders) PlayerPrefs.SetFloat(s.key, s.value);
        PlayerPrefs.Save();
    }

    void Apply()
    {
        if (bgm != null) bgm.volume = sliders[0].value * 0.14f; // スライダー50% = 実音量7%
        if (sfx != null) sfx.volume = sliders[1].value;
        EnsureOverlay();
        if (overlayMat != null)
            overlayMat.color = new Color(0f, 0f, 0f, 1f - Mathf.Lerp(MinBright, 1f, sliders[2].value));
    }

    void EnsureOverlay()
    {
        if (overlay != null || Camera.main == null) return;
        GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = "BrightnessOverlay";
        Object.Destroy(q.GetComponent<Collider>());
        q.transform.SetParent(Camera.main.transform, false);
        q.transform.localPosition = new Vector3(0f, 0f, 0.35f);
        q.transform.localScale = Vector3.one * 3f;
        overlay = q.GetComponent<Renderer>();
        overlayMat = new Material(Shader.Find("Sprites/Default"));
        overlayMat.renderQueue = 4000;
        overlay.material = overlayMat;
        overlayMat = overlay.material; // 実際に使われるインスタンスを保持
        overlay.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
}
