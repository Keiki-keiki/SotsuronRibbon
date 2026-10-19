using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// トンボの回しが成功した後、次のトンボが来る前に出す「虫籠」演出を担当する。
/// 流れ：虫籠が下からフェードイン → 蓋が開く → 棒の先端でトンボに触れて持ち上げる
///      → 籠の中で少し止めると手放したことになり、蓋が閉まる → 下へフェードアウト。
/// RotationBurstGame と同じ GameObject にアタッチして使う（蓋の開閉は Blender のアニメーションクリップを再生）。
/// </summary>
public class MushikagoController : MonoBehaviour
{
    [Header("モデル")]
    [Tooltip("虫籠のモデル（Assets/Tonbo/Mushikagonew.blend）")]
    public GameObject cagePrefab;
    [Tooltip("蓋の開閉アニメ（Mushikagonew.blend 内のクリップ。1〜80フレーム/24fps）")]
    public AnimationClip lidClip;
    [Tooltip("モデルの幅・奥行き・高さ（スケール1のときのメートル）")]
    public float modelWidth = 0.36f;
    public float modelDepth = 0.28f;
    public float modelHeight = 0.31f;
    [Tooltip("虫籠の正面がプレイヤーを向かないときに、Y軸回転で補正する角度（度）")]
    public float modelYawOffset = 0f;

    [Header("大きさ・位置")]
    [Tooltip("トンボのPrefab倍率1あたりの翼幅（メートル）。元モデルは約3.9m")]
    public float wingspanPerScale = 3.9f;
    [Tooltip("虫籠の幅 ÷ トンボの翼幅")]
    public float cageWidthPerWingspan = 1.3f;
    [Tooltip("トンボと虫籠のすき間（メートル）")]
    public float sideGap = 0.08f;
    [Tooltip("虫籠の高さの微調整（メートル。籠の中心がトンボの高さに来るのが基準）")]
    public float centerHeightOffset = 0f;

    [Header("蓋アニメの時刻（秒）")]
    public float lidOpenEndTime = 29f / 24f;
    public float lidCloseStartTime = 49f / 24f;
    public float lidCloseEndTime = 79f / 24f;

    [Header("演出")]
    public float fadeInSeconds = 0.7f;
    public float fadeOutSeconds = 0.7f;
    [Tooltip("フェードのとき、どれだけ下から上がってくるか（メートル）")]
    public float riseDistance = 0.4f;
    [Tooltip("トンボをつかめる距離（メートル）。翼幅の1/4が加算される")]
    public float grabRadius = 0.1f;
    [Tooltip("籠の中で先端をこの秒数止めると、手を離したことにする")]
    public float releaseHoldSeconds = 0.35f;
    public float dropSeconds = 0.35f;
    [Tooltip("籠の底からトンボの中心までの高さ（モデルのメートル）")]
    public float floorHeight = 0.05f;
    [Tooltip("つかまないまま放置した場合に、自動で次へ進むまでの秒数")]
    public float catchTimeoutSeconds = 30f;

    private enum Phase { Idle, FadingIn, Opening, WaitGrab, Carrying, Dropping, Closing, FadingOut }
    private Phase phase = Phase.Idle;

    private GameObject cage;
    private Animator animator;
    private PlayableGraph graph;
    private AnimationClipPlayable clipPlayable;
    private bool graphReady;
    private Renderer[] renderers;
    private readonly List<Material> mats = new List<Material>();
    private readonly List<int> colorIds = new List<int>();
    private readonly List<Color> baseColors = new List<Color>();

    private Transform tonbo;
    private float wingspan;
    private float cageScale;
    private Vector3 startPos, endPos, dropFrom;
    private Quaternion cageRot;
    private Vector3 tonboLocalScale;
    private float phaseTime, catchElapsed, insideTimer;
    private float dbgNextLog; // [MUSHI-DBG] 一時ログ用（原因特定後に削除）

    public bool CanCatch { get { return cagePrefab != null && lidClip != null; } }
    public bool IsBusy { get { return phase != Phase.Idle; } }

    void OnDestroy()
    {
        if (graphReady && graph.IsValid()) graph.Destroy();
        graphReady = false;
    }

    /// <summary>虫籠の演出を始める。tonboTf＝杭に止まっているトンボ、tonboScaleMul＝そのPrefab倍率</summary>
    public void BeginCatch(Transform tonboTf, float tonboScaleMul, Transform head)
    {
        if (!CanCatch || tonboTf == null) return;
        EnsureCage();

        tonbo = tonboTf;
        wingspan = tonboScaleMul * wingspanPerScale;
        cageScale = Mathf.Max(0.05f, wingspan * cageWidthPerWingspan / modelWidth);

        Vector3 toPlayer = head != null ? Vector3.ProjectOnPlane(head.position - tonbo.position, Vector3.up) : Vector3.back;
        if (toPlayer.sqrMagnitude < 1e-4f) toPlayer = Vector3.back;
        toPlayer.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, -toPlayer); // プレイヤーから見て右

