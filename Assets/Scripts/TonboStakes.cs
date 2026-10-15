using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 杭（トンボが止まる場所）の生成・配置と、プレイ開始時のプレイヤー前進を担当する。
/// RotationBurstGame と同じ GameObject にアタッチして使う。
/// </summary>
[RequireComponent(typeof(RotationBurstGame))]
public class TonboStakes : MonoBehaviour
{
    [Header("杭（トンボが止まる）")]
    public int stakeCount = 7;
    public float stakeRadius = 0.015f;
    public Color stakeColor = new Color(0.40f, 0.28f, 0.18f);
    [Tooltip("水面の高さ（Mesh_Waterが見つかれば自動で上書き）")]
    public float waterSurfaceY = -0.5f;
    [Tooltip("杭を水面より下に沈める深さ（m）")]
    public float stakeSinkDepth = 0.3f;
    [Tooltip("プレイ開始時にプレイヤーを前（水面の杭の方）へ出す距離（m）。杭もこの分だけ前に立つ")]
    public float playerAdvanceMeters = 1.6f;
    [Tooltip("杭の広がり倍率（左右・奥行き）。大きいほど杭どうしが離れる")]
    public float stakeSpread = 1.8f;
    [Tooltip("杭のモデル（Tonbo_Stake.fbx）。未設定なら円柱で代用")]
    public GameObject stakePrefab;
    public float stakeModelLength = 1.0f;
    [Tooltip("杭どうしの最小間隔（m）。大きいほどバラけて立つ")]
    public float stakeMinSpacing = 0.6f;
    [Tooltip("杭を木の足場(Mesh_Wood)から離す距離（m）。杭は水面の上にだけ立つ")]
    public float stakeWoodMargin = 0.3f;
    [Tooltip("杭の奥行き方向のばらつき（±m）")]
    public float stakeDepthJitter = 0.1f;
    [Tooltip("杭の前後（奥行き）方向の広がり倍率。手前（初期位置の奥行き）を基準に、1より小さいほど前後が狭まる。左右はstakeSpreadのまま")]
    public float stakeDepthSpread = 0.4f;

    private RotationBurstGame game;
    private Transform stakeRoot;
    private readonly List<Vector3> stakeCenters = new List<Vector3>();
    private readonly List<Quaternion> stakeRots = new List<Quaternion>();
    private int lastStakeIndex = -1;
    private bool waterFound = false;
    private bool woodFound = false;
    private bool hasWood = false;
    private Bounds woodBounds;
    private bool playerAdvanced = false;
    private Vector3 stakeShiftVec = Vector3.zero;

    /// <summary>プレイヤーが前に出ている状態か（再センタリング時の位置合わせで使う）</summary>
    public bool PlayerAdvanced { get { return playerAdvanced; } }
    /// <summary>プレイヤーを前へ出すときの移動量</summary>
    public Vector3 ShiftVec { get { return stakeShiftVec; } }

    void Awake()
    {
        game = GetComponent<RotationBurstGame>();
    }

    /// <summary>杭がまだ無ければ作る（常時表示のため、準備完了後に毎フレーム呼んでよい）</summary>
    public void EnsureBuilt()
    {
        if (stakeRoot == null) BuildStakes();
    }

    // 木の足場（Mesh_Wood）の上は杭を立てない
    bool OnWood(Vector3 p)
    {
        if (!woodFound)
        {
            GameObject w = GameObject.Find("Mesh_Wood");
            Renderer wr = (w != null) ? w.GetComponent<Renderer>() : null;
            if (wr != null) { woodBounds = wr.bounds; hasWood = true; }
            woodFound = true;
        }
        if (!hasWood) return false;
        float m = stakeWoodMargin;
        return p.x > woodBounds.min.x - m && p.x < woodBounds.max.x + m && p.z > woodBounds.min.z - m && p.z < woodBounds.max.z + m;
    }

    Vector3 StakeShift()
    {
        // 杭は水面の上だけに立てる。プレイヤーは木の足場の端まで前に出て、水面の杭に手が届くようにする
        Vector3 initialPos = game.InitialPos;
        float d = playerAdvanceMeters;
        OnWood(initialPos); // woodBoundsを読み込む
        if (hasWood)
        {
            Vector3 fwd = -game.DirToHead(initialPos); fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
            Vector3 target = new Vector3(initialPos.x, 0f, woodBounds.max.z + stakeWoodMargin + 0.3f);
            Vector3 from = new Vector3(initialPos.x, 0f, initialPos.z);
            d = Mathf.Max(d, Vector3.Dot(target - from, fwd));
        }
        return game.Off(new Vector3(0f, 0f, d));
    }

