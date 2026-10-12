using UnityEngine;

/// <summary>
/// 横長のキューブに説明文を書いたボタン。棒の先端で一定時間触れ続けると押したことになる。
/// キューブは頭の方（水平方向）を向いて置かれ、ラベルは手前側に表示される。
/// </summary>
public class HoldButton
{
    public Transform t;
    private Renderer r;
    private TextMesh tm;
    private float timer;
    private Color idle, held;
    private Vector2 half; // キューブの（幅/2, 高さ/2）

    public HoldButton(string name, Vector3 pos, Quaternion rot, Vector2 size, string label, float charSize, Color idle, Color held)
    {
        this.idle = idle;
        this.held = held;
        half = size * 0.5f;

        GameObject root = new GameObject(name);
        root.transform.SetPositionAndRotation(pos, rot);

        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Body";
        Object.Destroy(cube.GetComponent<Collider>());
        cube.transform.SetParent(root.transform, false);
        cube.transform.localScale = new Vector3(size.x, size.y, 0.02f);
        r = cube.GetComponent<Renderer>();
        r.material = new Material(Shader.Find("Sprites/Default"));
        r.material.color = idle;

        // ラベル（頭の側＝ローカル-Z側から読める向き）
        GameObject tg = new GameObject("Label");
        tg.transform.SetParent(root.transform, false);
        tg.transform.localPosition = new Vector3(0f, 0f, -0.015f);
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        tm = tg.AddComponent<TextMesh>();
        tm.font = font;
        tm.text = label;
        tm.fontSize = 64;
        tm.characterSize = charSize;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = Color.white;
        tg.GetComponent<MeshRenderer>().sharedMaterial = font.material;

        t = root.transform;
    }

    public void SetActive(bool active)
    {
        timer = 0f;
        if (r != null) r.material.color = idle;
        if (tm != null) tm.color = Color.white;
        if (t != null) t.gameObject.SetActive(active);
    }

    /// <summary>毎フレーム呼ぶ。holdSeconds秒触れ続けたらtrueを返す。radiusは奥行きの判定幅に使う</summary>
    public bool Tick(Transform tip, float radius, float holdSeconds)
    {
        if (t == null) return false;
        bool touching = false;
        if (tip != null)
        {
            Vector3 l = Quaternion.Inverse(t.rotation) * (tip.position - t.position);
            float margin = radius * 0.2f; // 隣のボタンと同時に反応しないよう、縦横の余裕は小さめ
            touching = Mathf.Abs(l.x) <= half.x + margin
                    && Mathf.Abs(l.y) <= half.y + margin
                    && Mathf.Abs(l.z) <= radius;
        }
        timer = touching ? timer + Time.deltaTime : Mathf.Max(0f, timer - Time.deltaTime * 2f);
        float p = Mathf.Clamp01(timer / holdSeconds);
        r.material.color = Color.Lerp(idle, held, p);
        if (tm != null) tm.color = Color.Lerp(Color.white, Color.black, p); // 明るい色に変わっても読めるように
        if (timer >= holdSeconds)
        {
            timer = 0f;
            return true;
        }
        return false;
    }
}
