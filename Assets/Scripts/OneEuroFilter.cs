using UnityEngine;

/// <summary>
/// One Euro Filter (Casiez, Roussel, Vogel 2012)。
/// 低速時はしっかりノイズを除去し、高速時は追従性を優先する適応型ローパスフィルタ。
/// スカラー値1つを対象とする。Vector3やQuaternionは複数インスタンスを組み合わせて使う。
/// </summary>
public class OneEuroFilter
{
    private readonly float minCutoff;
    private readonly float beta;
    private readonly float dCutoff;

    private LowPass xFilter;
    private LowPass dxFilter;
    private float lastTime = -1f;
    private bool initialized = false;

    public OneEuroFilter(float minCutoff = 1.0f, float beta = 0.0f, float dCutoff = 1.0f)
    {
        this.minCutoff = minCutoff;
        this.beta = beta;
        this.dCutoff = dCutoff;
    }

    public float Filter(float x, float timestamp)
    {
        if (!initialized)
        {
            xFilter = new LowPass(x);
            dxFilter = new LowPass(0f);
            lastTime = timestamp;
            initialized = true;
            return x;
        }

        float dt = Mathf.Max(timestamp - lastTime, 1e-5f);
        lastTime = timestamp;

        float dx = (x - xFilter.LastRaw()) / dt;
        float edx = dxFilter.Apply(dx, Alpha(dCutoff, dt));

        float cutoff = minCutoff + beta * Mathf.Abs(edx);
        return xFilter.Apply(x, Alpha(cutoff, dt));
    }

    private static float Alpha(float cutoff, float dt)
    {
        float tau = 1f / (2f * Mathf.PI * Mathf.Max(cutoff, 1e-5f));
        return 1f / (1f + tau / dt);
    }

    private class LowPass
    {
        private float smoothed;
        private float raw;
        private bool hasValue;

        public LowPass(float initial)
        {
            smoothed = initial;
            raw = initial;
            hasValue = true;
        }

        public float LastRaw() => raw;

        public float Apply(float value, float alpha)
        {
            if (!hasValue)
            {
                smoothed = value;
                raw = value;
                hasValue = true;
                return value;
            }

            smoothed = alpha * value + (1f - alpha) * smoothed;
            raw = value;
            return smoothed;
        }
    }
}
