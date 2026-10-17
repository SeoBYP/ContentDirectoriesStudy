# Unity 6.6 Content Directories 학습 보고서

Unity 6.6에서 AssetBundle을 대체하려고 도입된 **Content Directories**를 공식 문서대로 직접 구현하고, 빌드 산출물과 런타임 동작을 **에디터와 Player 양쪽에서 실측**해서 설계 의도, 이득, 한계를 정리한 문서입니다.

| 항목 | 내용 |
|---|---|
| 에디터 | Unity `6000.6.3f1`, URP `17.6.0`, Input System `1.20.0`(새 Input System만 활성) |
| 빌드 타깃 | `StandaloneWindows64` (Development Build) |
| 진행 범위 | 생성 → 빌드 → 런타임 로딩 → Player 배포·검증 완료. 코드 스트리핑 비교 실험과 Addressables 변환은 미진행 |
| 표기 | **실측**: 이 프로젝트에서 직접 측정 / **문서**: 공식 문서에 명시 / **추정**: 근거로부터 추론(설계 의도 포함) |

---

## 목차
1. [요약](#1-요약)
2. [Content Directories란](#2-content-directories란)
3. [핵심 개념: Root와 참조 그래프](#3-핵심-개념-root와-참조-그래프)
4. [빌드: 그래프 수집과 산출물](#4-빌드-그래프-수집과-산출물)
5. [런타임 API와 설계 의도](#5-런타임-api와-설계-의도)
6. [에디터 실험 결과](#6-에디터-실험-결과)
7. [Player 검증 결과](#7-player-검증-결과)
8. [참조 카운트 규칙 (확정)](#8-참조-카운트-규칙-확정)
9. [AssetBundle과의 비교](#9-assetbundle과의-비교)
10. [한계점 정리](#10-한계점-정리)
11. [미확인 항목](#11-미확인-항목)
12. [프로젝트 구조와 재현 방법](#12-프로젝트-구조와-재현-방법)
13. [참고 문서](#13-참고-문서)

---

## 1. 요약

- **무엇인가**: 게임 데이터를 Player 밖의 폴더로 빌드하고, 실행 중에 **에셋 단위**로 로드·해제하는 엔진 내장 시스템. AssetBundle이 번들 하나에 묶어 두었던 "정리 / 파일 / 로드·해제" 단위를 **Root / 디렉터리 / `Loadable`** 로 분리했다.
- **빌드**: Root에서 GUID 참조를 재귀적으로 따라가 에셋을 모으고, 에셋 하나를 해시 이름의 파일 하나로 저장한다. 같은 빌드 안에서는 자동으로 중복이 제거된다. 변경 없는 재빌드는 64.9초 → 8.9초(실측).
- **런타임**: 등록하면 Root와 실선 의존성만 올라오고(Direct 6.93 KB / Total 2.85 MB), 점선(`Loadable`, `LoadableSceneId`)은 필요할 때 로드한다. 해제는 **핸들 단위 참조 카운트**이고 마지막 참조가 사라진 **1프레임 뒤** 언로드된다(에디터·Player 동일, 실측).
- **가장 큰 한계**: 로컬 전용(원격 배포·패치 없음), 빌드끼리 중복 제거 불가, 그리고 **`Instantiate`한 인스턴스는 참조로 세지 않아서** 사용 중에 해제하면 인스턴스가 경고 없이 화면에서 사라진다(실측).

---

## 2. Content Directories란

```
[Player 빌드]  코드 + 시작 씬(SampleScene)
     │ 실행 중 RegisterContentDirectory(".../ContentPack")
     ▼
[콘텐츠 디렉터리]  .cf, .resS, 매니페스트 json, BuildManifestHash.txt
   에디터: Builds/ContentDirectories/ContentPack/
   Player: <Product>_Data/StreamingAssets/ContentPack/
```

| 구분 | 내용 |
|---|---|
| **왜 만들었나** | AssetBundle은 번들을 크게 만들면 불필요한 것까지 로드되고, 잘게 만들면 관리 오버헤드와 공용 에셋 중복이 생겼다. 단위를 분리해서 이 딜레마를 없앤 것으로 보인다(추정). 문서는 "AssetBundle 시스템의 대체"라고 소개한다. |
| **이득** | 빌드 내 자동 중복 제거, 에셋 단위 의존성과 해제, 타입 안전한 참조(`Loadable<T>`), 증분 빌드, `UnloadUnusedAssets` 같은 전체 스캔 없이 해제(문서, 실측) |
| **단점** | 로컬 전용, 빌드끼리 중복, 런타임 수명 관리가 개발자 책임, 사용 중 해제에 대한 보호·경고 없음(문서, 실측) |

---

## 3. 핵심 개념: Root와 참조 그래프

### 3.1 Root Asset: 콘텐츠 빌드의 "Scene List"
Player 빌드가 **Scene List**에서 출발해 참조된 것을 담듯, 콘텐츠 빌드는 **Root Asset**(직접 만든 `ScriptableObject`)에서 출발해 닿는 것을 모두 담습니다. Root는 런타임에서 콘텐츠로 들어가는 **진입점(메뉴판)** 이기도 합니다. 파일 이름이 해시라서 경로 문자열로는 찾을 수 없기 때문입니다.

```csharp
public class ContentPackRootAsset : ScriptableObject
{
    public ContentPackMetadata metadata;     // 일반 참조 (실선)
    public Loadable<GameObject> bonusPrefab; // 지연 참조 (점선)
    public LoadableSceneId bonusLevel;       // 씬 참조   (점선)
}
```

### 3.2 참조 3종류: 실선과 점선
| 참조 | 빌드 | 런타임 |
|---|---|---|
| 일반 참조 (실선) | 포함 | 소유자와 **같이 로드** |
| `Loadable<T>` (점선) | 포함 | `Load()` 해야 로드, `Release()`로 해제 |
| `LoadableSceneId` (점선) | 포함 | `SceneManager.LoadSceneAsync`로 로드 |

**빌드는 실선과 점선을 전부 따라가고, 런타임 로드는 점선에서 멈춥니다.** Root 설계는 곧 "어디에 점선을 두어 로딩을 끊을지" 정하는 일이며, 원칙은 **수명이 같은 것끼리는 실선, 수명이 다른 경계에는 점선**입니다. `Loadable<T>`는 Root뿐 아니라 MonoBehaviour 필드에도 둘 수 있습니다(문서). 예: 로비 씬의 컨트롤러가 인벤토리 UI 프리팹을 `Loadable`로 들고 있다가 열 때만 로드.

### 3.3 참조는 무엇으로 찾나: GUID + fileID
`ContentPackRootAsset.asset`의 실제 저장 값(실측):
```yaml
bonusPrefab:
  m_LoadableObjectId: {fileID: 645800571622001937, loadable: 1, guid: 462de8f7fb5dac54eb97111ce3d2eda1, type: 3}
bonusLevel:
  m_SceneGUID: 988cbc230d1f5174ab9aa0a8beda1dfe
```
- `guid` = `Cube.prefab.meta`의 GUID(어느 파일), `fileID` = 프리팹 안 루트 GameObject 번호(파일 안의 어느 오브젝트)
- 씬은 파일 전체가 로드 단위라서 **GUID만** 저장
- `loadable: 1`이 "빌드에는 넣되 로드는 끊는다"는 점선 표식
- 씬 안 오브젝트끼리는 `{fileID: X}`(GUID 없음), 바깥 에셋은 `{fileID, guid, type}`. **그래프를 넓히는 것은 GUID가 붙은 참조뿐**
- GUID의 원본은 `.meta`라서 이름을 바꾸거나 옮겨도 유지되고, **`.meta`를 잃으면 참조가 끊김**

### 3.4 Root, 디렉터리, Loadable의 역할 분담
| 나누는 것 | 정하는 것 | 따로 할 수 있는 것 | 대가 |
|---|---|---|---|
| `Loadable` | **메모리** 단위 | 에셋 하나를 따로 Load/Release | Load/Release 짝 관리 |
| Root | **목차(찾는 방법)** 단위 | `GetRootAssets<T>()`로 묶음별 조회 | 메모리 이득 없음(등록 시 모든 Root와 실선이 로드) |
| 디렉터리 | **빌드·배포·등록** 단위 | 따로 빌드, 동봉 여부, Register/Unregister | 디렉터리끼리 에셋 중복 |

한 빌드에 Root가 여러 개면 방문 목록을 공유해서 중복이 없고, 디렉터리를 나누면 각자 담아서 중복됩니다. 문서는 "대부분 Root 하나로 충분하고, 선택 설치 콘텐츠 팩 같은 경우에만 늘려라"고 권합니다. 판단 기준은 **"설치·배포 단위로 붙였다 떼야 하나? → 디렉터리 분리, 아니면 한 디렉터리 + `Loadable`"** 입니다.

---

## 4. 빌드: 그래프 수집과 산출물

### 4.1 빌드 단계 (실측, `BuildLog.jsonl`)
```
Gather and collect build metadata   ① Root에서 GUID 참조를 따라가며 방문 목록 수집
Calculate build layout              ② 에셋 → 파일 배치, 실선/점선 기록
Generate build instructions         ③ 에셋별 작업 지시서 (Library/BuildInstructions/<GUID>.buildinst)
Execute instructions                ④ 직렬화, 셰이더 프리컴파일, 파일 쓰기
Generate build manifest             ⑤ 런타임용 매니페스트 생성
```
개념적으로는 **방문 목록을 둔 재귀 탐색**이며, 키가 에셋 GUID라서 같은 에셋은 한 번만 담깁니다. 이것이 "빌드 내 자동 중복 제거"입니다. 실제 알고리즘은 비공개입니다(추정).
```text
Collect(asset):
    if asset in visited: return
    visited.add(asset)
    for ref in asset의 GUID 참조(실선, Loadable, 씬 참조 전부):
        Collect(ref)
```
순환 참조는 런타임 로드 순서를 정할 수 없어서 **고리 전체를 한 덩어리로 묶고, 64 MB를 넘으면 빌드 실패**합니다(문서, `circularrefs.json`).

### 4.2 산출물 (실측)
| 파일 | 개수 | 역할 |
|---|---|---|
| `<해시>.cf` | 18 | 직렬화된 오브젝트. 에셋 하나 = 파일 하나 |
| `<해시>.resS` | 5 | 텍스처·메시 같은 대용량 데이터 |
| `<해시>.json` | 1 | 런타임 매니페스트 |
| `BuildManifestHash.txt` | 1 | 현재 매니페스트를 가리키는 고정 이름 포인터. `RegisterContentDirectory`가 가장 먼저 읽음 |

파일 이름이 **내용 해시**라서 내용이 같으면 이름도 같고, 재빌드 때 다시 쓰지 않습니다. 콘텐츠가 쓰는 셰이더(`Lit.shader`)도 콘텐츠 빌드 안에 직접 들어갑니다.

### 4.3 `ContentLayout.json`과 런타임 매니페스트 (실측)
`Library/BuildHistory/<빌드ID>/ContentLayout.json`의 파일별 필드:

| 필드 | 뜻 |
|---|---|
| `SourceAssets` | 이 파일에 들어 있는 프로젝트 에셋 |
| `SerializedFileDependencies` | 이 파일을 열 때 **같이 열어야 하는 파일**(실선) |
| `LoadableDependencies` | 이 파일 안의 `Loadable` 대상(점선) |
| `LoadableSceneDependencies` | 이 파일 안의 씬 참조(점선) |

```
5  ContentPackRootAsset.asset
   SerializedFileDependencies: [4, 14]           ← 스크립트 정보(MonoScript), 메타데이터
   LoadableDependencies:       [Cube의 ObjectIdHash]
   LoadableSceneDependencies:  [Assets/Scenes/InDoor.unity]
```
런타임 매니페스트에는 파일별로 `Index, ID, IsBuiltIn, SerializedFileDependencies, ContentHash`만 있습니다. **런타임에는 실선만 필요**하고, 점선은 오브젝트 안의 `Loadable` 값과 매니페스트의 `Loadables` 표(`ObjectIdHash → 파일 번호 + Identifier`)로 찾습니다. 에디터의 `fileID`는 빌드 후 런타임용 `Identifier`로 다시 매겨집니다(`645800571622001937` → `7048807285321821040`).

### 4.4 빌드 측정 (실측)
| 항목 | 1차 빌드 | 2차 빌드(변경 없음) |
|---|---|---|
| 전체 시간 | 64,991 ms | **8,853 ms** |
| 스크립트 컴파일 | 20.9초 | 7.1초 |
| 데이터 빌드 실행 | 42.3초 | 0.5초 미만 |
| 재빌드 / 재사용 파일 | 18 / 0 | **0 / 18** |

Build Analysis(Window > Analysis > Build Analysis):

| Root | Direct Size | Direct Assets | Total Size | Total Assets |
|---|---|---|---|---|
| ContentPackRootAsset | **6.93 KB** | 2 | **2.85 MB** | 17 |

Direct 6.93 KB = Root 파일 2,268 B + 메타데이터 1,068 B + 스크립트 정보 3,756 B = 7,092 B로 정확히 일치합니다. 약 420배 차이가 점선 두 개의 효과입니다.

### 4.5 코드 스트리핑 연결: `ScriptsOnlyCache.yaml` (실측, 문서)
콘텐츠 빌드는 사용 타입 목록을 남기고, `ContentDirectoryDeployment`가 Player 빌드 때 `AddPreviousBuildReportDirectory`로 전달해 스트리핑을 막습니다.
```yaml
usedScripts:
  Assembly-CSharp.dll:
  - ContentsDirectories.ContentPackMetadata
  - ContentsDirectories.ContentPackRootAsset
  Unity.RenderPipelines.Universal.Runtime.dll:
  - UnityEngine.Rendering.Universal.ColorAdjustments        ← InDoor 볼륨에만 있음
  - UnityEngine.Rendering.Universal.ScreenSpaceLensFlare    ← InDoor 볼륨에만 있음
  ...
```
`ColorAdjustments`와 `ScreenSpaceLensFlare`는 Player의 `SampleSceneProfile`에는 없는 **콘텐츠 전용 타입**입니다. Player에서 InDoor 화면이 에디터와 같았던 것이 이 전달 덕분인지는 비교 실험을 하지 않아 미확인입니다.

---

## 5. 런타임 API와 설계 의도

API 시그니처는 설치된 에디터의 `UnityEngine.ContentLoadModule.xml`에서 확인했습니다.

| API | 동작 |
|---|---|
| `ContentLoadManager.RegisterContentDirectory(path)` | 매니페스트를 읽고 Root와 실선 의존성을 로드, `ContentDirectoryHandle` 반환. 폴더가 없으면 `FileNotFoundException` |
| `ContentLoadManager.GetRootAssets<T>()` | 등록된 디렉터리에서 타입 `T`인 Root 배열 |
| `ContentLoadManager.UnregisterContentDirectory(handle)` | 등록 해제(열린 파일이 있으면 에러 로그, 문서) |
| `Loadable<T>.Load()` / `LoadAsync()` | 동기/비동기 로드, 실패 시 `null` |
| `Loadable<T>.Release()` | 참조를 놓음 |
| `Loadable<T>.Status` / `Target` | `None`/`Loading`/`Loaded`/`Failed`, 로드 결과 |
| `SceneManager.LoadSceneAsync(LoadableSceneId, LoadSceneParameters)` | 콘텐츠 씬 로드 |
| `SceneManager.GetSceneByLoadableSceneId(id)` | 로드된 씬 조회 |

| 설계 요소 | 왜 이렇게 (추정 포함) | 이득 | 단점 |
|---|---|---|---|
| 출력 경로를 `Assets` 밖에 | 해시 파일 수천 개의 임포트 방지(문서) | 에디터 반복 속도 | StreamingAssets 복사 연결 코드 필요, 에디터·Player 경로 분기 |
| 명시적 등록 | 콘텐츠가 Player 밖에 있어 엔진이 존재를 모름. 매니페스트로 조회 표를 만듦 | 가시성·수명 통제, 폴더 단위 붙이기/떼기, 스택 순서 조회 | 동기 파일 접근(Web은 비공식 우회), 등록 시 Root+실선 일괄 로드, 해제 순서 준수 |
| 타입으로 Root 조회 | 해시 파일명이라 경로가 없음 | 이름 변경에 강함, 타입 검사 | Root 타입·필드가 Player와 콘텐츠에서 같아야 함, 에디터 Play가 빌드 누락을 가림 |
| `Loadable<T>` 핸들 | 참조 하나가 에셋 하나의 수명 손잡이 | 에셋 단위 관리, 중복 호출에 안전 | Load/Release 짝 책임, RAII 아님, 인스턴스 공유 금지 |
| 씬은 기존 `SceneManager` 확장 | 씬 고유 규칙 재사용 | 기존 워크플로 유지 | 부분 해제 불가, 상태 필드 없음, `UnloadSceneAsync(LoadableSceneId)` 없음 |
| 핸들만 세는 참조 카운트 | 모든 에셋 대입을 추적하는 비용 회피. `UnloadUnusedAssets`의 전체 스캔(예: 13개 정리에 23.32 ms) 대신 놓는 순간 그 에셋만 처리 | 예측 가능하고 비용이 해제량에 비례 | 인스턴스 사용은 보호되지 않음, 해제 완료 시점·참조 수 조회 API 없음 |

---

## 6. 에디터 실험 결과

측정 지표: 처음에는 `Resources.FindObjectsOfTypeAll<Material/Shader>()` 전체 개수, 이후에는 노이즈를 줄인 **콘텐츠 머티리얼 수**(`hideFlags == None`이고 이름이 `Hidden/`으로 시작하지 않는 머티리얼).

### 6.1 빌드 내 공유 (`ContentLayout.json`)
Cube의 `Lit.mat`(파일 18)과 InDoor 머티리얼 6개(파일 1, 6, 9, 10, 12, 15)가 **모두 파일 3번(`Lit.shader`)** 을 가리킵니다. 중복 여부를 정하는 것은 실선/점선이 아니라 **같은 빌드인가**입니다.

### 6.2 등록과 로드
| 단계 | 결과 |
|---|---|
| 등록 직후 | `Root 개수: 1`, `metadata: Hello World!`, Cube `Status: None` |
| `bonusPrefab.Load()` | 셰이더 **+3** = 파일 3, 16, 2(Lit, FallbackError, FallbackShader) |
| 씬 로드 | 콘텐츠 머티리얼 **+6**, 셰이더 **+0**(공유) |
| 같은 씬 Additive 반복 | 씬 인스턴스는 늘고 머티리얼 +0 → 에셋 공유, 씬 오브젝트만 중복 |

### 6.3 해제 순서 실험 A, B
| 실험 | 순서 | 결과 |
|---|---|---|
| A | Cube Release → 씬 언로드 | 씬 언로드 후 머티리얼 −6, 셰이더 **−3** |
| B | 씬 언로드 → Cube Release | 씬 언로드 후 셰이더 **유지**(5회 동일) → Cube Release 후 셰이더 **−3** |

공유 에셋은 **마지막 사용자가 놓을 때만** 해제됩니다. `Release()`와 씬 언로드 완료 이벤트 모두 그 시점에는 아직 메모리에 남아 있고 이후 해제됩니다.

### 6.4 콘텐츠만 센 기준선 복귀
| 시점 | 콘텐츠 머티리얼 |
|---|---|
| 등록 직후 | 9 (에디터의 일반 머티리얼) |
| Cube Load + 씬 로드 | 16 |
| Cube Release 후 | 15 |
| 씬 언로드 후 | **9** |

전체 개수에 남은 +8은 `Hidden/Universal Render Pipeline/…`(`HideAndDontSave`) **URP 내부 머티리얼**로, 콘텐츠 관리 범위 밖입니다. URP가 이 머티리얼을 파괴·재생성하기 때문에(추정) 객체 기준 비교는 교체분까지 "추가"로 잡혔습니다.

### 6.5 E3: 같은 핸들로 Load 2회
| 단계 | +0프레임 | +1프레임 | Status |
|---|---|---|---|
| `Load()` ×2 | 10 | 10 | `Loaded`, 두 결과 같은 객체 |
| `Release()` 1회 | 10 | **9** | `None` |
| `Release()` 2회 | 9 | 9 | `None`, 에러 없음 |

### 6.6 E3b: 같은 에셋, 다른 핸들 2개
| 단계 | +1프레임 | 원본 / 두 번째 |
|---|---|---|
| 두 핸들 `Load()` | 10 | `Loaded` / `Loaded`, 같은 객체 |
| 원본만 `Release()` | **10 유지** | `None` / `Loaded` |
| 두 번째도 `Release()` | **9** | `None` / `None` |

### 6.7 E1′: 해제된 것을 계속 쓰면
| 시점 | 원본 프리팹 | 씬의 인스턴스 | 인스턴스 머티리얼 | 콘텐츠 |
|---|---|---|---|---|
| Release +0프레임 | 정상 | 정상 | `Lit` | 10 |
| Release +1프레임 | **가짜 null** | **살아 있음** | **진짜 null**(접근 시 `NullReferenceException`) | **9** |
| 화면 | | **Cube가 사라짐(분홍색 아님)** | | |
| 해제된 원본으로 `Instantiate` | `MissingReferenceException: The object of type 'UnityEngine.GameObject' has been destroyed…` | | | |

### 6.8 기타 관찰
- 빌드 경고 `Fallback fonts are not supported in content directory builds.` → UI 폴백 폰트 제외
- FBX 메시(`UnityMaterialBall.fbx`)가 독립 파일이 되지 않고 씬 파일 쪽에 합쳐져 집계됨(규칙 미확인)
- `UnloadSceneAsync`에 `LoadableSceneId` 오버로드가 없음(컴파일 에러 CS1503)
- 같은 씬 중복 로드 방지는 `GetSceneByLoadableSceneId(id).isLoaded`로 가능하지만, 비동기 로드 중에는 `false`라 연타 시 빈틈이 있음

---

## 7. Player 검증 결과

Development Build로 `Builds/Player/`에 빌드하고 `%USERPROFILE%\AppData\LocalLow\DefaultCompany\ContentDirectoriesStudy\Player.log`로 확인했습니다.

### 7.1 배포
`Builds/Player/ContentDirectoriesStudy_Data/StreamingAssets/ContentPack/`의 25개 파일이 에디터 쪽 `Builds/ContentDirectories/ContentPack/`과 **`diff` 결과 완전히 동일**했습니다(매니페스트 해시 `8df80217…` 동일). `AddAdditionalPathToStreamingAssets`가 임포트 없이 복사합니다. Player에서는 `ContentDirectoryManager`의 `#else` 분기(`Application.streamingAssetsPath`)로 정상 등록되었습니다.

### 7.2 에디터와 비교
| 항목 | 에디터 | Player |
|---|---|---|
| 전체 Material / Shader (M1) | 400~500 / 141 | **6 / 51** |
| 콘텐츠 머티리얼 기준선 | 9 | **2** |
| Cube Load | +1 | **+1** |
| 씬 로드 | +6 | **+6** |
| Cube Release | −1 | **−1** |
| 씬 언로드 후 | 기준선 복귀 | **기준선 복귀** |
| `UnloadUnusedAssets` 없이 해제 | 됨 | **됨** |
| E3 (2회 반복) | 핸들별, 1프레임 지연, 중복 Release 안전 | **동일** |
| E3b | 핸들마다 1개, 객체 공유 | **동일** |
| E1′ 수치 | 원본 가짜 null, 머티리얼 null, −1 | **동일** |
| E1′ 화면 | Cube 사라짐 | **Cube 사라짐** |
| E1′ 해제된 원본 재사용 에러 | `MissingReferenceException`("이미 파괴됨") | **`ArgumentNullException: Value cannot be null. Parameter name: data`** |
| InDoor 포스트 효과(렌즈 플레어, 색 보정) | 기준 | **동일** |

**동작 규칙은 에디터와 Player가 같고, 다른 것은 기준선 숫자와 해제된 객체 재사용 시의 에러 종류뿐**입니다. Player의 에러 메시지는 원인을 떠올리기 더 어렵습니다.

### 7.3 Player에서 추가로 알게 된 것
- `Start()`는 첫 렌더링 전이라, 그 직후 URP가 내부 머티리얼·셰이더를 만들면서 전체 개수가 크게 늘어남(M1 6 → M2 66). 기준선은 **첫 프레임 이후**에 찍어야 함.
- `Shader 'Hidden/Universal Render Pipeline/GaussianDepthOfField' is not supported or has been stripped from the build …` 등 DoF·Panini 스트리핑 경고. 두 볼륨 프로파일 모두 쓰지 않는 효과라 영향 없음.
- 코드만 바꾸면 **Player만** 다시 빌드하면 되고 콘텐츠는 재사용됨. 반영 여부는 `ContentDirectoriesStudy.exe`가 아니라 `ContentDirectoriesStudy_Data/Managed/Assembly-CSharp.dll`의 시각으로 확인해야 함(exe는 실행용 껍데기).

---

## 8. 참조 카운트 규칙 (확정)

에디터와 Player에서 모두 확인한 규칙입니다.

```
에셋이 메모리에 남아 있는 조건
  = 그 에셋을 Load() 상태로 잡은 Loadable 인스턴스 ≥ 1
    또는 그 에셋을 실선으로 쓰는 로드된 씬 / 등록된 Root가 존재

- Loadable 인스턴스 하나는 Load()를 몇 번 해도 참조 1개         (E3)
- 인스턴스가 다르면 각자 1개씩, 가리키는 객체는 공유             (E3b)
- 참조 수가 0이 되면 정확히 1프레임 뒤 언로드                   (E3, E3b)
- Release() 중복 호출은 에러 없음                               (E3)
- Instantiate한 인스턴스는 참조로 세지 않음                     (E1′)
- Root는 등록 해제 전까지 유지(Root에는 Release가 없음)
```

`std::shared_ptr`와 비교하면, 인스턴스 하나가 강한 참조 하나라는 점은 같지만 **복사·소멸로 자동 증감하지 않는(RAII 아님)** 명시적 핸들입니다. 그래서 문서의 "쓰는 곳마다 자기 `Loadable` 인스턴스를 가져라"가 이 규칙에서 나옵니다. 위험은 이중 해제가 아니라 **해제된 것을 다른 곳이 계속 쓰는 것(use-after-free)** 입니다.

**실무 패턴**
- 핸들의 수명 ≥ 그 프리팹으로 만든 인스턴스의 수명. 인스턴스를 모두 파괴한 뒤 `Release()`
- 프리팹을 캐시하는 시스템(풀, 스포너)은 자기 `Loadable` 인스턴스를 직접 소유
- `OnDestroy`에서 `Release()` 호출
- 해제 직후 메모리를 측정하거나 의존 로직을 돌리려면 최소 1프레임 대기

---

## 9. AssetBundle과의 비교

| 항목 | AssetBundle | Content Directories |
|---|---|---|
| 구성 | 사람이 에셋을 번들에 배정 | Root에서 참조를 따라 자동 수집 |
| 로드 단위 | 번들(의존 번들 전체도 열어야 함) | **에셋**(실선 의존 파일만) |
| 중복 | 배정하지 않은 공용 에셋은 번들마다 복사 | 빌드 내 자동 제거, 빌드끼리는 중복 |
| 참조 | 문자열 `LoadAsset("…")` | 타입 있는 `Loadable<T>` |
| 해제 | `Unload(false)` 중복 위험 / `Unload(true)` 강제 파괴 | 핸들별 참조 카운트, 1프레임 뒤 해제 |
| 사용 중 해제 사고 | `Unload(true)` 시 분홍 머티리얼 | 마지막 `Release` 시 인스턴스가 조용히 사라짐(범위만 에셋으로 좁아짐) |
| 원격 배포·패치 | 가능(Addressables 원격 카탈로그) | **불가(로컬 전용)** |
| 코드 스트리핑 | `assetBundleManifestPath` | `ScriptsOnlyCache.yaml` 디렉터리 전달 |

---

## 10. 한계점 정리

### 10.1 배포·운영
1. **로컬 전용**: 원격 다운로드, CDN, 카탈로그 업데이트, 패치 없음(문서). 해시 파일명 덕분에 차분 패치를 직접 만들 수는 있으나 다운로드·저장·정리 로직을 모두 구현해야 함.
2. **코드 변경은 앱 업데이트 필수**: 콘텐츠에 코드가 들어가지 않으므로 새 Root 클래스·새 컴포넌트는 Player 재빌드 필요(문서, 실측).
3. **빌드 순서 결합**: 콘텐츠를 먼저 빌드하고 `ScriptsOnlyCache.yaml` 디렉터리를 Player 빌드에 넘겨야 콘텐츠 전용 타입이 보존됨. yaml은 내부 포맷(문서).
4. **Web 플랫폼**: 동기 파일 접근 때문에 비공식 경로 우회 필요(문서).
5. **콘텐츠 빌드마다 스크립트 컴파일 비용**(약 7초)이 변경 여부와 무관하게 발생(실측).

### 10.2 중복·의존성
6. **빌드끼리 중복 제거 불가**: 디렉터리끼리, Player와 콘텐츠 사이에 같은 에셋이 각각 들어감(문서).
7. **같은 씬을 Player와 콘텐츠에 동시에 넣으면 어느 쪽이 로드될지 보장 안 됨**(문서).
8. **순환 참조는 한 덩어리로 로드, 64 MB 초과 시 빌드 실패**(문서).
9. **씬 내부 부분 해제 불가**: 부분 해제가 필요하면 프리팹 + `Loadable` 또는 Additive 씬 분리로 설계.
10. **문자열 기반 로드는 그래프에 안 보임**: `Shader.Find`, `Resources.Load` 등은 추적되지 않아 에디터에서만 동작할 수 있음(문서).
11. **폴백 폰트 미지원**(실측, 빌드 경고).

### 10.3 런타임 수명 관리
12. **`Loadable` 인스턴스는 참조 1개만**: 공유하면 첫 `Release()`가 모두에게 영향(문서, 실측).
13. **RAII 아님**: 파괴돼도 `Release()`를 부르지 않으면 계속 잡고 있음.
14. **인스턴스는 참조 카운트에 포함되지 않음**: 사용 중 해제 시 인스턴스가 **경고 없이 화면에서 사라짐**(에디터·Player 동일, 실측).
15. **Player에서는 진단이 더 어려움**: 해제된 원본 재사용 시 에디터는 `MissingReferenceException`, Player는 `ArgumentNullException`(실측).
16. **해제 완료를 알 수 없음**: `Release()`·씬 언로드 완료 이벤트 후에도 실제 해제는 1프레임 뒤. 참조 수 조회 API를 찾지 못함(실측).
17. **씬에는 상태 필드·참조 카운트 없음**: Additive 중복 로드 방지가 개발자 몫(문서, 실측).
18. **API 비대칭**: `UnloadSceneAsync(LoadableSceneId)` 없음. 같은 ID의 인스턴스가 여러 개일 수 있어 `Scene` 핸들로만 언로드하게 한 것으로 보임(실측, 추정).
19. **등록 실패는 예외**: 폴더가 없으면 `FileNotFoundException`(실측).
20. **관리 범위는 콘텐츠 에셋까지**: URP 내부 머티리얼, `renderer.material` 복제본 등 런타임 생성 오브젝트는 별도 관리(실측).

### 10.4 검증 방법
21. **에디터 측정은 절대값을 믿기 어려움**: 프로젝트 에셋과 렌더 파이프라인 내부 오브젝트가 섞여 실행마다 다름. 콘텐츠만 거른 지표의 증감으로 판단하고 Player에서 재확인해야 함(실측).

---

## 11. 미확인 항목

| 항목 | 비고 |
|---|---|
| `ScriptsOnlyCache.yaml` 전달 유무에 따른 스트리핑 차이 | 비교 실험 미진행(Stripping Level High, `AddPreviousBuildReportDirectory` 주석 처리로 비교 가능) |
| 참조 카운트 단위(파일인지 오브젝트인지) | 비공개 |
| `Loadable<T>`가 클래스인지 구조체인지 | 비어 있는 필드에서 `.Status` 접근 시 NRE → 클래스로 추정 |
| FBX 메시가 씬 파일에 합쳐진 배치 규칙 | 실험 L1 예정 |
| `Loadable<Material>`로 로드한 머티리얼과 씬의 같은 머티리얼 공유 여부 | 실험 E2 예정 |
| `Resources.UnloadUnusedAssets()`가 콘텐츠 에셋에 영향을 주는지 | 실험 E4 예정 |
| `Shader.Find`가 콘텐츠 셰이더를 찾는지 | 미확인 |
| 콘텐츠만 다시 빌드해서 Player의 StreamingAssets만 교체하는 방식 | 미확인 |
| 모바일 `persistentDataPath`에서 등록 가능 여부 | 미확인 |
| Addressables 4.x 그룹 변환 | 패키지 미설치 |

공식 문서 진행 상황:

| 문서 | 상태 |
|---|---|
| content-directories-introduction | 완료 |
| content-directories-create | 완료 |
| content-directories-references | 완료 |
| content-directories-scenes | 완료 |
| content-directories-load | 완료(에디터 + Player 배포) |
| managed-code-stripping-content | 구조 확인, 비교 실험 미진행 |
| assets-avoid-duplication | 빌드 내부만 확인 |
| Addressables convert-content-directories | 미진행 |

---

## 12. 프로젝트 구조와 재현 방법

```
Assets/
├─ ContentPacks/
│  ├─ ContentPackRootAsset.asset     Root Asset
│  └─ ContentPackMetadata.asset      직접 참조 대상
├─ Prefab/Cube.prefab                Loadable 대상
├─ Scenes/
│  ├─ SampleScene.unity              Player 시작 씬 (Scene List에 포함)
│  └─ InDoor.unity                   콘텐츠 씬 (Scene List에 넣지 않음)
├─ Scripts/
│  ├─ ContentPackRootAsset.cs        Root 클래스
│  ├─ ContentPackMetadata.cs
│  ├─ ContentDirectoryManager.cs     등록/해제 도우미 (공식 문서 코드)
│  └─ ContentBootstrap.cs            실험용 부트스트랩
└─ Editor/
   ├─ BuildContentPackRootAsset.cs   메뉴: Build/Content Directories/Content Pack
   └─ ContentDirectoryDeployment.cs  Player 빌드 시 StreamingAssets 복사 + 스트리핑 정보 전달 (공식 문서 코드)
```

**재현 순서**
1. **Build > Content Directories > Content Pack** → `Builds/ContentDirectories/ContentPack/` 생성
2. 에디터: `SampleScene` Play 후 Game 뷰 클릭 / Player: Build Profiles에서 Windows, Scene List는 `SampleScene`만, 출력 `Builds/Player/`
3. 첫 프레임 이후 키패드 5로 기준선을 찍고 아래 키로 실험

| 키 | 동작 |
|---|---|
| 키패드 1 | Cube `Load()` + `Instantiate` |
| 키패드 2 | InDoor 씬 Additive 로드(중복 방지) |
| 키패드 3 | 인스턴스 `Destroy` + `Release()` |
| 키패드 4 | InDoor 씬 언로드 |
| 키패드 5 | 콘텐츠 머티리얼 수 출력 |
| 키패드 6 / 7 / 8 | E3: Load ×2 / Release 1회 / Release 2회 |
| Q / W / E | E3b: 두 핸들 Load / 원본만 Release / 두 번째도 Release |
| R / T / Y / U | E1′: Load + Instantiate / Release만 / 해제된 원본으로 Instantiate / 인스턴스 정리 |
| 키패드 0 | 전체 Material/Shader 수 출력 |

로그 위치: 에디터 `Logs/Editor.log`, Player `%USERPROFILE%\AppData\LocalLow\DefaultCompany\ContentDirectoriesStudy\Player.log`
빌드 분석 자료: `Library/BuildHistory/<빌드ID>/` (`ContentLayout.json`, `ContentSizeSummary.txt`, `ScriptsOnlyCache.yaml`, `BuildLog.jsonl`)

---

## 13. 참고 문서
- [Content directories introduction](https://docs.unity3d.com/6000.6/Documentation/Manual/content-directories-introduction.html)
- [Create a content directory](https://docs.unity3d.com/6000.6/Documentation/Manual/content-directories-create.html)
- [Load content directories](https://docs.unity3d.com/6000.6/Documentation/Manual/content-directories-load.html)
- [Managed code stripping with content](https://docs.unity3d.com/6000.6/Documentation/Manual/managed-code-stripping-content.html)
- [Avoid asset duplication](https://docs.unity3d.com/6000.6/Documentation/Manual/assets-avoid-duplication.html)
- [Build Analysis window reference](https://docs.unity3d.com/6000.6/Documentation/Manual/build-analysis-window-reference.html)
- [Addressables 4.1](https://docs.unity3d.com/Packages/com.unity.addressables@4.1/manual/index.html)
- [Convert Addressables projects to content directories](https://docs.unity3d.com/Packages/com.unity.addressables@4.1/manual/convert-content-directories.html)
