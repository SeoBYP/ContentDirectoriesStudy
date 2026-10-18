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

        // CD2: InDoor 씬도 쓰는 머티리얼(Gold.mat)을 따로 잡는 핸들
        public Loadable<Material> goldMaterial;
    }

}
