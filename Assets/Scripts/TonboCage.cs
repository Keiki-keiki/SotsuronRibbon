using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;

/// <summary>
/// 回しに成功したあと、次のトンボが来る前の「虫籠に入れる」演出を担当する。
/// 流れ：トンボを手でつかむ → 虫籠（Mushikagonew）がトンボの大きさに合わせて下からフェードインして開く
///       → トンボを籠に入れて手を離す → 籠が閉まり、下へフェードアウト → 次のトンボへ。
/// RotationBurstGame と同じ GameObject にアタッチして使う。
/// 籠のアニメーション（Blenderの1クリップ "Scene"）は 開く(0〜openEndTime) → 保持 → 閉じる(closeStartTime〜終わり) の構成。
/// </summary>
[RequireComponent(typeof(RotationBurstGame))]
public class TonboCage : MonoBehaviour
{
    public enum GrabMode { Touch, Pinch }

    [Header("虫籠のモデル")]
    [Tooltip("Assets/Tonbo/Mushikagonew.blend")]
    public GameObject cagePrefab;
    [Tooltip("Mushikagonew.blend に入っている Scene クリップ（開く→保持→閉じる）")]
    public AnimationClip cageClip;
    [Tooltip("フェード用シェーダーバリアントがビルドから除外されないようにするための参照（Fadeモードのマテリアル）")]
    public Material fadeVariantKeeper;
    [Tooltip("開き終わる時刻（秒）。Blenderの31フレーム目＝約1.25秒")]
    public float openEndTime = 1.25f;
    [Tooltip("閉じ始める時刻（秒）。Blenderの49フレーム目＝約2.0秒")]
    public float closeStartTime = 2.0f;

    [Header("大きさ・出現")]
    [Tooltip("トンボの大きさ1.0のときの翼幅（メートル）。TonboControllerのコメントより約3.9m")]
    public float tonboWingspanAtScale1 = 3.9f;
    [Tooltip("籠の幅 ÷ トンボの翼幅")]
    public float cageWidthMargin = 1.3f;
    [Tooltip("籠の上端をトンボの何メートル下に置くか")]
    public float gapBelowTonbo = 0.03f;
    [Tooltip("虫かごの位置：トンボを基準に、プレイヤーから見て左右どちらか（生成ごとにランダム）へずらす距離（m）")]
    public float cageSideOffset = 0.35f;
    [Tooltip("虫かごと杭の重なり判定の余白（m）。虫かごの範囲にこの分を足して、杭が入るかを見る")]
    public float cageStakeMargin = 0.05f;
    [Tooltip("下から上がってくる距離（メートル）")]
    public float riseDistance = 0.3f;
    [Tooltip("フェードイン/アウトの秒数")]
    public float fadeSeconds = 0.7f;
    [Tooltip("つかんでから籠が開き始めるまでの秒数")]
    public float openDelay = 0.2f;

    [Header("つかむ・離す")]
    [Tooltip("Touch=触れたらつかむ（推奨）。Pinch=つまんでいる間だけつかむ")]
    public GrabMode grabMode = GrabMode.Touch;
    [Tooltip("つかめる距離 = 翼幅 × この値")]
    public float grabRadiusScale = 0.35f;
    public float minGrabRadius = 0.07f;
    [Tooltip("Touchモード：籠の中で手をこの秒数とどめると「離した」とみなす")]
    public float releaseDwellSeconds = 0.6f;
    [Range(0f, 1f)] public float pinchThreshold = 0.8f;
    [Tooltip("捕獲の制限時間（秒）。この時間内に籠へ収納できないとトンボは飛び去り、失敗（スコアなし）で次のリングへ進む")]
    public float catchTimeLimit = 10f;
    [Tooltip("飛び去るのにかかる秒数")]
    public float escapeSeconds = 1.2f;
    [Tooltip("飛び去る距離（メートル）")]
    public float escapeDistance = 3f;

