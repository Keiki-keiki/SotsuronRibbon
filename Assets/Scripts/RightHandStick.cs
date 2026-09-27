using UnityEngine;

/// <summary>
/// 右手首に600mmの棒を付随させるスクリプト。
/// RightHandAnchor に追加してください。
/// 棒は手に対して平行（前方向）に伸び、手首側に棒の端が来ます。
/// </summary>
public class RightHandStick : MonoBehaviour
{
    [Tooltip("棒の長さ (メートル)。0.6 = 600mm")]
    public float stickLength = 0.6f;

    [Tooltip("棒の半径 (メートル)")]
    public float stickRadius = 0.01f;

    private GameObject stick;

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
    }
}
