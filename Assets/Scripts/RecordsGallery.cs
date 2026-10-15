using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 捕まえたトンボの記録（PlayerPrefs）と、記録画面のギャラリー表示を担当する。
/// RotationBurstGame と同じ GameObject にアタッチして使う。
/// </summary>
[RequireComponent(typeof(RotationBurstGame))]
[RequireComponent(typeof(TonboController))]
public class RecordsGallery : MonoBehaviour
{
    [Header("配置")]
    [Tooltip("記録画面（トンボ一覧）の位置。リングの初期位置からのずれ（X=右、Y=上、Z=奥）")]
    public Vector3 galleryOffset = new Vector3(0.3f, 0.038f, 0.032f);

    // トンボの実寸の目安：Prefabの倍率1あたりの翼長（メートル）。元モデルは約3.9m
    private float tonboModelSpanMeters = 3.9f;
    // 保存する色違いの最大種類数（超えたら古いものから消える）
    private int maxStoredVariants = 10;
    // 記録画面：トンボ一覧の列数（超えたら下の段に折り返す）
    private int galleryColumns = 3;
    // 一覧のトンボ同士の間隔（メートル）
    private float gallerySpacing = 0.09f;
    // 一覧のトンボの大きさ（Prefab倍率）
    private float galleryTonboScale = 0.02f;
    // 記録画面：最大・最小のトンボを並べる段の高さ（一覧の中心からのローカルZ、メートル）
    private float recordsHighlightLocalZ = 0.13f;
    // 記録画面：最大と最小のトンボの左右の間隔（メートル）
    private float recordsHighlightSpacing = 0.22f;
    // 記録画面：色違い一覧の中心の高さ（一覧の中心からのローカルZ、メートル）
    private float recordsGridLocalZ = -0.12f;
    // 記録画面：「捕まえた数」ラベルの高さ（ローカルZ、メートル）
    private float recordsCountLocalZ = 0.26f;
    // 記録画面：「つかまえた色」見出しの高さ（ローカルZ、メートル）
    private float recordsColorsHeaderLocalZ = -0.01f;
    // 記録画面：トンボをプレイヤーに向けたうえで、さらに横へ回す角度（度）。0で真正面
    private float galleryViewYaw = 30f;
    // 記録画面：トンボをプレイヤーに向けたうえで、頭を下げる角度（度）。上から見える分、胴体の色が分かりやすい
    private float galleryViewPitch = 20f;

    private RotationBurstGame game;
    private TonboController tonbo;
    private GameObject galleryRoot;
    private readonly List<Material> galleryMats = new List<Material>();

    void Awake()
    {
        game = GetComponent<RotationBurstGame>();
        tonbo = GetComponent<TonboController>();
    }

    // 記録はタイプ（リアル／キュート）ごとに別保存（色の数などがモデルで違うため）
    private string Sfx { get { return game.CuteMode ? "_cute" : ""; } }
    private string KeyCount { get { return "Tonbo_Count" + Sfx; } }
    private string KeyNormal { get { return "Tonbo_NormalCount" + Sfx; } }
    private string KeyMax { get { return "Tonbo_MaxSize" + Sfx; } }
    private string KeyMin { get { return "Tonbo_MinSize" + Sfx; } }
    private string KeyVariants { get { return "Tonbo_Variants" + Sfx; } }
    private string KeyMaxColors { get { return "Tonbo_MaxColors" + Sfx; } } // 最大個体の配色（空=通常色）
    private string KeyMinColors { get { return "Tonbo_MinColors" + Sfx; } } // 最小個体の配色（空=通常色）

    /// <summary>ギャラリーを隠す（タイトルやゲーム開始時）</summary>
    public void HideGallery()
    {
        if (galleryRoot != null) galleryRoot.SetActive(false);
    }

    // ===== 捕まえたトンボの記録 =====

    /// <summary>円をクリアした瞬間に、今のトンボの色・大きさを記録する</summary>
    public void RecordCaught()
    {
        if (!tonbo.IsActive) return;

        List<Color> variantColors = tonbo.VariantColors;
        float size = tonbo.CurrentScale * tonboModelSpanMeters;
        int count = PlayerPrefs.GetInt(KeyCount, 0);
        float prevMax = PlayerPrefs.GetFloat(KeyMax, size);
        float prevMin = PlayerPrefs.GetFloat(KeyMin, size);
        string colorStr = (variantColors.Count == 0) ? "" : EncodeVariant(variantColors);

        // 最大・最小を更新したら、その個体の配色（モデル）も一緒に保存する
        if (count == 0 || size > prevMax)
        {
            PlayerPrefs.SetFloat(KeyMax, size);
            PlayerPrefs.SetString(KeyMaxColors, colorStr);
        }
        if (count == 0 || size < prevMin)
        {
            PlayerPrefs.SetFloat(KeyMin, size);
            PlayerPrefs.SetString(KeyMinColors, colorStr);
        }
        PlayerPrefs.SetInt(KeyCount, count + 1);

        if (variantColors.Count == 0)
        {
            PlayerPrefs.SetInt(KeyNormal, PlayerPrefs.GetInt(KeyNormal, 0) + 1);
        }
        else
        {
            List<string> list = LoadVariantStrings();
            if (!list.Contains(colorStr))
            {
                list.Add(colorStr);
                while (list.Count > maxStoredVariants) list.RemoveAt(0);
                PlayerPrefs.SetString(KeyVariants, string.Join(";", list.ToArray()));
            }
        }
        PlayerPrefs.Save();
    }

