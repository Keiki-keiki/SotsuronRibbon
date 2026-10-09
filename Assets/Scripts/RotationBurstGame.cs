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
    [Tooltip("円の不透明度（0=完全に透明、1=不透明）。小さいほど奥のトンボが見やすい")]
    [Range(0f, 1f)]
    public float ringAlpha = 0.3f;

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

    [Header("メニューボタン（棒の先端でresetHoldSeconds秒触れると、いつでもメニュー画面に戻る）")]
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

    [Header("オープニング画面 / スタートボタン（球に触れ続けるとゲーム開始）")]
    [Tooltip("オープニング画面に表示するタイトル")]
    public string gameTitle = "リングゲーム";
    [Tooltip("スタートボタンの位置：最初のリング中心からのローカルオフセット（X=右, Y=奥行き, Z=上）")]
    public Vector3 startButtonLocalOffset = Vector3.zero;
    [Tooltip("スタートボタンの見た目のサイズ（直径、メートル）")]
    public float startButtonSize = 0.12f;
    [Tooltip("スタートボタンの判定半径（この距離以内なら触れているとみなす）")]
    public float startButtonRadius = 0.09f;
    [Tooltip("この秒数だけ触れ続けたらゲーム開始")]
    public float startHoldSeconds = 2f;

    [Header("記録（オープニング画面に表示。PlayerPrefsに保存され、アプリを閉じても残る）")]
    [Tooltip("トンボの実寸の目安：Prefabの倍率1あたりの翼長（メートル）。元モデルは約3.9m")]
    public float tonboModelSpanMeters = 3.9f;
    [Tooltip("保存する色違いの最大種類数（超えたら古いものから消える）")]
    public int maxStoredVariants = 10;
    [Tooltip("記録画面：捕まえたトンボ一覧（右側）の中心位置。最初のリング中心からのローカルオフセット（X=右, Y=奥行き, Z=上）")]
    public Vector3 recordsGalleryLocalOffset = Vector3.zero;
    [Tooltip("記録画面：トンボ一覧の列数（超えたら下の段に折り返す）")]
    public int galleryColumns = 3;
    [Tooltip("記録画面：文字（左側）の横位置（Canvas上の座標。0が中央、マイナスで左）")]
    public float recordsTextLocalX = -180f;
    [Tooltip("記録画面：文字の幅（Canvas上の座標。一覧と重ならないよう狭める）")]
    public float recordsTextWidth = 450f;
    [Tooltip("一覧のトンボ同士の間隔（メートル）")]
    public float gallerySpacing = 0.09f;
    [Tooltip("一覧のトンボの大きさ（Prefab倍率）")]
    public float galleryTonboScale = 0.02f;
    [Tooltip("記録画面：最大・最小のトンボを並べる段の高さ（一覧の中心からのローカルZ、メートル）")]
    public float recordsHighlightLocalZ = 0.15f;
    [Tooltip("記録画面：最大と最小のトンボの左右の間隔（メートル）")]
    public float recordsHighlightSpacing = 0.22f;
    [Tooltip("記録画面：色違い一覧の中心の高さ（一覧の中心からのローカルZ、メートル）")]
    public float recordsGridLocalZ = -0.13f;
    [Tooltip("記録画面：最大・最小ラベルの文字の大きさ（TextMeshのcharacterSize）")]
    public float recordsLabelCharSize = 0.003f;
    [Tooltip("記録画面：「捕まえた数」ラベルの高さ（ローカルZ、メートル）")]
    public float recordsCountLocalZ = 0.26f;
    [Tooltip("記録画面：「つかまえた色」見出しの高さ（ローカルZ、メートル）")]
    public float recordsColorsHeaderLocalZ = -0.01f;
    [Tooltip("記録画面：トンボをプレイヤーに向けたうえで、さらに横へ回す角度（度）。0で真正面")]
    public float galleryViewYaw = 30f;
    [Tooltip("記録画面：トンボをプレイヤーに向けたうえで、頭を下げる角度（度）。上から見える分、胴体の色が分かりやすい")]
    public float galleryViewPitch = 20f;



    [Header("メニューボタン配置（スタート=左 / 記録=右）")]
    [Tooltip("スタートボタンと記録ボタンの間隔（メートル）。startButtonLocalOffsetを中心に左右へ半分ずつ離れる")]
    public float menuButtonGap = 0.3f;
    [Tooltip("記録画面の「もどる」ボタンの位置：startButtonLocalOffsetからの追加オフセット")]
    public Vector3 backButtonLocalOffset = Vector3.zero;

    [Header("表示設定")]
    public float textLocalX = 0f;
    public float textLocalY = -80f;
    public int textFontSize = 36;

    [Header("参照")]
    public Transform tipTransform;
    public AudioClip sparkleClip;

    [Header("ランダム出現設定（手の届く範囲）")]
    [Tooltip("頭の位置。未設定ならCamera.main（CenterEyeAnchor）を使う")]
    public Transform headTransform;
    [Tooltip("頭から肩を推定するときの右方向オフセット（メートル。右手用）")]
    public float shoulderOffsetRight = 0.15f;
    [Tooltip("頭から肩を推定するときの下方向オフセット（メートル）")]
    public float shoulderOffsetDown = 0.2f;
    [Tooltip("肩から棒の先端まで届く距離（メートル）。実機で調整すること")]
    public float reachRadius = 0.7f;
    
    [Tooltip("初期位置を中心に、左右（リング平面のローカルX）へ動かす最大距離（メートル）")]
    public float spawnRangeRight = 0.3f;
    [Tooltip("初期位置を中心に、上方向（リング平面のローカルZ+）へ動かす最大距離（メートル）")]
    public float spawnRangeUp = 0.45f;
    [Tooltip("初期位置を中心に、下方向（リング平面のローカルZ-）へ動かす最大距離（メートル）")]
    public float spawnRangeDown = 0.25f;
    
    
    [Tooltip("前回のリング中心から最低これだけ離す（メートル）")]
    public float minSpawnSeparation = 0.2f;
    [Tooltip("クリア後、次のリングが出るまでの待ち時間（秒）")]
    public float nextRingDelay = 1f;
    [Tooltip("true: リング出現と同時に制限時間スタート / false: なぞり始めてからスタート")]
    public bool timerStartsOnSpawn = true;

    [Header("トンボ演出")]
    [Tooltip("円に迫ってくるトンボのPrefab（Assets/Tonbo/Tonbo.prefab）")]
    public GameObject tonboPrefab;
    [Tooltip("トンボの大きさ（元モデルは翼を広げて約3.9mなので、0.08で約0.3m）")]
    public float tonboScale = 0.08f;
    [Tooltip("円の奥（プレイヤーと反対側）にどれだけ離れた位置から飛んでくるか（メートル）")]
    public float tonboStartDistance = 2.0f;
    [Tooltip("飛んでくる途中の上下左右のゆらぎの大きさ（メートル）")]
    public float tonboWobble = 0.04f;
    [Tooltip("トンボの色がランダムになる確率（0.001 = 0.1%）。トンボが出現するたびに抽選")]
    [Range(0f, 1f)]
    public float tonboRandomColorChance = 0.001f;
    [Tooltip("デバッグ用：trueにすると毎回ランダム色になる（確認後はfalseに戻すこと）")]
    public bool tonboForceRandomColor = false;
    [Tooltip("大きさのばらつき（対数正規分布のσ）。0で全部同じ大きさ。0.25なら約68%が×0.78〜×1.28、約95%が×0.61〜×1.65")]
    public float tonboSizeSigma = 0.25f;
    [Tooltip("大きさの倍率の下限（極端に小さくなりすぎないように）")]
    public float tonboSizeMinMultiplier = 0.4f;
    [Tooltip("大きさの倍率の上限（極端に大きくなりすぎないように）")]
    public float tonboSizeMaxMultiplier = 2.5f;

    private LineRenderer lr;
    private AudioSource audioSource;
    private Text uiText;

    private static readonly Color colorNormal = Color.white;

    private static readonly Color colorTouching = new Color(0.3f, 0.7f, 1f); // リングに触れている間
    private static readonly Color colorBurst = new Color(1f, 0.84f, 0f);   // バースト達成の瞬間
    private static readonly Color colorClear = new Color(0.2f, 1f, 0.4f);  // クリア
    private static readonly Color colorFail = new Color(1f, 0.3f, 0.3f);  // 失敗

    private static readonly Color resetButtonColorIdle = new Color(0.5f, 0.5f, 0.5f); // 未接触
    private static readonly Color resetButtonColorHeld = new Color(1f, 0.2f, 0.2f);   // 押し込み中〜リセット直前

    private enum GameState { Title, Records, Waiting, Playing, Cleared, Failed }
    private GameState state = GameState.Title;

    private float lastAngle = float.NaN;
    private float cumulativeAngle = 0f;
    private int rotationsCounted = 0;
    private float sessionStartTime = float.NaN;

    private readonly Queue<float> rotationTimestamps = new Queue<float>();
    private bool burstQualifiedLastFrame = false;
    private int burstCount = 0;

    // リセットボタン関連
    private Transform resetButtonTransform;
    private Renderer resetButtonRenderer;
    private float resetHoldTimer = 0f;

    // スタートボタン関連
    private Transform startButtonTransform;
    private Renderer startButtonRenderer;
    private float startHoldTimer = 0f;

    // 記録関連
    private const string KeyCount = "Tonbo_Count";
    private const string KeyNormal = "Tonbo_NormalCount";
    private const string KeyMax = "Tonbo_MaxSize";
    private const string KeyMin = "Tonbo_MinSize";
    private const string KeyVariants = "Tonbo_Variants";
    private const string KeyMaxColors = "Tonbo_MaxColors"; // 最大個体の配色（空=通常色）
    private const string KeyMinColors = "Tonbo_MinColors"; // 最小個体の配色（空=通常色）

    private float currentTonboScale = 0f;
    private readonly List<Color> currentVariantColors = new List<Color>();
    private GameObject galleryRoot;
    private HoldButton recordButton;
    private HoldButton backButton;

    /// <summary>球に触れ続けると押せるシンプルなボタン（記録ボタン・もどるボタン用）</summary>
    private class HoldButton
    {
        public Transform t;
        private Renderer r;
        private float timer;
        private Color idle, held;

        public HoldButton(string name, Vector3 pos, float size, Color idle, Color held)
        {
            this.idle = idle;
            this.held = held;
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            Object.Destroy(go.GetComponent<SphereCollider>());
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * size;
            r = go.GetComponent<Renderer>();
            r.material = new Material(Shader.Find("Sprites/Default"));
            r.material.color = idle;
            t = go.transform;
        }

        public void SetActive(bool active)
        {
            timer = 0f;
            if (r != null) r.material.color = idle;
            if (t != null) t.gameObject.SetActive(active);
        }

        /// <summary>毎フレーム呼ぶ。holdSeconds秒触れ続けたらtrueを返す</summary>
        public bool Tick(Transform tip, float radius, float holdSeconds)
        {
            if (t == null) return false;
            bool touching = tip != null && Vector3.Distance(tip.position, t.position) <= radius;
            timer = touching ? timer + Time.deltaTime : Mathf.Max(0f, timer - Time.deltaTime * 2f);
            r.material.color = Color.Lerp(idle, held, Mathf.Clamp01(timer / holdSeconds));
            if (timer >= holdSeconds)
            {
                timer = 0f;
                return true;
            }
            return false;
        }
    }
    private readonly List<Material> galleryMats = new List<Material>();
    private static readonly Color startButtonColorIdle = new Color(0.2f, 0.6f, 0.3f);
    private static readonly Color startButtonColorHeld = new Color(0.4f, 1f, 0.5f);

    // ランダム出現・スコア関連
    private int score = 0;
    private int bestScore = 0;
    private float nextSpawnTime = 0f;
    private Vector3 initialPos;
    private Quaternion initialRot;
    private Vector3 lastCenter;

    // トンボ
    private GameObject tonboInstance;
    private bool tonboRunning = false;
    private Vector3 tonboStart;
    private Vector3 tonboEnd;
    private Renderer[] tonboRenderers;
    private Material[][] tonboOriginalMats;
    private readonly List<Material> tonboTempMats = new List<Material>();

    // デバッグ表示用（原因切り分け後に削除してよい）
    private float dbgLocalY = 0f;
    private float dbgDistFromRing = 0f;
    private bool dbgHasTip = false;
    private float flashUntil = -1f;

    void Start()
    {
        initialPos = transform.position;
        initialRot = transform.rotation;
        lastCenter = initialPos;

        lr = gameObject.AddComponent<LineRenderer>();
        lr.loop = true;
        lr.positionCount = segments;
        lr.useWorldSpace = true;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startWidth = thicknesHalf * 2f;
        lr.endWidth = thicknesHalf * 2f;

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.clip = sparkleClip;
        audioSource.loop = false;
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

        try
        {
            CreateStartButton();
        }
        catch (System.Exception e)
        {
            Debug.LogError("[RotationBurstGame] CreateStartButton failed: " + e);
        }

        EnterTitle();
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
        btn.transform.localScale = Vector3.one * resetButtonSize;

        resetButtonRenderer = btn.GetComponent<Renderer>();
        // "Standard"は実機ビルドでシェーダーが含まれずnullになることがあるため、
        // LineRendererと同じ実機動作確認済みの"Sprites/Default"を使う。
        resetButtonRenderer.material = new Material(Shader.Find("Sprites/Default"));
        resetButtonRenderer.material.color = resetButtonColorIdle;

        resetButtonTransform = btn.transform;
        // リングはランダムに動くので、リセットボタンは初期位置（手が届く位置）に固定する
        btn.transform.SetParent(null, true);
    }

