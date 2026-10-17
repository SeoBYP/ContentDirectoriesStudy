using System.Collections.Generic;
using Unity.Loading;
using UnityEngine;

namespace ContentsDirectories
{
    public class ContentDirectoryManager
    {
        public const string ContentBuildsPath = "Builds/ContentDirectories";

        readonly List<ContentDirectoryHandle> m_Handles =
            new List<ContentDirectoryHandle>();

        static string contentRootPath
        {
            get
            {
#if UNITY_EDITOR
                return ContentBuildsPath;
#elif UNITY_WEBGL
                // Preloaded StreamingAssets files are in the Emscripten virtual
                // file system at this fixed path.
                return "/vfs_streamingassets";
#else
                return Application.streamingAssetsPath;
#endif
            }
        }

        public void Register(string directoryName)
        {
            string path = contentRootPath + "/" + directoryName;
            m_Handles.Add(ContentLoadManager.RegisterContentDirectory(path));
        }

        public void UnregisterAll()
        {
            for (int i = m_Handles.Count - 1; i >= 0; i--)
            {
                ContentLoadManager.UnregisterContentDirectory(m_Handles[i]);
            }

            m_Handles.Clear();
        }
    }
}