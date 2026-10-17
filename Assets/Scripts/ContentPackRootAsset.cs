using UnityEngine;
using Unity.Loading;

namespace ContentsDirectories
{
    [CreateAssetMenu(fileName = "ContentPackRootAsset", menuName = "Content/Content Pack Root Asset")]
    public class ContentPackRootAsset : ScriptableObject
    {
        public Loadable<GameObject> bonusPrefab;

        public LoadableSceneId bonusLevel;
    
        public ContentPackMetadata metadata;
    }

}
