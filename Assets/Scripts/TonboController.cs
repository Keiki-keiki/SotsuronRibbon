using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// トンボの出現・飛行・着地・色/大きさのランダム化を担当する。
/// RotationBurstGame と同じ GameObject にアタッチして使う（ゲーム状態は RotationBurstGame から参照する）。
/// </summary>
[RequireComponent(typeof(RotationBurstGame))]
public class TonboController : MonoBehaviour
{
    [Header("トンボ演出")]
    [Tooltip("円に迫ってくるトンボのPrefab（Assets/Tonbo/Tonbo.prefab）")]
    public GameObject tonboPrefab;
    [Tooltip("キュート版トンボのPrefab（Assets/Tonbo/Tonbo_cute.prefab）。選ぶとTonboと全く同じ処理で動く")]
    public GameObject tonboCutePrefab;
    [Tooltip("トンボの大きさ（元モデルは翼を広げて約3.9mなので、0.08で約0.3m）")]
    private float tonboScale = 0.08f;
    [Tooltip("円の奥（プレイヤーと反対側）にどれだけ離れた位置から飛んでくるか（メートル）")]
    private float tonboStartDistance = 2f;
    [Tooltip("飛んでくる途中の上下左右のゆらぎの大きさ（メートル）")]
    private float tonboWobble = 0.04f;
    [Tooltip("トンボの色がランダムになる確率（0.001 = 0.1%）。トンボが出現するたびに抽選")]
    [Range(0f, 1f)]
    private float tonboRandomColorChance = 0.01f;
    [Tooltip("デバッグ用：trueにすると毎回ランダム色になる（確認後はfalseに戻すこと）")]
    public bool tonboForceRandomColor = false;
    [Tooltip("大きさのばらつき（対数正規分布のσ）。0で全部同じ大きさ。0.25なら約68%が×0.78〜×1.28、約95%が×0.61〜×1.65")]
    private float tonboSizeSigma = 0.25f;
    [Tooltip("大きさの倍率の下限（極端に小さくなりすぎないように）")]
    private float tonboSizeMinMultiplier = 0.4f;
    [Tooltip("大きさの倍率の上限（極端に大きくなりすぎないように）")]
    private float tonboSizeMaxMultiplier = 2.5f;

    [Header("着地演出")]
    [Tooltip("トンボが杭に向かって飛んで着地するまでの秒数（着地するまでなぞり判定は始まらない）")]
    public float tonboFlightSeconds = 2f;
    private float tonboFlightStartTime = 0f;

    private RotationBurstGame game;
    private GameObject realPrefab;
    private GameObject tonboInstance;
    private bool tonboRunning = false;
    private Vector3 tonboStart;
    private Vector3 tonboEnd;
    private Renderer[] tonboRenderers;
    private Material[][] tonboOriginalMats;
    private readonly List<Material> tonboTempMats = new List<Material>();
    private float currentTonboScale = 0f;
    private readonly List<Color> currentVariantColors = new List<Color>();

    // ---- RotationBurstGame 向けの公開API ----
    /// <summary>いま使っているトンボのPrefab（リアル or キュート）</summary>
    public GameObject CurrentPrefab { get { return tonboPrefab; } }
    public bool HasCutePrefab { get { return tonboCutePrefab != null; } }
    /// <summary>トンボの基準の大きさ（Prefab倍率）</summary>
    public float BaseScale { get { return tonboScale; } }
    /// <summary>今飛んでいるトンボの大きさ（Prefab倍率）</summary>
    public float CurrentScale { get { return currentTonboScale; } }
    /// <summary>今飛んでいるトンボのランダム色（通常色なら空）</summary>
    public List<Color> VariantColors { get { return currentVariantColors; } }
    /// <summary>トンボが出ていて飛行中（または着地済み）か</summary>
    public bool IsActive { get { return tonboInstance != null && tonboRunning; } }
    /// <summary>着地するまでは、リング表示となぞり判定を止めるための判定</summary>
    public bool WaitingForLanding
    {
        get { return tonboPrefab != null && !(tonboRunning && Time.time - tonboFlightStartTime >= tonboFlightSeconds); }
    }

