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
    private TonboController tonbo; // トンボの出現・飛行・色/大きさは TonboController.cs に分離
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
        menu.Show(MenuScreen.Records);
        SetTextLayout(true);
        records.BuildGallery();
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
        if (titleLogo == null) titleLogo = GameObject.Find("LibelSpiralLogo");
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
                case MenuAction.Real: SetMode(false); StartGame(); break;
                case MenuAction.Cute: SetMode(true); StartGame(); break;
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

        HandleResetButton();
        tonbo.Tick();

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
        if (state == GameState.Waiting && tonbo.WaitingForLanding)
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
    /// 棒の先端がゲームオーバー時のメニューボタンに触れ続けたら、強制リセットする（ボタンの実体は GameMenuUI）。
    /// ボタンの色を押し込み進捗に応じて変化させ、触れていない間は徐々に進捗を戻す。
    /// </summary>
void HandleResetButton()
    {
        // ゲームオーバー時だけ出る「メニュー」ボタンに触れ続けたら、強制リセットしてオープニングへ戻る
        if (menu.TickGameOver(state == GameState.Failed, tipTransform)) ResetGame();
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
        records.RecordCaught();
        HideTonbo();
        if (score > bestScore) bestScore = score;
        state = GameState.Cleared;
        flashUntil = Time.time + 0.3f;
        nextSpawnTime = Time.time + nextRingDelay;
    }



void HideTonbo()
    {
        if (tonbo != null) tonbo.Hide();
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