    /// <summary>プレイ開始で前へ出る／タイトルに戻ると元の位置へ戻る</summary>
    public void AdvancePlayer(bool forward)
    {
        OVRCameraRig rig = Object.FindObjectOfType<OVRCameraRig>();
        if (rig == null) return;
        if (forward && !playerAdvanced) { rig.transform.position += stakeShiftVec; playerAdvanced = true; }
        else if (!forward && playerAdvanced) { rig.transform.position -= stakeShiftVec; playerAdvanced = false; }
    }

    float GetWaterY()
    {
        if (!waterFound)
        {
            GameObject w = GameObject.Find("Mesh_Water");
            if (w != null) { Renderer wr = w.GetComponent<Renderer>(); waterSurfaceY = (wr != null) ? wr.bounds.max.y : w.transform.position.y; }
            waterFound = true;
        }
        return waterSurfaceY;
    }

    void BuildStakes()
    {
        if (stakeRoot != null) return;
        stakeRoot = new GameObject("TonboStakes").transform;
        Vector3 initialPos = game.InitialPos;
        Quaternion initialRot = game.InitialRot;
        Vector3 shift = StakeShift();
        stakeShiftVec = shift;
        Transform hd = game.headTransform != null ? game.headTransform : (Camera.main != null ? Camera.main.transform : null);
        Vector3 playerAfter = (hd != null ? hd.position : Vector3.zero) + shift;
        for (int n = 0; n < stakeCount; n++)
        {
            Vector3 c = initialPos; Quaternion r = initialRot;
            float bestMin = -1f;
            for (int t = 0; t < 200; t++)
            {
                Vector3 cc; Quaternion rr;
                game.ChooseSpawnPose(out cc, out rr);
                { Vector3 dh = cc - initialPos; dh.y = 0f; Vector3 fw = game.Off(Vector3.forward); fw.y = 0f; fw = fw.normalized; float dd = Vector3.Dot(dh, fw); Vector3 lat = dh - fw * dd; cc += shift + lat * (stakeSpread - 1f) + fw * (dd * (stakeDepthSpread - 1f)); cc += fw * Random.Range(0f, stakeDepthJitter); } // 前後は手前基準で狭める（ばらつきは奥側のみ）
                if (OnWood(cc)) continue;
                { Vector3 dp = playerAfter - cc; rr = Quaternion.Euler(0f, Mathf.Atan2(dp.x, dp.z) * Mathf.Rad2Deg - 180f, 0f) * initialRot; }
                float minD = float.MaxValue;
                for (int k = 0; k < stakeCenters.Count; k++) minD = Mathf.Min(minD, Vector3.Distance(cc, stakeCenters[k]));
                if (minD > bestMin) { bestMin = minD; c = cc; r = rr; }
                if (minD >= stakeMinSpacing) break;
            }
            if (bestMin < 0f) c = initialPos + shift;
            stakeCenters.Add(c); stakeRots.Add(r);
            float perchY = c.y - 0.04f; // 杭の先端（トンボの体の少し下）
            float floorY = GetWaterY() - stakeSinkDepth; // 水面の下まで立てる
            float len = Mathf.Max(0.3f, perchY - floorY);
            GameObject go;
            if (stakePrefab != null)
            {
                go = Instantiate(stakePrefab);
                go.name = "Stake" + n;
                go.transform.SetParent(stakeRoot, false);
                float yScale = len / Mathf.Max(0.01f, stakeModelLength);
                go.transform.SetPositionAndRotation(new Vector3(c.x, perchY, c.z), Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                go.transform.localScale = new Vector3(1f, yScale, 1f);
                Transform rope = go.transform.Find("Tonbo_Stake_Rope");
                if (rope != null) { rope.localScale = new Vector3(1f, 1f / yScale, 1f); rope.localPosition = new Vector3(0f, -0.09f / yScale, 0f); }
                foreach (Collider col in go.GetComponentsInChildren<Collider>()) Destroy(col);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.name = "Stake" + n;
                Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(stakeRoot, false);
                go.transform.position = new Vector3(c.x, perchY - len * 0.5f, c.z);
                go.transform.localScale = new Vector3(stakeRadius * 2f, len * 0.5f, stakeRadius * 2f);
                Renderer rend = go.GetComponent<Renderer>();
                if (rend.material.HasProperty("_BaseColor")) rend.material.SetColor("_BaseColor", stakeColor);
                if (rend.material.HasProperty("_Color")) rend.material.color = stakeColor;
            }
        }
    }

    /// <summary>ランダムな杭を1本選び、その先端（トンボが止まる位置）をリングの中心にする</summary>
    public void ChooseStakePose(out Vector3 center, out Quaternion rot)
    {
        BuildStakes();
        int idx = Random.Range(0, stakeCenters.Count);
        int guard = 0;
        while (idx == lastStakeIndex && stakeCenters.Count > 1 && guard++ < 20) idx = Random.Range(0, stakeCenters.Count);
        lastStakeIndex = idx;
        center = stakeCenters[idx]; rot = stakeRots[idx];
    }
}
