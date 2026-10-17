using System.Collections;
using System.Collections.Generic;
using ContentsDirectories;
using Unity.Loading;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class ContentBootstrap : MonoBehaviour
{
    private ContentDirectoryManager m_Dirs = new ContentDirectoryManager();
    private ContentPackRootAsset m_Root;
    private GameObject m_Instance;
    Loadable<GameObject> m_Second; // 반드시 클래스 필드로
    
    GameObject m_Held;          // 케이스 a: Load()로 받은 원본
    GameObject m_HeldInstance;  // 케이스 b: 씬에 둔 인스턴스

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        Dump("M0 등록 전");
        m_Dirs.Register("ContentPack");
        var roots = ContentLoadManager.GetRootAssets<ContentPackRootAsset>();
        Debug.Log($"Root 개수: {roots.Length}");
        m_Root = roots[0];
        Debug.Log($"metadata: {m_Root.metadata.description}");
        Dump("M1 등록 직후");
    }

    // Update is called once per frame
    void Update()
    {
        var kb = Keyboard.current;
        if (kb.numpad1Key.wasPressedThisFrame)
        {
            m_Instance = Instantiate(m_Root.bonusPrefab.Load());
            Dump("M2 Cube Load");
        }

        if (kb.numpad2Key.wasPressedThisFrame)
        {
            if (!SceneManager.GetSceneByLoadableSceneId(m_Root.bonusLevel).isLoaded)
            {
                SceneManager.LoadSceneAsync(m_Root.bonusLevel, new LoadSceneParameters(LoadSceneMode.Additive));
            }
        }

        if (kb.numpad3Key.wasPressedThisFrame)
        {
            Destroy(m_Instance);
            m_Root.bonusPrefab.Release();
            Dump("M4 Cube Release");
        }

        if (kb.numpad4Key.wasPressedThisFrame)
        {
            var scene = SceneManager.GetSceneByLoadableSceneId(m_Root.bonusLevel);
            if (scene.isLoaded)
            {
                var op = SceneManager.UnloadSceneAsync(scene); // 로드할 때 저장해 둔 Scene
                if (op != null)
                    op.completed += _ => Dump("M5 씬 언로드 완료");
            }
        }

        if (kb.numpad5Key.wasPressedThisFrame)
        {
            // 나중에
            int contentMats = 0;
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                if (m.hideFlags == HideFlags.None && !m.name.StartsWith("Hidden/"))
                    contentMats++;
            Debug.Log($"추가된 Material: {contentMats}");
        }

        if (kb.numpad6Key.wasPressedThisFrame)
        {
            var a = m_Root.bonusPrefab.Load();
            var b = m_Root.bonusPrefab.Load();
            Debug.Log($"같은 객체? {ReferenceEquals(a, b)}");
            StartCoroutine(LogFrames("E3 Load×2"));
        }

        if (kb.numpad7Key.wasPressedThisFrame)
        {
            m_Root.bonusPrefab.Release();
            StartCoroutine(LogFrames("E3 Release 1회"));
        }

        if (kb.numpad8Key.wasPressedThisFrame)
        {
            m_Root.bonusPrefab.Release();
            StartCoroutine(LogFrames("E3 Release 2회"));
        }

        // Update()
        if (kb.qKey.wasPressedThisFrame)
        {
            var id = m_Root.bonusPrefab.LoadableObjectId;
            m_Second = new Loadable<GameObject>(id); // 컴파일 에러 나면 (in id)
            var a = m_Root.bonusPrefab.Load();
            var b = m_Second.Load();
            Debug.Log($"두 핸들 결과가 같은 객체? {ReferenceEquals(a, b)}");
            StartCoroutine(LogFrames("E3b 두 핸들 Load"));
        }

        if (kb.wKey.wasPressedThisFrame)
        {
            m_Root.bonusPrefab.Release();
            StartCoroutine(LogFrames("E3b 원본만 Release"));
        }

        if (kb.eKey.wasPressedThisFrame)
        {
            m_Second.Release();
            StartCoroutine(LogFrames("E3b 두번째도 Release"));
        }

        // R: 로드 + 인스턴스 생성
        if (kb.rKey.wasPressedThisFrame)
        {
            m_Held = m_Root.bonusPrefab.Load();
            m_HeldInstance = Instantiate(m_Held);
            StartCoroutine(CheckHeld("E1′ Load"));
        }
// T: 아무것도 정리하지 않고 Release만
        if (kb.tKey.wasPressedThisFrame)
        {
            m_Root.bonusPrefab.Release();
            StartCoroutine(CheckHeld("E1′ Release 후"));
        }
// Y: 해제된 원본으로 Instantiate 시도
        if (kb.yKey.wasPressedThisFrame)
        {
            try { var go = Instantiate(m_Held); Debug.Log($"Instantiate 성공: {go.name}"); }
            catch (System.Exception e) { Debug.Log($"Instantiate 실패: {e.GetType().Name} / {e.Message}"); }
        }
// U: 정리
        if (kb.uKey.wasPressedThisFrame) { if (m_HeldInstance) Destroy(m_HeldInstance); }
        
        if (kb.numpad0Key.wasPressedThisFrame)
        {
            Dump("현재 상태");
        }
    }

    void Dump(string label)
    {
        var mats = Resources.FindObjectsOfTypeAll<Material>();
        var shaders = Resources.FindObjectsOfTypeAll<Shader>();
        Debug.Log(
            $"[{label}] Material {mats.Length}개, Shader {shaders.Length}개, Cube 상태: {(m_Root != null ? m_Root.bonusPrefab.Status.ToString() : "-")}");
    }

    int CountContentMats()
    {
        int n = 0;
        foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
            if (m.hideFlags == HideFlags.None && !m.name.StartsWith("Hidden/"))
                n++;
        return n;
    }

    IEnumerator LogFrames(string label)
    {
        for (int i = 0; i <= 3; i++)
        {
            // LogFrames의 로그 줄을 두 핸들 상태가 다 보이게 수정
            Debug.Log(
                $"[{label} +{i}프레임] 콘텐츠 {CountContentMats()}, 원본 {m_Root.bonusPrefab.Status}, 두번째 {m_Second.Status}");
            yield return null;
        }
    }
    
    IEnumerator CheckHeld(string label)
    {
        for (int i = 0; i <= 3; i++)
        {
            var r = m_HeldInstance ? m_HeldInstance.GetComponent<Renderer>() : null;
            var mat = r ? r.sharedMaterial : null;
            string matName;
            try { matName = r.sharedMaterial.name; } catch (System.Exception e) { matName = $"<{e.GetType().Name}>"; }

            Debug.Log($"[{label} +{i}프레임] " +
                      $"원본 ==null:{m_Held == null} / 진짜null:{ReferenceEquals(m_Held, null)} | " +
                      $"인스턴스 살아있음:{(bool)m_HeldInstance} | " +
                      $"머티리얼 ==null:{mat == null} / 진짜null:{ReferenceEquals(r?.sharedMaterial, null)} / 이름:{matName} | " +
                      $"콘텐츠 {CountContentMats()}");
            yield return null;
        }
    }

    void OnDestroy() => m_Dirs.UnregisterAll();
}