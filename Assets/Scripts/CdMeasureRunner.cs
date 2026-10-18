using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Loading;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace ContentsDirectories
{
    // -cdmeasure 인자로 실행했을 때만 동작하는 자동 측정 러너. 결과는 "[CDM]" 로그로 남긴다.
    public class CdMeasureRunner : MonoBehaviour
    {
        readonly ContentDirectoryManager m_Dirs = new ContentDirectoryManager();
        ContentPackRootAsset m_Root;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-cdmeasure") < 0)
                return;
            foreach (var b in FindObjectsByType<ContentBootstrap>(FindObjectsSortMode.None))
                b.enabled = false;

            // CD6: -grd off 이면 첫 렌더 전에 GPU Resident Drawer를 끈다
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null && Arg("-grd") == "off")
                urp.gpuResidentDrawerMode = GPUResidentDrawerMode.Disabled;
            Log($"pipeline={(urp != null ? urp.name : "null")} gpuResidentDrawerMode={(urp != null ? urp.gpuResidentDrawerMode.ToString() : "-")}");

            new GameObject("CdMeasureRunner").AddComponent<CdMeasureRunner>();
        }

        IEnumerator Start()
        {
            yield return Frames(10); // URP 초기화 이후부터 측정
            m_Dirs.Register("ContentPack");
            m_Root = ContentLoadManager.GetRootAssets<ContentPackRootAsset>()[0];
            yield return Frames(2);
            Log($"baseline {Snap()}");

            if (Arg("-cd56") != null)
            {
                yield return Cd6();
                yield return Cd5();
                m_Dirs.UnregisterAll();
                Log("done");
                Application.Quit();
                yield break;
            }

            yield return Cd1();
            yield return Cd2();
            yield return Cd3();

            m_Dirs.UnregisterAll();
            Log("done");
            Application.Quit();
        }

        // CD1: Resources.UnloadUnusedAssets가 콘텐츠 에셋에 영향을 주는가
        IEnumerator Cd1()
        {
            yield return UnloadUnused("CD1 아무것도 로드 안 함");

            var go = m_Root.bonusPrefab.Load();
            go = null; // C# 참조도, 인스턴스도 없이 핸들만 잡은 상태
            yield return Frames(2);
            Log($"CD1 Cube 핸들만 Load {Snap()} status={m_Root.bonusPrefab.Status}");
            yield return UnloadUnused("CD1 Cube 핸들 보유 중");
            Log($"CD1 UnloadUnused 후 {Snap()} status={m_Root.bonusPrefab.Status} target==null:{m_Root.bonusPrefab.Target == null}");
            m_Root.bonusPrefab.Release();
            yield return Frames(2);
            Log($"CD1 Cube Release 후 {Snap()}");

            m_Root.goldMaterial.Load();
            yield return Frames(2);
            Log($"CD1 Gold 핸들만 Load {Snap()}");
            yield return UnloadUnused("CD1 Gold 핸들 보유 중");
            Log($"CD1 UnloadUnused 후 {Snap()} target==null:{m_Root.goldMaterial.Target == null}");
            m_Root.goldMaterial.Release();
            yield return Frames(2);
            Log($"CD1 Gold Release 후 {Snap()}");
        }

        // CD2: Loadable<Material>로 올린 머티리얼과 씬의 같은 머티리얼이 같은 객체인가
        IEnumerator Cd2()
        {
            // 순서 1: 씬 먼저 → 머티리얼 핸들
            Scene scene = default;
            yield return LoadScene(s => scene = s);
            var sceneGold = FindMaterial(scene, "Gold");
            Log($"CD2-1 씬 로드 {Snap()} sceneGold={(sceneGold != null ? sceneGold.GetEntityId().ToString() : "없음")}");
            var gold = m_Root.goldMaterial.Load();
            yield return Frames(2);
            Log($"CD2-1 Gold Load {Snap()} same={ReferenceEquals(gold, sceneGold)} id={gold.GetEntityId()}");
            yield return UnloadScene(scene);
            Log($"CD2-1 씬 언로드 후 {Snap()} gold 살아있음:{gold != null}");
            m_Root.goldMaterial.Release();
            yield return Frames(2);
            Log($"CD2-1 Gold Release 후 {Snap()}");

            // 순서 2: 머티리얼 핸들 먼저 → 씬
            gold = m_Root.goldMaterial.Load();
            yield return Frames(2);
            Log($"CD2-2 Gold Load {Snap()} id={gold.GetEntityId()}");
            yield return LoadScene(s => scene = s);
            sceneGold = FindMaterial(scene, "Gold");
            Log($"CD2-2 씬 로드 {Snap()} same={ReferenceEquals(gold, sceneGold)}");
            m_Root.goldMaterial.Release();
            yield return Frames(2);
            Log($"CD2-2 Gold Release 후(씬 유지) {Snap()} sceneGold 살아있음:{sceneGold != null}");
            yield return UnloadScene(scene);
            Log($"CD2-2 씬 언로드 후 {Snap()}");
        }

        // CD3: 콘텐츠 전용 타입이 Player에 남아 있는가 (스트리핑 비교용)
        IEnumerator Cd3()
        {
            // 타입 이름을 상수 문자열로 쓰면 UnityLinker가 리플렉션 패턴으로 보고 타입을 남길 수 있어서,
            // 이름을 런타임에 뒤집어 만들어 측정 코드가 결과에 영향을 주지 않게 한다.
            Log("CD3 타입 존재: " + string.Join(" ", new[]
            {
                "prahSC-ylbmessA,rotatoR.seirotceriDstnetnoC",
                "emitnuR.eroC.senilepiPredneR.ytinU,aremaCeerF.gniredneR.enignEytinU",
                "emitnuR.eroC.senilepiPredneR.ytinU,emuloVeborP.gniredneR.enignEytinU",
                "emitnuR.lasrevinU.senilepiPredneR.ytinU,stnemtsujdAroloC.lasrevinU.gniredneR.enignEytinU",
                "emitnuR.lasrevinU.senilepiPredneR.ytinU,eralFsneLecapSneercS.lasrevinU.gniredneR.enignEytinU",
            }.Select(r => { var n = Rev(r).Replace(",", ", "); return $"{n.Split(',')[0].Split('.').Last()}={Type.GetType(n) != null}"; })));

            var inst = Instantiate(m_Root.bonusPrefab.Load());
            var names = inst.GetComponents<Component>().Select(c => c == null ? "<missing>" : c.GetType().Name);
            float y0 = inst.transform.eulerAngles.y;
            yield return Frames(10);
            Log($"CD3 Cube 컴포넌트: {string.Join(", ", names)} | GetComponent(Rotator)={(inst.GetComponent(Rev("rotatoR")) != null)} | 회전 {y0:F1}→{inst.transform.eulerAngles.y:F1}");
            Destroy(inst);
            yield return null;
            m_Root.bonusPrefab.Release();
            yield return Frames(2);
        }

        // CD6: E1′(인스턴스가 살아 있는데 핸들 Release)일 때 화면에 어떻게 보이는가. GRD 켜짐/꺼짐 비교용
        IEnumerator Cd6()
        {
            var cam = Camera.main;
            var inst = Instantiate(m_Root.bonusPrefab.Load(), cam.transform.position + cam.transform.forward * 3f, Quaternion.identity);
            yield return Frames(5);
            yield return Shot("CD6_before", cam, inst.transform.position);
            m_Root.bonusPrefab.Release();
            yield return Frames(5);
            var r = inst.GetComponent<Renderer>();
            Log($"CD6 Release 후 sharedMaterial==null:{r.sharedMaterial == null} renderer.enabled:{r.enabled} {Snap()}");
            yield return Shot("CD6_after", cam, inst.transform.position);
            Destroy(inst);
            yield return Frames(2);
        }

        // CD5: InDoor 씬을 씬 자체 카메라로 찍는다(Normal / NoReport 비교용)
        IEnumerator Cd5()
        {
            Scene scene = default;
            yield return LoadScene(s => scene = s);
            var sampleCam = Camera.main;
            Camera sceneCam = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>(true)).FirstOrDefault();
            if (sceneCam != null && sampleCam != null && sampleCam != sceneCam)
                sampleCam.gameObject.SetActive(false);
            int missing = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MonoBehaviour>(true)).Count(m => m == null);
            var compNames = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Component>(true)).Where(c => c != null).Select(c => c.GetType().Name).Distinct().OrderBy(n => n);
            Log($"CD5 sceneCam={(sceneCam != null ? sceneCam.name : "없음")} missingScripts={missing} types=[{string.Join(",", compNames)}]");
            yield return Frames(30); // 조명·프로브 적용 대기
            yield return Shot("CD5_indoor", sceneCam != null ? sceneCam : sampleCam, null);
            if (sampleCam != null) sampleCam.gameObject.SetActive(true);
            yield return UnloadScene(scene);
        }

        // 화면 캡처. world가 있으면 그 지점 픽셀 색, 없으면 전체 평균 밝기를 기록하고 -shotdir에 PNG 저장
        IEnumerator Shot(string label, Camera cam, Vector3? world)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            string info;
            if (world.HasValue)
            {
                var sp = cam.WorldToScreenPoint(world.Value);
                Color32 c = tex.GetPixel(Mathf.Clamp((int)sp.x, 0, tex.width - 1), Mathf.Clamp((int)sp.y, 0, tex.height - 1));
                info = $"center=({(int)sp.x},{(int)sp.y}) rgb=({c.r},{c.g},{c.b})";
            }
            else
            {
                var px = tex.GetPixels32();
                double lum = px.Average(p => 0.2126 * p.r + 0.7152 * p.g + 0.0722 * p.b);
                info = $"meanLuma={lum:F2}";
            }
            string dir = Arg("-shotdir");
            if (dir != null)
            {
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, $"{label}_{Arg("-tag") ?? "run"}.png"), tex.EncodeToPNG());
            }
            Log($"{label} {tex.width}x{tex.height} {info}");
            Destroy(tex);
        }

        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, name);
            if (i < 0) return null;
            return i + 1 < a.Length && !a[i + 1].StartsWith("-") ? a[i + 1] : "";
        }

        IEnumerator LoadScene(Action<Scene> done)
        {
            var op = SceneManager.LoadSceneAsync(m_Root.bonusLevel, new LoadSceneParameters(LoadSceneMode.Additive));
            while (!op.isDone) yield return null;
            yield return Frames(2);
            done(SceneManager.GetSceneByLoadableSceneId(m_Root.bonusLevel));
        }

        IEnumerator UnloadScene(Scene scene)
        {
            var op = SceneManager.UnloadSceneAsync(scene);
            while (!op.isDone) yield return null;
            yield return Frames(2);
        }

        IEnumerator UnloadUnused(string label)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int frames = 0;
            var op = Resources.UnloadUnusedAssets();
            while (!op.isDone) { frames++; yield return null; }
            sw.Stop();
            yield return Frames(2);
            Log($"{label}: UnloadUnusedAssets {sw.Elapsed.TotalMilliseconds:F2} ms ({frames}프레임)");
        }

        static Material FindMaterial(Scene scene, string name)
        {
            if (!scene.IsValid()) return null;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            foreach (var m in r.sharedMaterials)
                if (m != null && m.name == name) return m;
            return null;
        }

        // 콘텐츠 머티리얼(hideFlags None, Hidden/ 제외) 수와 이름
        static string Snap()
        {
            var names = new List<string>();
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                if (m.hideFlags == HideFlags.None && !m.name.StartsWith("Hidden/"))
                    names.Add(m.name);
            names.Sort();
            return $"content={names.Count} [{string.Join(",", names)}]";
        }

        static string Rev(string s)
        {
            var a = s.ToCharArray();
            Array.Reverse(a);
            return new string(a);
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static void Log(string msg) => Debug.Log($"[CDM] {msg}");
    }
}