void CreateStartButton()
    {
        GameObject btn = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        btn.name = "StartButton_" + gameObject.name;
        Destroy(btn.GetComponent<SphereCollider>());
        btn.transform.position = MenuButtonPos(-0.5f);
        btn.transform.localScale = Vector3.one * startButtonSize;

        startButtonRenderer = btn.GetComponent<Renderer>();
        startButtonRenderer.material = new Material(Shader.Find("Sprites/Default"));
        startButtonRenderer.material.color = startButtonColorIdle;

        startButtonTransform = btn.transform;

        recordButton = new HoldButton("RecordButton_" + gameObject.name, MenuButtonPos(0.5f), startButtonSize,
            new Color(0.2f, 0.4f, 0.8f), new Color(0.4f, 0.7f, 1f));
        backButton = new HoldButton("BackButton_" + gameObject.name,
            initialPos + initialRot * (startButtonLocalOffset + backButtonLocalOffset), startButtonSize,
            new Color(0.5f, 0.5f, 0.5f), new Color(1f, 1f, 1f));
        backButton.SetActive(false);
    }

    /// <summary>オープニング画面に入る：リングとリセットボタンを隠し、スタートボタンを表示する</summary>
    void EnterTitle()
    {
        state = GameState.Title;
        startHoldTimer = 0f;
        if (lr != null) lr.enabled = false;
        if (galleryRoot != null) galleryRoot.SetActive(false);
        if (recordButton != null) recordButton.SetActive(true);
        SetTextLayout(false);
        if (backButton != null) backButton.SetActive(false);
        if (resetButtonTransform != null) resetButtonTransform.gameObject.SetActive(false);
        if (startButtonTransform != null) startButtonTransform.gameObject.SetActive(true);
        UpdateStatusText();
    }

    /// <summary>スタートボタンにstartHoldSeconds秒触れ続けたらゲーム開始</summary>
    void HandleStartButton()
    {
        if (startButtonTransform == null)
        {
            // ボタンが生成できなかった場合でもゲームが進められるようにする
            StartGame();
            return;
        }

        bool touching = false;
        if (tipTransform != null)
        {
            float dist = Vector3.Distance(tipTransform.position, startButtonTransform.position);
            touching = dist <= startButtonRadius;
        }

        if (touching) startHoldTimer += Time.deltaTime;
        else startHoldTimer = Mathf.Max(0f, startHoldTimer - Time.deltaTime * 2f);

        if (startButtonRenderer != null)
        {
            float progress = Mathf.Clamp01(startHoldTimer / startHoldSeconds);
            startButtonRenderer.material.color = Color.Lerp(startButtonColorIdle, startButtonColorHeld, progress);
        }

        if (startHoldTimer >= startHoldSeconds)
        {
            startHoldTimer = 0f;
            StartGame();
        }
    }

    void StartGame()
    {
        if (startButtonTransform != null) startButtonTransform.gameObject.SetActive(false);
        if (resetButtonTransform != null) resetButtonTransform.gameObject.SetActive(true);
        if (lr != null) lr.enabled = true;
        if (galleryRoot != null) galleryRoot.SetActive(false);
        if (recordButton != null) recordButton.SetActive(false);
        if (backButton != null) backButton.SetActive(false);
        score = 0;
        ResetRingProgress();
        state = GameState.Waiting;
        SetColor(colorNormal);
        UpdateStatusText();
    }

