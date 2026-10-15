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
/// tipTransformをstartHoldSeconds秒触れ続けると、状態（Waiting/Playing/Cleared/Failed）に
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
    private int segments = 64;
    [Tooltip("円の不透明度（0=完全に透明、1=不透明）。小さいほど奥のトンボが見やすい")]
    [Range(0f, 1f)]
    private float ringAlpha = 0.3f;

    [Header("なぞり判定設定（この範囲内にいる間だけ回転をカウントする）")]
    private float thicknesHalf = 0.07f;
    private float depthTolerance = 0.08f;

    [Header("クリア条件")]
    [Tooltip("挑戦1回あたりの制限時間（秒）")]
    public float sessionTimeLimit = 7f;
    [Tooltip("「速い回転」と判定する時間窓（秒）")]
    public float burstWindowSeconds = 1f;
    [Tooltip("burstWindowSeconds以内に必要な回転数")]
    public int rotationsPerBurst = 4;
    [Tooltip("クリアに必要なバースト達成回数")]
    public int requiredBursts = 3;


    [Tooltip("スタートボタンの判定半径（この距離以内なら触れているとみなす）")]
    private float startButtonRadius = 0.09f;
    [Tooltip("この秒数だけ触れ続けたらゲーム開始")]
    public float startHoldSeconds = 2f;

    [Header("記録（オープニング画面に表示。PlayerPrefsに保存され、アプリを閉じても残る）")]
    [Tooltip("トンボの実寸の目安：Prefabの倍率1あたりの翼長（メートル）。元モデルは約3.9m")]
    private float tonboModelSpanMeters = 3.9f;
    [Tooltip("保存する色違いの最大種類数（超えたら古いものから消える）")]
    private int maxStoredVariants = 10;
    [Tooltip("記録画面：トンボ一覧の列数（超えたら下の段に折り返す）")]
    private int galleryColumns = 3;
    [Tooltip("記録画面：文字（左側）の横位置（Canvas上の座標。0が中央、マイナスで左）")]
    private float recordsTextLocalX = -180f;
    [Tooltip("記録画面：文字の幅（Canvas上の座標。一覧と重ならないよう狭める）")]
    private float recordsTextWidth = 450f;
    [Tooltip("一覧のトンボ同士の間隔（メートル）")]
    private float gallerySpacing = 0.09f;
    [Tooltip("一覧のトンボの大きさ（Prefab倍率）")]
    private float galleryTonboScale = 0.02f;
    [Tooltip("記録画面：最大・最小のトンボを並べる段の高さ（一覧の中心からのローカルZ、メートル）")]
    private float recordsHighlightLocalZ = 0.13f;
    [Tooltip("記録画面：最大と最小のトンボの左右の間隔（メートル）")]
    private float recordsHighlightSpacing = 0.22f;
    [Tooltip("記録画面：色違い一覧の中心の高さ（一覧の中心からのローカルZ、メートル）")]
    private float recordsGridLocalZ = -0.12f;
    [Tooltip("記録画面：最大・最小ラベルの文字の大きさ（TextMeshのcharacterSize）")]
    private float recordsLabelCharSize = 0.003f;
    [Tooltip("記録画面：「捕まえた数」ラベルの高さ（ローカルZ、メートル）")]
    private float recordsCountLocalZ = 0.26f;
    [Tooltip("記録画面：「つかまえた色」見出しの高さ（ローカルZ、メートル）")]
    private float recordsColorsHeaderLocalZ = -0.01f;
    [Tooltip("記録画面：トンボをプレイヤーに向けたうえで、さらに横へ回す角度（度）。0で真正面")]
    private float galleryViewYaw = 30f;
    [Tooltip("記録画面：トンボをプレイヤーに向けたうえで、頭を下げる角度（度）。上から見える分、胴体の色が分かりやすい")]
    private float galleryViewPitch = 20f;




    [Header("メニューボタン（横長キューブ）")]
    [Tooltip("キューブの幅（メートル）")]
    public float menuButtonWidth = 0.34f;
    [Tooltip("キューブの高さ（メートル）")]
    public float menuButtonHeight = 0.1f;
    [Tooltip("縦に並べたボタンの間隔（メートル）。高さより少し大きくすること")]
    public float menuRowSpacing = 0.14f;
    private HoldButton startButton;
    private HoldButton gameOverButton;
    private bool ready;
    [Tooltip("起動時にプレイヤーの頭をこの高さ（メートル）にあるものとして、ゲーム全体を置く。全体の高さを上下したいときはこの値を変える")]
    public float referenceHeadHeight = 1.3f;


    [Header("メニュー配置（X=右、Y=上、Z=奥。プレイヤーから遠ざかる向きが＋）")]
    [Tooltip("メニューボタン全体の位置。リングの初期位置からのずれ（メートル）")]
    public Vector3 menuOffset = new Vector3(0f, 0.551f, -0.19f);
    [Tooltip("「もどる」ボタンの位置。メニュー中心からのずれ")]
    public Vector3 backButtonOffset = new Vector3(0f, 0.959f, 0.413f);
    [Tooltip("ゲームオーバー時の「メニュー」ボタンの位置。メニュー中心からのずれ")]
    public Vector3 gameOverButtonOffset = new Vector3(0f, -0.3f, 0f);
    [Tooltip("設定画面（スライダー）の位置。Menu Offsetと同じ数値にすると、メニューと同じ高さ・奥行きになる（リングの初期位置からのずれ）")]
    public Vector3 settingsPanelOffset = new Vector3(0f, 0.551f, -0.19f);
    [Tooltip("記録画面（トンボ一覧）の位置。リングの初期位置からのずれ")]
    public Vector3 recordsGalleryOffset = new Vector3(0.3f, 0.038f, 0.032f);
    [Tooltip("ボタンが反応した（長押し完了）ときに鳴らす音")]
    public AudioClip buttonClip;



    [Header("表示設定")]
    public float textLocalX = 0f;
    public float textLocalY = -80f;
    private int textFontSize = 36;

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
    private float minSpawnSeparation = 0.2f;
    [Tooltip("クリア後、次のリングが出るまでの待ち時間（秒）")]
    public float nextRingDelay = 1f;
    [Tooltip("true: リング出現と同時に制限時間スタート / false: なぞり始めてからスタート")]
    private bool timerStartsOnSpawn = true;

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

    private LineRenderer lr;
    private AudioSource audioSource;
    private Text uiText;

    private static readonly Color colorNormal = Color.white;

    private static readonly Color colorTouching = new Color(0.3f, 0.7f, 1f); // リングに触れている間
    private static readonly Color colorBurst = new Color(1f, 0.84f, 0f);   // バースト達成の瞬間
    private static readonly Color colorClear = new Color(0.2f, 1f, 0.4f);  // クリア
    private static readonly Color colorFail = new Color(1f, 0.3f, 0.3f);  // 失敗


    private enum GameState { Title, Settings, ChooseType, Records, Waiting, Playing, Cleared, Failed }
    private GameState state = GameState.Title;
    private GameObject titleLogo;
    private TonboStakes stakes; // 杭の生成・配置・プレイヤー前進は TonboStakes.cs に分離
    private bool ringHiddenForLanding = false;
    [Header("着地演出")]
    [Tooltip("トンボが杭に向かって飛んで着地するまでの秒数（着地するまでなぞり判定は始まらない）")]
    public float tonboFlightSeconds = 2f;
    private float tonboFlightStartTime = 0f;

    private float lastAngle = float.NaN;
    private float cumulativeAngle = 0f;
    private int rotationsCounted = 0;
    private float sessionStartTime = float.NaN;

    private readonly Queue<float> rotationTimestamps = new Queue<float>();
    private bool burstQualifiedLastFrame = false;
    private int burstCount = 0;

    // リセットボタン関連
    private float resetHoldTimer = 0f;

    // スタートボタン関連
    private Transform startButtonTransform;
    private float startHoldTimer = 0f;

    // 記録関連
    private string KeyCount { get { return "Tonbo_Count" + Sfx; } }
    private string KeyNormal { get { return "Tonbo_NormalCount" + Sfx; } }
    private string KeyMax { get { return "Tonbo_MaxSize" + Sfx; } }
    private string KeyMin { get { return "Tonbo_MinSize" + Sfx; } }
    private string KeyVariants { get { return "Tonbo_Variants" + Sfx; } }
    private string KeyMaxColors { get { return "Tonbo_MaxColors" + Sfx; } } // 最大個体の配色（空=通常色）
    private string KeyMinColors { get { return "Tonbo_MinColors" + Sfx; } } // 最小個体の配色（空=通常色）

    private float currentTonboScale = 0f;
    private readonly List<Color> currentVariantColors = new List<Color>();
    private GameObject galleryRoot;
    private HoldButton recordButton;
    private HoldButton backButton;

    
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
    public Vector3 InitialPos { get { return initialPos; } }
    public Quaternion InitialRot { get { return initialRot; } }

    // ---- TonboController 向けの公開API ----
    public bool IsWaiting { get { return state == GameState.Waiting; } }
    public bool IsPlaying { get { return state == GameState.Playing; } }
    public bool IsFailed { get { return state == GameState.Failed; } }
    /// <summary>トンボが着地したら呼ぶ。なぞり判定とカウントを開始する</summary>
    public void BeginPlaying() { state = GameState.Playing; sessionStartTime = Time.time; }

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

    System.Collections.IEnumerator Start()
    {
        stakes = GetComponent<TonboStakes>();
        if (stakes == null)
        {
            Debug.LogWarning("[RotationBurstGame] TonboStakes が見つからないため、初期値で追加します。Inspectorでアタッチして値を設定してください。");
            stakes = gameObject.AddComponent<TonboStakes>();
        }
        // 頭の位置が取れるまで待ち、立ち位置・向き・身長に関係なく、毎回同じ位置関係でゲームを置く
        yield return AlignPlayerSpace();
        InitGame();
        ready = true;
        SubscribeRecenter();
    }

    // ---- 再センタリング対応 ----
    // Quest本体の「親指と人差し指をつまんで長押し」(位置リセット)をすると、トラッキング原点が動いて
    // ワールド固定のメニューが背後に回ってしまう。リセット直後にプレイヤー空間を合わせ直して、メニューを正面に戻す。
    private bool recenterSubscribed;
    private bool realigning;

    void SubscribeRecenter()
    {
        if (recenterSubscribed || OVRManager.display == null) return;
        OVRManager.display.RecenteredPose += OnRecentered;
        recenterSubscribed = true;
    }

    void OnDestroy()
    {
        if (recenterSubscribed && OVRManager.display != null)
            OVRManager.display.RecenteredPose -= OnRecentered;
        recenterSubscribed = false;
    }

    void OnRecentered()
    {
        if (!realigning && isActiveAndEnabled) StartCoroutine(RealignAfterRecenter());
    }

    System.Collections.IEnumerator RealignAfterRecenter()
    {
        realigning = true;
        yield return null; // 新しい原点でのトラッキングが反映されるのを1フレーム待つ
        yield return AlignPlayerSpace();
        realigning = false;
    }

    System.Collections.IEnumerator AlignPlayerSpace()
    {
        OVRCameraRig rig = null;
        Transform head = null;
        float t = 0f;
        while (t < 3f)
        {
            head = headTransform != null ? headTransform : (Camera.main != null ? Camera.main.transform : null);
            if (head != null)
            {
                if (rig == null) rig = head.GetComponentInParent<OVRCameraRig>();
                if (head.position.y > 0.3f) break; // 追跡が始まった
            }
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        if (rig == null || head == null || head.position.y <= 0.3f) yield break; // エディタなど追跡なし：そのまま

        yield return new WaitForSeconds(0.3f); // 追跡が落ち着くのを待つ

        // 頭の水平向きを+Zに、頭の位置をX=0・Z=0・高さreferenceHeadHeightに合わせる
        Vector3 f = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (f.sqrMagnitude > 1e-4f)
            rig.transform.RotateAround(head.position, Vector3.up, -Quaternion.LookRotation(f).eulerAngles.y);
        Vector3 p = head.position;
        rig.transform.position += new Vector3(-p.x, referenceHeadHeight - p.y, -p.z);
        if (stakes != null && stakes.PlayerAdvanced) rig.transform.position += stakes.ShiftVec;
    }

    void InitGame()
    {
        initialPos = transform.position;
        realPrefab = tonboPrefab;
        SetMode(PlayerPrefs.GetInt("Tonbo_CuteMode", 0) == 1);
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
        HoldButton.pressSource = audioSource;
        HoldButton.pressClip = buttonClip;


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
        // ゲームオーバー時だけ出る、横長キューブの「メニュー」ボタン
        gameOverButton = MakeButton("MenuButton_" + gameObject.name, MenuCenter() + Off(gameOverButtonOffset), "メニュー",
            new Color(0.5f, 0.5f, 0.5f), new Color(1f, 0.3f, 0.3f));
        gameOverButton.SetActive(false);
    }

void CreateStartButton()
    {
        startButton = MakeButton("StartButton_" + gameObject.name, MenuButtonPos(1f), "プレイ",
            new Color(0.2f, 0.6f, 0.3f), new Color(0.4f, 1f, 0.5f));
        startButtonTransform = startButton.t;
        recordButton = MakeButton("RecordButton_" + gameObject.name, MenuButtonPos(0f), "記録",
            new Color(0.2f, 0.4f, 0.8f), new Color(0.4f, 0.7f, 1f));
        backButton = MakeButton("BackButton_" + gameObject.name,
            MenuCenter() + Off(backButtonOffset), "もどる",
            new Color(0.5f, 0.5f, 0.5f), new Color(1f, 1f, 1f));
        backButton.SetActive(false);
    }

    /// <summary>頭の方を向いた横長キューブのボタンを作る</summary>
HoldButton MakeButton(string name, Vector3 pos, string label, Color idle, Color held)
    {
        // 向きは全ボタン共通（メニュー中心から頭への水平方向）にして、面を揃える
        Vector3 center = MenuCenter();
        Quaternion rot = Quaternion.LookRotation(-DirToHead(center), Vector3.up);
        return new HoldButton(name, pos, rot, new Vector2(menuButtonWidth, menuButtonHeight),
            label, recordsLabelCharSize * 1.3f, idle, held);
    }

    /// <summary>オープニング画面に入る：リングとリセットボタンを隠し、スタートボタンを表示する</summary>
    void EnterTitle()
    {
if (stakes != null) stakes.AdvancePlayer(false);
        state = GameState.Title;
        startHoldTimer = 0f;
        if (lr != null) lr.enabled = false;
        if (galleryRoot != null) galleryRoot.SetActive(false);
        if (recordButton != null) recordButton.SetActive(true);
        HideChoiceButtons();
        InitSettings();
        if (settingsButton != null) settingsButton.SetActive(true);
        SetTextLayout(false);
        if (backButton != null) backButton.SetActive(false);
        if (startButtonTransform != null) startButtonTransform.gameObject.SetActive(true);
        UpdateStatusText();
    }

    /// <summary>スタートボタンにstartHoldSeconds秒触れ続けたらゲーム開始</summary>
void HandleStartButton()
    {
        if (startButton == null)
        {
            // ボタンが生成できなかった場合でもゲームが進められるようにする
            EnterChoose();
            return;
        }
        if (startButton.Tick(tipTransform, startButtonRadius, startHoldSeconds)) EnterChoose();
    }

    void StartGame()
    {
        if (startButtonTransform != null) startButtonTransform.gameObject.SetActive(false);
        HideChoiceButtons();
        if (lr != null) lr.enabled = true;
        if (galleryRoot != null) galleryRoot.SetActive(false);
        if (recordButton != null) recordButton.SetActive(false);
        if (backButton != null) backButton.SetActive(false);
        if (stakes != null) stakes.AdvancePlayer(true);
        score = 0;
        ResetRingProgress();
        { Vector3 sc; Quaternion sr; stakes.ChooseStakePose(out sc, out sr); transform.SetPositionAndRotation(sc, sr); lastCenter = sc; UpdateLinePositions(); }
        state = GameState.Waiting;
        SetColor(colorNormal);
        UpdateStatusText();
    }

/// <summary>メニューボタンの位置。side=-0.5でスタート（左）、+0.5で記録（右）</summary>
/// <summary>メニュー座標のオフセットをワールドのずれに変換。X=右、Y=上、Z=奥（プレイヤーから遠ざかる向き）</summary>
    public Vector3 Off(Vector3 o)
    {
        Vector3 fwd = -DirToHead(initialPos);
        fwd.y = 0f;
        fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        return right * o.x + Vector3.up * o.y + fwd * o.z;
    }

    Vector3 MenuCenter()
    {
        return initialPos + Off(menuOffset);
    }

    Vector3 MenuButtonPos(float row)
    {
        // 行は必ずワールドの真上方向にずらす（リング面が傾いていても、全ボタンの奥行きが同じになる）
        Vector3 center = MenuCenter();
        return center + Vector3.up * (menuRowSpacing * row);
    }

    /// <summary>記録画面に入る：メニューボタンを隠し、捕まえたトンボ一覧と「もどる」ボタンを出す</summary>
    void EnterRecords()
    {
        state = GameState.Records;
        startHoldTimer = 0f;
        if (startButtonTransform != null) startButtonTransform.gameObject.SetActive(false);
        if (recordButton != null) recordButton.SetActive(false);
        if (backButton != null) backButton.SetActive(true);
        if (settingsButton != null) settingsButton.SetActive(false);
        SetTextLayout(true);
        ShowRecordModeButtons();
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
        // 傾けず、メニューボタンと同じ向きで立てる（文字もモデルも同じ面に並ぶ）。パネル内: X=右、Z=上
        Vector3 galleryPos = initialPos + Off(recordsGalleryOffset);
        galleryRoot.transform.SetPositionAndRotation(galleryPos, Quaternion.LookRotation(Vector3.up, DirToHead(galleryPos)));

        // 「もどる」ボタンの説明（ボタンの右隣）
        Vector3 backLocal = Quaternion.Inverse(initialRot) * (MenuCenter() + Off(backButtonOffset) - galleryRoot.transform.position);
        // （「もどる」はボタン自体にラベルあり）

        if (count == 0)
        {
            AddGalleryLabel("まだトンボを捕まえていません", new Vector3(0f, 0f, 0.1f), 1.3f);
            return;
        }

        AddGalleryLabel(string.Format("捕まえた数 {0}匹（{1}）", count, cuteMode ? "キュート" : "リアル"), new Vector3(0f, 0f, recordsCountLocalZ), 1.3f);
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
        lg.transform.rotation = Quaternion.LookRotation(-DirToHead(galleryRoot.transform.position), Vector3.up); // 全ラベル共通の向き

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
    public Vector3 DirToHead(Vector3 worldPos)
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
        return Quaternion.LookRotation(DirToHead(galleryRoot.transform.position), Vector3.up) * Quaternion.Euler(galleryViewPitch, galleryViewYaw, 0f);
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



    // ===== トンボのタイプ選択（リアル / キュート） =====
    private GameObject realPrefab;
    private bool cuteMode = false;
    private HoldButton realButton;
    private HoldButton cuteButton;
    // 記録はタイプごとに別保存（色の数などがモデルで違うため）
    private string Sfx { get { return cuteMode ? "_cute" : ""; } }

    /// <summary>使うトンボのPrefabを切り替える。Tonbo_cuteもTonboと同じ処理（大きさ・色・記録）で動く</summary>
    void SetMode(bool cute)
    {
        cuteMode = cute && tonboCutePrefab != null;
        PlayerPrefs.SetInt("Tonbo_CuteMode", cuteMode ? 1 : 0);
        GameObject p = cuteMode ? tonboCutePrefab : realPrefab;
        if (p == tonboPrefab) return;

        HideTonbo();
        if (tonboInstance != null) Destroy(tonboInstance);
        tonboInstance = null;
        tonboRenderers = null;
        tonboOriginalMats = null;
        foreach (var m in tonboTempMats) if (m != null) Destroy(m);
        tonboTempMats.Clear();
        tonboPrefab = p;
    }

    /// <summary>スタート球を選んだ後に出る、タイプ選択画面（リアル=左 / キュート=右）</summary>
void EnterChoose()
    {
        state = GameState.ChooseType;
        startHoldTimer = 0f;
        if (startButtonTransform != null) startButtonTransform.gameObject.SetActive(false);
        if (recordButton != null) recordButton.SetActive(false);
        if (settingsButton != null) settingsButton.SetActive(false);
        if (realButton == null)
        {
            realButton = MakeButton("RealButton_" + gameObject.name, MenuButtonPos(0.5f), "リアルなトンボ",
                new Color(0.55f, 0.35f, 0.15f), new Color(1f, 0.7f, 0.3f));
            cuteButton = MakeButton("CuteButton_" + gameObject.name, MenuButtonPos(-0.5f), "可愛いトンボ",
                new Color(0.95f, 0.5f, 0.7f), new Color(1f, 0.85f, 0.92f));
        }
        realButton.SetActive(true);
        cuteButton.SetActive(true);
        UpdateStatusText();
    }

void HideChoiceButtons()
    {
        if (settingsButton != null) settingsButton.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(false);
        if (realButton != null) realButton.SetActive(false);
        if (cuteButton != null) cuteButton.SetActive(false);
        if (recReal != null) recReal.SetActive(false);
        if (recCute != null) recCute.SetActive(false);
    }

    // ===== 記録画面：リアル／可愛いの記録切り替え =====
    private HoldButton recReal;
    private HoldButton recCute;

    void ShowRecordModeButtons()
    {
        if (recReal == null)
        {
            recReal = MakeButton("RecordRealButton_" + gameObject.name, MenuButtonPos(0.5f), "リアルの記録",
                new Color(0.55f, 0.35f, 0.15f), new Color(1f, 0.7f, 0.3f));
            recCute = MakeButton("RecordCuteButton_" + gameObject.name, MenuButtonPos(-0.5f), "可愛いの記録",
                new Color(0.95f, 0.5f, 0.7f), new Color(1f, 0.85f, 0.92f));
        }
        recReal.SetActive(true);
        recCute.SetActive(true);
    }

    // ===== 設定画面（BGM・効果音・画面の明るさを、棒の先端でスライドして調整） =====
    private HoldButton settingsButton;
    private SettingsPanel settingsPanel;

void InitSettings()
    {
        if (settingsPanel != null) return;
        GameObject b = GameObject.Find("BGM");
        AudioSource bgm = (b != null) ? b.GetComponent<AudioSource>() : null;
        Vector3 panelPos = initialPos + Off(settingsPanelOffset);
        Vector3 toPlayer = DirToHead(panelPos);
        toPlayer.y = 0f;
        toPlayer = toPlayer.sqrMagnitude > 1e-6f ? toPlayer.normalized : Vector3.back;
        // パネル内の座標: X=右、Y=手前（プレイヤー側）、Z=上。傾けず、メニューボタンと同じ向きで立てる
        Quaternion panelRot = Quaternion.LookRotation(Vector3.up, toPlayer);
        settingsPanel = new SettingsPanel(panelPos, panelRot, bgm, audioSource, recordsLabelCharSize,
            Quaternion.Inverse(panelRot) * (MenuCenter() + Off(backButtonOffset) - panelPos), "");
        settingsButton = MakeButton("SettingsButton_" + gameObject.name, MenuButtonPos(-1f), "設定",
            new Color(0.8f, 0.5f, 0.1f), new Color(1f, 0.8f, 0.4f));
        settingsPanel.SetActive(false);
    }

    void EnterSettings()
    {
        state = GameState.Settings;
        startHoldTimer = 0f;
        if (startButtonTransform != null) startButtonTransform.gameObject.SetActive(false);
        if (recordButton != null) recordButton.SetActive(false);
        if (settingsButton != null) settingsButton.SetActive(false);
        if (backButton != null) backButton.SetActive(true);
        if (settingsPanel != null) settingsPanel.SetActive(true);
        SetTextLayout(false);
        UpdateStatusText();
    }

    void Update()
    {
        // タイトル画面のときだけ Libel Spiral ロゴを表示する
        if (titleLogo == null) titleLogo = GameObject.Find("LibelSpiralLogo");
        if (titleLogo != null) { bool showLogo = (state == GameState.Title); if (titleLogo.activeSelf != showLogo) titleLogo.SetActive(showLogo); }
        if (ready && stakes != null) stakes.EnsureBuilt(); // 杭は最初から水辺に立てておく（常時表示）
        // トンボが杭に着地するまでは、リング（円）を表示しない
        if (lr != null)
        {
            bool waitLanding = (state == GameState.Waiting && tonboPrefab != null && !(tonboRunning && Time.time - tonboFlightStartTime >= tonboFlightSeconds));
            if (waitLanding) { if (lr.enabled) { lr.enabled = false; ringHiddenForLanding = true; } }
            else if (ringHiddenForLanding)
            {
                ringHiddenForLanding = false;
                if (state == GameState.Waiting || state == GameState.Playing) lr.enabled = true;
            }
        }
        if (!ready) return;
        // リセットボタンは状態に関係なく常にチェックする（いつでも強制リセット可能）
        // 記録画面：「もどる」ボタンに触れ続けたらオープニング画面へ
        if (state == GameState.Records)
        {
            // 記録の種類を切り替える（選んだタイプの記録とモデルで一覧を作り直す）
            if (recReal != null && recReal.Tick(tipTransform, startButtonRadius, startHoldSeconds)) { SetMode(false); BuildGallery(); }
            if (recCute != null && recCute.Tick(tipTransform, startButtonRadius, startHoldSeconds)) { SetMode(true); BuildGallery(); }
            if (backButton != null && backButton.Tick(tipTransform, startButtonRadius, startHoldSeconds)) EnterTitle();
            UpdateStatusText();
            return;
        }

        // 設定画面：スライダーを先端でなぞる。「もどる」球に触れ続けたら保存してオープニングへ
        if (state == GameState.Settings)
        {
            if (settingsPanel != null) settingsPanel.Tick(tipTransform);
            if (backButton != null && backButton.Tick(tipTransform, startButtonRadius, startHoldSeconds))
            {
                if (settingsPanel != null) settingsPanel.Save();
                EnterTitle();
            }
            UpdateStatusText();
            return;
        }

        // タイプ選択画面：リアル（左）かキュート（右）の球に触れ続けたら、そのタイプでゲーム開始
        if (state == GameState.ChooseType)
        {
            if (realButton != null && realButton.Tick(tipTransform, startButtonRadius, startHoldSeconds)) { SetMode(false); StartGame(); }
            else if (cuteButton != null && cuteButton.Tick(tipTransform, startButtonRadius, startHoldSeconds)) { SetMode(true); StartGame(); }
            UpdateStatusText();
            return;
        }

        // オープニング画面中はスタートボタンと記録ボタンだけを処理する
        if (state == GameState.Title)
        {
            HandleStartButton();
            if (state == GameState.Title && recordButton != null && recordButton.Tick(tipTransform, startButtonRadius, startHoldSeconds)) EnterRecords();
            if (state == GameState.Title && settingsButton != null && settingsButton.Tick(tipTransform, startButtonRadius, startHoldSeconds)) EnterSettings();
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

        // トンボが杭に着地するまでは、なぞり判定を始めない
        if (state == GameState.Waiting && tonboPrefab != null && !(tonboRunning && Time.time - tonboFlightStartTime >= tonboFlightSeconds))
        {
            lastAngle = float.NaN;
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
    /// 棒の先端がゲームオーバー時のメニューボタンにstartHoldSeconds秒触れ続けたら、状態を問わず強制リセットする。
    /// ボタンの色を押し込み進捗に応じて変化させ、触れていない間は徐々に進捗を戻す。
    /// </summary>
void HandleResetButton()
    {
        if (gameOverButton == null) return;
        bool show = (state == GameState.Failed);
        if (gameOverButton.t.gameObject.activeSelf != show) gameOverButton.SetActive(show);
        if (!show) return;
        if (gameOverButton.Tick(tipTransform, startButtonRadius, startHoldSeconds))
        {
            gameOverButton.SetActive(false);
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

        if ((state == GameState.Waiting || state == GameState.Playing) && !tonboRunning)
        {
            StartTonbo();
        }
        if (!tonboRunning || tonboInstance == null) return;

        // トンボが杭に着地したら（何回目でも）カウントスタート
        if (state == GameState.Waiting && Time.time - tonboFlightStartTime >= tonboFlightSeconds)
        {
            state = GameState.Playing;
            sessionStartTime = Time.time;
        }

        if (state == GameState.Waiting || state == GameState.Playing)
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
        stakes.ChooseStakePose(out center, out rot);

        transform.SetPositionAndRotation(center, rot);
        lastCenter = center;
        UpdateLinePositions();
        ResetRingProgress();
        SetColor(colorNormal);

        state = GameState.Waiting; // トンボが杭に止まったらカウントスタート（UpdateTonboで開始）
    }

    /// <summary>
    /// 肩を中心に、手の届く範囲（前方）からランダムにリング位置を選ぶ。
    /// リングは常にプレイヤー側を向ける（ローカルYが法線＝頭の方向、ローカルZが上方向）。
    /// </summary>
public void ChooseSpawnPose(out Vector3 center, out Quaternion rot)
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

            if (!IsValidSpawn(c)) continue;
            if (hasHead && Vector3.Distance(c, shoulder) > maxFromShoulder) continue;
            break;
        }
    }

    // ===== 杭（トンボが止まる場所）=====
    // 木の足場（Mesh_Wood）の上は杭を立てない




    // プレイ開始で前へ出る／タイトルに戻ると元の位置へ戻る






    /// <summary>ランダムな杭を1本選び、その先端（トンボが止まる位置）をリングの中心にする</summary>


    bool IsValidSpawn(Vector3 c)
    {
        if (Vector3.Distance(c, lastCenter) < minSpawnSeparation) return false;
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
            case GameState.Settings:
            case GameState.ChooseType:
            case GameState.Title:
                uiText.text = ""; // メニュー系の画面は、ボタン（横長キューブ）のラベルだけで案内する
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
                    "ゲームオーバー\nスコア: {0}（ベスト {1}）\nメニューのボタンに触れ続けてね",
                    score, bestScore);
                break;
        }
    }
}