    private enum Phase { Idle, WaitGrab, Held, Closing, Sinking, Escaping }
    private Phase phase = Phase.Idle;
    /// <summary>今回の捕獲が制限時間切れでトンボに逃げられたか（BeginCatch でリセット）。スコア加算の判定用</summary>
    public bool Escaped { get; private set; }
    private float catchStart;
    private float escapeStart;
    private Vector3 escapeFrom;
    private Vector3 escapeDir;
    // （逃げられたのは捕獲対象の1匹だけ。残りの気絶中の1匹は、そのまま次の捕獲に進む）

    private RotationBurstGame game;
    private TonboController tonbo;
    private Transform tonboTf;
    private Vector3 landingPos;
    private Quaternion landingRot;
    private float waitStart;
    // 捕獲対象（ハードモードでは気絶中のトンボから1匹ずつ選ぶ。1=メイン、2=2匹目）
    private int targetIndex = 1;
    private float targetScale;
    private bool followUp; // 2匹目以降の捕獲か（逃げられてもスコア判定のEscapedは変えない）


    // 籠
    private GameObject cage;
    private Renderer[] frameRenderers;
    private readonly List<Material> cageMats = new List<Material>();
    private readonly List<float> cageMatAlpha = new List<float>();
    private Vector3 cageTarget;
    private float cageScale = 1f;
    private float cageStart;
    private PlayableGraph graph;
    private AnimationClipPlayable clipPlayable;
    private bool graphValid;

    // つかみ
    private int heldHandId = -1;
    private Vector3 holdOffset;
    private float dwell;
    private bool heldByStick;
    private Vector3 releaseFromLocal;
    private Vector3 releaseToLocal;
    private Quaternion releaseFromRot;
    private Quaternion releaseToRot;

    private float closeStart;
    private float sinkStart;
    private Vector3 sinkFrom;

    private struct HandPt { public int id; public Vector3 pos; public Vector3 hold; public bool pinching; public bool stick; }
    private readonly List<HandPt> pts = new List<HandPt>();
    private OVRHand[] hands;
    private float nextHandSearch;

    public bool Available
    {
        get
        {
            if (cagePrefab == null || cageClip == null || tonbo == null) return false;
            if (game != null && game.HardMode)
            {
                // ハード：トンボ1に依存せず、PickTarget が Stunned のトンボ（1または2）を選べるときだけ使える
                int idx; Transform tf; float sc;
                return PickTarget(out idx, out tf, out sc);
            }
            return tonbo.TonboTransform != null; // ノーマル：従来どおり
        }
    }

    void Awake()
    {
        game = GetComponent<RotationBurstGame>();
        tonbo = GetComponent<TonboController>();
    }

    /// <summary>回しに成功した直後に呼ぶ。トンボは杭に止まったまま、つかまれるのを待つ</summary>
public void BeginCatch()
    {
        if (tonbo == null) tonbo = GetComponent<TonboController>();
        followUp = false;
        int idx; Transform tf; float sc;
        bool picked = PickTarget(out idx, out tf, out sc);
        if (game != null && game.HardMode && !picked) return; // ハード：Stunned のトンボがいなければ捕獲しない（Flying・Stored は対象外）
        StartCatchOn(idx, tf, sc);
    }

    /// <summary>指定したトンボを捕獲対象にして、つかまれるのを待つ状態に入る（虫かごは常に1つ）</summary>
    void StartCatchOn(int idx, Transform tf, float scale)
    {
        targetIndex = idx;
        tonboTf = tf;
        targetScale = scale;
        landingPos = tonboTf.position;
        landingRot = tonboTf.rotation;
        waitStart = Time.time;
        heldHandId = -1;
        Escaped = false;
        catchStart = Time.time;
        dwell = 0f;
        phase = Phase.WaitGrab;
        if (cage == null) BuildCage(tonboTf.position);
    }

