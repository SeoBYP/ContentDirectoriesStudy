using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Build.DataBuilders;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace ContentsDirectories
{
    // CD4: Content Directory 그룹과 AssetBundle 그룹이 같은 의존성(Lit.mat, Lit.shader)을 공유할 때
    // 혼합 빌드에서 공유 의존성이 두 번 빌드되는지 확인.
    // Unity.exe -batchmode -quit -projectPath . -executeMethod ContentsDirectories.CdAddressablesTools.Run
    public static class CdAddressablesTools
    {
        const string CubePath = "Assets/Prefab/Cube.prefab";
        const string CubeBPath = "Assets/Prefab/CubeB.prefab";
        const string BuilderPath = "Assets/AddressableAssetsData/DataBuilders/BuildScriptSchemaDriven.asset";

        public static void Run()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(CubeBPath) == null)
                AssetDatabase.CopyAsset(CubePath, CubeBPath); // Cube와 같은 Lit.mat을 쓰는 복제본

            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);

            var cdGroup = settings.FindGroup("CD_Group") ??
                settings.CreateGroup("CD_Group", false, false, false, null, typeof(ContentDirectoryGroupSchema));
            var abGroup = settings.FindGroup("AB_Group") ??
                settings.CreateGroup("AB_Group", false, false, false, null, typeof(BundledAssetGroupSchema));
            abGroup.GetSchema<BundledAssetGroupSchema>().Compression = BundledAssetGroupSchema.BundleCompressionMode.Uncompressed;

            settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(CubePath), cdGroup).address = "Cube";
            settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(CubeBPath), abGroup).address = "CubeB";

            int builderIndex = settings.DataBuilders.FindIndex(b => b is BuildScriptSchemaDriven);
            if (builderIndex < 0)
            {
                var builder = ScriptableObject.CreateInstance<BuildScriptSchemaDriven>();
                AssetDatabase.CreateAsset(builder, BuilderPath);
                settings.AddDataBuilder(builder, false);
                builderIndex = settings.DataBuilders.IndexOf(builder);
            }
            settings.ActivePlayerDataBuilderIndex = builderIndex;
            ProjectConfigData.GenerateBuildLayout = true;
            AssetDatabase.SaveAssets();

            Debug.Log("[CDM4] DataBuilders: " + string.Join(", ", settings.DataBuilders.Select((b, i) => $"{i}:{b.GetType().Name}")) +
                      $" | active={settings.ActivePlayerDataBuilderIndex}");
            foreach (var g in settings.groups)
                Debug.Log($"[CDM4] group {g.Name}: schemas=[{string.Join(",", g.Schemas.Select(s => s.GetType().Name))}] entries=[{string.Join(",", g.entries.Select(e => e.AssetPath))}]");

            AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
            Debug.Log($"[CDM4] build error='{result.Error}' duration={result.Duration:F1}s output={result.OutputPath}");
            foreach (var r in result.AssetBundleBuildResults) Debug.Log("[CDM4] bundle " + Dump(r));
            foreach (var r in result.ContentDirectoryBuildResults) Debug.Log("[CDM4] contentdir " + Dump(r));
        }

        static string Dump(object o)
        {
            var t = o.GetType();
            var f = t.GetFields(BindingFlags.Public | BindingFlags.Instance).Select(x => $"{x.Name}={x.GetValue(o)}");
            var p = t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(x => x.GetIndexParameters().Length == 0).Select(x => $"{x.Name}={x.GetValue(o)}");
            return string.Join(" | ", f.Concat(p));
        }
    }
}
