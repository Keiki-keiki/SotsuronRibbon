using UnityEditor;
using UnityEngine;
public static class LibelSpiralSetup
{
    const string Dir = "Assets/LibelSpiralLogo";
    [MenuItem("Tools/Libel Spiral/Setup Logo Materials")]
    static void Run()
    {
        var sh = Shader.Find("LibelSpiral/MatCapUnlit");
        string[,] map = { {"Kin_Gold","matcap_gold"}, {"Shu_Vermilion","matcap_red"}, {"Eye_Dark","matcap_dark"}, {"Washi_Cream","matcap_cream"}, {"Wing_Glass","matcap_cream"} };
        var imp = AssetImporter.GetAtPath(Dir + "/LibelSpiralLogo.fbx") as ModelImporter;
        for (int i = 0; i < map.GetLength(0); i++)
        {
            string tp = Dir + "/" + map[i,1] + ".png";
            var ti = AssetImporter.GetAtPath(tp) as TextureImporter;
            if (ti) { ti.wrapMode = TextureWrapMode.Clamp; ti.mipmapEnabled = false; ti.textureCompression = TextureImporterCompression.Uncompressed; ti.SaveAndReimport(); }
            string mp = Dir + "/" + map[i,0] + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, mp); }
            m.shader = sh;
            m.SetTexture("_MatCap", AssetDatabase.LoadAssetAtPath<Texture2D>(tp));
            if (map[i,0] == "Wing_Glass") { m.SetFloat("_Alpha", 0.45f); m.SetFloat("_ZWrite", 0); m.renderQueue = 3000; }
            EditorUtility.SetDirty(m);
            imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), map[i,0]), m);
        }
        AssetDatabase.SaveAssets();
        imp.SaveAndReimport();
    }
}
