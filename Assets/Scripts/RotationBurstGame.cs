using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// 手のひら（tipTransform）でリングをなぞり、
/// 「sessionTimeLimit秒(既定7秒)以内に、burstWindowSeconds秒(既定1秒)の間にrotationsPerBurst回(既定4回転)」
/// をrequiredBursts回(既定3回)達成するとクリアになるゲーム用スクリプト。
///
/// なぞり判定（半径方向の許容誤差thicknesHalf、奥行き方向の許容誤差depthTolerance）は
/// 元のSteering Law検証実験用スクリプト(RingTarget.cs)を踏襲しているが、
/// 元のスクリプトと違い、リングから外れても試行を打ち切らず、
/// 外れている間は回転をカウントしないだけの仕様にしてある（ゲームとして遊びやすくするため）。
/// 必要であれば、外れた瞬間に即失敗にする「abort-on-exit」方式に戻すことも可能。
///
/// リセットは物理ボタン方式：リング右上に生成される球（ResetButton）に
/// tipTransformをresetHoldSeconds秒(既定2秒)触れ続けると、状態（Waiting/Playing/Cleared/Failed）に
/// 関係なくいつでも強制リセットできる。ボタンはリングと同じ平面上（同じ奥行き）に置かれるため、
/// カメラ（顔）の近くに浮いて視界を塞ぐことはない。
///
/// 実機ビルドでは"Standard"シェーダーが含まれずShader.Findがnullを返すことがあるため、
/// ボタンの色にはLineRendererと同じ"Sprites/Default"（実機で動作確認済み）を使う。
/// また、ボタン生成で万一失敗してもステータステキストは必ず表示されるよう、
/// テキスト生成をボタン生成より先に行い、ボタン生成はtry-catchで保護する。
/// </summary>
public class RotationBurstGame : MonoBehaviour
{
    [Header("円の設定")]
    public float ringRadius = 0.15f;
    public int segments = 64;

    [Header("なぞり判定設定（この範囲内にいる間だけ回転をカウントする）")]
    public float thicknesHalf = 0.07f;
    public float depthTolerance = 0.05f;

    [Header("クリア条件")]
    [Tooltip("挑戦1回あたりの制限時間（秒）")]
    public float sessionTimeLimit = 7f;
    [Tooltip("「速い回転」と判定する時間窓（秒）")]
    public float burstWindowSeconds = 1f;
    [Tooltip("burstWindowSeconds以内に必要な回転数")]
    public int rotationsPerBurst = 4;
    [Tooltip("クリアに必要なバースト達成回数")]
    public int requiredBursts = 3;

    [Header("リセットボタン（棒の先端でresetHoldSeconds秒触れると、いつでも強制リセット）")]
    [Tooltip("ボタンの位置：リング中心からローカルX（右方向）にどれだけ離すか（メートル）")]
    public float resetButtonRightOffset = 0.25f;
    [Tooltip("ボタンの位置：リング中心からローカルZ（上方向）にどれだけ離すか（メートル）")]
    public float resetButtonUpOffset = 0.22f;
    [Tooltip("ボタンの見た目のサイズ（直径、メートル）")]
    public float resetButtonSize = 0.12f;
    [Tooltip("ボタンの判定半径（この距離以内なら触れているとみなす）")]
    public float resetButtonRadius = 0.09f;
    [Tooltip("この秒数だけ触れ続けたらリセットする")]
    public float resetHoldSeconds = 2f;

    [Header("表示設定")]
    public float textLocalX = 0f;
    public float textLocalY = -80f;
    public int textFontSize = 36;

    [Header("参照")]
    public Transform tipTransform;
    public AudioClip sparkleClip;

    private LineRenderer lr;
    private AudioSource audioSource;
    private Text uiText;

    private static readonly Color colorNormal = Color.white;

    private static readonly Color colorTouching = new Color(0.3f, 0.7f, 1f); // リングに触れている間
    private static readonly Color colorBurst  = new Color(1f, 0.84f, 0f);   // バースト達成の瞬間
    private static readonly Color colorClear  = new Color(0.2f, 1f, 0.4f);  // クリア
    private static readonly Color colorFail   = new Color(1f, 0.3f, 0.3f);  // 失敗

    private static readonly Color resetButtonColorIdle = new Color(0.5f, 0.5f, 0.5f); // 未接触
    private static readonly Color resetButtonColorHeld = new Color(1f, 0.2f, 0.2f);   // 押し込み中〜リセット直前

    private enum GameState { Waiting, Playing, Cleared, Failed }
    private GameState state = GameState.Waiting;

    private float lastAngle = float.NaN;
    private float cumulativeAngle = 0f;
    private int   rotationsCounted = 0;
    private float sessionStartTime = float.NaN;

    private readonly Queue<float> rotationTimestamps = new Queue<float>();
    private bool burstQualifiedLastFrame = false;
    private int  burstCount = 0;

    // リセットボタン関連
    private Transform resetButtonTransform;
    private Renderer  resetButtonRenderer;
    private float resetHoldTimer = 0f;