/// <summary>メニューボタンの位置。side=-0.5でスタート（左）、+0.5で記録（右）</summary>
    Vector3 MenuButtonPos(float side)
    {
        return initialPos + initialRot * (startButtonLocalOffset + new Vector3(menuButtonGap * side, 0f, 0f));
    }

    /// <summary>記録画面に入る：メニューボタンを隠し、捕まえたトンボ一覧と「もどる」ボタンを出す</summary>
    void EnterRecords()
    {
        state = GameState.Records;
        startHoldTimer = 0f;
        if (startButtonTransform != null) startButtonTransform.gameObject.SetActive(false);
        if (recordButton != null) recordButton.SetActive(false);
        if (backButton != null) backButton.SetActive(true);
        SetTextLayout(true);
        BuildGallery();
        UpdateStatusText();
    }

/// <summary>ステータステキストの配置を切り替える。記録画面では左側に寄せて幅を狭め、右側のトンボ一覧と重ならないようにする</summary>
    void SetTextLayout(bool recordsLayout)
    {
        if (uiText == null) return;
        RectTransform rt = uiText.rectTransform;
        if (recordsLayout)
        {
            rt.anchoredPosition = new Vector2(recordsTextLocalX, textLocalY);
            rt.sizeDelta = new Vector2(recordsTextWidth, 350f);
        }
        else
        {
            rt.anchoredPosition = new Vector2(textLocalX, textLocalY);
            rt.sizeDelta = new Vector2(700f, 250f);
        }
    }



