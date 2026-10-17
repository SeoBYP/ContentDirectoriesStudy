using UnityEditor;
using UnityEditor.Build.Reporting;

namespace ContentsDirectories
{
    public static class BuildContentPackRootAsset
    {
        [MenuItem("Build/Content Directories/Content Pack")]
        public static void Build()
        {
            var parameters = new BuildContentDirectoryParameters
            {
                outputPath = "Builds/ContentDirectories/ContentPack",
                rootAssetPaths = new[]
                {
                    "Assets/ContentPacks/ContentPackRootAsset.asset"
                },
                options = BuildContentOptions.None,
            };
            BuildReport report = BuildPipeline.BuildContentDirectory(parameters);
            if (report.summary.result == BuildResult.Succeeded)
                UnityEngine.Debug.Log($"Content directory built to: {parameters.outputPath}");
        }
    }
}