using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace ContentsDirectories
{
    // CD5 재측정: URP 파이프라인에서 APV를 켜고 InDoor에 APV를 베이크한 뒤 콘텐츠·Player를 다시 빌드한다.
    // Unity.exe -batchmode -quit -projectPath . -executeMethod ContentsDirectories.CdApvTools.BakeAndBuild
    public static class CdApvTools
    {
        const string ScenePath = "Assets/Scenes/InDoor.unity";
        const string BakingSetPath = "Assets/Scenes/InDoor/InDoor Baking Set.asset";

        public static void BakeAndBuild()
        {
            EnableApv("Assets/Settings/PC_RPAsset.asset");
            EnableApv("Assets/Settings/Mobile_RPAsset.asset");
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var set = AssetDatabase.LoadAssetAtPath<ProbeVolumeBakingSet>(BakingSetPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<ProbeVolumeBakingSet>();
                typeof(ProbeVolumeBakingSet).GetMethod("SetDefaults", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(set, null);
                set.name = "InDoor Baking Set";
                AssetDatabase.CreateAsset(set, BakingSetPath);
            }
            bool added = set.TryAddScene(AssetDatabase.AssetPathToGUID(ScenePath));
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            Debug.Log($"[CDAPV] bakingSet={AssetDatabase.GetAssetPath(set)} sceneAdded={added} probeVolumes={Object.FindObjectsByType<ProbeVolume>(FindObjectsSortMode.None).Length}");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool ok = Lightmapping.Bake();
            sw.Stop();
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            var files = Directory.GetFiles("Assets/Scenes/InDoor", "*", SearchOption.AllDirectories).Where(f => !f.EndsWith(".meta")).Select(Path.GetFileName);
            Debug.Log($"[CDAPV] Lightmapping.Bake ok={ok} {sw.Elapsed.TotalSeconds:F1}s files=[{string.Join(", ", files)}]");

            CdMeasureTools.All();
        }

        static void EnableApv(string path)
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            var so = new SerializedObject(asset);
            var p = so.FindProperty("m_LightProbeSystem");
            p.intValue = 1; // LightProbeSystem.ProbeVolumes
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[CDAPV] {path} m_LightProbeSystem={so.FindProperty("m_LightProbeSystem").intValue}");
        }
    }
}
