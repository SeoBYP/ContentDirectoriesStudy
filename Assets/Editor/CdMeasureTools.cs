using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using Unity.Loading;

namespace ContentsDirectories
{
    // 배치 모드 측정용.
    // Unity.exe -batchmode -quit -projectPath . -executeMethod ContentsDirectories.CdMeasureTools.All
    public static class CdMeasureTools
    {
        const string RootPath = "Assets/ContentPacks/ContentPackRootAsset.asset";
        const string CubePath = "Assets/Prefab/Cube.prefab";
        const string GoldPath = "Assets/Scenes/InDoor/Gold.mat";

        public static void All()
        {
            Setup();
            BuildContentPackRootAsset.Build();
            BuildPlayer("Builds/Measure/Normal", false);
            BuildPlayer("Builds/Measure/NoReport", true);
        }

        // CD2: Root에 Gold.mat 핸들 연결 / CD3: Cube.prefab에 콘텐츠 전용 컴포넌트 추가
        public static void Setup()
        {
            var root = AssetDatabase.LoadAssetAtPath<ContentPackRootAsset>(RootPath);
            var gold = AssetDatabase.LoadAssetAtPath<Material>(GoldPath);
            root.goldMaterial = new Loadable<Material>(LoadableObjectIdEditorUtility.CreateLoadableObjectId(gold));
            EditorUtility.SetDirty(root);

            var prefab = PrefabUtility.LoadPrefabContents(CubePath);
            if (prefab.GetComponent<Rotator>() == null)
                prefab.AddComponent<Rotator>();
            PrefabUtility.SaveAsPrefabAsset(prefab, CubePath);
            PrefabUtility.UnloadPrefabContents(prefab);

            AssetDatabase.SaveAssets();
            Debug.Log("[CDM] Setup done");
        }

        // CD3: 두 빌드 모두 스트리핑 High, 차이는 ScriptsOnlyCache 디렉터리 전달 여부뿐
        static void BuildPlayer(string dir, bool skipReport)
        {
            var target = NamedBuildTarget.Standalone;
            var prev = PlayerSettings.GetManagedStrippingLevel(target);
            PlayerSettings.SetManagedStrippingLevel(target, ManagedStrippingLevel.High);
            ContentDirectoryDeployment.SkipReportDirectoryForMeasurement = skipReport;
            try
            {
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { "Assets/Scenes/SampleScene.unity" },
                    locationPathName = dir + "/ContentDirectoriesStudy.exe",
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.Development
                });
                Debug.Log($"[CDM] Player {dir}: {report.summary.result}, {report.summary.totalSize} bytes, stripping=High, skipReport={skipReport}");
            }
            finally
            {
                ContentDirectoryDeployment.SkipReportDirectoryForMeasurement = false;
                PlayerSettings.SetManagedStrippingLevel(target, prev);
            }
        }
    }
}