    void Awake()
    {
        game = GetComponent<RotationBurstGame>();
        realPrefab = tonboPrefab; // 最初に設定されているPrefabを「リアル」として覚えておく
    }

    /// <summary>使うトンボのPrefabを切り替える。キュートもリアルと同じ処理（大きさ・色・記録）で動く</summary>
    public void SelectPrefab(bool cute)
    {
        GameObject p = cute ? tonboCutePrefab : realPrefab;
        if (p == tonboPrefab) return;

        Hide();
        if (tonboInstance != null) Destroy(tonboInstance);
        tonboInstance = null;
        tonboRenderers = null;
        tonboOriginalMats = null;
        foreach (var m in tonboTempMats) if (m != null) Destroy(m);
        tonboTempMats.Clear();
        tonboPrefab = p;
    }

    /// <summary>毎フレーム RotationBurstGame.Update から呼ばれる</summary>
    public void Tick()
    {
        if (tonboPrefab == null) return;

        if ((game.IsWaiting || game.IsPlaying) && !tonboRunning)
        {
            StartTonbo();
        }
        if (!tonboRunning || tonboInstance == null) return;

        // トンボが杭に着地したら（何回目でも）カウントスタート
        if (game.IsWaiting && Time.time - tonboFlightStartTime >= tonboFlightSeconds)
        {
            game.BeginPlaying();
        }

        if (game.IsWaiting || game.IsPlaying)
        {
            // 杭に向かって飛び、着地したらその場に止まる
            float t = Mathf.Clamp01((Time.time - tonboFlightStartTime) / Mathf.Max(0.1f, tonboFlightSeconds));
            Vector3 dir = (tonboEnd - tonboStart).normalized;
            Vector3 side = Vector3.Cross(dir, Vector3.up).normalized;
            float w = tonboWobble * (1f - t);
            Vector3 pos = Vector3.Lerp(tonboStart, tonboEnd, t)
                        + side * Mathf.Sin(Time.time * 4f) * w
                        + Vector3.up * Mathf.Sin(Time.time * 5.3f) * w;
            tonboInstance.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(dir, Vector3.up));
        }
        else if (game.IsFailed)
        {
            // ゲームオーバー時は円の中心に止まる
            tonboInstance.transform.position = tonboEnd;
        }
    }

    public void Hide()
    {
        tonboRunning = false;
        if (tonboInstance != null) tonboInstance.SetActive(false);
    }

    void StartTonbo()
    {
        if (tonboPrefab == null) return;

        // プレイヤー側の向き（リングの法線のうち頭がある側）を求める
        Transform head = game.headTransform;
        if (head == null && Camera.main != null) head = Camera.main.transform;
        // 円の角度（左右でY軸回転する）には依存せず、最初のリングの向きを基準にした固定方向にする
        Vector3 normal = game.InitialRot * Vector3.up;
        Vector3 toPlayer = normal;
        if (head != null && Vector3.Dot(head.position - game.InitialPos, normal) < 0f) toPlayer = -normal;

        // 円の奥（プレイヤーと反対側）から円の中心へ向かって飛んでくる
        // 水平方向だけにして、真っ直ぐ（上下に傾かず）飛んでくるようにする
        Vector3 flat = Vector3.ProjectOnPlane(toPlayer, Vector3.up);
        if (flat.sqrMagnitude > 0.01f) toPlayer = flat.normalized;
        if (head != null) { Vector3 hp = head.position - transform.position; hp.y = 0f; if (hp.sqrMagnitude > 0.01f) toPlayer = hp.normalized; }

        // 位置の座標だけ円の中心に合わせる
        tonboEnd = transform.position;
        tonboStart = tonboEnd - toPlayer * tonboStartDistance;

        if (tonboInstance == null)
        {
            tonboInstance = Instantiate(tonboPrefab);
            tonboInstance.name = "TonboRuntime";
        }
        // 大きさは対数正規分布：倍率 = exp(N(0, σ))。普通の大きさ(×1)が最も多く、低確率で大小に振れる
        float sizeMul = Mathf.Exp(RandomGaussian() * tonboSizeSigma);
        sizeMul = Mathf.Clamp(sizeMul, tonboSizeMinMultiplier, tonboSizeMaxMultiplier);
        currentTonboScale = tonboScale * sizeMul;
        tonboInstance.transform.localScale = Vector3.one * currentTonboScale;
        tonboInstance.transform.SetPositionAndRotation(tonboStart, Quaternion.LookRotation(toPlayer, Vector3.up));
        ApplyTonboColors();
        tonboInstance.SetActive(true);
        tonboFlightStartTime = Time.time;
        tonboRunning = true;
    }

    /// <summary>標準正規分布 N(0,1) に従う乱数（Box-Muller法）</summary>
    float RandomGaussian()
    {
        float u1 = Mathf.Max(Random.value, 1e-6f);
        float u2 = Random.value;
        return Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
    }

    /// <summary>
    /// トンボが出現するたびに抽選し、当たったら色をランダムにする（外れたら元の色に戻す）。
    /// 元のマテリアルが同じ部分（両目・足・腹部・頭と胸など）は同じ色に統一される。
    /// Prefab側のマテリアルは書き換えず、実行時にコピーを作って差し替える。
    /// </summary>
    void ApplyTonboColors()
    {
        if (tonboInstance == null) return;

        if (tonboRenderers == null)
        {
            tonboRenderers = tonboInstance.GetComponentsInChildren<Renderer>(true);
            tonboOriginalMats = new Material[tonboRenderers.Length][];
            for (int i = 0; i < tonboRenderers.Length; i++)
                tonboOriginalMats[i] = tonboRenderers[i].sharedMaterials;
        }

        // 前回のランダム色マテリアルを破棄し、まず元の色に戻す
        for (int i = 0; i < tonboRenderers.Length; i++)
            tonboRenderers[i].sharedMaterials = tonboOriginalMats[i];
        foreach (var m in tonboTempMats) if (m != null) Destroy(m);
        tonboTempMats.Clear();

        currentVariantColors.Clear();
        if (!tonboForceRandomColor && Random.value >= tonboRandomColorChance) return;

        // 元のマテリアルごとに1色だけ決める（同じマテリアル＝同じ色の部分は統一）
        var colorMap = new Dictionary<Material, Material>();
        for (int i = 0; i < tonboRenderers.Length; i++)
        {
            Material[] src = tonboOriginalMats[i];
            Material[] dst = new Material[src.Length];
            for (int j = 0; j < src.Length; j++)
            {
                Material orig = src[j];
                if (orig == null) { dst[j] = null; continue; }

                Material tinted;
                if (!colorMap.TryGetValue(orig, out tinted))
                {
                    tinted = new Material(orig);
                    Color c = Color.HSVToRGB(Random.value, Random.Range(0.6f, 1f), Random.Range(0.75f, 1f));
                    // 羽などの半透明マテリアルは元のアルファを保つ
                    if (tinted.HasProperty("_Color")) { c.a = tinted.color.a; tinted.color = c; }
                    else if (tinted.HasProperty("_BaseColor")) { c.a = tinted.GetColor("_BaseColor").a; tinted.SetColor("_BaseColor", c); }
                    colorMap[orig] = tinted;
                    tonboTempMats.Add(tinted);
                    currentVariantColors.Add(c);
                }
                dst[j] = tinted;
            }
            tonboRenderers[i].sharedMaterials = dst;
        }
    }
}