    // デバッグ表示用（原因切り分け後に削除してよい）
    private float dbgLocalY = 0f;
    private float dbgDistFromRing = 0f;
    private bool  dbgHasTip = false;
    private float flashUntil = -1f;

    void Start()
    {
        lr = gameObject.AddComponent<LineRenderer>();
        lr.loop          = true;
        lr.positionCount = segments;
        lr.useWorldSpace = true;
        lr.material      = new Material(Shader.Find("Sprites/Default"));
        lr.startWidth    = thicknesHalf * 2f;
        lr.endWidth      = thicknesHalf * 2f;

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.clip        = sparkleClip;
        audioSource.loop        = false;
        audioSource.playOnAwake = false;

        UpdateLinePositions();
        SetColor(colorNormal);

        // テキストを先に生成しておく（ボタン生成で何か問題があってもテキストは必ず出るように）
        CreateStatusText();
        UpdateStatusText();

        try
        {
            CreateResetButton();
        }
        catch (System.Exception e)
        {
            Debug.LogError("[RotationBurstGame] CreateResetButton failed: " + e);
        }
    }

    void CreateResetButton()
    {
        GameObject btn = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        btn.name = "ResetButton_" + gameObject.name;
        Destroy(btn.GetComponent<SphereCollider>());
        btn.transform.SetParent(this.transform, false);
        // リングと同じ平面上（ローカルY=0＝リングと同じ奥行き）の右上に配置。
        // カメラの近くに浮かないようにするため、奥行き方向にはオフセットしない。
        btn.transform.localPosition = new Vector3(resetButtonRightOffset, 0f, resetButtonUpOffset);
        btn.transform.localScale    = Vector3.one * resetButtonSize;

        resetButtonRenderer = btn.GetComponent<Renderer>();
        // "Standard"は実機ビルドでシェーダーが含まれずnullになることがあるため、
        // LineRendererと同じ実機動作確認済みの"Sprites/Default"を使う。
        resetButtonRenderer.material = new Material(Shader.Find("Sprites/Default"));
        resetButtonRenderer.material.color = resetButtonColorIdle;

        resetButtonTransform = btn.transform;
    }

    void Update()
    {
        // リセットボタンは状態に関係なく常にチェックする（いつでも強制リセット可能）
        HandleResetButton();

        if (state == GameState.Cleared)
        {
            if (Time.time >= flashUntil) SetColor(colorClear);
            UpdateStatusText();
            return;
        }

        if (state == GameState.Failed)
        {
            SetColor(colorFail);
            UpdateStatusText();
            return;
        }

        if (tipTransform == null)
        {
            UpdateStatusText();
            return;
        }

        Vector3 local = Quaternion.Inverse(transform.rotation) * (tipTransform.position - transform.position);

        // 奥行き方向：固定許容誤差
        bool outOfBounds = Mathf.Abs(local.y) > depthTolerance;

        // 半径方向：リングの太さ許容誤差
        float distXZ = new Vector2(local.x, local.z).magnitude;
        float distFromRing = Mathf.Abs(distXZ - ringRadius);
        if (!outOfBounds && distFromRing > thicknesHalf)
        {
            outOfBounds = true;
        }

        // デバッグ表示用
        dbgHasTip = true;
        dbgLocalY = local.y;
        dbgDistFromRing = distFromRing;

        if (outOfBounds)
        {
            // リングから外れている間は角度追跡だけリセットし、回転はカウントしない（試行は打ち切らない）
            lastAngle = float.NaN;
            if (Time.time >= flashUntil) SetColor(colorNormal);
            CheckTimeout();
            UpdateStatusText();
            return;
        }

        // リングに触れている間は常にタッチ色にする（バースト達成フラッシュ中は除く）
        if (Time.time >= flashUntil) SetColor(colorTouching);

        float angle = Mathf.Atan2(local.z, local.x) * Mathf.Rad2Deg;

        if (!float.IsNaN(lastAngle))
        {
            float delta = Mathf.Abs(Mathf.DeltaAngle(lastAngle, angle));

            if (state == GameState.Waiting && delta > 0f)
            {
                // なぞり始めた瞬間に7秒のセッションを開始
                state = GameState.Playing;
                sessionStartTime = Time.time;
            }

            if (state == GameState.Playing && delta > 0f)
            {
                cumulativeAngle += delta;

                int newRotations = Mathf.FloorToInt(cumulativeAngle / 360f);
                while (rotationsCounted < newRotations)
                {
                    rotationsCounted++;
                    rotationTimestamps.Enqueue(Time.time);
                }

                while (rotationTimestamps.Count > 0 && Time.time - rotationTimestamps.Peek() > burstWindowSeconds)
                {
                    rotationTimestamps.Dequeue();
                }

                bool burstQualifiedNow = rotationTimestamps.Count >= rotationsPerBurst;
                if (burstQualifiedNow && !burstQualifiedLastFrame)
                {
                    burstCount++;
                    flashUntil = Time.time + 0.3f;
                    SetColor(colorBurst);
                    PlaySparkle();

                    if (burstCount >= requiredBursts)
                    {
                        state = GameState.Cleared;
                        flashUntil = Time.time + 0.3f;
                    }
                }
                burstQualifiedLastFrame = burstQualifiedNow;
            }
        }
        lastAngle = angle;

        if (state == GameState.Playing)
        {
            CheckTimeout();
        }

        UpdateStatusText();
    }

