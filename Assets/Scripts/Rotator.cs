using UnityEngine;

namespace ContentsDirectories
{
    // CD3: Cube.prefab(콘텐츠)에만 붙는 컴포넌트. Player 코드에서는 절대 참조하지 않는다.
    public class Rotator : MonoBehaviour
    {
        public float degreesPerSecond = 45f;

        void Update() => transform.Rotate(0f, degreesPerSecond * Time.deltaTime, 0f);
    }
}
