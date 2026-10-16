using UnityEngine;

/// <summary>メニュー系の画面（ゲームの状態ではなく、どのボタン群を出すか）</summary>
public enum MenuScreen { None, Title, Settings, ChooseType, Records }

/// <summary>メニューのボタン操作の結果（長押しが完了したボタン）</summary>
public enum MenuAction { None, Play, Records, Settings, Back, Real, Cute }

/// <summary>
/// タイトル・タイプ選択・記録・設定の各画面のボタン（横長キューブ）と設定パネルを担当する。
/// ゲームの状態遷移は RotationBurstGame が行い、このクラスは「どのボタンを出すか」「どのボタンが押されたか」だけを扱う。
/// RotationBurstGame と同じ GameObject にアタッチして使う。
/// </summary>
[RequireComponent(typeof(RotationBurstGame))]
public class GameMenuUI : MonoBehaviour
{
    [Header("メニューボタン（横長キューブ）")]
    [Tooltip("キューブの幅（メートル）")]
    public float menuButtonWidth = 0.34f;
    [Tooltip("キューブの高さ（メートル）")]
    public float menuButtonHeight = 0.1f;
    [Tooltip("縦に並べたボタンの間隔（メートル）。高さより少し大きくすること")]
    public float menuRowSpacing = 0.14f;

    [Header("メニュー配置（X=右、Y=上、Z=奥。プレイヤーから遠ざかる向きが＋）")]
    [Tooltip("メニューボタン全体の位置。リングの初期位置からのずれ（メートル）")]
    public Vector3 menuOffset = new Vector3(0f, 0.551f, -0.19f);
    [Tooltip("「もどる」ボタンの位置。メニュー中心からのずれ")]
    public Vector3 backButtonOffset = new Vector3(0f, 0.959f, 0.413f);
    [Tooltip("ゲームオーバー時の「メニュー」ボタンの位置。メニュー中心からのずれ")]
    public Vector3 gameOverButtonOffset = new Vector3(0f, -0.3f, 0f);
    [Tooltip("設定画面（スライダー）の位置。Menu Offsetと同じ数値にすると、メニューと同じ高さ・奥行きになる（リングの初期位置からのずれ）")]
    public Vector3 settingsPanelOffset = new Vector3(0f, 0.551f, -0.19f);

    [Header("操作")]
    [Tooltip("ボタンが反応した（長押し完了）ときに鳴らす音")]
    public AudioClip buttonClip;
    [Tooltip("この秒数だけ触れ続けたらボタンが反応する")]
    public float startHoldSeconds = 2f;
    // ボタンの判定半径（この距離以内なら触れているとみなす）
    private float buttonRadius = 0.09f;

    private RotationBurstGame game;
    private HoldButton startButton;
    private HoldButton recordButton;
    private HoldButton backButton;
    private HoldButton settingsButton;
    private HoldButton handButton;
    private HoldButton realButton;
    private HoldButton cuteButton;
    private HoldButton recReal;
    private HoldButton recCute;
    private HoldButton gameOverButton;
    private SettingsPanel settingsPanel;

    void Awake()
    {
        game = GetComponent<RotationBurstGame>();
    }

    // ===== 配置 =====

    Vector3 MenuCenter()
    {
        return game.InitialPos + game.Off(menuOffset);
    }

    /// <summary>メニューボタンの位置。行は必ずワールドの真上方向にずらす（リング面が傾いていても奥行きが揃う）</summary>
    Vector3 MenuButtonPos(float row)
    {
        return MenuCenter() + Vector3.up * (menuRowSpacing * row);
    }

    /// <summary>頭の方を向いた横長キューブのボタンを作る。向きは全ボタン共通（メニュー中心から頭への水平方向）</summary>
    HoldButton MakeButton(string name, Vector3 pos, string label, Color idle, Color held)
    {
        Vector3 center = MenuCenter();
        Quaternion rot = Quaternion.LookRotation(-game.DirToHead(center), Vector3.up);
        return new HoldButton(name, pos, rot, new Vector2(menuButtonWidth, menuButtonHeight),
            label, game.LabelCharSize * 1.3f, idle, held);
    }

    // ===== 生成 =====