// ===== 捕まえたトンボの記録 =====

    /// <summary>円をクリアした瞬間に、今のトンボの色・大きさを記録する</summary>
void RecordCaught()
    {
        if (tonboInstance == null || !tonboRunning) return;

        float size = currentTonboScale * tonboModelSpanMeters;
        int count = PlayerPrefs.GetInt(KeyCount, 0);
        float prevMax = PlayerPrefs.GetFloat(KeyMax, size);
        float prevMin = PlayerPrefs.GetFloat(KeyMin, size);
        string colorStr = (currentVariantColors.Count == 0) ? "" : EncodeVariant(currentVariantColors);

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

        if (currentVariantColors.Count == 0)
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
void BuildGallery()
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
        galleryRoot.transform.SetPositionAndRotation(initialPos + initialRot * recordsGalleryLocalOffset, initialRot);

        // 「もどる」ボタンの説明（ボタンの右隣）
        Vector3 backLocal = startButtonLocalOffset + backButtonLocalOffset - recordsGalleryLocalOffset;
        AddGalleryLabel(string.Format("球に{0}秒触れてもどる", startHoldSeconds), backLocal + new Vector3(0.2f, 0f, 0f), 1f);

        if (count == 0)
        {
            AddGalleryLabel("まだトンボを捕まえていません", new Vector3(0f, 0f, 0.1f), 1.3f);
            return;
        }

        AddGalleryLabel(string.Format("捕まえた数 {0}匹", count), new Vector3(0f, 0f, recordsCountLocalZ), 1.3f);
        if (tonboPrefab == null) return;

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

        GameObject go = Instantiate(tonboPrefab, galleryRoot.transform);
        go.transform.localPosition = new Vector3(x, 0f, z);
        go.transform.rotation = GalleryModelRotation(go.transform.position); // プレイヤーの方（正面）を向ける
        go.transform.localScale = Vector3.one * galleryTonboScale;
        if (colors != null) TintModel(go, colors);
    }

    /// <summary>最大・最小の個体を、実際の大きさの比（ゲーム中の倍率）で表示し、下にラベルを付ける</summary>