    List<string> LoadVariantStrings()
    {
        var list = new List<string>();
        string s = PlayerPrefs.GetString(KeyVariants, "");
        if (s.Length > 0) list.AddRange(s.Split(';'));
        return list;
    }

    string EncodeVariant(List<Color> colors)
    {
        var parts = new string[colors.Count];
        for (int i = 0; i < colors.Count; i++) parts[i] = ColorUtility.ToHtmlStringRGB(colors[i]);
        return string.Join(",", parts);
    }

    Color[] DecodeVariant(string s)
    {
        string[] parts = s.Split(',');
        var colors = new Color[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            Color c;
            colors[i] = ColorUtility.TryParseHtmlString("#" + parts[i], out c) ? c : Color.white;
        }
        return colors;
    }

    string GetRecordText()
    {
        int count = PlayerPrefs.GetInt(KeyCount, 0);
        if (count == 0) return "\n\nまだトンボを\n捕まえていません";
        float max = PlayerPrefs.GetFloat(KeyMax, 0f) * 100f;
        float min = PlayerPrefs.GetFloat(KeyMin, 0f) * 100f;
        return string.Format("\n\n捕まえた数: {0}匹\n色違い: {1}種類\n最大: {2:F0}cm\n最小: {3:F0}cm",
            count, LoadVariantStrings().Count, max, min);
    }

    /// <summary>捕まえたことのある色のトンボを、オープニング画面に小さく並べて表示する</summary>
    public void BuildGallery()
    {
        if (galleryRoot != null) Destroy(galleryRoot);
        galleryRoot = null;
        foreach (var m in galleryMats) if (m != null) Destroy(m);
        galleryMats.Clear();

        bool normal = PlayerPrefs.GetInt(KeyNormal, 0) > 0;
        var variants = new List<Color[]>();
        foreach (string s in LoadVariantStrings()) variants.Add(DecodeVariant(s));
        int total = (normal ? 1 : 0) + variants.Count;
        int count = PlayerPrefs.GetInt(KeyCount, 0);

        galleryRoot = new GameObject("TonboGallery");
        // 傾けず、メニューボタンと同じ向きで立てる（文字もモデルも同じ面に並ぶ）。パネル内: X=右、Z=上
        Vector3 galleryPos = game.InitialPos + game.Off(galleryOffset);
        galleryRoot.transform.SetPositionAndRotation(galleryPos, Quaternion.LookRotation(Vector3.up, game.DirToHead(galleryPos)));

        if (count == 0)
        {
            AddGalleryLabel("まだトンボを捕まえていません", new Vector3(0f, 0f, 0.1f), 1.3f);
            return;
        }

        AddGalleryLabel(string.Format("捕まえた数 {0}匹（{1}）", count, game.CuteMode ? "キュート" : "リアル"), new Vector3(0f, 0f, recordsCountLocalZ), 1.3f);
        if (tonbo.CurrentPrefab == null) return;

        // 上段：最大・最小の個体（モデル＋大きさ）を実際の大きさの比で並べ、ラベルで大きさを示す
        float max = PlayerPrefs.GetFloat(KeyMax, 0f);
        float min = PlayerPrefs.GetFloat(KeyMin, 0f);
        float half = recordsHighlightSpacing * 0.5f;
        AddHighlightModel(new Vector3(-half, 0f, recordsHighlightLocalZ), max,
            PlayerPrefs.GetString(KeyMaxColors, ""), string.Format("最大 {0:F0}cm", max * 100f));
        AddHighlightModel(new Vector3(half, 0f, recordsHighlightLocalZ), min,
            PlayerPrefs.GetString(KeyMinColors, ""), string.Format("最小 {0:F0}cm", min * 100f));

        // 下段：つかまえた色の一覧
        if (total > 0)
        {
            AddGalleryLabel(string.Format("つかまえた色 {0}種類", total), new Vector3(0f, 0f, recordsColorsHeaderLocalZ), 1.1f);
            int index = 0;
            if (normal) AddGalleryModel(index++, total, null);
            foreach (var colors in variants) AddGalleryModel(index++, total, colors);
        }
    }

