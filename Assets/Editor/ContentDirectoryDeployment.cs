using System.IO;
using UnityEditor;
using UnityEditor.Build;

namespace ContentsDirectories
{
    public class ContentDirectoryDeployment : BuildPlayerProcessor
    {
        // CD3 비교군용: true면 ScriptsOnlyCache 디렉터리를 Player 빌드에 넘기지 않음
        public static bool SkipReportDirectoryForMeasurement;

        private const string k_WebPreloadFolder =
            "Library/PlayerDataCache/WebGLPreloadedStreamingAssets";

        private const string k_WebPreloadManifest =
            k_WebPreloadFolder + "/content-directories.manifest";
        
        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
            string buildsPath = ContentDirectoryManager.ContentBuildsPath;

            if (!Directory.Exists(buildsPath))
                return;
            
            string[] directories = Directory.GetDirectories(buildsPath);
            if (directories.Length == 0)
                return;
            
            buildPlayerContext.AddAdditionalPathToStreamingAssets(buildsPath);

            foreach (string directory in directories)
            {
                if (SkipReportDirectoryForMeasurement)
                    break;

                if (BuildHistory.TryGetBuildSummaryForOutputPath(directory, out BuildReportSummary summary))
                {
                    if (BuildHistory.TryGetBuildReportDirectory(
                            summary.BuildSessionGUID, out string reportDirectory))
                    {
                        buildPlayerContext.AddPreviousBuildReportDirectory(
                            reportDirectory);
                    }
                }
            }
            
            if (buildPlayerContext.BuildPlayerOptions.target ==
                BuildTarget.WebGL)
            {
                WriteWebPreloadManifest(buildsPath, directories);
            }
            else if (File.Exists(k_WebPreloadManifest))
            {
                // Remove the manifest from an earlier Web build, so that it
                // can't preload files in a later one.
                File.Delete(k_WebPreloadManifest);
            }
        }

        static void WriteWebPreloadManifest(string buildsPath,
            string[] directories)
        {
            Directory.CreateDirectory(k_WebPreloadFolder);

            using StreamWriter writer = new StreamWriter(k_WebPreloadManifest, false);
            foreach (string directory in directories)
            {
                string[] files = Directory.GetFiles(
                    directory, "*", SearchOption.AllDirectories);
                    
                string directoryName = Path.GetFileName(directory);
                foreach (string file in files)
                {
                    string relativePath = directoryName + "/" +
                                          Path.GetRelativePath(directory, file);
                    writer.WriteLine(relativePath.Replace('\\', '/'));
                }
            }
        }
    }
}