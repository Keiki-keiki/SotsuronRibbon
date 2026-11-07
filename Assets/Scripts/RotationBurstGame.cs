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
    [Tooltip("ハードモードの制限時間（秒）。ノーマルは上の sessionTimeLimit を使う")]
    public float hardSessionTimeLimit = 14f;
    /// <summary>現在のモードの制限時間（ハード=hardSessionTimeLimit / ノーマル=sessionTimeLimit）</summary>
    private float TimeLimit { get { return hardMode ? hardSessionTimeLimit : sessionTimeLimit; } }

    [Tooltip("「速い回転」と判定する時間窓（秒）")]
    public float burstWindowSeconds = 1f;
    [Tooltip("burstWindowSeconds以内に必要な回転数")]
    public int rotationsPerBurst = 4;
    [Tooltip("クリアに必要なバースト達成回数")]
    public int requiredBursts = 3;


    // ボタンの判定半径・長押し秒数は GameMenuUI に移動

    // 記録画面の文字配置（ギャラリー本体は RecordsGallery.cs）
    private float recordsTextLocalX = -180f;     // 記録画面：文字（左側）の横位置（Canvas上の座標。0が中央、マイナスで左）
    private float recordsTextWidth = 450f;       // 記録画面：文字の幅（Canvas上の座標。一覧と重ならないよう狭める）
    private float recordsLabelCharSize = 0.003f; // ラベル文字の大きさ（TextMeshのcharacterSize）。ボタンのラベルにも使う




    private GameMenuUI menu; // メニューのボタン・設定パネル・画面の切り替えは GameMenuUI.cs に分離
    private bool ready;
    [Header("プレイヤー位置")]
    [Tooltip("起動時にプレイヤーの頭をこの高さ（メートル）にあるものとして、ゲーム全体を置く。全体の高さを上下したいときはこの値を変える")]
    public float referenceHeadHeight = 1.3f;


    // メニュー配置（menuOffset等）とボタン音は GameMenuUI に、記録画面の位置は RecordsGallery.galleryOffset に移動



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

    // トンボ処理は TonboController.cs に分離

    private LineRenderer lr;
    private LineRenderer lr2; // ハードモード用の2つ目のリング（表示のみ。判定はまだ1つ目だけ）

    private AudioSource audioSource;
    private Text uiText;

    [Header("HUD（カメラ追従の表示）")]
    [Tooltip("HMDカメラの正面から、HUDまでの距離（メートル）")]
    public float hudDistance = 0.65f;
    public float hudWidth = 800f;
    public float hudHeight = 480f;
    [Tooltip("HUDキャンバスの縮尺（1px = この値メートル）")]
    public float hudScale = 0.001f;
    [Tooltip("HUD端からの余白（キャンバス上のpx）")]
    public float hudMargin = 40f;
    private Text hudTimeText;   // 左下：残り時間
    private Text hudScoreText;  // 右下：スコア表示用の領域（未実装・空のまま）
    private bool hudMode = false;
    private bool hudClearedPrev = false; // 表示専用：直前フレームがClearedだったか
    private bool hudCapturing = false;    // 表示専用：このClearedが虫かご演出つきか（Cleared突入時に1回だけ判定）



    private static readonly Color colorNormal = Color.white;

    private static readonly Color colorTouching = new Color(0.3f, 0.7f, 1f); // リングに触れている間
    private static readonly Color colorBurst = new Color(1f, 0.84f, 0f);   // バースト達成の瞬間
    private static readonly Color colorClear = new Color(0.2f, 1f, 0.4f);  // クリア
    private static readonly Color colorFail = new Color(1f, 0.3f, 0.3f);  // 失敗


    private enum GameState { Title, Settings, ChooseType, ChooseMode, Records, Waiting, Playing, Cleared, Failed }
    private GameState state = GameState.Title;
    private GameObject titleLogo;
    private TonboStakes stakes; // 杭の生成・配置・プレイヤー前進は TonboStakes.cs に分離
    private TonboController tonbo; // トンボの出現・飛行・色/大きさは TonboController.cs に分離
    private MushikagoController mushikago; // 虫籠演出は MushikagoController.cs に分離（未アタッチなら従来どおり）
    private RecordsGallery records; // 記録の保存とギャラリー表示は RecordsGallery.cs に分離
    private bool ringHiddenForLanding = false;
    // 着地演出の設定（tonboFlightSeconds等）は TonboController に移動

    private float lastAngle = float.NaN;
    private float cumulativeAngle = 0f;
    private int rotationsCounted = 0;
    private float sessionStartTime = float.NaN;

    private readonly Queue<float> rotationTimestamps = new Queue<float>();
    private bool burstQualifiedLastFrame = false;
    private int burstCount = 0;
    [Tooltip("ノーマル：速い回転（burstWindowSeconds以内にrotationsPerBurst回以上）を維持した累計秒数がこれに達したらクリア")]
    public float requiredFastSeconds = 5f;
    private float fastSeconds = 0f;
    [Tooltip("リング1：この秒数以内のはみ出しは、はみ出し判定にしない（なぞり続けているとみなす）")]
    public float outOfBoundsGrace = 0.2f;
    private float outOfBoundsSince = float.NaN;

    // ---- ハードモード用：リング2の独立した判定状態（リング1は上の既存フィールドをそのまま使う） ----
    private class RingJudge
    {
        public float lastAngle = float.NaN;
        public float cumulativeAngle = 0f;
        public int rotationsCounted = 0;
        public readonly Queue<float> rotationTimestamps = new Queue<float>();
        public bool burstQualifiedLastFrame = false;
        public int burstCount = 0;
        public float fastSeconds = 0f;
        public float outSince = float.NaN;
        public void Reset()
        {
            lastAngle = float.NaN; cumulativeAngle = 0f; rotationsCounted = 0;
            rotationTimestamps.Clear(); burstQualifiedLastFrame = false; burstCount = 0; fastSeconds = 0f; outSince = float.NaN;
        }
    }
    private readonly RingJudge ring2 = new RingJudge();
    // ハード：捕獲演出中にリングが成功した場合の保留フラグ（虫かごが空いた時点で気絶化＋捕獲を開始する）
    private bool pendingCatch1 = false;
    private bool pendingCatch2 = false;

    // ハード：捕獲演出中も制限時間（sessionStartTime基準）は止めない
    /// <summary>ハードモードで、両方のトンボが Stored になったか</summary>
    public bool AllTonboStored { get { return hardMode && Tonbo1State == TonboState.Stored && Tonbo2State == TonboState.Stored; } }


    // ---- ハードモード用：トンボごとの状態（ノーマルでは更新しない）。1=メインのトンボ、2=2匹目 ----
    public enum TonboState { Flying, Stunned, Stored }
    public TonboState Tonbo1State { get; private set; }
    public TonboState Tonbo2State { get; private set; }

    /// <summary>リング成功：Flying→Stunned（ハードモードのときだけ）</summary>
    void MarkTonboStunned(int tonboIndex)
    {
        if (!hardMode) return;
        if (tonboIndex == 1 && Tonbo1State == TonboState.Flying) Tonbo1State = TonboState.Stunned;
        else if (tonboIndex == 2 && Tonbo2State == TonboState.Flying) Tonbo2State = TonboState.Stunned;
    }

    /// <summary>虫かごに入れた：Stunned→Stored（ハードモードのときだけ。TonboCage から呼ぶ）</summary>