    void AddGalleryModel(int index, int total, Color[] colors)
    {
        // 格子状に並べる（galleryColumns列、超えたら下の段へ。全体はrecordsGridLocalZを中心に配置）
        int cols = Mathf.Max(1, galleryColumns);
        int col = index % cols;
        int row = index / cols;
        int colsUsed = Mathf.Min(total, cols);
        int rows = (total + cols - 1) / cols;
        float x = (col - (colsUsed - 1) * 0.5f) * gallerySpacing;
        float z = ((rows - 1) * 0.5f - row) * gallerySpacing + recordsGridLocalZ;

        GameObject go = Instantiate(tonbo.CurrentPrefab, galleryRoot.transform);
        go.transform.localPosition = new Vector3(x, 0f, z);
        go.transform.rotation = GalleryModelRotation(go.transform.position); // プレイヤーの方（正面）を向ける
        go.transform.localScale = Vector3.one * galleryTonboScale;
        if (colors != null) TintModel(go, colors);
    }

    /// <summary>最大・最小の個体を、実際の大きさの比（ゲーム中の倍率）で表示し、下にラベルを付ける</summary>
    void AddHighlightModel(Vector3 localPos, float sizeMeters, string colorStr, string label)
    {
        float baseSize = tonbo.BaseScale * tonboModelSpanMeters;
        float mul = baseSize > 0f ? sizeMeters / baseSize : 1f;

        GameObject go = Instantiate(tonbo.CurrentPrefab, galleryRoot.transform);
        go.transform.localPosition = localPos;
        go.transform.rotation = GalleryModelRotation(go.transform.position);
        go.transform.localScale = Vector3.one * galleryTonboScale * mul;
        if (!string.IsNullOrEmpty(colorStr)) TintModel(go, DecodeVariant(colorStr));

        AddGalleryLabel(label, localPos + new Vector3(0f, 0f, -0.08f), 1.2f);
    }

    /// <summary>ギャラリー内の文字ラベル。プレイヤー（頭）の方を向いて読めるようにする</summary>
    void AddGalleryLabel(string text, Vector3 localPos, float sizeMul)
    {
        GameObject lg = new GameObject("GalleryLabel");
        lg.transform.SetParent(galleryRoot.transform, false);
        lg.transform.localPosition = localPos;
        // 文字はプレイヤーの方から読める向き（TextMeshは+Z方向の反対側から見ると読める）
        lg.transform.rotation = Quaternion.LookRotation(-game.DirToHead(galleryRoot.transform.position), Vector3.up); // 全ラベル共通の向き

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        TextMesh tm = lg.AddComponent<TextMesh>();
        tm.font = font;
        tm.text = text;
        tm.fontSize = 64;
        tm.characterSize = game.LabelCharSize * sizeMul;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = Color.white;
        lg.GetComponent<MeshRenderer>().sharedMaterial = font.material;
    }

    /// <summary>トンボをプレイヤーに向け、galleryViewYaw/Pitchぶんだけ傾けた向き</summary>
    Quaternion GalleryModelRotation(Vector3 worldPos)
    {
        return Quaternion.LookRotation(game.DirToHead(galleryRoot.transform.position), Vector3.up) * Quaternion.Euler(galleryViewPitch, galleryViewYaw, 0f);
    }

    /// <summary>元のマテリアルの出現順に、保存しておいた色を割り当てる（トンボの色のランダム化と同じ対応）</summary>
    void TintModel(GameObject go, Color[] colors)
    {
        Renderer[] rends = go.GetComponentsInChildren<Renderer>(true);
        var map = new Dictionary<Material, Material>();
        int k = 0;
        foreach (Renderer r in rends)
        {
            Material[] src = r.sharedMaterials;
            Material[] dst = new Material[src.Length];
            for (int j = 0; j < src.Length; j++)
            {
                Material orig = src[j];
                if (orig == null) { dst[j] = null; continue; }

                Material tinted;
                if (!map.TryGetValue(orig, out tinted))
                {
                    tinted = new Material(orig);
                    Color c = (k < colors.Length) ? colors[k] : Color.white;
                    k++;
                    if (tinted.HasProperty("_Color")) { c.a = tinted.color.a; tinted.color = c; }
                    else if (tinted.HasProperty("_BaseColor")) { c.a = tinted.GetColor("_BaseColor").a; tinted.SetColor("_BaseColor", c); }
                    map[orig] = tinted;
                    galleryMats.Add(tinted);
                }
                dst[j] = tinted;
            }
            r.sharedMaterials = dst;
        }
    }

    [ContextMenu("トンボの記録をリセット")]
    void ClearRecords()
    {
        PlayerPrefs.DeleteKey(KeyCount);
        PlayerPrefs.DeleteKey(KeyNormal);
        PlayerPrefs.DeleteKey(KeyMax);
        PlayerPrefs.DeleteKey(KeyMin);
        PlayerPrefs.DeleteKey(KeyVariants);
        PlayerPrefs.Save();
    }
}
