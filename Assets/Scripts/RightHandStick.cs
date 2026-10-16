using UnityEngine;

/// <summary>
/// 右手首に600mmの棒を付随させるスクリプト。
/// RightHandAnchor に追加してください。
/// 棒は手に対して平行（前方向）に伸び、手首側に棒の端が来ます。
/// </summary>
public class RightHandStick : MonoBehaviour
{

void Awake()
    {
        Instance = this;
    }

    [Tooltip("棒の長さ (メートル)。0.6 = 600mm")]
    public float stickLength = 0.6f;

    [Tooltip("棒の半径 (メートル)")]
    public float stickRadius = 0.01f;

    private GameObject stick;

    // ---- 棒を持つ手（右／左）の切り替え ----
    public static RightHandStick Instance { get; private set; }
    /// <summary>true: 左手に棒を持つ / false: 右手に棒を持つ</summary>
    public static bool UseLeft { get; private set; }
    private const string HandPrefKey = "Set_HandLeft";
    private SmoothedHandAnchor smoothed;
    private Transform rightSource, leftSource;
    private OVRHand rightConfidence, leftConfidence;

    void Start()
    {
        // Cylinderで棒を生成
        stick = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        stick.name = "RightHandStick";
        Destroy(stick.GetComponent<CapsuleCollider>());
        stick.transform.SetParent(this.transform, false);
        stick.transform.localScale = new Vector3(stickRadius * 2f, stickLength / 2f, stickRadius * 2f);
        stick.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        stick.transform.localPosition = new Vector3(0f, 0f, stickLength / 2f);

        // 先端に球体を追加
        GameObject tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        tip.name = "RightHandStickTip";
        Destroy(tip.GetComponent<SphereCollider>());
        tip.transform.SetParent(this.transform, false);
        float tipRadius = stickRadius * 2.5f;
        tip.transform.localScale = Vector3.one * tipRadius * 2f;
        tip.transform.localPosition = new Vector3(0f, 0f, stickLength);

        // 全てのRingTarget / RotationBurstGameに先端のTransformを渡す
        var rings = FindObjectsOfType<RingTarget>();
        foreach (var ring in rings)
            ring.tipTransform = tip.transform;

        var burstGames = FindObjectsOfType<RotationBurstGame>();
        foreach (var bg in burstGames)
            bg.tipTransform = tip.transform;

        InitHandSide();
    }


/// <summary>平滑化アンカーの元になる右手・左手のTransformを控え、保存された設定を反映する</summary>
    void InitHandSide()
    {
        smoothed = GetComponent<SmoothedHandAnchor>();
        if (smoothed == null) return;
        rightSource = smoothed.source;
        rightConfidence = smoothed.handConfidence;
        GameObject la = GameObject.Find("LeftHandAnchor");
        if (la != null)
        {
            leftSource = la.transform;
            leftConfidence = la.GetComponentInChildren<OVRHand>(true);
        }
        SetLeft(PlayerPrefs.GetInt(HandPrefKey, 0) == 1, false);
    }

    /// <summary>棒を持つ手を切り替える。left=trueで左手、falseで右手。設定はPlayerPrefsに保存する</summary>
    public void SetLeft(bool left, bool save = true)
    {
        if (smoothed == null) return;
        if (left && leftSource == null) left = false; // 左手のアンカーが見つからなければ右のまま
        UseLeft = left;
        smoothed.enabled = false; // OnEnableでフィルタを初期化し、新しい手の位置へ即座に移す
        smoothed.source = left ? leftSource : rightSource;
        smoothed.handConfidence = left ? leftConfidence : rightConfidence;
        smoothed.enabled = true;
        if (save)
        {
            PlayerPrefs.SetInt(HandPrefKey, left ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