        float half = modelWidth * cageScale * 0.5f;
        Vector3 c = tonbo.position + right * (wingspan * 0.5f + half + sideGap) + toPlayer * (modelDepth * cageScale * 0.3f);
        endPos = new Vector3(c.x, tonbo.position.y - modelHeight * cageScale * 0.5f + centerHeightOffset, c.z);
        startPos = endPos - Vector3.up * riseDistance;
        cageRot = Quaternion.LookRotation(toPlayer, Vector3.up) * Quaternion.Euler(0f, modelYawOffset, 0f);

        cage.transform.localScale = Vector3.one * cageScale;
        cage.transform.SetPositionAndRotation(startPos, cageRot);
        SetLidTime(0f);
        SetAlpha(0f);
        SetVisible(true);

        phase = Phase.FadingIn;
        Debug.Log("[MUSHI-DBG] BeginCatch tonbo=" + tonbo.position.ToString("F2") + " wingspan=" + wingspan.ToString("F3") + " cageScale=" + cageScale.ToString("F2") + " cagePos=" + endPos.ToString("F2") + " tonboActive=" + tonbo.gameObject.activeInHierarchy); // [MUSHI-DBG]
        phaseTime = 0f;
        catchElapsed = 0f;
        insideTimer = 0f;
    }

    /// <summary>毎フレーム RotationBurstGame.Update から呼ばれる。演出が終わったら true</summary>
    public bool Tick(Transform tip)
    {
        if (phase == Phase.Idle || cage == null) return true;

        float dt = Time.deltaTime;
        phaseTime += dt;
        catchElapsed += dt;

        // つかまれないまま放置されたら、そのまま次へ進める
        if (catchElapsed > catchTimeoutSeconds && phase <= Phase.Carrying)
        {
            Cancel();
            return true;
        }

        switch (phase)
        {
            case Phase.FadingIn:
            {
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(phaseTime / Mathf.Max(0.05f, fadeInSeconds)));
                cage.transform.position = Vector3.Lerp(startPos, endPos, k);
                SetAlpha(k);
                if (phaseTime >= fadeInSeconds) { SetAlpha(1f); NextPhase(Phase.Opening); }
                break;
            }
            case Phase.Opening:
            {
                SetLidTime(Mathf.Lerp(0f, lidOpenEndTime, Mathf.Clamp01(phaseTime / Mathf.Max(0.05f, lidOpenEndTime))));
                if (phaseTime >= lidOpenEndTime) NextPhase(Phase.WaitGrab);
                break;
            }
            case Phase.WaitGrab:
            {
                // 棒の先端がトンボに触れたら、つかんだことにする
                if (Time.time >= dbgNextLog) { dbgNextLog = Time.time + 0.5f; Debug.Log("[MUSHI-DBG] WaitGrab tip=" + (tip != null ? tip.position.ToString("F2") : "NULL") + " tonbo=" + tonbo.position.ToString("F2") + " active=" + tonbo.gameObject.activeInHierarchy + " dist=" + (tip != null ? Vector3.Distance(tip.position, tonbo.position).ToString("F3") : "-") + " need<" + (grabRadius + wingspan * 0.25f).ToString("F3")); } // [MUSHI-DBG]
                if (tip != null && Vector3.Distance(tip.position, tonbo.position) < grabRadius + wingspan * 0.25f)
                    NextPhase(Phase.Carrying);
                break;
            }
            case Phase.Carrying:
            {
                if (tip == null) break;
                tonbo.position = Vector3.Lerp(tonbo.position, tip.position, 1f - Mathf.Exp(-25f * dt));
                if (IsInsideCage(tip.position)) insideTimer += dt; else insideTimer = 0f;
                if (insideTimer >= releaseHoldSeconds)
                {
                    dropFrom = tonbo.position;
                    NextPhase(Phase.Dropping);
                }
                break;
            }
            case Phase.Dropping:
            {
                Vector3 target = cage.transform.TransformPoint(new Vector3(0f, floorHeight, 0f));
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(phaseTime / Mathf.Max(0.05f, dropSeconds)));
                tonbo.position = Vector3.Lerp(dropFrom, target, k);
                if (phaseTime >= dropSeconds)
                {
                    tonbo.SetParent(cage.transform, true); // 籠と一緒に動く
                    tonboLocalScale = tonbo.localScale;
                    NextPhase(Phase.Closing);
                }
                break;
            }
            case Phase.Closing:
            {
                float span = Mathf.Max(0.1f, lidCloseEndTime - lidCloseStartTime);
                SetLidTime(Mathf.Lerp(lidCloseStartTime, lidCloseEndTime, Mathf.Clamp01(phaseTime / span)));
                if (phaseTime >= span) NextPhase(Phase.FadingOut);
                break;
            }
            case Phase.FadingOut:
            {
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(phaseTime / Mathf.Max(0.05f, fadeOutSeconds)));
                cage.transform.position = Vector3.Lerp(endPos, startPos, k);
                SetAlpha(1f - k);
                // 籠が消えていく最後の半分で、中のトンボも小さくして消す
                float shrink = Mathf.Clamp01((k - 0.5f) / 0.5f);
                tonbo.localScale = tonboLocalScale * (1f - shrink);
                if (phaseTime >= fadeOutSeconds)
                {
                    Cancel();
                    return true;
                }
                break;
            }
        }
        return false;
    }

    /// <summary>演出を中断して片付ける（トンボを籠から外し、籠を非表示にする）</summary>
    public void Cancel()
    {
        if (phase == Phase.Idle) return;
        if (tonbo != null && cage != null && tonbo.parent == cage.transform) tonbo.SetParent(null, true);
        tonbo = null;
        SetVisible(false);
        phase = Phase.Idle;
    }

    void NextPhase(Phase p) { phase = p; phaseTime = 0f; Debug.Log("[MUSHI-DBG] phase -> " + p); } // [MUSHI-DBG]

    bool IsInsideCage(Vector3 worldPos)
    {
        Vector3 l = cage.transform.InverseTransformPoint(worldPos); // モデルのメートル（スケール解除済み）
        return Mathf.Abs(l.x) < modelWidth * 0.5f
            && Mathf.Abs(l.z) < modelDepth * 0.5f
            && l.y > 0f && l.y < modelHeight + 0.15f;
    }

    void EnsureCage()
    {
        if (cage != null) return;
        cage = Instantiate(cagePrefab);
        cage.name = "MushikagoRuntime";

        // モデルに含まれるBlender由来のカメラ・ライトは不要。残るとVRで視点を奪うので、無効化して削除する
        foreach (Camera cam in cage.GetComponentsInChildren<Camera>(true))
        {
            cam.enabled = false;
            if (cam.gameObject != cage) Destroy(cam.gameObject); else Destroy(cam);
        }
        foreach (Light lt in cage.GetComponentsInChildren<Light>(true))
        {
            lt.enabled = false;
            if (lt.gameObject != cage) Destroy(lt.gameObject); else Destroy(lt);
        }

        animator = cage.GetComponent<Animator>();
        if (animator == null) animator = cage.AddComponent<Animator>();
        animator.runtimeAnimatorController = null;
        animator.applyRootMotion = false;

        // コントローラなしで、クリップの任意の時刻を直接再生する（開く・待つ・閉じるを時刻で制御）
        graph = PlayableGraph.Create("MushikagoLid");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "Lid", animator);
        clipPlayable = AnimationClipPlayable.Create(graph, lidClip);
        output.SetSourcePlayable(clipPlayable);
        graph.Play();
        graphReady = true;

        // フェード用にマテリアルを実行時コピーして半透明対応にする
        renderers = cage.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in renderers)
        {
            Material[] ms = r.materials;
            foreach (Material m in ms)
            {
                MakeTransparent(m);
                int id = m.HasProperty("_BaseColor") ? Shader.PropertyToID("_BaseColor")
                       : (m.HasProperty("_Color") ? Shader.PropertyToID("_Color") : -1);
                mats.Add(m);
                colorIds.Add(id);
                baseColors.Add(id >= 0 ? m.GetColor(id) : Color.white);
            }
        }
        SetVisible(false);
    }

    void SetVisible(bool v)
    {
        if (renderers == null) return;
        foreach (Renderer r in renderers) if (r != null) r.enabled = v;
    }

    void SetLidTime(float t)
    {
        if (!graphReady) return;
        clipPlayable.SetTime(t);
        graph.Evaluate(0f);
    }

    void SetAlpha(float a)
    {
        for (int i = 0; i < mats.Count; i++)
        {
            if (mats[i] == null || colorIds[i] < 0) continue;
            Color c = baseColors[i];
            c.a = baseColors[i].a * a;
            mats[i].SetColor(colorIds[i], c);
        }
    }

    /// <summary>URP Lit / Standard のマテリアルを、アルファで消せる設定にする</summary>
    static void MakeTransparent(Material m)
    {
        if (m.HasProperty("_Surface")) // URP
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = 3000;
        }
        else if (m.HasProperty("_Mode")) // Standard
        {
            m.SetFloat("_Mode", 2f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = 3000;
        }
    }
}