    /// <summary>
    /// 捕獲対象を選ぶ。ノーマルモードは常に1匹目。ハードモードでは Stunned のトンボだけが候補で、
    /// 両方 Stunned なら虫かごに近い方（虫かごがまだ無いときは、捕獲のきっかけになった1匹目の位置を基準にする）。
    /// 候補があれば true、なければ（従来どおり1匹目を返して）false。
    /// </summary>
    bool PickTarget(out int idx, out Transform tf, out float scale)
    {
        idx = 1; tf = tonbo.TonboTransform; scale = tonbo.CurrentScale;
        if (game == null || !game.HardMode) return false;

        Transform t2 = tonbo.SecondTonboTransform;
        bool c1 = tf != null && game.Tonbo1State == RotationBurstGame.TonboState.Stunned;
        bool c2 = t2 != null && game.Tonbo2State == RotationBurstGame.TonboState.Stunned;
        if (!c1 && !c2) return false;

        bool pick2 = c2 && !c1;
        if (c1 && c2)
        {
            Vector3 refPos = (cage != null && frameRenderers != null) ? FrameBounds().center : tf.position;
            pick2 = Vector3.Distance(t2.position, refPos) < Vector3.Distance(tf.position, refPos);
        }
        if (pick2) { idx = 2; tf = t2; scale = tonbo.SecondScale; }
        return true;
    }

    /// <summary>収納が終わったあと、まだ Stunned のトンボがいれば、収納済みを隠して虫かごを作り直し、次の1匹へ進む</summary>
    bool TryStartNextCatch()
    {
        int idx; Transform tf; float sc;
        if (!PickTarget(out idx, out tf, out sc) || idx == targetIndex) return false;
        tonboTf.SetParent(null, true);
        tonbo.HideOne(targetIndex);
        DestroyCage();
        followUp = true;
        StartCatchOn(idx, tf, sc);
        return true;
    }

    /// <summary>毎フレーム呼ぶ。演出がすべて終わったら true を返す</summary>
    public bool Tick(Transform stickTip)
    {
        if (phase == Phase.Idle) return true;
        if (tonboTf == null) { Abort(); return true; }

        switch (phase)
        {
            case Phase.WaitGrab: TickWaitGrab(stickTip); break;
            case Phase.Held: TickHeld(stickTip); break;
            case Phase.Closing: TickClosing(); break;
            case Phase.Sinking: if (TickSinking()) return true; break;
            case Phase.Escaping: if (TickEscaping()) return true; break;
        }
        return phase == Phase.Idle;
    }

    // ---------------- つかむのを待つ ----------------
    void TickWaitGrab(Transform stickTip)
    {
        if (cage != null) UpdateCageIntro();

        // 制限時間内に収納できなければ、トンボは飛び去る
        if (Time.time - catchStart >= catchTimeLimit)
        {
            StartEscape();
            return;
        }

        SampleHands(stickTip);
        float radius = Mathf.Max(minGrabRadius, tonboWingspanAtScale1 * targetScale * grabRadiusScale);
        foreach (var p in pts)
        {
            if (grabMode == GrabMode.Pinch && !p.pinching) continue;
            if (Vector3.Distance(p.pos, tonboTf.position) > radius) continue;

            heldHandId = p.id;
            heldByStick = p.stick;
            holdOffset = tonboTf.position - p.hold;
            dwell = 0f;
            if (cage == null) BuildCage(tonboTf.position);
            phase = Phase.Held;
            return;
        }
    }

    // ---------------- 手に持っている ----------------
    void TickHeld(Transform stickTip)
    {
        // 掴んでいても、制限時間内に収納できなければ逃げられる
        if (Time.time - catchStart >= catchTimeLimit)
        {
            heldHandId = -1;
            StartEscape();
            return;
        }
        UpdateCageIntro();
        SampleHands(stickTip);

        bool found = false; HandPt hp = default(HandPt);
        foreach (var p in pts) if (p.id == heldHandId) { hp = p; found = true; break; }
        if (found)
        {
            Vector3 target = hp.hold + holdOffset;
            tonboTf.position = Vector3.Lerp(tonboTf.position, target, 1f - Mathf.Exp(-20f * Time.deltaTime));
        }

        bool ready = AnimTimeSinceOpen() >= openEndTime - 0.05f;
        bool inside = ready && InsideZone(tonboTf.position);

        bool release = false;
        if (grabMode == GrabMode.Pinch && !heldByStick)
        {
            if (found && !hp.pinching)
            {
                if (inside) release = true;
                else { phase = Phase.WaitGrab; waitStart = Time.time; StartCoroutine(ReturnToLanding()); return; } // 籠の外で離したらトンボは杭へ戻る
            }
        }
        else
        {
            dwell = inside ? dwell + Time.deltaTime : 0f;
            if (dwell >= releaseDwellSeconds) release = true;
        }

        if (release) ReleaseIntoCage();
    }

