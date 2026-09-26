using UnityEngine;
using System.Collections.Generic;
using System.IO;
using UnityEngine.UI;

/// <summary>
/// 5周(1800度)=1試行、true abort-on-exit方式のSteering Law検証実験用スクリプト。
/// ringRadius/thicknesHalfは試行ごとにランダム生成（連続ランダムID設計）。リトライ中は値を据え置く。
/// 半径方向(thicknesHalf)を1回でもはみ出したら、その試行(1800度分)を即座に失敗として打ち切り、
/// 同じtrialNoで最初(0度)からやり直す。
/// 奥行き方向(depthTolerance)は固定変数のため打ち切り対象外。
/// 1試行が trialTimeoutSeconds を超えたら疲労等によるタイムアウトとして無効化し、同じtrialNoでやり直す。
/// 失敗（境界逸脱／タイムアウト）はメモリ上で回数だけカウントし、CSVには書かない。
/// 成功(1800度を最後まで走り切った)瞬間にだけ、そこに至るまでの失敗回数と一緒に1行記録する。
///   → CSVの行数は常に「ブロック数 × trialsPerBlock」に固定され、MT-ID回帰に使いやすい。
/// 1ブロック=「成功」trialsPerBlock回がそろうまで継続。ブロック完了後は startNextBlockKey で手動再開。
/// </summary>
public class RingTarget : MonoBehaviour
{
    [Header("円の設定")]
    public float ringRadius = 0.15f;
    public int segments = 64;

    [Header("判定設定")]
    // thicknesHalf: 半径方向（リング平面内）の許容誤差 = 実験操作変数 W/2
    public float thicknesHalf    = 0.07f;
    // depthTolerance: 奥行き方向（リング平面に垂直）の固定許容誤差。条件間で変化させない。
    public float depthTolerance  = 0.05f;

    [Header("条件のランダム生成（連続ランダムID設計、試行ごとに更新）")]
    [Tooltip("ringRadiusの乱数範囲（最小）")]
    public float ringRadiusMin = 0.144f;
    [Tooltip("ringRadiusの乱数範囲（最大）")]
    public float ringRadiusMax = 0.176f;
    [Tooltip("thicknesHalfの乱数範囲（最小）")]
    public float thicknesHalfMin = 0.054f;
    [Tooltip("thicknesHalfの乱数範囲（最大）")]
    public float thicknesHalfMax = 0.066f;

    [Header("試行設計（5周=1試行、true abort-on-exit、成功trialsPerBlock回でブロック完了）")]
    [Tooltip("1試行分の目標角度。1800度(5周)で固定。途中ではみ出すと0度からやり直し。")]
    public float trialTargetAngle = 1800f;
    [Tooltip("1ブロックあたりの「成功(1800度完走)」試行回数")]
    public int trialsPerBlock = 10;
    [Tooltip("ブロック終了後、次のブロックを開始するキー")]
    public KeyCode startNextBlockKey = KeyCode.R;
    [Tooltip("1試行(1800度)にこの秒数以上かかったら疲労等によるタイムアウトとして無効化し、同じ試行をやり直す")]
    public float trialTimeoutSeconds = 600f;

    [Header("試行間インターバル")]
    [Tooltip("1試行(5周)成功ごとに設ける休止時間（秒）。等速でなぞり続ける癖を防ぎ、視覚的にも一度リセットするための間。")]
    public float intervalSeconds = 10f;

    [Header("タイム計測表示設定")]
    public float textLocalX = 0f;
    public float textLocalY = -80f;
    public float textLocalZ = -1f;
    public int textFontSize = 40;
    public int completeFontSize = 60;

    [Header("参照")]
    public Transform tipTransform;
    public AudioClip sparkleClip;

    private LineRenderer lr;
    private AudioSource audioSource;
    private float lastAngle = float.NaN;

    // 試行間インターバル状態
    private bool  isInInterval    = false;
    private float intervalEndTime = float.NaN;

    // タイマー関連（1試行=1800度単位）
    private float timerStartTime    = float.NaN;
    private float timerAccumAngle   = 0f;
    private float timerInTunnelTime = 0f;
    private bool  timerRunning      = false;

    // 現在の試行（trialNo）における失敗回数カウント（CSVには書かず、成功時にまとめて記録）
    private int currentAttemptCount     = 0; // 成功も含めた延べ試行回数
    private int currentBoundaryOutCount = 0;
    private int currentTimeoutCount     = 0;

