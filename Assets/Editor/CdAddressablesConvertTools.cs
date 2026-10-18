using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Build.DataBuilders;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace ContentsDirectories
{
    // CD4b: 같은 런타임 코드(CdAddressablesRunner)로
    //   A) AssetBundle 그룹 → B) Groups 창의 "Convert schema(s) to Content Directory"와 같은 변환 후
    // 각각 Player를 빌드한다. 빌드 스크립트는 UI의 "Default Build Script"인 BuildScriptSchemaDriven.
    // B0는 AssetBundle 전용 빌더(BuildScriptPackedMode, "Build Script (AssetBundles)")로 변환된 그룹을 빌드한 경우.
    // Unity.exe -batchmode -quit -projectPath . -executeMethod ContentsDirectories.CdAddressablesConvertTools.PhaseA (그다음 PhaseB0, PhaseB)
    public static class CdAddressablesConvertTools
    {
        const string CubePath = "Assets/Prefab/Cube.prefab";
        const string GroupName = "Conv_Group";

        public static void PhaseA()
        {
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            foreach (var name in new[] { "CD_Group", "AB_Group", GroupName })
            {
                var g = settings.FindGroup(name);
                if (g != null) settings.RemoveGroup(g);
            }
            var group = settings.CreateGroup(GroupName, false, false, false, null,
                typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
            settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(CubePath), group).address = "Cube";
            BuildAll(settings, "A", typeof(BuildScriptSchemaDriven));
        }

        public static void PhaseB0() => ConvertAndBuild("B0", typeof(BuildScriptPackedMode));
        public static void PhaseB() => ConvertAndBuild("B", typeof(BuildScriptSchemaDriven));

        static void ConvertAndBuild(string phase, Type builder)
        {
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            var group = settings.FindGroup(GroupName);
            ConvertGroupToContentDirectory(settings, group);
            // UI도 변환 직후 기본 빌드 경로의 AssetBundle을 지운다
            string buildPath = Path.Combine(Addressables.LibraryPath, "aa");
            if (Directory.Exists(buildPath)) Directory.Delete(buildPath, true);
            BuildAll(settings, phase, builder);
        }

        // AddressableAssetsSettingsGroupTreeView.ConvertGroupToContentDirectory(로컬 그룹 경로)와 같은 처리
        static void ConvertGroupToContentDirectory(AddressableAssetSettings settings, AddressableAssetGroup group)
        {
            var bundled = group.GetSchema<BundledAssetGroupSchema>();
            var cd = group.GetSchema<ContentDirectoryGroupSchema>() ?? group.AddSchema<ContentDirectoryGroupSchema>(false);
            cd.BuildPath.SetVariableById(settings, bundled.BuildPath.Id);
            cd.LoadPath.SetVariableById(settings, bundled.LoadPath.Id);
            bundled.IsEnabled = false; // 지우지 않고 비활성화
            cd.IsEnabled = true;
            EditorUtility.SetDirty(group);
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.GroupSchemaModified, group, true, true);
            Debug.Log($"[CDM4b] converted {group.Name}: schemas=[{string.Join(", ", group.Schemas.Select(s => $"{s.GetType().Name}({(s is BundledAssetGroupSchema b ? b.IsEnabled : s is ContentDirectoryGroupSchema c ? c.IsEnabled : true)})"))}]");
        }

        static void BuildAll(AddressableAssetSettings settings, string phase, Type builder)
        {
            settings.ActivePlayerDataBuilderIndex = settings.DataBuilders.FindIndex(b => b.GetType() == builder);
            AssetDatabase.SaveAssets();
            AddressableAssetSettings.CleanPlayerContent(null);
            UnityEditor.Build.Pipeline.Utilities.BuildCache.PurgeCache(false);

            AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
            Debug.Log($"[CDM4b] {phase} builder={settings.ActivePlayerDataBuilder.GetType().Name}(\"{settings.ActivePlayerDataBuilder.Name}\") error='{result.Error}' bundles={result.AssetBundleBuildResults.Count} contentDirs={result.ContentDirectoryBuildResults.Count}");
            foreach (var b in result.AssetBundleBuildResults) Debug.Log($"[CDM4b] {phase} bundle {Path.GetFileName(b.FilePath)}");
            foreach (var c in result.ContentDirectoryBuildResults) Debug.Log($"[CDM4b] {phase} contentdir {c.ContentDirectoryPath} report={c.BuildReportDirectory}");
            Debug.Log($"[CDM4b] {phase} aa files: {string.Join(", ", Directory.GetFiles(Path.Combine(Addressables.LibraryPath, "aa"), "*", SearchOption.AllDirectories).Select(Path.GetFileName))}");

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/SampleScene.unity" },
                locationPathName = $"Builds/MeasureAddr/{phase}/ContentDirectoriesStudy.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            Debug.Log($"[CDM4b] {phase} player {report.summary.result} {report.summary.totalSize} bytes");
        }
    }
}