    System.Collections.IEnumerator ReturnToLanding()
    {
        Vector3 from = tonboTf.position; float t = 0f;
        while (t < 0.4f && tonboTf != null && phase == Phase.WaitGrab)
        {
            t += Time.deltaTime;
            tonboTf.position = Vector3.Lerp(from, landingPos, Mathf.SmoothStep(0f, 1f, t / 0.4f));
            yield return null;
        }
    }

    // ---------------- 籠に入れて閉じる ----------------
void ReleaseIntoCage()
    {
        // 籠の子にして一緒に下がる。向きと位置は、はみ出しが最小になるように決める
        tonboTf.SetParent(cage.transform, true);
        releaseFromLocal = tonboTf.localPosition;
        releaseFromRot = tonboTf.localRotation;
        FitTonboInCage(out releaseToLocal, out releaseToRot);
        if (game != null) game.MarkTonboStored(targetIndex); // ハードモード：虫かごに入れたトンボ（今は1匹目のみ）を Stunned→Stored

        closeStart = Time.time;
        phase = Phase.Closing;
    }

    /// <summary>
    /// 籠の軸に揃えた状態で、トンボを水平に15度刻みで回してはみ出し量が最小の向きを選び、
    /// メッシュ範囲の中心を籠の枠中心に合わせた収納姿勢（ローカル位置・回転）を返す。サイズは変えない。
    /// </summary>
    void FitTonboInCage(out Vector3 toLocal, out Quaternion toRot)
    {
        Transform ct = cage.transform;
        Quaternion keepCageRot = ct.rotation;
        Vector3 keepPos = tonboTf.localPosition;
        Quaternion keepRot = tonboTf.localRotation;

        ct.rotation = Quaternion.identity; // 籠の軸を世界軸に揃えて測る（子のトンボも一緒に回る）
        Bounds cb = FrameBounds();
        float innerX = cb.size.x * 0.9f, innerZ = cb.size.z * 0.9f; // 枠の太さ分を除いた内寸の目安

        float bestScore = float.MaxValue; float bestYaw = 0f;
        for (int a = 0; a < 360; a += 15)
        {
            float yaw = a > 180 ? a - 360 : a;
            tonboTf.localRotation = Quaternion.AngleAxis(yaw, Vector3.up) * keepRot;
            Bounds tb = TonboBounds();
            float score = Mathf.Max(0f, tb.size.x - innerX) + Mathf.Max(0f, tb.size.z - innerZ) + 0.0005f * Mathf.Abs(yaw);
            if (score < bestScore) { bestScore = score; bestYaw = yaw; }
        }

        tonboTf.localRotation = Quaternion.AngleAxis(bestYaw, Vector3.up) * keepRot;
        toRot = tonboTf.localRotation;
        Vector3 worldDelta = cb.center - TonboBounds().center; // メッシュ中心を籠の中心へ
        toLocal = keepPos + ct.InverseTransformVector(worldDelta);

        tonboTf.localPosition = keepPos; tonboTf.localRotation = keepRot; // 測定のために動かした分を戻す
        ct.rotation = keepCageRot;
    }

    Bounds TonboBounds()
    {
        Bounds b = new Bounds(tonboTf.position, Vector3.zero);
        bool first = true;
        foreach (var r in tonboTf.GetComponentsInChildren<Renderer>())
        {
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
            if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
        }
        return b;
    }