public void MarkTonboStored(int tonboIndex)
    {
        if (!hardMode) return;
        if (tonboIndex == 1 && Tonbo1State == TonboState.Stunned) { Tonbo1State = TonboState.Stored; AddScore(); } // ハード：1匹収納した瞬間に+1
        else if (tonboIndex == 2 && Tonbo2State == TonboState.Stunned) { Tonbo2State = TonboState.Stored; AddScore(); }
    }

/// <summary>捕まえた匹数を+1し、最高記録を更新・保存する</summary>
    void AddScore()
    {
        score++;
        if (score > bestScore) { bestScore = score; PlayerPrefs.SetInt(BestScoreKey, bestScore); PlayerPrefs.Save(); }
    }


    void ResetTonboStates()
    {
        Tonbo1State = TonboState.Flying;
        Tonbo2State = TonboState.Flying;
    }

    /// <summary>リング1のバースト達成数が必要数に達したか</summary>
    public bool Ring1Success { get { return fastSeconds >= requiredFastSeconds; } }
    /// <summary>リング2のバースト達成数が必要数に達したか（ハードモードのときだけ）</summary>
    public bool Ring2Success { get { return hardMode && ring2.fastSeconds >= requiredFastSeconds; } }
    /// <summary>両方のリングが成功したか（ハードモードのときだけ）</summary>
    public bool BothRingsSuccess { get { return Ring1Success && Ring2Success; } }


    // リセットボタン関連
    private float resetHoldTimer = 0f;

    // （スタートボタンは GameMenuUI 側）

    // 記録（PlayerPrefsのキー・ギャラリー・トンボの大きさ/色の参照）は RecordsGallery.cs に分離
    // （各ボタンは GameMenuUI 側）

    
    // （ギャラリー用マテリアルは RecordsGallery 側）
    // （ボタンの色は GameMenuUI 側）

    // ランダム出現・スコア関連
    private int score = 0;
    private int bestScore = 0;
    private const string BestScoreKey = "Tonbo_BestScore"; // 最高スコアのPlayerPrefsキー
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

    // ---- RecordsGallery 向けの公開API ----
    public bool CuteMode { get { return cuteMode; } }
    public float LabelCharSize { get { return recordsLabelCharSize; } }

    // ---- GameMenuUI 向けの公開API ----
    public AudioSource Audio { get { return audioSource; } }

    // トンボの実行時状態は TonboController に移動

    // デバッグ表示用（原因切り分け後に削除してよい）
    private float dbgLocalY = 0f;
    private float dbgDistFromRing = 0f;
    private bool dbgHasTip = false;
    private float flashUntil = -1f;
    [Header("ゲームオーバー")]
    [Tooltip("ゲームオーバーになってから、自動でメニューに戻るまでの時間（秒）")]
    public float gameOverReturnSeconds = 3f;
    private float failedTime = 0f;
    private float nextLogoSearch = 0f; // ロゴの検索を1秒に1回に制限する（GameObject.Findの毎フレーム呼び出し対策）


    System.Collections.IEnumerator Start()
    {
        stakes = GetComponent<TonboStakes>();
        if (stakes == null)
        {
            Debug.LogWarning("[RotationBurstGame] TonboStakes が見つからないため、初期値で追加します。Inspectorでアタッチして値を設定してください。");
            stakes = gameObject.AddComponent<TonboStakes>();
        }
        tonbo = GetComponent<TonboController>();
        if (tonbo == null)
        {
            Debug.LogWarning("[RotationBurstGame] TonboController が見つからないため、初期値で追加します。Inspectorでアタッチして値を設定してください。");
            tonbo = gameObject.AddComponent<TonboController>();
        }
        mushikago = GetComponent<MushikagoController>(); // 未アタッチでもOK（nullなら虫籠演出なし）
        records = GetComponent<RecordsGallery>();
        if (records == null)
        {
            Debug.LogWarning("[RotationBurstGame] RecordsGallery が見つからないため追加します。");
            records = gameObject.AddComponent<RecordsGallery>();
        }
        menu = GetComponent<GameMenuUI>();
        if (menu == null)
        {
            Debug.LogWarning("[RotationBurstGame] GameMenuUI が見つからないため、初期値で追加します。Inspectorでアタッチして値を設定してください。");
            menu = gameObject.AddComponent<GameMenuUI>();
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
        // リアル版Prefabの保持は TonboController 側で行う
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
        // ボタン音の設定は GameMenuUI.CreateButtons で行う


        UpdateLinePositions();
        SetColor(colorNormal);

        // テキストを先に生成しておく（ボタン生成で何か問題があってもテキストは必ず出るように）
        CreateStatusText();
        UpdateStatusText();

        menu.CreateButtons(); // 失敗してもステータステキストは出る（GameMenuUI内でtry-catch）

        EnterTitle();
    }





    /// <summary>オープニング画面に入る：リングを隠し、プレイ・記録・設定のボタンを表示する</summary>
void EnterTitle()
    {
        if (stakes != null) stakes.AdvancePlayer(false);
        state = GameState.Title;
        if (lr != null) lr.enabled = false;
        records.HideGallery();
        menu.Show(MenuScreen.Title);
        SetTextLayout(false);
        UpdateStatusText();
    }

    /// <summary>ゲーム開始：メニューを隠し、杭の上のリングで最初のトンボを待つ</summary>


void StartGame()
    {
        menu.Show(MenuScreen.None);
        if (lr != null) lr.enabled = true;
        records.HideGallery();
        if (stakes != null) stakes.AdvancePlayer(true);
        score = 0;
        bestScore = PlayerPrefs.GetInt(BestScoreKey, 0); // 保存済みの最高スコアを読み込む
        ResetRingProgress();
        { Vector3 sc; Quaternion sr; stakes.ChooseStakePose(out sc, out sr); transform.SetPositionAndRotation(sc, sr); lastCenter = sc; UpdateLinePositions(); }
        state = GameState.Waiting;
        SetColor(colorNormal);
        UpdateStatusText();
    }

/// <summary>メニュー座標のオフセットをワールドのずれに変換。X=右、Y=上、Z=奥（プレイヤーから遠ざかる向き）</summary>
    public Vector3 Off(Vector3 o)
    {
        Vector3 fwd = -DirToHead(initialPos);
        fwd.y = 0f;
        fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        return right * o.x + Vector3.up * o.y + fwd * o.z;
    }





    /// <summary>記録画面に入る：メニューボタンを隠し、捕まえたトンボ一覧と「もどる」ボタンを出す</summary>
void EnterRecords()
    {
        state = GameState.Records;
        bestScore = PlayerPrefs.GetInt(BestScoreKey, 0); // 記録画面に出す最高スコアを読み込む
        menu.Show(MenuScreen.Records);
        SetTextLayout(true);
        records.BuildGallery();
        UpdateStatusText();
    }

/// <summary>ステータステキストの配置を切り替える。記録画面では左側に寄せて幅を狭め、右側のトンボ一覧と重ならないようにする</summary>
void SetTextLayout(bool recordsLayout)
    {
        if (uiText == null || hudMode) return; // HUDはカメラ固定なので配置は切り替えない
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



// 記録の保存とギャラリー表示は RecordsGallery.cs に分離

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

    // ===== トンボのタイプ選択（リアル / キュート） =====
    // （リアル版Prefabの保持は TonboController 側）
    private bool cuteMode = false;
    // （タイプ選択ボタンは GameMenuUI 側）
    // 記録はタイプごとに別保存（色の数などがモデルで違うため）
    // （記録キーの接尾辞は RecordsGallery が CuteMode から決める）

    /// <summary>使うトンボのPrefabを切り替える。Tonbo_cuteもTonboと同じ処理（大きさ・色・記録）で動く</summary>
void SetMode(bool cute)
    {
        cuteMode = cute && tonbo.HasCutePrefab;
        PlayerPrefs.SetInt("Tonbo_CuteMode", cuteMode ? 1 : 0);
        tonbo.SelectPrefab(cuteMode);
    }

    /// <summary>スタート球を選んだ後に出る、タイプ選択画面（リアル=左 / キュート=右）</summary>
void EnterChoose()
    {
        state = GameState.ChooseType;
        menu.Show(MenuScreen.ChooseType);
        UpdateStatusText();
    }



    // （記録画面の切り替えボタンは GameMenuUI 側）

    // ===== ゲームモード選択（ノーマル / ハード（2匹）） =====
    // ハードの2匹出現は未実装。今は選択結果の保存だけ行い、どちらも1匹で開始する
    private const string HardModeKey = "Game_HardMode";
    private bool hardMode = false;
    public bool HardMode { get { return hardMode; } }

    void SetHardMode(bool hard)
    {
        hardMode = hard;
        PlayerPrefs.SetInt(HardModeKey, hardMode ? 1 : 0);
    }

    /// <summary>トンボのタイプを選んだ後に出る、ゲームモード選択画面（ノーマル=上 / ハード=下）</summary>
    void EnterChooseMode()
    {
        state = GameState.ChooseMode;
        menu.Show(MenuScreen.ChooseMode);
        UpdateStatusText();
    }




    // ===== 設定画面（BGM・効果音・画面の明るさを、棒の先端でスライドして調整） =====
    // （設定ボタン・設定パネルは GameMenuUI 側）



void EnterSettings()
    {
        state = GameState.Settings;
        menu.Show(MenuScreen.Settings);
        SetTextLayout(false);
        UpdateStatusText();
    }

    void Update()
    {
        // タイトル画面のときだけ Libel Spiral ロゴを表示する
        if (titleLogo == null && Time.time >= nextLogoSearch) { titleLogo = GameObject.Find("LibelSpiralLogo"); nextLogoSearch = Time.time + 1f; }
        if (titleLogo != null) { bool showLogo = (state == GameState.Title); if (titleLogo.activeSelf != showLogo) titleLogo.SetActive(showLogo); }
        if (ready && stakes != null) stakes.EnsureBuilt(); // 杭は最初から水辺に立てておく（常時表示）
        // トンボが杭に着地するまでは、リング（円）を表示しない
        if (lr != null)
        {
            bool waitLanding = (state == GameState.Waiting && tonbo.WaitingForLanding);
            if (waitLanding) { if (lr.enabled) { lr.enabled = false; ringHiddenForLanding = true; } }
            else if (ringHiddenForLanding)
            {
                ringHiddenForLanding = false;
                if (state == GameState.Waiting || state == GameState.Playing) lr.enabled = true;
            }
        }
        UpdateSecondRing();
        
if (!ready) return;
        // リセットボタンは状態に関係なく常にチェックする（いつでも強制リセット可能）
        // メニュー系の画面（記録・設定・タイプ選択・オープニング）は、ボタンの長押し操作だけを処理する
        if (state == GameState.Records)
        {
            // 記録の種類を切り替える（選んだタイプの記録とモデルで一覧を作り直す）。「もどる」でオープニングへ
            switch (menu.Tick(MenuScreen.Records, tipTransform))
            {
                case MenuAction.Real: SetMode(false); records.BuildGallery(); break;
                case MenuAction.Cute: SetMode(true); records.BuildGallery(); break;
                case MenuAction.Back: EnterTitle(); break;
            }
            UpdateStatusText();
            return;
        }

        // 設定画面：スライダーを先端でなぞる。「もどる」に触れ続けたら保存してオープニングへ
        if (state == GameState.Settings)
        {
            if (menu.Tick(MenuScreen.Settings, tipTransform) == MenuAction.Back) EnterTitle();
            UpdateStatusText();
            return;
        }

        // タイプ選択画面：リアルかキュートに触れ続けたら、そのタイプでゲーム開始
        if (state == GameState.ChooseType)
        {
            switch (menu.Tick(MenuScreen.ChooseType, tipTransform))
            {
                case MenuAction.Real: SetMode(false); EnterChooseMode(); break;
                case MenuAction.Cute: SetMode(true); EnterChooseMode(); break;
            }
            UpdateStatusText();
            return;
        }

        // ゲームモード選択画面：ノーマルかハードに触れ続けたら、そのモードでゲーム開始（ハードの2匹は未実装＝今は1匹）
        if (state == GameState.ChooseMode)
        {
            switch (menu.Tick(MenuScreen.ChooseMode, tipTransform))
            {
                case MenuAction.Normal: SetHardMode(false); StartGame(); break;
                case MenuAction.Hard: SetHardMode(true); StartGame(); break;
            }
            UpdateStatusText();
            return;
        }

        
// オープニング画面：プレイ・記録・設定のボタンだけを処理する
        if (state == GameState.Title)
        {
            switch (menu.Tick(MenuScreen.Title, tipTransform))
            {
                case MenuAction.Play: EnterChoose(); break;
                case MenuAction.Records: EnterRecords(); break;
                case MenuAction.Settings: EnterSettings(); break;
            }
            UpdateStatusText();
            return;
        }

        tonbo.Tick();
        if (mushikago != null && mushikago.IsBusy) mushikago.Tick(tipTransform); // 虫籠演出（終わるとIsBusyがfalseになる）
        bool cageFinished = true; // TonboCage.Tick の戻り値（演出終了=true。未使用・Idleのときも true）
        { TonboCage tonboCage = GetComponent<TonboCage>(); if (tonboCage != null && tonboCage.enabled) cageFinished = tonboCage.Tick(tipTransform); } // 手で掴む虫籠演出


        if (hardMode && state == GameState.Playing && AllTonboStored) { state = GameState.Cleared; flashUntil = Time.time + 0.3f; } // ハード：両方収納済みで初めてクリア

        
if (state == GameState.Cleared)
        {
            if (Time.time >= flashUntil) SetColor((!hardMode || Ring1Success) ? colorClear : (Ring1Touched() ? colorTouching : colorNormal)); // ハード：リング1の色はリング1の成功状態だけで決める
            if (HardTimeUp()) { FailByTimeout(); UpdateStatusText(); return; }
            if (hardMode && tipTransform != null) { TickSecondRing(); TickRing1WhileCleared(); } // ハード：捕獲演出中も、未成功（Flying）のリングだけ判定を続ける // ハード：捕獲演出中でも制限時間内に両方収納できなければゲームオーバー
            
if (cageFinished)
            {
                TonboCage cageForScore = GetComponent<TonboCage>();
                bool escaped = (cageForScore != null && cageForScore.enabled && cageForScore.Escaped);
                if (hardMode && !AllTonboStored && !escaped)
                {
                    // ハード：片方だけ収納済み。ゲーム続行（制限時間は止めない）
                    if (BeginPendingCatch()) { UpdateStatusText(); return; } // 捕獲演出中に成功していたリングがあれば、そのトンボの捕獲を続けて開始
                    lastAngle = float.NaN;
                    state = GameState.Playing;
                }
                else
                {
                    if (!escaped && !hardMode) AddScore(); // ノーマルのみここで+1（ハードは MarkTonboStored で収納ごとに加算済み）
                    HideTonbo(); SpawnNextRing();
                }
            }
            UpdateStatusText();
            return;
        }

        if (state == GameState.Failed)
        {
            if (Time.time - failedTime >= gameOverReturnSeconds) { ResetGame(); return; } // 一定時間後に自動でメニューへ（プレイヤー位置も初期位置に戻る）
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
        if (state == GameState.Waiting && tonbo.WaitingForLanding)
        {
            lastAngle = float.NaN;
            return;
        }

        TickSecondRing(); // ハードモードのときだけ、リング2を独立して判定する（リング1の処理は以下のまま）
        if (state == GameState.Cleared) { UpdateStatusText(); return; } // リング2成功で捕獲が始まったフレームは、リング1の判定をしない


        
if (hardMode && Tonbo1State != TonboState.Flying)
        {
            // ハード：トンボ1が気絶・収納済みなら、リング1は再判定しない（トンボ2のリング2は上のTickSecondRingで継続）
            lastAngle = float.NaN;
            if (Time.time >= flashUntil) SetColor(colorClear);
            CheckTimeout();
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
        if (outOfBounds)
        {
            // はみ出しの猶予：outOfBoundsGrace秒以内なら、まだリング上にいるものとして扱う
            if (float.IsNaN(outOfBoundsSince)) outOfBoundsSince = Time.time;
            if (Time.time - outOfBoundsSince <= outOfBoundsGrace) outOfBounds = false;
        }
        else outOfBoundsSince = float.NaN;

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
                if (burstQualifiedNow) fastSeconds += Time.deltaTime; // ノーマル：速い回転を保っている時間を累計
                if (burstQualifiedNow && !burstQualifiedLastFrame)
                {
                    burstCount++;
                    flashUntil = Time.time + 0.3f;
                    SetColor(colorBurst);
                    PlaySparkle();

                    // （クリア判定は累計秒数で行う：下の fastSeconds チェック）
                }
                burstQualifiedLastFrame = burstQualifiedNow;
                if (state == GameState.Playing && fastSeconds >= requiredFastSeconds) { OnRingCleared(); flashUntil = Time.time + 0.3f; }
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
    /// 棒の先端がゲームオーバー時のメニューボタンに触れ続けたら、強制リセットする（ボタンの実体は GameMenuUI）。
    /// ボタンの色を押し込み進捗に応じて変化させ、触れていない間は徐々に進捗を戻す。
    /// </summary>


void CheckTimeout()
    {
        if (state != GameState.Playing || float.IsNaN(sessionStartTime)) return;

        float elapsed = Time.time - sessionStartTime;
        // ノーマル：従来どおり / ハード：制限時間内に両方 Stored になっていなければ失敗
        bool unfinished = hardMode ? !AllTonboStored : (fastSeconds < requiredFastSeconds);
        if (elapsed >= TimeLimit && unfinished)
        {
            state = GameState.Failed;
            failedTime = Time.time;
        }
    }

/// <summary>ハードモード：制限時間を過ぎたのに、まだ両方 Stored になっていないか（捕獲演出中の判定用）</summary>
    bool HardTimeUp()
    {
        return hardMode && !AllTonboStored && !float.IsNaN(sessionStartTime)
            && Time.time - sessionStartTime >= TimeLimit;
    }

    /// <summary>捕獲演出中のタイムアップ：虫かごを消してゲームオーバーにする</summary>
    void FailByTimeout()
    {
        TonboCage cage = GetComponent<TonboCage>();
        if (cage != null && cage.enabled) cage.Abort();
        state = GameState.Failed;
        failedTime = Time.time;
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
        ring2.Reset(); pendingCatch1 = false; pendingCatch2 = false;
        ResetTonboStates();


        burstQualifiedLastFrame = false;
        burstCount = 0;
        fastSeconds = 0f;
        outOfBoundsSince = float.NaN;
        flashUntil = -1f;
    }

void OnRingCleared()
    {
        MarkTonboStunned(1); // ハードモード：リング1成功→トンボ1は Flying→Stunned
        records.RecordCaught();
        if (!TryBeginMushikago()) HideTonbo(); // 虫籠演出を始められないときは従来どおりトンボを隠す
        state = GameState.Cleared;
        flashUntil = Time.time + 0.3f;
        nextSpawnTime = Time.time + nextRingDelay;
    }



void HideTonbo()
    {
        if (tonbo != null) tonbo.Hide();
    }

/// <summary>虫籠演出を始める。始められなければ false を返す（呼び出し側が従来どおりトンボを隠す）</summary>
    bool TryBeginMushikago()
    {
        TonboCage cage = GetComponent<TonboCage>();
        Debug.Log("[TMP-DEBUG] TryBeginMushikago called: cage=" + (cage != null) + " enabled=" + (cage != null && cage.enabled) + " Available=" + (cage != null && cage.Available) + " prefab=" + (cage != null && cage.cagePrefab != null) + " clip=" + (cage != null && cage.cageClip != null) + " tonboTf=" + (tonbo != null && tonbo.TonboTransform != null));
        if (cage != null && cage.enabled && cage.Available)
        {
            Debug.Log("[TMP-DEBUG] cage.BeginCatch() called"); cage.BeginCatch(); // 手で掴む虫籠演出（TonboCage）。Tick・終了判定は次のステップで配線
            return true;
        }
        if (mushikago == null || !mushikago.CanCatch || tonbo == null) return false;
        Transform tf = tonbo.TonboTransform;
        if (tf == null) return false;
        Transform head = headTransform != null ? headTransform : (Camera.main != null ? Camera.main.transform : null);
        mushikago.BeginCatch(tf, tonbo.CurrentScale, head);
        return mushikago.IsBusy;
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

    // 杭の生成・配置は TonboStakes.cs に分離

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

    /// <summary>
    /// ハードモードのときだけ、2匹目の杭に2つ目のリングを出す（表示のみ）。
    /// 1つ目のリングが表示されている間（着地後〜）だけ、2匹目が出ていれば表示する。
    /// </summary>
    void UpdateSecondRing()
    {
        bool show = hardMode && lr != null && lr.enabled && tonbo != null && tonbo.SecondActive;
        if (!show) { if (lr2 != null && lr2.enabled) lr2.enabled = false; return; }

        if (lr2 == null)
        {
            GameObject go = new GameObject("Ring2_" + gameObject.name);
            lr2 = go.AddComponent<LineRenderer>();
            lr2.loop = true;
            lr2.positionCount = segments;
            lr2.useWorldSpace = true;
            lr2.material = lr.material;
            lr2.startWidth = thicknesHalf * 2f;
            lr2.endWidth = thicknesHalf * 2f;
            Color c = colorNormal; c.a = ringAlpha;
            lr2.startColor = c;
            lr2.endColor = c;
        }
        lr2.enabled = true;
        { Color rc = colorNormal; if (Ring2Success) rc = colorClear; else if (Ring2Touched()) rc = colorTouching; rc.a = ringAlpha; lr2.startColor = rc; lr2.endColor = rc; } // リング2の色はリング2の状態だけで決める（成功=緑 / なぞり中=青）
        Vector3 center = tonbo.SecondStakePos;
        Quaternion rot = tonbo.SecondStakeRot;
        for (int i = 0; i < segments; i++)
        {
            float rad = 2f * Mathf.PI * i / segments;
            lr2.SetPosition(i, center + rot * new Vector3(Mathf.Cos(rad) * ringRadius, 0f, Mathf.Sin(rad) * ringRadius));
        }
    }

/// <summary>表示専用：棒の先端がリング2をなぞれる範囲にあるか（TickSecondRing と同じ条件。判定には使わない）</summary>
    bool Ring2Touched()
    {
        if (tipTransform == null || tonbo == null || !tonbo.SecondActive) return false;
        if (state != GameState.Playing && state != GameState.Waiting && state != GameState.Cleared) return false;
        Vector3 local = Quaternion.Inverse(tonbo.SecondStakeRot) * (tipTransform.position - tonbo.SecondStakePos);
        float distFromRing = Mathf.Abs(new Vector2(local.x, local.z).magnitude - ringRadius);
        return Mathf.Abs(local.y) <= depthTolerance && distFromRing <= thicknesHalf;
    }

/// <summary>表示専用：棒の先端がリング1をなぞれる範囲にあるか（リング1の判定と同じ条件。判定には使わない）</summary>
    bool Ring1Touched()
    {
        if (tipTransform == null) return false;
        Vector3 local = Quaternion.Inverse(transform.rotation) * (tipTransform.position - transform.position);
        float distFromRing = Mathf.Abs(new Vector2(local.x, local.z).magnitude - ringRadius);
        return Mathf.Abs(local.y) <= depthTolerance && distFromRing <= thicknesHalf;
    }



    
/// <summary>
    /// ハードモードのときだけ、リング2のなぞり判定を行う（リング1と同じ条件・同じ数え方で、状態だけ独立）。
    /// 結果は ring2 に溜めるだけで、捕獲・スコア・ゲーム進行にはまだ使わない。
    /// </summary>
    void TickSecondRing()
    {
        if (!hardMode || (state != GameState.Playing && state != GameState.Waiting && state != GameState.Cleared) || tipTransform == null || tonbo == null || !tonbo.SecondActive)
        {
            ring2.lastAngle = float.NaN;
            return;
        }
        if (ring2.fastSeconds >= requiredFastSeconds) return; // 成功済み（次のリングまで状態を保持）
        if (Tonbo2State != TonboState.Flying) return; // 気絶・収納済みのトンボ2はリング判定しない


        Vector3 local = Quaternion.Inverse(tonbo.SecondStakeRot) * (tipTransform.position - tonbo.SecondStakePos);
        float distFromRing = Mathf.Abs(new Vector2(local.x, local.z).magnitude - ringRadius);
        bool out2 = Mathf.Abs(local.y) > depthTolerance || distFromRing > thicknesHalf;
        if (out2)
        {
            // はみ出しの猶予：outOfBoundsGrace秒以内なら、まだリング上にいるものとして扱う
            if (float.IsNaN(ring2.outSince)) ring2.outSince = Time.time;
            if (Time.time - ring2.outSince > outOfBoundsGrace) { ring2.lastAngle = float.NaN; return; }
        }
        else ring2.outSince = float.NaN;

        float angle = Mathf.Atan2(local.z, local.x) * Mathf.Rad2Deg;
        if (!float.IsNaN(ring2.lastAngle))
        {
            float delta = Mathf.Abs(Mathf.DeltaAngle(ring2.lastAngle, angle));
            if (delta > 0f)
            {
                if (state == GameState.Waiting) { state = GameState.Playing; sessionStartTime = Time.time; } // リング2を先になぞった場合も、リング1と同じく制限時間を開始
                
ring2.cumulativeAngle += delta;
                int newRotations = Mathf.FloorToInt(ring2.cumulativeAngle / 360f);
                while (ring2.rotationsCounted < newRotations)
                {
                    ring2.rotationsCounted++;
                    ring2.rotationTimestamps.Enqueue(Time.time);
                }
                while (ring2.rotationTimestamps.Count > 0 && Time.time - ring2.rotationTimestamps.Peek() > burstWindowSeconds)
                    ring2.rotationTimestamps.Dequeue();

                bool qualifiedNow = ring2.rotationTimestamps.Count >= rotationsPerBurst;
                if (qualifiedNow) ring2.fastSeconds += Time.deltaTime;
                if (qualifiedNow && !ring2.burstQualifiedLastFrame)
                {
                    ring2.burstCount++;
                    // （成功判定は累計秒数で行う：下の fastSeconds チェック） // リング2成功→トンボ2は Flying→Stunned、捕獲を開始
                }
                ring2.burstQualifiedLastFrame = qualifiedNow;
                if (ring2.fastSeconds >= requiredFastSeconds)
                {
                    if (state == GameState.Cleared) pendingCatch2 = true; // 捕獲演出中：虫かごが空いてから気絶化＋捕獲
                    else { MarkTonboStunned(2); OnSecondRingCleared(); }
                }
            }
        }
        ring2.lastAngle = angle;
    }

/// <summary>
    /// ハードモード：リング2成功で、トンボ2の捕獲を開始する（対象は TonboCage.PickTarget が Stunned のトンボから選ぶ）。
    /// 捕獲中（state==Cleared）は判定自体が止まっているため、虫かごが二重に出ることはない。
    /// </summary>
    void OnSecondRingCleared()
    {
        if (!hardMode || state != GameState.Playing) return;
        TonboCage cage = GetComponent<TonboCage>();
        if (cage == null || !cage.enabled || !cage.Available)
        {
            Debug.LogWarning("[RotationBurstGame] リング2成功：TonboCage が使えないため捕獲を開始できません");
            return;
        }
        cage.BeginCatch();
        state = GameState.Cleared;
        nextSpawnTime = Time.time + nextRingDelay;
    }


    
/// <summary>
    /// ハードモード・Cleared中だけ：リング1が未成功（トンボ1がFlying）なら、判定を続ける。
    /// 成功しても捕獲はすぐ始めず、pendingCatch1 を立てる（虫かごが空いてから開始）。
    /// </summary>
    void TickRing1WhileCleared()
    {
        if (!hardMode || state != GameState.Cleared || tipTransform == null) return;
        if (Tonbo1State != TonboState.Flying) { lastAngle = float.NaN; return; } // 気絶・収納済みは再判定しない
        if (fastSeconds >= requiredFastSeconds) return; // 成功済み（保留中）

        Vector3 local = Quaternion.Inverse(transform.rotation) * (tipTransform.position - transform.position);
        float distFromRing = Mathf.Abs(new Vector2(local.x, local.z).magnitude - ringRadius);
        bool outC = Mathf.Abs(local.y) > depthTolerance || distFromRing > thicknesHalf;
        if (outC)
        {
            // はみ出しの猶予（リング1と共通）
            if (float.IsNaN(outOfBoundsSince)) outOfBoundsSince = Time.time;
            if (Time.time - outOfBoundsSince > outOfBoundsGrace) { lastAngle = float.NaN; return; }
        }
        else outOfBoundsSince = float.NaN;

        float angle = Mathf.Atan2(local.z, local.x) * Mathf.Rad2Deg;
        if (!float.IsNaN(lastAngle))
        {
            float delta = Mathf.Abs(Mathf.DeltaAngle(lastAngle, angle));
            if (delta > 0f)
            {
                cumulativeAngle += delta;
                int newRotations = Mathf.FloorToInt(cumulativeAngle / 360f);
                while (rotationsCounted < newRotations) { rotationsCounted++; rotationTimestamps.Enqueue(Time.time); }
                while (rotationTimestamps.Count > 0 && Time.time - rotationTimestamps.Peek() > burstWindowSeconds) rotationTimestamps.Dequeue();

                bool qualifiedNow = rotationTimestamps.Count >= rotationsPerBurst;
                if (qualifiedNow) fastSeconds += Time.deltaTime;
                if (fastSeconds >= requiredFastSeconds) pendingCatch1 = true;
                if (qualifiedNow && !burstQualifiedLastFrame)
                {
                    burstCount++;
                    flashUntil = Time.time + 0.3f;
                    SetColor(colorBurst);
                    PlaySparkle();
                    // （保留フラグは累計秒数で立てる：上のチェック）
                }
                burstQualifiedLastFrame = qualifiedNow;
            }
        }
        lastAngle = angle;
    }

    /// <summary>
    /// ハードモード：保留されていたリング成功の捕獲を開始する。開始したら true。
    /// （リング1=従来の OnRingCleared / リング2=気絶化して虫かごの BeginCatch）
    /// </summary>
bool BeginPendingCatch()
    {
        if (!hardMode) return false;
        if (pendingCatch1) { pendingCatch1 = false; OnRingCleared(); return true; }
        if (pendingCatch2)
        {
            Debug.Log("[HardDebug] BeginPendingCatch pendingCatch2");
            pendingCatch2 = false;       // 1. 保留フラグを消す
            MarkTonboStunned(2);         // 2. 先にトンボ2を Flying→Stunned にする
            TonboCage cage = GetComponent<TonboCage>();
            if (cage == null || !cage.enabled || !cage.Available) // 3. そのあと虫かごが使えるか確認
            {
                Debug.LogWarning("[RotationBurstGame] リング2成功：TonboCage が使えないため捕獲を開始できません");
                return false;
            }
            cage.BeginCatch();           // 4. 使えるなら捕獲開始
            state = GameState.Cleared;
            flashUntil = Time.time + 0.3f;
            nextSpawnTime = Time.time + nextRingDelay;
            return true;
        }
        return false;
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
        // HMDカメラ（CenterEyeAnchor）の子としてHUDを作る。取れない場合のみ従来のワールドCanvasにフォールバック
        Transform head = headTransform != null ? headTransform : (Camera.main != null ? Camera.main.transform : null);
        if (head != null)
        {
            CreateHud(head);
            return;
        }

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

        uiText = CreateHudText("StatusText_" + gameObject.name, targetCanvas.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(textLocalX, textLocalY),
            new Vector2(700f, 250f), TextAnchor.MiddleCenter, textFontSize);
    }

Text CreateHudText(string objName, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, TextAnchor align, int fontSize)
    {
        GameObject go = new GameObject(objName);
        go.transform.SetParent(parent, false);

        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        Text t = go.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = fontSize;
        t.alignment = align;
        t.color = Color.white;
        t.fontStyle = FontStyle.Bold;
        t.raycastTarget = false;
        t.text = "";

        Outline o = go.AddComponent<Outline>(); // 背景が明るくても読めるように縁取り
        o.effectColor = new Color(0f, 0f, 0f, 0.8f);
        o.effectDistance = new Vector2(2f, -2f);
        return t;
    }


void CreateHud(Transform head)
    {
        GameObject canvasGO = new GameObject("HUDCanvas");
        canvasGO.transform.SetParent(head, false);
        canvasGO.transform.localPosition = new Vector3(0f, 0f, hudDistance);
        canvasGO.transform.localRotation = Quaternion.identity;
        canvasGO.transform.localScale = Vector3.one * hudScale;

        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.GetComponent<RectTransform>().sizeDelta = new Vector2(hudWidth, hudHeight);

        // 上中央：状態メッセージ（開始案内・バースト進捗・クリア・ゲームオーバー）
        uiText = CreateHudText("StatusText", canvasGO.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -hudMargin),
            new Vector2(700f, 200f), TextAnchor.UpperCenter, textFontSize);
        // 左下：残り時間
        hudTimeText = CreateHudText("TimeText", canvasGO.transform,
            new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(hudMargin, hudMargin),
            new Vector2(360f, 80f), TextAnchor.LowerLeft, 44);
        // 右下：スコア表示の領域（今回は表示しない）
        hudScoreText = CreateHudText("ScoreText", canvasGO.transform,
            new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-hudMargin, hudMargin),
            new Vector2(360f, 80f), TextAnchor.LowerRight, 44);

        hudMode = true;
    }


void UpdateStatusText()
    {
        if (uiText == null) return;
        string timeStr = ""; // 残り時間はプレイ中だけ表示（左下テキストへは最後に変化したときだけ代入する）
        if (hudScoreText != null) // 右下：スコア（プレイ中の画面だけ表示）
            hudScoreText.text = (state == GameState.Waiting || state == GameState.Playing || state == GameState.Cleared || state == GameState.Failed) ? string.Format("{0}匹", score) : "";

        // 表示専用：Clearedに入った瞬間に、虫かご演出があるかを1回だけ判定して保持する
        bool isCleared = (state == GameState.Cleared);
        if (isCleared && !hudClearedPrev)
        {
            TonboCage cageForHud = GetComponent<TonboCage>();
            hudCapturing = (cageForHud != null && cageForHud.enabled && cageForHud.Available)
                           || (mushikago != null && mushikago.IsBusy);
        }
        if (!isCleared) hudCapturing = false;
        hudClearedPrev = isCleared;

        switch (state)
        {
            case GameState.Records:
                uiText.color = Color.white;
                uiText.text = ""; // 最高記録は HUD ではなく RecordsGallery（ギャラリー面）に表示する
                break;

            case GameState.Settings:
            case GameState.ChooseMode:
            case GameState.ChooseType:
            case GameState.Title:
                uiText.text = ""; // メニュー系の画面は、ボタン（横長キューブ）のラベルだけで案内する
                break;

            case GameState.Waiting:
                uiText.color = Color.white;
                uiText.text = ""; // 開始案内はHUDに出さない
                break;

            case GameState.Playing:
                float remaining = Mathf.Max(0f, TimeLimit - (Time.time - sessionStartTime));
                uiText.color = Color.white;
                if (hudTimeText != null)
                {
                    timeStr = string.Format("残り {0:F1}秒", remaining);
                    uiText.text = ""; // バースト進捗はHUDに出さない（残り時間は左下）
                }
                else
                {
                    uiText.text = string.Format("残り {0:F1}秒", remaining);
                }
                break;

            case GameState.Cleared:
                uiText.color = colorClear;
                uiText.text = hudCapturing ? "捕獲しよう！" : ""; // 捕獲フェーズだけ画面中央上に表示（それ以外のクリア時は非表示）
                if (hardMode && !AllTonboStored && hudTimeText != null && !float.IsNaN(sessionStartTime))
                {
                    // ハード：捕獲演出中も残り時間を表示（Playing中と同じ計算）
                    float remainingCaught = Mathf.Max(0f, TimeLimit - (Time.time - sessionStartTime));
                    timeStr = string.Format("残り {0:F1}秒", remainingCaught);
                }

                break;

            case GameState.Failed:
                uiText.color = colorFail;
                uiText.text = string.Format(
                    "ゲームオーバー\n{0}秒後にメニューに戻ります",
                    gameOverReturnSeconds);
                break;
        }
        if (hudTimeText != null && hudTimeText.text != timeStr) hudTimeText.text = timeStr; // 変化したときだけ更新（毎フレームの空↔文字列の切り替えによるテキスト再構築を避ける）
    }
}