    /// <summary>起動時に、常に使うボタン（ゲームオーバー・プレイ・記録・もどる）を作る。失敗してもゲーム自体は進められるようにする</summary>
    public void CreateButtons()
    {
        HoldButton.pressSource = game.Audio;
        HoldButton.pressClip = buttonClip;

        try
        {
            // ゲームオーバー時だけ出る、横長キューブの「メニュー」ボタン
            gameOverButton = MakeButton("MenuButton_" + gameObject.name, MenuCenter() + game.Off(gameOverButtonOffset), "メニュー",
                new Color(0.5f, 0.5f, 0.5f), new Color(1f, 0.3f, 0.3f));
            gameOverButton.SetActive(false);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[GameMenuUI] CreateResetButton failed: " + e);
        }

        try
        {
            startButton = MakeButton("StartButton_" + gameObject.name, MenuButtonPos(1f), "プレイ",
                new Color(0.2f, 0.6f, 0.3f), new Color(0.4f, 1f, 0.5f));
            recordButton = MakeButton("RecordButton_" + gameObject.name, MenuButtonPos(0f), "記録",
                new Color(0.2f, 0.4f, 0.8f), new Color(0.4f, 0.7f, 1f));
            backButton = MakeButton("BackButton_" + gameObject.name,
                MenuCenter() + game.Off(backButtonOffset), "もどる",
                new Color(0.5f, 0.5f, 0.5f), new Color(1f, 1f, 1f));
            backButton.SetActive(false);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[GameMenuUI] CreateStartButton failed: " + e);
        }
    }

    /// <summary>設定ボタンと設定パネル（BGM・効果音・画面の明るさを、棒の先端でスライドして調整）を作る</summary>
    void EnsureSettings()
    {
        if (settingsPanel != null) return;
        GameObject b = GameObject.Find("BGM");
        AudioSource bgm = (b != null) ? b.GetComponent<AudioSource>() : null;
        Vector3 panelPos = game.InitialPos + game.Off(settingsPanelOffset);
        Vector3 toPlayer = game.DirToHead(panelPos);
        toPlayer.y = 0f;
        toPlayer = toPlayer.sqrMagnitude > 1e-6f ? toPlayer.normalized : Vector3.back;
        // パネル内の座標: X=右、Y=手前（プレイヤー側）、Z=上。傾けず、メニューボタンと同じ向きで立てる
        Quaternion panelRot = Quaternion.LookRotation(Vector3.up, toPlayer);
        settingsPanel = new SettingsPanel(panelPos, panelRot, bgm, game.Audio, game.LabelCharSize,
            Quaternion.Inverse(panelRot) * (MenuCenter() + game.Off(backButtonOffset) - panelPos), "");
        settingsButton = MakeButton("SettingsButton_" + gameObject.name, MenuButtonPos(-1f), "設定",
            new Color(0.8f, 0.5f, 0.1f), new Color(1f, 0.8f, 0.4f));
        settingsPanel.SetActive(false);

        // 棒を持つ手（右／左）の切り替えボタン。設定画面の一番下（明るさスライダーの下）に出す
        handButton = MakeButton("HandButton_" + gameObject.name, panelPos + Vector3.up * -0.27f, HandLabel(),
            new Color(0.45f, 0.3f, 0.7f), new Color(0.75f, 0.6f, 1f));
        handButton.SetActive(false);
    }

    /// <summary>タイプ選択画面（リアル=左 / キュート=右）のボタンを、最初に必要になったときに作る</summary>
    void EnsureChoiceButtons()
    {
        if (realButton != null) return;
        realButton = MakeButton("RealButton_" + gameObject.name, MenuButtonPos(0.5f), "リアルなトンボ",
            new Color(0.55f, 0.35f, 0.15f), new Color(1f, 0.7f, 0.3f));
        cuteButton = MakeButton("CuteButton_" + gameObject.name, MenuButtonPos(-0.5f), "可愛いトンボ",
            new Color(0.95f, 0.5f, 0.7f), new Color(1f, 0.85f, 0.92f));
    }

    /// <summary>記録画面のリアル／可愛いの記録切り替えボタンを、最初に必要になったときに作る</summary>
    void EnsureRecordButtons()
    {
        if (recReal != null) return;
        recReal = MakeButton("RecordRealButton_" + gameObject.name, MenuButtonPos(0.5f), "リアルの記録",
            new Color(0.55f, 0.35f, 0.15f), new Color(1f, 0.7f, 0.3f));
        recCute = MakeButton("RecordCuteButton_" + gameObject.name, MenuButtonPos(-0.5f), "可愛いの記録",
            new Color(0.95f, 0.5f, 0.7f), new Color(1f, 0.85f, 0.92f));
    }

    // ===== 表示切り替え =====

    static void SetActive(HoldButton b, bool on)
    {
        if (b != null) b.SetActive(on);
    }

