using UnityEngine;

/// <summary>
/// 湖畔シーンの「時間帯ごとの見た目」を1セットにまとめたプリセット。
/// TimeOfDayController がこれらを補間して時間経過を表現する。
/// </summary>
[CreateAssetMenu(fileName = "TimeOfDayPreset", menuName = "LakeScene/Time Of Day Preset")]
public class TimeOfDayPreset : ScriptableObject
{
    [Header("Sky (Skybox/LakeGradient)")]
    public Color skyTop = Color.black;
    public Color skyMid = Color.black;
    public Color skyHorizon = Color.black;

    [Header("Light")]
    public Color sunColor = Color.white;
    public float sunIntensity = 1f;
    public Vector3 sunEuler = new Vector3(50f, 342f, 0f);
    public Color ambientColor = Color.gray;

    [Header("Fog (Exponential)")]
    public Color fogColor = Color.gray;
    [Range(0f, 0.05f)] public float fogDensity = 0.008f;

    [Header("Surface colors")]
    public Color water = Color.blue;
    public Color ripple = Color.white;
    public Color sakura = Color.white;
    public Color mountainNear = Color.gray;
    public Color mountainMid = Color.gray;
    public Color mountainFar = Color.gray;

    [Header("Visibility / Brightness (0-1)")]
    [Range(0f, 1f)] public float moon = 0f;
    [Range(0f, 1f)] public float stars = 0f;
    [Range(0f, 1f)] public float fireflies = 0f;
    [Range(0f, 1f)] public float moonGlint = 0f;
    [Range(0f, 1f)] public float lanterns = 1f;
}