    /// <summary>
    /// 棒の先端がリセットボタンにresetHoldSeconds秒触れ続けたら、状態を問わず強制リセットする。
    /// ボタンの色を押し込み進捗に応じて変化させ、触れていない間は徐々に進捗を戻す。
    /// </summary>
    void HandleResetButton()
    {
        if (resetButtonTransform == null) return;

        bool touchingButton = false;
        if (tipTransform != null)
        {
            float dist = Vector3.Distance(tipTransform.position, resetButtonTransform.position);
            touchingButton = dist <= resetButtonRadius;
        }

        if (touchingButton)
        {
            resetHoldTimer += Time.deltaTime;
        }
        else
        {
            resetHoldTimer = Mathf.Max(0f, resetHoldTimer - Time.deltaTime * 2f); // 離すと少し早めに進捗が戻る
        }

        if (resetButtonRenderer != null)
        {
            float progress = Mathf.Clamp01(resetHoldTimer / resetHoldSeconds);
            resetButtonRenderer.material.color = Color.Lerp(resetButtonColorIdle, resetButtonColorHeld, progress);
        }

        if (resetHoldTimer >= resetHoldSeconds)
        {
            resetHoldTimer = 0f;
            ResetGame();
        }
    }

    void CheckTimeout()
    {
        if (state != GameState.Playing || float.IsNaN(sessionStartTime)) return;

        float elapsed = Time.time - sessionStartTime;
        if (elapsed >= sessionTimeLimit && burstCount < requiredBursts)
        {
            state = GameState.Failed;
        }
    }

    void ResetGame()
    {
        state = GameState.Waiting;
        lastAngle = float.NaN;
        cumulativeAngle = 0f;
        rotationsCounted = 0;
        sessionStartTime = float.NaN;
        rotationTimestamps.Clear();
        burstQualifiedLastFrame = false;
        burstCount = 0;
        flashUntil = -1f;
        SetColor(colorNormal);
        UpdateStatusText();
    }

    void PlaySparkle()
    {
        if (audioSource != null && sparkleClip != null)
        {
            audioSource.PlayOneShot(sparkleClip);
        }
    }

    void UpdateLinePositions()
    {
        for (int i = 0; i < segments; i++)
        {
            float rad = 2f * Mathf.PI * i / segments;
            float x = Mathf.Cos(rad) * ringRadius;
            float z = Mathf.Sin(rad) * ringRadius;
            lr.SetPosition(i, transform.position + transform.rotation * new Vector3(x, 0f, z));
        }
    }

    void SetColor(Color c)
    {
        if (lr == null) return;
        lr.startColor = c;
        lr.endColor   = c;
    }

    void CreateStatusText()
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

        GameObject textGO = new GameObject("StatusText_" + gameObject.name);
        textGO.transform.SetParent(targetCanvas.transform, false);

        RectTransform rt = textGO.AddComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0.5f, 0.5f);
        rt.anchorMax        = new Vector2(0.5f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(textLocalX, textLocalY);
        rt.sizeDelta        = new Vector2(700f, 250f);

        uiText = textGO.AddComponent<Text>();
        uiText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        uiText.fontSize  = textFontSize;
        uiText.alignment = TextAnchor.MiddleCenter;
        uiText.color     = Color.white;
        uiText.fontStyle = FontStyle.Bold;
        uiText.text      = "";
    }

    void UpdateStatusText()
    {
        if (uiText == null) return;

        switch (state)
        {
            case GameState.Waiting:
                uiText.color = Color.white;
                if (dbgHasTip)
                {
                    uiText.text = string.Format(
                        "リングをなぞって開始\n(1秒間に{0}回転を{1}回で成功)\n\n[デバッグ] 奥行きズレ:{2:F3}m(許容{3:F3})\nリングまでの距離:{4:F3}m(許容{5:F3})",
                        rotationsPerBurst, requiredBursts, dbgLocalY, depthTolerance, dbgDistFromRing, thicknesHalf);
                }
                else
                {
                    uiText.text = string.Format(
                        "リングをなぞって開始\n(1秒間に{0}回転を{1}回で成功)",
                        rotationsPerBurst, requiredBursts);
                }
                break;

            case GameState.Playing:
                float remaining = Mathf.Max(0f, sessionTimeLimit - (Time.time - sessionStartTime));
                uiText.color = Color.white;
                uiText.text  = string.Format(
                    "残り {0:F1}秒\nバースト: {1} / {2}",
                    remaining, burstCount, requiredBursts);
                break;

            case GameState.Cleared:
                uiText.color = colorClear;
                uiText.text  = "クリア！\nリセットボタンに2秒触れてリトライ";
                break;

            case GameState.Failed:
                uiText.color = colorFail;
                uiText.text  = string.Format(
                    "失敗… (バースト {0} / {1})\nリセットボタンに2秒触れてリトライ",
                    burstCount, requiredBursts);
                break;
        }
    }
}