    void TickClosing()
    {
        float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Time.time - closeStart) / 0.4f));
        tonboTf.localPosition = Vector3.Lerp(releaseFromLocal, releaseToLocal, k);
        tonboTf.localRotation = Quaternion.Slerp(releaseFromRot, releaseToRot, k);


        float closeLen = Mathf.Max(0.1f, cageClip.length - closeStartTime);
        float t = closeStartTime + (Time.time - closeStart);
        SetAnimTime(Mathf.Min(t, cageClip.length));
        if (Time.time - closeStart >= closeLen)
        {
            sinkStart = Time.time;
            sinkFrom = cage.transform.position;
            phase = Phase.Sinking;
        }
    }

bool TickSinking()
    {
        float k = Mathf.Clamp01((Time.time - sinkStart) / Mathf.Max(0.05f, fadeSeconds));
        float e = Mathf.SmoothStep(0f, 1f, k);
        cage.transform.position = sinkFrom + Vector3.down * riseDistance * e;
        SetAlpha(1f - e);
        if (k >= 1f)
        {
            if (TryStartNextCatch()) return false; // ハードモード：まだ気絶中のトンボがいれば続けて次の1匹へ
            Finish();
            return true;
        }
        return false;
    }

    /// <summary>制限時間切れ：トンボがプレイヤーと反対の斜め上へ飛び去り、籠は下へ消える</summary>
    void StartEscape()
    {
        if (!followUp) Escaped = true; // 2匹目以降で逃げられても、スコア判定は1匹目の結果のまま
        tonboTf.SetParent(null, true);
        escapeFrom = tonboTf.position;
        Vector3 away = -game.DirToHead(escapeFrom);
        escapeDir = (away + Vector3.up * 0.6f).normalized;
        if (game != null) game.MarkTonboEscaped(targetIndex); // ハード：逃げたトンボを Stunned→Escaped にする
        escapeStart = Time.time;
        if (cage != null) sinkFrom = cage.transform.position;
        phase = Phase.Escaping;
    }

    bool TickEscaping()
    {
        float k = Mathf.Clamp01((Time.time - escapeStart) / Mathf.Max(0.1f, escapeSeconds));
        tonboTf.position = escapeFrom + escapeDir * (escapeDistance * k * k); // だんだん加速して離れる
        tonboTf.rotation = Quaternion.LookRotation(escapeDir, Vector3.up);
        // （逃げるのは捕獲対象の1匹のみ）
        if (cage != null)
        {
            float e = Mathf.SmoothStep(0f, 1f, k);
            cage.transform.position = sinkFrom + Vector3.down * riseDistance * e;
            SetAlpha(1f - e);
        }
        if (k >= 1f) { Finish(); return true; }
        return false;
    }

    
