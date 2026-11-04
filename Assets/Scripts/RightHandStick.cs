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

    [Tooltip("棒を手の前方向から下に傾ける角度（度）。大きいほど先端が下がる")]
    public float pitchDownDegrees = 20f;

    // ---- 手のモデル（OVRSkeleton）のボーンへの追従 ----
    private Transform pivotT;
    private OVRSkeleton boundSkeleton;


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
        GameObject pivot = new GameObject("StickPivot");
        pivot.transform.SetParent(this.transform, false);
        pivot.transform.localRotation = Quaternion.Euler(pitchDownDegrees, 0f, 0f);
        stick.transform.SetParent(pivot.transform, false);
        pivotT = pivot.transform;

        stick.transform.localScale = new Vector3(stickRadius * 2f, stickLength / 2f, stickRadius * 2f);
        stick.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        stick.transform.localPosition = new Vector3(0f, 0f, stickLength / 2f);

        // 先端に球体を追加
        GameObject tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        tip.name = "RightHandStickTip";
        Destroy(tip.GetComponent<SphereCollider>());
        tip.transform.SetParent(pivot.transform, false);
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

/// <summary>棒を平滑化アンカーではなく、手のモデルの手首ボーンの子にして、手と完全に同期させる。切り替え時は自動で付け替える</summary>
    void Update()
    {
        if (pivotT == null) return;
        Transform src = UseLeft ? leftSource : rightSource;
        OVRHand hand = UseLeft ? leftConfidence : rightConfidence;
        if (src == null || hand == null) return;

        OVRSkeleton sk = hand.GetComponent<OVRSkeleton>();
        if (sk == null) sk = hand.GetComponentInChildren<OVRSkeleton>(true);
        if (sk == null || sk == boundSkeleton) return;
        if (!hand.IsTracked || !sk.IsInitialized || sk.Bones == null || sk.Bones.Count == 0) return;

        Transform bone = sk.Bones[0].Transform; // 手首ルート
        if (bone == null) return;

        // 生のアンカー姿勢（平滑化なし）に棒を合わせた状態でボーンの子にする
        Quaternion rot = src.rotation * Quaternion.Euler(pitchDownDegrees, 0f, 0f);
        pivotT.SetParent(bone, false);
        pivotT.SetPositionAndRotation(src.position, rot);
        boundSkeleton = sk;
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