    // ブロック／試行管理
    private int  blockNo      = 1;
    private int  trialNo      = 1;     // 1-indexed, 1..trialsPerBlock（成功した試行だけ進む）
    private bool blockRunning = true;  // false = 成功trialsPerBlock回そろって待機中

    private float  lastRecordedTime = float.NaN;
    private float  lastAccuracy     = float.NaN;
    private string lastReason       = ""; // 画面表示用（"boundary_out" / "timeout"）。CSVには書かない。
    private float  bestTime         = float.NaN;
    private float  bestAccuracy     = float.NaN;

    private static string sharedCsvPath = null;
    private static int    globalTrialNo = 0;
    private static readonly object csvLock = new object();
    private const  string  sharedCsvFileName = "steeringResult_new.csv";

    private string csvPath;
    private string ringName;

    private Text uiText;

    private static readonly Color colorNormal = Color.white;
    private static readonly Color colorGold   = new Color(1f, 0.84f, 0f);

    // 周回ごとの色分け（1周目=青,2周目=緑,3周目=紫,4周目=オレンジ,5周目=金=成功色）
    private static readonly Color colorLap1 = new Color(0.25f, 0.45f, 1f);
    private static readonly Color colorLap2 = new Color(0.20f, 0.80f, 0.30f);
    private static readonly Color colorLap3 = new Color(0.60f, 0.25f, 0.85f);
    private static readonly Color colorLap4 = new Color(1f, 0.55f, 0f);

    void Start()
    {
        lr = gameObject.AddComponent<LineRenderer>();
        lr.loop           = true;
        lr.positionCount  = segments;
        lr.useWorldSpace  = true;
        lr.material       = new Material(Shader.Find("Sprites/Default"));

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.clip        = sparkleClip;
        audioSource.loop        = true;
        audioSource.playOnAwake = false;

        SetColor(colorNormal);
        RandomizeCondition(); // 最初の試行用にringRadius/thicknesHalfをランダム生成（width・ライン位置も同時更新）
        InitCsv();
        CreateTimerText();
        UpdateTimerText();
    }

    /// <summary>
    /// ringRadius/thicknesHalfを指定範囲内でランダムに再生成し、LineRendererの見た目（幅・位置）に反映する。
    /// 呼び出しタイミング：起動時（最初の試行）と、成功して次の試行に進む直前。
    /// リトライ中（境界逸脱・タイムアウトで同じ試行をやり直す間）は呼ばない＝値を据え置く。
    /// </summary>
    void RandomizeCondition()
    {
        // 連続ランダムID設計（Maruyama et al. WISS2023方式）：
        // R・Wを独立に一様サンプリングするとID=A/Wの比の分布が中央に偏ってしまうため、
        // まずIDそのものを目標範囲内で一様にサンプリングし、
        // そのIDを満たすringRadius・thicknesHalfの組み合わせを有効範囲内から一様に選ぶ。
        float laps = trialTargetAngle / 360f;
        float wMin = thicknesHalfMin * 2f;
        float wMax = thicknesHalfMax * 2f;
        float aMin = laps * 2f * Mathf.PI * ringRadiusMin;
        float aMax = laps * 2f * Mathf.PI * ringRadiusMax;
        float idMin = aMin / wMax;
        float idMax = aMax / wMin;

        float targetID = Random.Range(idMin, idMax);

        // targetIDを満たすR（W=A/targetIDがW範囲に収まる）の有効区間を求める
        float rMinValid = Mathf.Max(ringRadiusMin, targetID * wMin / (2f * Mathf.PI * laps));
        float rMaxValid = Mathf.Min(ringRadiusMax, targetID * wMax / (2f * Mathf.PI * laps));
        if (rMinValid > rMaxValid)
        {
            float tmp = rMinValid; rMinValid = rMaxValid; rMaxValid = tmp;
        }

        float r = Random.Range(rMinValid, rMaxValid);
        float a = laps * 2f * Mathf.PI * r;
        float w = a / targetID;

        ringRadius   = r;
        thicknesHalf = w / 2f;

        if (lr != null)
        {
            float visualWidth = thicknesHalf * 2f;
            lr.startWidth = visualWidth;
            lr.endWidth   = visualWidth;
            UpdateLinePositions();
        }
    }

