using UnityEngine;

/// <summary>
/// source（生のハンドトラッキングTransform、例：RightHandAnchor）の位置・回転を
/// One Euro Filterで平滑化し、自身のTransformに反映する。
/// RightHandStickなど、平滑化した座標を使いたいスクリプトはこのGameObjectに付ける。
///
/// 速度が速い区間（Steering中の実際の移動）はほぼ平滑化がかからず生の軌跡に近く、
/// 静止に近い微小なジッター区間だけ強く効くため、実験データへの影響（レイテンシ・
/// 軌跡の歪み）を最小限にできる。
/// </summary>
public class SmoothedHandAnchor : MonoBehaviour
{
    [Tooltip("平滑化元の生トラッキングTransform（例：RightHandAnchor）")]
    public Transform source;

    [Tooltip("任意。設定すると、IsTracked=falseの間は生座標を無視して直前の平滑化値を保持する（テレポート対策）")]
    public OVRHand handConfidence;

    [Header("One Euro Filter パラメータ")]
    [Tooltip("最低カットオフ周波数。小さいほど静止時のジッター除去が強くなる")]
    public float minCutoff = 1.0f;

    [Tooltip("速度に対する追従性の係数。大きいほど高速動作時のラグが減る（ジッター除去は弱まる）")]
    public float beta = 0.3f;

    [Tooltip("速度推定用のカットオフ周波数。通常は変更不要")]
    public float dCutoff = 1.0f;

    private OneEuroFilter filterX;
    private OneEuroFilter filterY;
    private OneEuroFilter filterZ;

    private Quaternion filteredRotation;
    private Quaternion lastRawRotation;
    private bool rotInitialized = false;

    void OnEnable()
    {
        filterX = new OneEuroFilter(minCutoff, beta, dCutoff);
        filterY = new OneEuroFilter(minCutoff, beta, dCutoff);
        filterZ = new OneEuroFilter(minCutoff, beta, dCutoff);
        rotInitialized = false;

        if (source != null)
        {
            transform.position = source.position;
            transform.rotation = source.rotation;
        }
    }

    void LateUpdate()
    {
        
        if (handConfidence != null && !handConfidence.IsTracked) return; // 低信頼度中は生座標を無視
if (source == null) return;

        float t = Time.time;

        // 位置：軸ごとに独立してOne Euro Filterを適用
        Vector3 p = source.position;
        float fx = filterX.Filter(p.x, t);
        float fy = filterY.Filter(p.y, t);
        float fz = filterZ.Filter(p.z, t);
        transform.position = new Vector3(fx, fy, fz);

        // 回転：角速度に応じてSlerpの補間係数を適応させる（One Euroと同じ考え方）
        Quaternion raw = source.rotation;
        float dt = Mathf.Max(Time.deltaTime, 1e-5f);

        if (!rotInitialized)
        {
            filteredRotation = raw;
            lastRawRotation = raw;
            rotInitialized = true;
        }
        else
        {
            float angularSpeedRad = Quaternion.Angle(lastRawRotation, raw) * Mathf.Deg2Rad / dt;
            lastRawRotation = raw;

            float cutoff = minCutoff + beta * angularSpeedRad;
            float tau = 1f / (2f * Mathf.PI * Mathf.Max(cutoff, 1e-5f));
            float alpha = 1f / (1f + tau / dt);

            filteredRotation = Quaternion.Slerp(filteredRotation, raw, alpha);
        }

        transform.rotation = filteredRotation;
    }
}
