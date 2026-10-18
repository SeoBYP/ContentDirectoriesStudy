using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace ContentsDirectories
{
    // CD4b: 평범한 Addressables 로드 코드. AssetBundle 그룹일 때와 Content Directory로 변환한 뒤에
    // 이 코드를 한 글자도 바꾸지 않고 그대로 실행한다. -cdaddr 인자로 실행했을 때만 동작.
    public class CdAddressablesRunner : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-cdaddr") < 0)
                return;
            foreach (var b in FindObjectsByType<ContentBootstrap>(FindObjectsSortMode.None))
                b.enabled = false;
            new GameObject("CdAddressablesRunner").AddComponent<CdAddressablesRunner>();
        }

        IEnumerator Start()
        {
            for (int i = 0; i < 10; i++) yield return null;

            Log("start");
            var init = Addressables.InitializeAsync(false);
            yield return Wait(init, "init");
            if (!init.IsDone) { Log("init 미완료로 중단"); Application.Quit(); yield break; }
            Addressables.Release(init);

            var locs = Addressables.LoadResourceLocationsAsync("Cube", typeof(GameObject));
            yield return locs;
            foreach (var l in locs.Result)
                Log($"location provider={l.ProviderId} internalId={l.InternalId} deps={string.Join(",", (l.Dependencies ?? new System.Collections.Generic.List<UnityEngine.ResourceManagement.ResourceLocations.IResourceLocation>()).Select(d => d.ProviderId + ":" + d.InternalId))}");
            Addressables.Release(locs);

            // 1) LoadAssetAsync + Instantiate + Release
            var h = Addressables.LoadAssetAsync<GameObject>("Cube");
            yield return h;
            Log($"LoadAssetAsync status={h.Status} result={(h.Result != null ? h.Result.name : "null")} error={h.OperationException?.Message}");
            if (h.Status == AsyncOperationStatus.Succeeded)
            {
                var inst = Instantiate(h.Result);
                var r = inst.GetComponent<Renderer>();
                Log($"Instantiate 컴포넌트=[{string.Join(", ", inst.GetComponents<Component>().Select(c => c == null ? "<missing>" : c.GetType().Name))}] 머티리얼={(r != null && r.sharedMaterial != null ? r.sharedMaterial.name + "/" + r.sharedMaterial.shader.name : "null")} {Snap()}");
                yield return null;
                Destroy(inst);
                Addressables.Release(h);
                for (int i = 0; i < 3; i++) yield return null;
                Log($"Release 후 {Snap()}");
            }

            // 2) InstantiateAsync + ReleaseInstance
            var ih = Addressables.InstantiateAsync("Cube");
            yield return ih;
            Log($"InstantiateAsync status={ih.Status} result={(ih.Result != null ? ih.Result.name : "null")} {Snap()}");
            if (ih.Status == AsyncOperationStatus.Succeeded)
            {
                Addressables.ReleaseInstance(ih.Result);
                for (int i = 0; i < 3; i++) yield return null;
                Log($"ReleaseInstance 후 {Snap()}");
            }

            Log("done");
            Application.Quit();
        }

        // 끝나지 않는 핸들에 막히지 않도록 15초 제한을 두고 상태를 기록
        static IEnumerator Wait(AsyncOperationHandle h, string label)
        {
            float t = Time.realtimeSinceStartup;
            while (!h.IsDone && Time.realtimeSinceStartup - t < 15f)
                yield return null;
            Log($"{label} done={h.IsDone} status={h.Status} pct={h.PercentComplete:F2} {(Time.realtimeSinceStartup - t):F2}s err={h.OperationException?.Message}");
        }

        static string Snap()
        {
            var names = Resources.FindObjectsOfTypeAll<Material>()
                .Where(m => m.hideFlags == HideFlags.None && !m.name.StartsWith("Hidden/"))
                .Select(m => m.name).OrderBy(n => n).ToArray();
            return $"content={names.Length} [{string.Join(",", names)}]";
        }

        static void Log(string msg) => Debug.Log($"[CDM4b] {msg}");
    }
}
