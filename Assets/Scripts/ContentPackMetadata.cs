using UnityEngine;
using Unity.Loading;


namespace ContentsDirectories
{
    [CreateAssetMenu(fileName = "ContentPackMetadata", menuName = "Content/Content Pack Metadata")]
    public class ContentPackMetadata : ScriptableObject
    {
        public string description;

        // Stored as a string because Unity doesn't serialize System.DateTime.
        public string expirationDate;

        public int difficulty;
    }
}