    void InitCsv()
    {
        ringName = gameObject.name;
        lock (csvLock)
        {
            if (sharedCsvPath == null)
            {
                sharedCsvPath = Path.Combine(Application.persistentDataPath, sharedCsvFileName);

                if (!File.Exists(sharedCsvPath))
                {
                    File.WriteAllText(sharedCsvPath,
                        "trial_no,ring_name,block_no,trial_no_in_block,ring_radius,W,A,ID,MT_sec,attempts,boundary_out_count,timeout_count,accuracy,timestamp\n");
                    globalTrialNo = 0;
                }
                else
                {
                    int lineCount = File.ReadAllLines(sharedCsvPath).Length;
                    globalTrialNo = Mathf.Max(0, lineCount - 1);
                }
            }
            csvPath = sharedCsvPath;
        }
    }

    // カラム: trial_no, ring_name, block_no, trial_no_in_block, ring_radius, W, A, MT_sec,
    //         attempts(この試行が成功するまでの延べ試行回数), boundary_out_count, timeout_count, accuracy, timestamp
    // ※ この行は「成功した試行(1800度完走)」のみを表す。attempts=1なら1発成功、3ならやり直し2回を経て成功。
    void AppendCsvRow(int block, int trial, float mt, int attempts, int boundaryOutCount, int timeoutCount, float accuracy)
    {
        float W  = thicknesHalf * 2f;
        // A: 1試行分(trialTargetAngle度)の総移動距離。trialTargetAngleが全条件で固定(1800度=5周)のため、
        // 周回数 = trialTargetAngle/360 を circumference に掛けて算出する。
        float A  = (trialTargetAngle / 360f) * 2f * Mathf.PI * ringRadius;
        float id = (W > 0f) ? (A / W) : 0f;

        lock (csvLock)
        {
            globalTrialNo++;
            string row = string.Format("{0},{1},{2},{3},{4:F4},{5:F4},{6:F4},{7:F4},{8:F4},{9},{10},{11},{12:F4},{13}\n",
                globalTrialNo, ringName, block, trial, ringRadius, W, A, id, mt,
                attempts, boundaryOutCount, timeoutCount, accuracy,
                System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            File.AppendAllText(sharedCsvPath, row);
        }
    }

    void CreateTimerText()
    {
        Canvas targetCanvas = null;
        foreach (var c in Object.FindObjectsOfType<Canvas>())
        {
            if (c.renderMode == RenderMode.WorldSpace && c.gameObject.name == "Canvas")
            {
                targetCanvas = c;
                break;
            }
        }
        if (targetCanvas == null) return;

        GameObject textGO = new GameObject("TimerText_" + gameObject.name);
        textGO.transform.SetParent(targetCanvas.transform, false);

        RectTransform rt = textGO.AddComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0.5f, 0.5f);
        rt.anchorMax        = new Vector2(0.5f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(textLocalX, textLocalY);
        rt.localPosition    = new Vector3(textLocalX, textLocalY, textLocalZ);
        rt.sizeDelta        = new Vector2(600f, 200f);

        uiText = textGO.AddComponent<Text>();
        uiText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        uiText.fontSize  = textFontSize;
        uiText.alignment = TextAnchor.MiddleCenter;
        uiText.color     = Color.white;
        uiText.fontStyle = FontStyle.Bold;
        uiText.text      = "---";
    }

    void Update()
    {
        if (!blockRunning && Input.GetKeyDown(startNextBlockKey))
        {
            StartNewBlock();
        }

        if (isInInterval)
        {
            UpdateTimerText();
            if (Time.time >= intervalEndTime)
            {
                EndInterval();
            }
            return;
        }

        if (tipTransform == null) return;

        // 疲労等によるタイムアウト：1試行(1800度)に trialTimeoutSeconds 以上かかったら無効化し、同じ試行をやり直す。
        if (timerRunning && blockRunning && (Time.time - timerStartTime) > trialTimeoutSeconds)
        {
            RegisterFailedAttempt("timeout");
            SetColor(colorNormal);
            ResetTracking();
        }

        Vector3 local = Quaternion.Inverse(transform.rotation) * (tipTransform.position - transform.position);

        // 奥行き方向：固定許容誤差。条件間で変化させる操作変数ではないため打ち切り対象外。
        if (Mathf.Abs(local.y) > depthTolerance)
        {
            SetColor(colorNormal);
            ResetTracking();
            return;
        }

        // 半径方向：実験操作変数 W(=thicknesHalf*2) に対応する唯一の判定軸。
        // true abort-on-exit：1回でもはみ出したら試行全体(1800度の進捗)を打ち切る。
        float distXZ       = new Vector2(local.x, local.z).magnitude;
        float distFromRing = Mathf.Abs(distXZ - ringRadius);

        if (distFromRing > thicknesHalf)
        {
            SetColor(colorNormal);
            ResetTracking();

            if (timerRunning && blockRunning)
            {
                RegisterFailedAttempt("boundary_out");
            }
            return;
        }

        if (timerRunning)
            timerInTunnelTime += Time.deltaTime;

        float angle = Mathf.Atan2(local.z, local.x) * Mathf.Rad2Deg;
        float now   = Time.time;

        float delta = 0f;
        if (!float.IsNaN(lastAngle))
        {
            delta = Mathf.Abs(Mathf.DeltaAngle(lastAngle, angle));
        }
        lastAngle = angle;

        if (delta > 0f && blockRunning)
        {
            if (!timerRunning)
            {
                timerRunning       = true;
                timerStartTime     = now;
                timerInTunnelTime  = 0f;
                timerAccumAngle    = 0f;
            }

            timerAccumAngle += delta;
        }

        // 周回数に応じた色分け（1周目=青,2周目=緑,3周目=紫,4周目=オレンジ,5周目=金）。
        // どこまで連続してなぞれているかが色だけで直感的にわかるようにする。
        if (timerRunning)
        {
            int lapIndex = Mathf.FloorToInt(timerAccumAngle / 360f); // 0=1周目,1=2周目,2=3周目,3=4周目,4以降=5周目(成功)
            Color lapColor;
            if (lapIndex <= 0)      lapColor = colorLap1;
            else if (lapIndex == 1) lapColor = colorLap2;
            else if (lapIndex == 2) lapColor = colorLap3;
            else if (lapIndex == 3) lapColor = colorLap4;
            else                     lapColor = colorGold;
            SetColor(lapColor);
        }
        else
        {
            SetColor(colorNormal);
        }

        if (timerRunning && blockRunning && timerAccumAngle >= trialTargetAngle)
        {
            float elapsed  = now - timerStartTime;
            float accuracy = (elapsed > 0f) ? Mathf.Clamp01(timerInTunnelTime / elapsed) : 0f;
            RegisterSuccess(elapsed, accuracy);
        }
    }

    /// <summary>
    /// 境界逸脱またはタイムアウトによる失敗を記録（CSVには書かず、メモリ上のカウントのみ増やす）。
    /// タイマーと蓄積角度をリセットし、同じtrialNoを0度からやり直せる状態に戻す。
    /// </summary>
    void RegisterFailedAttempt(string reason)
    {
        currentAttemptCount++;
        if (reason == "boundary_out") currentBoundaryOutCount++;
        else if (reason == "timeout")  currentTimeoutCount++;

        lastReason = reason;

        timerRunning       = false;
        timerStartTime     = float.NaN;
        timerAccumAngle    = 0f;
        timerInTunnelTime  = 0f;

        UpdateTimerText();
    }

    /// <summary>
    /// 1試行(1800度)成功。ここに至るまでの失敗回数（currentAttemptCount等）と一緒にCSVへ1行書き、
    /// trialNoを進める。trialNoがtrialsPerBlockを超えたらブロックを停止して待機状態にする。
    /// </summary>
    void RegisterSuccess(float elapsed, float accuracy)
    {
        currentAttemptCount++; // 成功した今回の試行を含める

        lastRecordedTime = elapsed;
        lastAccuracy     = accuracy;
        lastReason       = "success";

        if (float.IsNaN(bestTime) || elapsed < bestTime) bestTime = elapsed;
        if (float.IsNaN(bestAccuracy) || accuracy > bestAccuracy) bestAccuracy = accuracy;

        AppendCsvRow(blockNo, trialNo, elapsed, currentAttemptCount, currentBoundaryOutCount, currentTimeoutCount, accuracy);

        timerRunning       = false;
        timerStartTime     = float.NaN;
        timerAccumAngle    = 0f;
        timerInTunnelTime  = 0f;
        ResetTracking();

        // 次の試行のためにカウントをリセット
        currentAttemptCount     = 0;
        currentBoundaryOutCount = 0;
        currentTimeoutCount     = 0;

        trialNo++;
        if (trialNo > trialsPerBlock)
        {
            blockRunning = false;
            UpdateTimerText();
        }
        else
        {
            StartInterval(); // 5周成功→即座に次の試行に入らず、intervalSeconds秒の休止を挟んでからRandomizeCondition()
        }
    }

    /// <summary>
    /// 5周成功後、次の試行に入る前の休止。リングを非表示にして等速でなぞり続ける癖を防ぎ、
    /// 視覚的にも一度区切りを作る。intervalSeconds経過後にEndInterval()で次の条件を表示する。
    /// </summary>
    void StartInterval()
    {
        isInInterval    = true;
        intervalEndTime = Time.time + intervalSeconds;
        if (lr != null) lr.enabled = false;
        if (audioSource != null && audioSource.isPlaying) audioSource.Stop();
        UpdateTimerText();
    }

    void EndInterval()
    {
        isInInterval    = false;
        intervalEndTime = float.NaN;
        RandomizeCondition(); // 次の試行のために新しい条件をランダム生成
        if (lr != null) lr.enabled = true;
        UpdateTimerText();
    }

    /// <summary>
    /// 次のブロック（成功trialsPerBlock回）を開始する。startNextBlockKey押下、または外部スクリプトから呼び出し可能。
    /// </summary>
    public void StartNewBlock()
    {
        blockNo++;
        trialNo                  = 1;
        blockRunning             = true;
        timerRunning             = false;
        timerStartTime           = float.NaN;
        timerAccumAngle          = 0f;
        timerInTunnelTime        = 0f;
        currentAttemptCount      = 0;
        currentBoundaryOutCount  = 0;
        currentTimeoutCount      = 0;
        RandomizeCondition(); // 新ブロック最初の試行用に条件を再生成
        ResetTracking();
        UpdateTimerText();
    }

    void ResetTracking()
    {
        lastAngle = float.NaN;
    }

    void UpdateLinePositions()
    {
        for (int i = 0; i < segments; i++)
        {
            float rad = 2f * Mathf.PI * i / segments;
            float x   = Mathf.Cos(rad) * ringRadius;
            float z   = Mathf.Sin(rad) * ringRadius;
            lr.SetPosition(i, transform.position + transform.rotation * new Vector3(x, 0f, z));
        }
    }

    void SetColor(Color c)
    {
        lr.startColor = c;
        lr.endColor   = c;

        if (audioSource == null || sparkleClip == null) return;
        bool isGold = (c == colorGold);
        if (isGold && !audioSource.isPlaying) audioSource.Play();
        else if (!isGold && audioSource.isPlaying) audioSource.Stop();
    }

    void UpdateTimerText()
    {
        if (uiText == null) return;

        if (isInInterval)
        {
            float remaining = Mathf.Max(0f, intervalEndTime - Time.time);
            uiText.fontSize = textFontSize;
            uiText.color    = Color.white;
            uiText.text     = string.Format("休止中…\n{0:F0}秒後に次の試行", Mathf.Ceil(remaining));
            return;
        }

        if (!blockRunning)
        {
            // ブロック完了テロップ：条件はランダム生成のため手動調整は不要。休憩の合図として表示。
            uiText.fontSize = completeFontSize;
            uiText.color    = colorGold;
            uiText.text     = string.Format("Block {0} 完了！({1}回成功)\n少し休憩しよう\n\n（準備できたら[{2}]で次ブロック開始）",
                blockNo, trialsPerBlock, startNextBlockKey);
            return;
        }

        uiText.fontSize = textFontSize;
        uiText.color    = Color.white;

        string retryStr = (currentAttemptCount > 0)
            ? string.Format("\n(やり直し中: {0}回目 [{1}])", currentAttemptCount + 1, lastReason)
            : "";

        uiText.text = string.Format("Block {0}  Trial {1}/{2}{3}\nR: {4:F3}m  W: {5:F3}m",
            blockNo, trialNo, trialsPerBlock, retryStr, ringRadius, thicknesHalf * 2f);
    }
}