void Finish()
    {
        if (tonboTf != null) tonboTf.SetParent(null, true); // 籠を消す前に親から外す（籠と一緒に消えないように）
        // ハード：収納済みのトンボだけを非表示にする（逃げられたトンボ・もう1匹には触らない）
        if (game != null && game.HardMode && tonbo != null)
        {
            RotationBurstGame.TonboState st = (targetIndex == 2) ? game.Tonbo2State : game.Tonbo1State;
            if (st == RotationBurstGame.TonboState.Stored || st == RotationBurstGame.TonboState.Escaped) tonbo.HideOne(targetIndex); // 収納済み・逃げたトンボを非表示にする
        }
        DestroyCage();
        phase = Phase.Idle;
    }

    /// <summary>ゲームを中断したときなど。籠を消してトンボを親から外す</summary>
    public void Abort()
    {
        if (tonboTf != null) tonboTf.SetParent(null, true);
        DestroyCage();
        phase = Phase.Idle;
    }

    // ---------------- 籠の生成・表示 ----------------
    void BuildCage(Vector3 tonboPos)
    {
        cage = Instantiate(cagePrefab);
        cage.name = "TonboCageRuntime";
        // モデルに含まれるカメラ・ライトは不要
        foreach (var c in cage.GetComponentsInChildren<Camera>(true)) Destroy(c.gameObject);
        foreach (var l in cage.GetComponentsInChildren<Light>(true)) Destroy(l.gameObject);

        cage.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        cage.transform.localScale = Vector3.one;

        // 蓋以外のパーツ（土台・枠・格子）
        var frames = new List<Renderer>();
        foreach (var r in cage.GetComponentsInChildren<Renderer>(true))
            if (r.transform.name != "Lid" && !r.transform.IsChildOf(FindChild(cage.transform, "Lid_Hinge"))) frames.Add(r);
        frameRenderers = frames.ToArray();

        Bounds b0 = CombinedBounds();
        float width = Mathf.Max(b0.size.x, b0.size.z);
        float want = tonboWingspanAtScale1 * targetScale * cageWidthMargin;
        cageScale = want / Mathf.Max(0.01f, width);
        cage.transform.localScale = Vector3.one * cageScale;

        // 蝶番（蓋の根元）がプレイヤーと反対側になるように向きを合わせる
        Vector3 toHead = game.DirToHead(tonboPos);
        Transform hinge = FindChild(cage.transform, "Lid_Hinge");
        Vector3 hingeDir = hinge != null ? Vector3.ProjectOnPlane(hinge.position - b0.center * cageScale, Vector3.up) : Vector3.forward;
        if (hingeDir.sqrMagnitude < 1e-6f) hingeDir = Vector3.forward;
        float yaw = Vector3.SignedAngle(hingeDir, -toHead, Vector3.up);
        cage.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        // トンボを基準に、プレイヤーから見て左右どちらかへずらして置く（生成時に一度だけ決定、以後は固定）。高さは上端がトンボの少し下
        Bounds b = CombinedBounds();
        Transform headTf = (game != null && game.headTransform != null) ? game.headTransform : (Camera.main != null ? Camera.main.transform : null);
        Vector3 side = Vector3.right; // 頭が取れないときの代替
        if (headTf != null)
        {
            Vector3 toTonbo = tonboPos - headTf.position; toTonbo.y = 0f;
            if (toTonbo.sqrMagnitude > 1e-4f) side = Vector3.Cross(Vector3.up, toTonbo.normalized); // プレイヤーから見た右方向
        }
        float sideSign = UnityEngine.Random.value < 0.5f ? -1f : 1f;
        float towardPlayer = 0f; // 左右とも杭と重なるときだけ、プレイヤー側へ寄せる距離（m）
        // 左右2候補のうち、杭と重ならない側があればそちらを採用（両方空き・両方重なりのときは上のランダムのまま）
        {
            bool plusBlocked = CageOverlapsStake(tonboPos + side * cageSideOffset, tonboPos, b);
            bool minusBlocked = CageOverlapsStake(tonboPos - side * cageSideOffset, tonboPos, b);
            if (plusBlocked != minusBlocked) sideSign = plusBlocked ? -1f : 1f;
            else if (plusBlocked && minusBlocked)
            {
                // 左右とも重なる：プレイヤー側へ0.1m、だめなら0.2m寄せた左右候補で再判定し、最初に重ならなかった候補を採用
                float[] pullSteps = { 0.1f, 0.2f };
                for (int ps = 0; ps < pullSteps.Length; ps++)
                {
                    Vector3 pull = toHead * pullSteps[ps];
                    bool pBlocked = CageOverlapsStake(tonboPos + side * cageSideOffset + pull, tonboPos, b);
                    bool mBlocked = CageOverlapsStake(tonboPos - side * cageSideOffset + pull, tonboPos, b);
                    if (pBlocked && mBlocked) continue;
                    if (pBlocked != mBlocked) sideSign = pBlocked ? -1f : 1f; // 片方だけ空き→空いている側（両方空きなら上のランダムのまま）
                    towardPlayer = pullSteps[ps];
                    break;
                }
            }
        }
        Vector3 cageXZ = tonboPos + side * cageSideOffset * sideSign;
        cageXZ += toHead * (towardPlayer + 0.1f); // 左右とも重なったときだけ、プレイヤー側へ寄せた分を足す（通常は0）
        Vector3 delta = new Vector3(cageXZ.x - b.center.x, (tonboPos.y - gapBelowTonbo) - b.max.y, cageXZ.z - b.center.z);
        cage.transform.position += delta;
        cageTarget = cage.transform.position;

        // フェード用にマテリアルを複製して半透明化
        cageMats.Clear(); cageMatAlpha.Clear();
        foreach (var r in cage.GetComponentsInChildren<Renderer>(true))
        {
            Material[] ms = r.materials; // 複製される
            foreach (var m in ms)
            {
                if (m == null) continue;
                MakeFadeable(m);
                cageMats.Add(m);
                cageMatAlpha.Add(GetAlpha(m));
            }
        }

        // Scene クリップをAnimator+Playableで手動再生
        Animator anim = cage.GetComponent<Animator>();
        if (anim == null) anim = cage.AddComponent<Animator>();
        anim.applyRootMotion = false;
        graph = PlayableGraph.Create("TonboCageAnim");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var output = AnimationPlayableOutput.Create(graph, "out", anim);
        clipPlayable = AnimationClipPlayable.Create(graph, cageClip);
        output.SetSourcePlayable(clipPlayable);
        graphValid = true;
        SetAnimTime(0f);

        cageStart = Time.time;
        cage.transform.position = cageTarget + Vector3.down * riseDistance;
        SetAlpha(0f);
    }

    /// <summary>つかんだ直後〜：下から上がってフェードインし、少し遅れて開く</summary>
    void UpdateCageIntro()
    {
        if (cage == null) return;
        float k = Mathf.Clamp01((Time.time - cageStart) / Mathf.Max(0.05f, fadeSeconds));
        float e = Mathf.SmoothStep(0f, 1f, k);
        cage.transform.position = cageTarget + Vector3.down * riseDistance * (1f - e);
        SetAlpha(e);
        SetAnimTime(Mathf.Min(openEndTime, AnimTimeSinceOpen()));
    }

    float AnimTimeSinceOpen()
    {
        return Mathf.Max(0f, Time.time - cageStart - openDelay);
    }

    void SetAnimTime(float t)
    {
        if (!graphValid) return;
        clipPlayable.SetTime(t);
        graph.Evaluate();
    }

    void DestroyCage()
    {
        if (graphValid) { graph.Destroy(); graphValid = false; }
        foreach (var m in cageMats) if (m != null) Destroy(m);
        cageMats.Clear(); cageMatAlpha.Clear();
        if (cage != null) Destroy(cage);
        cage = null; frameRenderers = null;
    }

    // ---------------- 判定の補助 ----------------
    Bounds CombinedBounds()
    {
        Bounds b = new Bounds();
        bool first = true;
        foreach (var r in frameRenderers)
        {
            if (r == null) continue;
            if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
        }
        return b;
    }

    Bounds FrameBounds() { return CombinedBounds(); }

    /// <summary>
    /// 虫かごの中心を cageCenter に置いたとき、杭と重なるか。
    /// 水平：虫かごの範囲（b）＋余白の中に杭がある。高さ：杭の先端（杭の位置の0.04m下）が虫かごの下端より上にある。
    /// トンボ自身の杭は左右どちらでも同じ距離で選択に影響しないので除外する。
    /// </summary>
    bool CageOverlapsStake(Vector3 cageCenter, Vector3 tonboPos, Bounds b)
    {
        TonboStakes stakes = GetComponent<TonboStakes>();
        if (stakes == null) return false;
        float cageBottom = (tonboPos.y - gapBelowTonbo) - b.size.y;
        var list = stakes.StakeCenters;
        for (int i = 0; i < list.Count; i++)
        {
            Vector3 s = list[i];
            float ox = s.x - tonboPos.x, oz = s.z - tonboPos.z;
            if (ox * ox + oz * oz < 0.05f * 0.05f) continue; // トンボ自身の杭
            if (s.y - 0.04f < cageBottom) continue;          // 杭の先端が虫かごの下端より低い＝重ならない
            if (Mathf.Abs(s.x - cageCenter.x) <= b.extents.x + cageStakeMargin
                && Mathf.Abs(s.z - cageCenter.z) <= b.extents.z + cageStakeMargin) return true;
        }
        return false;
    }

    /// <summary>籠の口（蓋の下〜上）の範囲にトンボがあるか</summary>
    bool InsideZone(Vector3 p)
    {
        Bounds b = FrameBounds();
        float lo = b.min.y + 0.2f * b.size.y;
        float hi = b.max.y + 0.25f * b.size.y;
        return Mathf.Abs(p.x - b.center.x) <= b.extents.x * 0.9f
            && Mathf.Abs(p.z - b.center.z) <= b.extents.z * 0.9f
            && p.y >= lo && p.y <= hi;
    }

    static Transform FindChild(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
        return root; // 見つからなければ根（IsChildOf(root)=全部になるのを避けるため呼び出し側で注意）
    }

    // ---------------- 手の取得 ----------------
    void SampleHands(Transform stickTip)
    {
        pts.Clear();
        if (Time.time >= nextHandSearch) { hands = FindObjectsOfType<OVRHand>(); nextHandSearch = Time.time + 1f; }
        if (hands != null)
        {
            for (int i = 0; i < hands.Length; i++)
            {
                OVRHand h = hands[i];
                if (h == null || !h.IsTracked) continue;
                OVRSkeleton sk = h.GetComponent<OVRSkeleton>();
                if (sk == null || !sk.IsInitialized || sk.Bones == null) continue;

                Transform idx = null, thumb = null, mid = null;
                foreach (var b in sk.Bones)
                {
                    if (b.Id == OVRSkeleton.BoneId.Hand_IndexTip) idx = b.Transform;
                    else if (b.Id == OVRSkeleton.BoneId.Hand_ThumbTip) thumb = b.Transform;
                    else if (b.Id == OVRSkeleton.BoneId.Hand_MiddleTip) mid = b.Transform;
                }
                if (idx == null) continue;
                Vector3 hold = thumb != null ? (idx.position + thumb.position) * 0.5f : idx.position;
                bool pinch = h.GetFingerPinchStrength(OVRHand.HandFinger.Index) >= pinchThreshold;
                pts.Add(new HandPt { id = i, pos = idx.position, hold = hold, pinching = pinch, stick = false });
                if (thumb != null) pts.Add(new HandPt { id = i, pos = thumb.position, hold = hold, pinching = pinch, stick = false });
                if (mid != null) pts.Add(new HandPt { id = i, pos = mid.position, hold = hold, pinching = pinch, stick = false });
            }
        }
        // 手が追跡されていないとき（コントローラ・エディタ）は棒の先端で代用
        if (pts.Count == 0 && stickTip != null)
            pts.Add(new HandPt { id = 100, pos = stickTip.position, hold = stickTip.position, pinching = true, stick = true });
    }

    // ---------------- フェード ----------------
    static void MakeFadeable(Material m)
    {
        if (m.HasProperty("_Mode")) // Standard
        {
            m.SetFloat("_Mode", 2f);
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = 3000;
        }
        else if (m.HasProperty("_Surface")) // URP Lit/Unlit
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = 3000;
        }
    }

    static float GetAlpha(Material m)
    {
        if (m.HasProperty("_Color")) return m.color.a;
        if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor").a;
        return 1f;
    }

    void SetAlpha(float a)
    {
        for (int i = 0; i < cageMats.Count; i++)
        {
            Material m = cageMats[i];
            if (m == null) continue;
            float v = cageMatAlpha[i] * a;
            if (m.HasProperty("_Color")) { Color c = m.color; c.a = v; m.color = c; }
            else if (m.HasProperty("_BaseColor")) { Color c = m.GetColor("_BaseColor"); c.a = v; m.SetColor("_BaseColor", c); }
        }
    }
}