void AddHighlightModel(Vector3 localPos, float sizeMeters, string colorStr, string label)
    {
        float baseSize = tonboScale * tonboModelSpanMeters;
        float mul = baseSize > 0f ? sizeMeters / baseSize : 1f;

        GameObject go = Instantiate(tonboPrefab, galleryRoot.transform);
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
        lg.transform.rotation = Quaternion.LookRotation(-DirToHead(lg.transform.position), Vector3.up);

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        TextMesh tm = lg.AddComponent<TextMesh>();
        tm.font = font;
        tm.text = text;
        tm.fontSize = 64;
        tm.characterSize = recordsLabelCharSize * sizeMul;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = Color.white;
        lg.GetComponent<MeshRenderer>().sharedMaterial = font.material;
    }

    /// <summary>指定位置から頭（プレイヤー）へ向かう水平方向の単位ベクトル</summary>
    Vector3 DirToHead(Vector3 worldPos)
    {
        Transform head = headTransform;
        if (head == null && Camera.main != null) head = Camera.main.transform;
        Vector3 d = (head != null) ? head.position - worldPos : Vector3.back;
        d = Vector3.ProjectOnPlane(d, Vector3.up);
        if (d.sqrMagnitude < 0.0001f) d = Vector3.back;
        return d.normalized;
    }

    /// <summary>トンボをプレイヤーに向け、galleryViewYaw/Pitchぶんだけ傾けた向き</summary>
    Quaternion GalleryModelRotation(Vector3 worldPos)
    {
        return Quaternion.LookRotation(DirToHead(worldPos), Vector3.up) * Quaternion.Euler(galleryViewPitch, galleryViewYaw, 0f);
    }

    /// <summary>元のマテリアルの出現順に、保存しておいた色を割り当てる（ApplyTonboColorsと同じ対応）</summary>
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



    void Update()
    {
        // リセットボタンは状態に関係なく常にチェックする（いつでも強制リセット可能）
        // 記録画面：「もどる」ボタンに触れ続けたらオープニング画面へ
        if (state == GameState.Records)
        {
            if (backButton != null && backButton.Tick(tipTransform, startButtonRadius, startHoldSeconds)) EnterTitle();
            UpdateStatusText();
            return;
        }

        // オープニング画面中はスタートボタンと記録ボタンだけを処理する
        if (state == GameState.Title)
        {
            HandleStartButton();
            if (state == GameState.Title && recordButton != null && recordButton.Tick(tipTransform, startButtonRadius, startHoldSeconds)) EnterRecords();
            UpdateStatusText();
            return;
        }

        HandleResetButton();
        UpdateTonbo();

        if (state == GameState.Cleared)
        {
            if (Time.time >= flashUntil) SetColor(colorClear);
            if (Time.time >= nextSpawnTime) SpawnNextRing();
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
                        OnRingCleared();
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
        // メニューボタン：ゲームを中断して初期状態に戻し、オープニング（メニュー）画面へ戻る
        score = 0;
        HideTonbo();
        transform.SetPositionAndRotation(initialPos, initialRot);
        lastCenter = initialPos;
        UpdateLinePositions();
        ResetRingProgress();
        SetColor(colorNormal);
        EnterTitle();
    }

void ResetRingProgress()
    {
        lastAngle = float.NaN;
        cumulativeAngle = 0f;
        rotationsCounted = 0;
        sessionStartTime = float.NaN;
        rotationTimestamps.Clear();
        burstQualifiedLastFrame = false;
        burstCount = 0;
        flashUntil = -1f;
    }

    void OnRingCleared()
    {
        score++;
        RecordCaught();
        HideTonbo();
        if (score > bestScore) bestScore = score;
        state = GameState.Cleared;
        flashUntil = Time.time + 0.3f;
        nextSpawnTime = Time.time + nextRingDelay;
    }

void StartTonbo()
    {
        if (tonboPrefab == null) return;

        // プレイヤー側の向き（リングの法線のうち頭がある側）を求める
        Transform head = headTransform;
        if (head == null && Camera.main != null) head = Camera.main.transform;
        // 円の角度（左右でY軸回転する）には依存せず、最初のリングの向きを基準にした固定方向にする
        Vector3 normal = initialRot * Vector3.up;
        Vector3 toPlayer = normal;
        if (head != null && Vector3.Dot(head.position - initialPos, normal) < 0f) toPlayer = -normal;

        // 円の奥（プレイヤーと反対側）から円の中心へ向かって飛んでくる
        // 水平方向だけにして、真っ直ぐ（上下に傾かず）飛んでくるようにする
        Vector3 flat = Vector3.ProjectOnPlane(toPlayer, Vector3.up);
        if (flat.sqrMagnitude > 0.01f) toPlayer = flat.normalized;

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
        tonboRunning = true;
    }

    void HideTonbo()
    {
        tonboRunning = false;
        if (tonboInstance != null) tonboInstance.SetActive(false);
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


    void UpdateTonbo()
    {
        if (tonboPrefab == null) return;

        if (state == GameState.Playing && !tonboRunning && !float.IsNaN(sessionStartTime))
        {
            StartTonbo();
        }
        if (!tonboRunning || tonboInstance == null) return;

        if (state == GameState.Playing && !float.IsNaN(sessionStartTime))
        {
            // 制限時間いっぱいかけて円の中心に到達する
            float t = Mathf.Clamp01((Time.time - sessionStartTime) / sessionTimeLimit);
            Vector3 dir = (tonboEnd - tonboStart).normalized;
            Vector3 side = Vector3.Cross(dir, Vector3.up).normalized;
            float w = tonboWobble * (1f - t);
            Vector3 pos = Vector3.Lerp(tonboStart, tonboEnd, t)
                        + side * Mathf.Sin(Time.time * 4f) * w
                        + Vector3.up * Mathf.Sin(Time.time * 5.3f) * w;
            tonboInstance.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(dir, Vector3.up));
        }
        else if (state == GameState.Failed)
        {
            // ゲームオーバー時は円の中心に止まる
            tonboInstance.transform.position = tonboEnd;
        }
    }


    void SpawnNextRing()
    {
        Vector3 center;
        Quaternion rot;
        ChooseSpawnPose(out center, out rot);

        transform.SetPositionAndRotation(center, rot);
        lastCenter = center;
        UpdateLinePositions();
        ResetRingProgress();
        SetColor(colorNormal);

        if (timerStartsOnSpawn)
        {
            state = GameState.Playing;
            sessionStartTime = Time.time;
        }
        else
        {
            state = GameState.Waiting;
        }
    }

    /// <summary>
    /// 肩を中心に、手の届く範囲（前方）からランダムにリング位置を選ぶ。
    /// リングは常にプレイヤー側を向ける（ローカルYが法線＝頭の方向、ローカルZが上方向）。
    /// </summary>
void ChooseSpawnPose(out Vector3 center, out Quaternion rot)
    {
        // プレイヤー（頭）を中心にした円弧上で左右に動かし、左右位置に応じてリングをY軸回転させる。
        // 距離（奥行き）は初期位置のまま、リングは常にプレイヤーの方を向く。
        rot = initialRot;
        center = initialPos;

        Transform head = headTransform;
        if (head == null && Camera.main != null) head = Camera.main.transform;
        bool hasHead = head != null;

        Vector3 pivot = hasHead ? new Vector3(head.position.x, initialPos.y, head.position.z) : initialPos;
        Vector3 toRing = initialPos - pivot;
        float r = new Vector2(toRing.x, toRing.z).magnitude;

        float avoidReset = ringRadius + thicknesHalf + resetButtonRadius + 0.05f;
        float maxFromShoulder = reachRadius - ringRadius - thicknesHalf;

        Vector3 shoulder = Vector3.zero;
        if (hasHead)
        {
            Quaternion headYaw = Quaternion.Euler(0f, head.eulerAngles.y, 0f);
            shoulder = head.position + headYaw * new Vector3(shoulderOffsetRight, -shoulderOffsetDown, 0f);
        }

        for (int i = 0; i < 30; i++)
        {
            float x = Random.Range(-spawnRangeRight, spawnRangeRight);
            float z = Random.Range(-spawnRangeDown, spawnRangeUp);

            Vector3 c;
            Quaternion yawRot = Quaternion.identity;
            if (hasHead && r > 0.05f)
            {
                // 左右の移動量(x)を円弧の長さとみなして角度に変換
                float thetaDeg = (x / r) * Mathf.Rad2Deg;
                yawRot = Quaternion.Euler(0f, thetaDeg, 0f);
                c = pivot + yawRot * (toRing + initialRot * new Vector3(0f, 0f, z));
            }
            else
            {
                c = initialPos + initialRot * new Vector3(x, 0f, z);
            }

            center = c;
            rot = yawRot * initialRot;

            if (!IsValidSpawn(c, avoidReset)) continue;
            if (hasHead && Vector3.Distance(c, shoulder) > maxFromShoulder) continue;
            break;
        }
    }

    bool IsValidSpawn(Vector3 c, float avoidReset)
    {
        if (Vector3.Distance(c, lastCenter) < minSpawnSeparation) return false;
        if (resetButtonTransform != null && Vector3.Distance(c, resetButtonTransform.position) < avoidReset) return false;
        return true;
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
        c.a = ringAlpha;
        lr.startColor = c;
        lr.endColor = c;
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
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(textLocalX, textLocalY);
        rt.sizeDelta = new Vector2(700f, 250f);

        uiText = textGO.AddComponent<Text>();
        uiText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        uiText.fontSize = textFontSize;
        uiText.alignment = TextAnchor.MiddleCenter;
        uiText.color = Color.white;
        uiText.fontStyle = FontStyle.Bold;
        uiText.text = "";
    }

void UpdateStatusText()
    {
        if (uiText == null) return;

        switch (state)
        {
            case GameState.Records:
                uiText.text = ""; // 記録画面の情報は3D側のラベルで表示する
                break;

            case GameState.Title:
                uiText.color = Color.white;
                uiText.text = string.Format(
                    "{0}\n\n緑の球（左）: スタート　青い球（右）: 記録\n{1}秒触れ続けると選択",
                    gameTitle, startHoldSeconds);
                break;

            case GameState.Waiting:
                uiText.color = Color.white;
                uiText.text = string.Format(
                    "リングをなぞって開始\n({0}秒以内に、1秒間に{1}回転を{2}回)\nスコア: {3}",
                    sessionTimeLimit, rotationsPerBurst, requiredBursts, score);
                break;

            case GameState.Playing:
                float remaining = Mathf.Max(0f, sessionTimeLimit - (Time.time - sessionStartTime));
                uiText.color = Color.white;
                uiText.text = string.Format(
                    "残り {0:F1}秒   スコア: {3}\nバースト: {1} / {2}",
                    remaining, burstCount, requiredBursts, score);
                break;

            case GameState.Cleared:
                uiText.color = colorClear;
                uiText.text = string.Format("クリア！\nスコア: {0}", score);
                break;

            case GameState.Failed:
                uiText.color = colorFail;
                uiText.text = string.Format(
                    "ゲームオーバー\nスコア: {0}（ベスト {1}）\nメニューボタンに{2}秒触れてメニューへ",
                    score, bestScore, resetHoldSeconds);
                break;
        }
    }
}