    /// <summary>画面に合わせて、出すボタンだけを表示する（それ以外は隠す）</summary>
    public void Show(MenuScreen screen)
    {
        if (screen == MenuScreen.Title || screen == MenuScreen.Settings) EnsureSettings();
        if (screen == MenuScreen.ChooseType) EnsureChoiceButtons();
        if (screen == MenuScreen.Records) EnsureRecordButtons();

        SetActive(startButton, screen == MenuScreen.Title);
        SetActive(recordButton, screen == MenuScreen.Title);
        SetActive(settingsButton, screen == MenuScreen.Title);
        SetActive(backButton, screen == MenuScreen.Settings || screen == MenuScreen.Records);
        SetActive(realButton, screen == MenuScreen.ChooseType);
        SetActive(cuteButton, screen == MenuScreen.ChooseType);
        SetActive(recReal, screen == MenuScreen.Records);
        SetActive(recCute, screen == MenuScreen.Records);
        if (settingsPanel != null) settingsPanel.SetActive(screen == MenuScreen.Settings);
        SetActive(handButton, screen == MenuScreen.Settings);
    }

    // ===== 操作 =====

    /// <summary>棒の先端でボタンに触れ続けたかを調べ、長押しが完了したボタンを返す。毎フレーム呼ぶこと</summary>
    public MenuAction Tick(MenuScreen screen, Transform tip)
    {
        switch (screen)
        {
            case MenuScreen.Title:
                // ボタンが生成できなかった場合でもゲームが進められるようにする
                if (startButton == null) return MenuAction.Play;
                if (startButton.Tick(tip, buttonRadius, startHoldSeconds)) return MenuAction.Play;
                if (recordButton != null && recordButton.Tick(tip, buttonRadius, startHoldSeconds)) return MenuAction.Records;
                if (settingsButton != null && settingsButton.Tick(tip, buttonRadius, startHoldSeconds)) return MenuAction.Settings;
                return MenuAction.None;

            case MenuScreen.Records:
            {
                bool real = recReal != null && recReal.Tick(tip, buttonRadius, startHoldSeconds);
                bool cute = recCute != null && recCute.Tick(tip, buttonRadius, startHoldSeconds);
                bool back = backButton != null && backButton.Tick(tip, buttonRadius, startHoldSeconds);
                if (real) return MenuAction.Real;
                if (cute) return MenuAction.Cute;
                if (back) return MenuAction.Back;
                return MenuAction.None;
            }

            case MenuScreen.Settings:
                // スライダーを先端でなぞる。「もどる」に触れ続けたら保存して戻る
                if (settingsPanel != null) settingsPanel.Tick(tip);
                if (handButton != null && handButton.Tick(tip, buttonRadius, startHoldSeconds)) ToggleHand();
                if (backButton != null && backButton.Tick(tip, buttonRadius, startHoldSeconds))
                {
                    if (settingsPanel != null) settingsPanel.Save();
                    return MenuAction.Back;
                }
                return MenuAction.None;

            case MenuScreen.ChooseType:
                if (realButton != null && realButton.Tick(tip, buttonRadius, startHoldSeconds)) return MenuAction.Real;
                if (cuteButton != null && cuteButton.Tick(tip, buttonRadius, startHoldSeconds)) return MenuAction.Cute;
                return MenuAction.None;
        }
        return MenuAction.None;
    }

    /// <summary>
    /// ゲームオーバー時だけ出る「メニュー」ボタン。show=trueの間だけ表示し、
    /// startHoldSeconds秒触れ続けたらtrueを返す（押した後は自動で隠す）。
    /// </summary>
    public bool TickGameOver(bool show, Transform tip)
    {
        if (gameOverButton == null) return false;
        if (gameOverButton.t.gameObject.activeSelf != show) gameOverButton.SetActive(show);
        if (!show) return false;
        if (gameOverButton.Tick(tip, buttonRadius, startHoldSeconds))
        {
            gameOverButton.SetActive(false);
            return true;
        }
        return false;
    }


string HandLabel()
    {
        return RightHandStick.UseLeft ? "棒の手：左" : "棒の手：右";
    }

    /// <summary>棒を持つ手を右⇔左で切り替え、ボタンの文字を更新する</summary>
    void ToggleHand()
    {
        RightHandStick stick = RightHandStick.Instance;
        if (stick == null) return;
        stick.SetLeft(!RightHandStick.UseLeft);
        if (handButton != null) handButton.SetLabel(HandLabel());
    }
}
