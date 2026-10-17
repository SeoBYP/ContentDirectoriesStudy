# Unity 6.6 Content Directories 학습 보고서

Unity 6.6에서 AssetBundle을 대체하려고 도입된 **Content Directories**를 공식 문서대로 직접 구현해 보고, 빌드 산출물과 런타임 동작을 실측해서 **설계 의도, 이득, 한계**를 정리한 문서입니다.

- 에디터: Unity `6000.6.3f1`, URP `17.6.0`, Input System `1.20.0`(새 Input System만 활성)
- 빌드 타깃: `StandaloneWindows64`
- 진행 범위: 공식 문서의 생성·빌드·런타임 로딩까지 완료. Player 빌드 검증과 Addressables 변환은 아직 하지 않음
- 표기: **실측**은 이 프로젝트에서 직접 측정한 값, **문서**는 공식 문서에 명시된 내용, **추정**은 근거로부터 추론한 내용(설계 의도 포함)입니다.

---

## 목차
1. [Content Directories란](#1-content-directories란)
2. [핵심 개념: Root와 참조 그래프](#2-핵심-개념-root와-참조-그래프)
3. [빌드: 그래프 수집과 산출물](#3-빌드-그래프-수집과-산출물)
4. [런타임 API와 설계 의도](#4-런타임-api와-설계-의도)
5. [실험 결과](#5-실험-결과)
6. [AssetBundle과의 비교](#6-assetbundle과의-비교)
7. [한계점 정리](#7-한계점-정리)
8. [미확인 항목과 다음 단계](#8-미확인-항목과-다음-단계)
9. [프로젝트 구조와 재현 방법](#9-프로젝트-구조와-재현-방법)
10. [참고 문서](#10-참고-문서)

---

## 1. Content Directories란

**한 줄 정의**: 게임 데이터를 Player 빌드 밖의 별도 폴더로 빌드해 두고, 실행 중에 필요한 것만 에셋 단위로 로드·해제하는 엔진 내장 콘텐츠 시스템입니다. 공식 문서는 "AssetBundle 시스템의 대체"로 소개합니다.

```
[Player 빌드]  코드 + 시작 씬
     │ 실행 중 RegisterContentDirectory("…/ContentPack")
     ▼
[콘텐츠 디렉터리]  Builds/ContentDirectories/ContentPack/  (.cf, .resS, 매니페스트)
```

| 구분 | 내용 |
|---|---|
| **왜 만들었나** | AssetBundle은 "정리 단위 = 파일 단위 = 로드·해제 단위"가 번들 하나로 묶여 있어서, 번들을 크게 만들면 불필요한 것까지 로드되고 잘게 만들면 관리 오버헤드와 공용 에셋 중복이 생겼다. 이 셋을 분리한 것이 Content Directories다(추정). |
| **이득** | 빌드 내부 자동 중복 제거, 에셋 단위 의존성과 해제, 타입 안전한 참조(`Loadable<T>`), 증분 빌드(문서, 실측) |
| **단점** | 로컬 전용(원격 배포, 패치 없음), 빌드끼리 중복 제거 불가, 런타임 수명 관리가 개발자 책임(문서, 실측) |

---

## 2. 핵심 개념: Root와 참조 그래프

### 2.1 Root Asset: 콘텐츠 빌드의 "Scene List"
Player 빌드가 **Scene List**에서 출발해 참조된 것을 모두 담듯, 콘텐츠 빌드는 **Root Asset**(직접 만든 `ScriptableObject`)에서 출발해 닿는 것을 모두 담습니다. Root는 런타임에서 콘텐츠로 들어가는 **진입점(메뉴판)** 역할도 합니다. 파일 이름이 해시라서 경로 문자열로는 찾을 수 없기 때문입니다.

```csharp
public class ContentPackRootAsset : ScriptableObject
{
    public ContentPackMetadata metadata;     // 일반 참조 (실선)
    public Loadable<GameObject> bonusPrefab; // 지연 참조 (점선)
    public LoadableSceneId bonusLevel;       // 씬 참조   (점선)
}
```

### 2.2 참조 3종류: 실선과 점선
| 참조 | 빌드 | 런타임 | 비유 |
|---|---|---|---|
| 일반 참조 | 포함 | 소유자와 **같이 로드** | 실선 |
| `Loadable<T>` | 포함 | `Load()` 해야 로드, `Release()`로 해제 | 점선 |
| `LoadableSceneId` | 포함 | `SceneManager.LoadSceneAsync`로 로드 | 점선 |

**빌드는 실선과 점선을 전부 따라가고, 런타임 로드는 점선에서 멈춥니다.** 그래서 Root 설계는 곧 "어디에 점선을 두어 로딩을 끊을지" 정하는 일입니다. 원칙은 **수명이 같은 것끼리는 실선, 수명이 다른 경계에는 점선**입니다.

### 2.3 참조는 무엇으로 찾나: GUID + fileID
`ContentPackRootAsset.asset`에 저장된 실제 값(실측):
```yaml
bonusPrefab:
  m_LoadableObjectId: {fileID: 645800571622001937, loadable: 1, guid: 462de8f7fb5dac54eb97111ce3d2eda1, type: 3}
bonusLevel:
  m_SceneGUID: 988cbc230d1f5174ab9aa0a8beda1dfe
```
- `guid`는 `Cube.prefab.meta`의 GUID(어느 파일인가), `fileID`는 `Cube.prefab` 안의 루트 GameObject(`--- !u!1 &645800571622001937`) 번호(파일 안의 어느 오브젝트인가)입니다.
- 씬은 파일 전체가 로드 단위라서 **GUID만** 저장합니다.
- `loadable: 1` 표시가 "빌드에는 넣되 로드는 끊는다"는 점선 표식입니다.
- GUID의 원본은 `.meta`이므로 이름을 바꾸거나 옮겨도 참조가 유지되고, 반대로 **`.meta`를 잃으면 모든 참조가 끊깁니다.**

씬 안의 오브젝트끼리는 `{fileID: X}`(GUID 없음)로 서로를 가리키고, 바깥 에셋은 `{fileID, guid, type}`으로 가리킵니다. 그래프를 넓히는 것은 **GUID가 붙은 참조뿐**입니다.

### 2.4 Root, 디렉터리, Loadable의 역할 분담
AssetBundle 하나가 하던 역할이 세 곳으로 나뉘었습니다.

| 나누는 것 | 정하는 것 | 따로 할 수 있는 것 | 대가 |
|---|---|---|---|
| `Loadable` | **메모리** 단위 | 에셋 하나를 따로 Load/Release | Load/Release 짝 관리 |
| Root | **목차(찾는 방법)** 단위 | `GetRootAssets<T>()`로 묶음별 조회 | 메모리 이득 없음(등록 시 모든 Root가 로드됨) |
| 디렉터리 | **빌드·배포·등록** 단위 | 따로 빌드, 동봉 여부, Register/Unregister | 디렉터리끼리 에셋 중복 |

한 빌드에 Root가 여러 개면 방문 목록을 공유해서 중복이 없고, 디렉터리를 나누면 각자 따로 담아서 중복됩니다. 문서는 "대부분 Root 하나로 충분하고, 선택 설치 콘텐츠 팩 같은 경우에만 늘려라"고 권합니다.

---

## 3. 빌드: 그래프 수집과 산출물

### 3.1 빌드 단계 (실측, `BuildLog.jsonl`)
```
Gather and collect build metadata   ① Root에서 GUID 참조를 따라가며 방문 목록 수집
Calculate build layout              ② 에셋 → 파일 배치, 실선/점선 기록
Generate build instructions         ③ 에셋별 작업 지시서 (Library/BuildInstructions/<GUID>.buildinst)
Execute instructions                ④ 직렬화, 셰이더 컴파일, 파일 쓰기
Generate build manifest             ⑤ 런타임용 매니페스트 생성
```
개념적으로는 **방문 목록(visited set)을 둔 재귀 탐색**입니다. 목록의 키가 에셋 GUID라서 같은 에셋은 몇 번 만나도 한 번만 담기고, 이것이 곧 "빌드 내 자동 중복 제거"입니다. 실제 탐색 알고리즘은 공개되어 있지 않습니다(추정).

```text
Collect(asset):
    if asset in visited: return
    visited.add(asset)
    for ref in asset의 GUID 참조(실선, Loadable, 씬 참조 전부):
        Collect(ref)
```

순환 참조는 런타임에 로드 순서를 정할 수 없어서 **고리 전체를 한 덩어리로 묶고**, 그 크기가 **64 MB를 넘으면 빌드가 실패**합니다(문서, `circularrefs.json`).

### 3.2 산출물 (실측, `Builds/ContentDirectories/ContentPack/`)
| 파일 | 개수 | 역할 |
|---|---|---|
| `<해시>.cf` | 18 | 직렬화된 오브젝트. 에셋 하나 = 파일 하나 |
| `<해시>.resS` | 5 | 텍스처·메시 같은 대용량 데이터 |
| `<해시>.json` | 1 | 런타임 매니페스트 |
| `BuildManifestHash.txt` | 1 | 현재 매니페스트를 가리키는 고정 이름 포인터 |

파일 이름이 **내용 해시**라서 내용이 같으면 이름도 같고, 재빌드 때 다시 쓰지 않습니다. `RegisterContentDirectory`는 `BuildManifestHash.txt`부터 읽습니다(폴더가 없을 때의 스택 트레이스로 실측).

### 3.3 `ContentLayout.json`과 런타임 매니페스트
`Library/BuildHistory/<빌드ID>/ContentLayout.json`은 파일별로 다음을 기록합니다(실측).

| 필드 | 뜻 |
|---|---|
| `SourceAssets` | 이 파일에 들어 있는 프로젝트 에셋 |
| `SerializedFileDependencies` | 이 파일을 열 때 **같이 열어야 하는 파일**(실선) |
| `LoadableDependencies` | 이 파일 안의 `Loadable` 대상(점선, 같이 열지 않음) |
| `LoadableSceneDependencies` | 이 파일 안의 씬 참조(점선) |

Root 파일 예시:
```
5  ContentPackRootAsset.asset
   SerializedFileDependencies: [4, 14]           ← 스크립트 정보, 메타데이터
   LoadableDependencies:       [Cube의 ObjectIdHash]
   LoadableSceneDependencies:  [Assets/Scenes/InDoor.unity]
```
**런타임 매니페스트**(출력 폴더의 json)에는 파일별로 `Index, ID, IsBuiltIn, SerializedFileDependencies, ContentHash`만 있습니다. 런타임에는 **실선만 필요**하고, 점선 정보는 오브젝트 안의 `Loadable` 값과 매니페스트의 `Loadables` 표(`ObjectIdHash → 파일 번호 + Identifier`)로 찾습니다. 빌드 후에는 에디터의 `fileID`가 런타임용 `Identifier`로 다시 매겨집니다(`645800571622001937` → `7048807285321821040`).

### 3.4 빌드 측정 (실측)
| 항목 | 1차 빌드 | 2차 빌드(변경 없음) |
|---|---|---|
| 전체 시간 | 64,991 ms | **8,853 ms** |
| 스크립트 컴파일 | 20.9초 | 7.1초 |
| 데이터 빌드 실행 | 42.3초 | 0.5초 미만 |
| 재빌드 / 재사용 파일 | 18 / 0 | **0 / 18** |
| 크기 | 2,995,011 B | 동일, 매니페스트 해시 동일 |

변경이 없어도 **스크립트 컴파일 비용(약 7초)은 매번** 듭니다.

**Build Analysis 창**(Window > Analysis > Build Analysis) 결과:

| Root | Direct Size | Direct Assets | Total Size | Total Assets |
|---|---|---|---|---|
| ContentPackRootAsset | **6.93 KB** | 2 | **2.85 MB** | 17 |

Direct 6.93 KB = Root 파일(2,268 B) + 메타데이터(1,068 B) + 스크립트 정보 파일(3,756 B) = 7,092 B로 정확히 일치합니다. **등록 시 6.93 KB만 올라오고 2.85 MB는 필요할 때 로드**된다는 뜻이며, 약 420배 차이가 점선 두 개의 효과입니다.

---

## 4. 런타임 API와 설계 의도

API 시그니처는 설치된 에디터의 `UnityEngine.ContentLoadModule.xml`에서 확인했습니다.

| API | 동작 |
|---|---|
| `ContentLoadManager.RegisterContentDirectory(path)` | 매니페스트를 읽고 Root와 그 실선 의존성을 로드, `ContentDirectoryHandle` 반환 |
| `ContentLoadManager.GetRootAssets<T>()` | 등록된 디렉터리에서 타입 `T`인 Root 배열 |
| `ContentLoadManager.UnregisterContentDirectory(handle)` | 등록 해제(열린 파일이 있으면 에러 로그, 문서) |
| `Loadable<T>.Load()` / `LoadAsync()` | 동기/비동기 로드, 실패 시 `null` |
| `Loadable<T>.Release()` | 참조를 놓음 |
| `Loadable<T>.Status` / `Target` | `None`/`Loading`/`Loaded`/`Failed`, 로드 결과 |
| `SceneManager.LoadSceneAsync(LoadableSceneId, LoadSceneParameters)` | 콘텐츠 씬 로드 |
| `SceneManager.GetSceneByLoadableSceneId(id)` | 로드된 씬 조회 |

| API | 왜 이렇게 설계했나 (추정 포함) | 이득 | 단점 |
|---|---|---|---|
| 출력 경로를 `Assets` 밖에 | 해시 파일 수천 개가 Asset Database에 임포트되는 것을 막음(문서) | 에디터 반복 속도 | Player 빌드 때 StreamingAssets로 복사하는 연결 코드 필요, 에디터·Player 경로 분기 |
| 명시적 등록 | 콘텐츠가 Player 밖에 있어 엔진이 존재를 모름. 매니페스트를 한 번 읽어 조회 표를 만듦 | 콘텐츠 가시성과 수명을 개발자가 통제, 폴더 단위 붙이기/떼기, 스택 순서 조회 | 동기 파일 접근(Web은 비공식 우회 필요), Root와 실선이 등록 시 한꺼번에 로드, 해제 순서 준수 필요 |
| 타입으로 Root 조회 | 해시 파일명이라 문자열 경로가 없음 | 이름 변경에 강함, 컴파일 타임 타입 검사 | Root 타입·필드 구성이 Player와 콘텐츠에서 같아야 함(새 Root 클래스는 앱 업데이트), 에디터 Play 모드가 빌드 누락을 가림 |
| `Loadable<T>` 핸들 | 번들 대신 참조 하나가 손잡이가 되어 에셋 하나의 수명을 관리 | 에셋 단위 정밀 관리, 상태 조회 가능 | Load/Release 짝 책임, 인스턴스 공유 금지, RAII 아님 |
| 씬은 기존 `SceneManager` 확장 | 씬 고유 규칙(Single/Additive 등)을 재사용 | 기존 워크플로 유지 | 씬 내부 부분 해제 불가, 중복 로드 방지가 개발자 몫, `UnloadSceneAsync(LoadableSceneId)` 오버로드 없음 |

---

## 5. 실험 결과

모든 런타임 측정은 **에디터 Play 모드**에서 했습니다. 측정 지표는 처음엔 `Resources.FindObjectsOfTypeAll<Material/Shader>()` 전체 개수, 이후에는 노이즈를 줄인 **콘텐츠 머티리얼 수**(`hideFlags == None` 이고 이름이 `Hidden/`으로 시작하지 않는 머티리얼)입니다.

### 5.1 빌드 내 공유 (실측, `ContentLayout.json`)
Cube 프리팹의 `Lit.mat`(파일 18)과 InDoor 씬 머티리얼 6개(파일 1, 6, 9, 10, 12, 15)가 **모두 같은 파일 3번(`Lit.shader`)** 을 가리킵니다. 셰이더는 한 번만 저장되었습니다. 중복 여부를 정하는 것은 실선/점선이 아니라 **같은 빌드인가**입니다.

### 5.2 등록과 로드 (실측)
| 단계 | 결과 |
|---|---|
| 등록 직후 | `Root 개수: 1`, `metadata: Hello World!`, Cube `Status: None` → Root와 실선만 로드 |
| `bonusPrefab.Load()` | 셰이더 **+3** = 파일 3, 16, 2(Lit, FallbackError, FallbackShader). 실선을 따라 열림 |
| 씬 로드 | 머티리얼 **+6**, 셰이더 **+0** → 이미 열린 `Lit.shader`를 공유 |
| 같은 씬 Additive 반복 로드 | 씬 인스턴스는 늘고 머티리얼은 +0 → 에셋은 공유, 씬 오브젝트만 중복 |

### 5.3 해제 순서 실험 A, B (실측)
| 실험 | 순서 | 결과 |
|---|---|---|
| A | Cube Release → 씬 언로드 | 씬 언로드 후 머티리얼 −6, 셰이더 **−3**, 셰이더 기준선 복귀 |
| B | 씬 언로드 → Cube Release | 씬 언로드 후 머티리얼 −6, 셰이더 **유지**(144, 5회 동일) → Cube Release 후 머티리얼 −1, 셰이더 **−3** |

**공유 에셋은 마지막 사용자가 놓을 때만 해제됩니다.** `Resources.UnloadUnusedAssets()`를 호출하지 않았는데도 해제되었습니다(에디터 관찰, Player 재확인 필요).

### 5.4 콘텐츠만 센 기준선 복귀 (실측)
| 시점 | 콘텐츠 머티리얼 |
|---|---|
| 등록 직후 | 9 (에디터의 일반 머티리얼) |
| Cube Load + 씬 로드 | 16 (+7 = `Lit` 1 + 씬 6) |
| Cube Release 후 | 15 |
| 씬 언로드 후 | **9 (기준선 복귀)** |

전체 머티리얼 수는 기준선으로 돌아오지 않았는데(+8), 차이는 `Hidden/Universal Render Pipeline/…`(Bloom, UberPost, CopyDepth 등), `hideFlags: HideAndDontSave`인 **URP 내부 머티리얼**이었습니다. 콘텐츠 디렉터리의 관리 범위 밖입니다. URP는 이 머티리얼들을 파괴하고 다시 만들기 때문에(추정) 객체 기준 비교에서는 교체된 것까지 "추가"로 잡혔습니다.

### 5.5 E3: 같은 핸들로 Load 2회 (실측)
| 단계 | +0프레임 | +1프레임 | Status |
|---|---|---|---|
| `Load()` ×2 | 10 | 10 | `Loaded` (두 결과 `ReferenceEquals` = True) |
| `Release()` 1회 | 10 | **9** | `None` |
| `Release()` 2회 | 9 | 9 | `None`, 에러 없음 |

→ **핸들별 카운트**. 인스턴스 하나는 `Load()` 횟수와 무관하게 참조 1개. 해제는 **정확히 1프레임 뒤**.

### 5.6 E3b: 같은 에셋, 다른 핸들 2개 (실측)
| 단계 | +1프레임 | 원본 / 두 번째 Status |
|---|---|---|
| 두 핸들 `Load()` | 10 | `Loaded` / `Loaded` (같은 객체) |
| 원본만 `Release()` | **10 유지** | `None` / `Loaded` |
| 두 번째도 `Release()` | **9** | `None` / `None` |

→ 인스턴스마다 참조 1개, 객체는 공유. `std::shared_ptr` 복사본 두 개와 같은 동작이지만 **RAII가 아니라** 명시적 `Load()`/`Release()`가 필요합니다.

### 5.7 E1′: 해제된 것을 계속 쓰면 (실측)
| 시점 | 원본 프리팹 | 씬의 인스턴스 | 인스턴스의 머티리얼 | 콘텐츠 |
|---|---|---|---|---|
| Release +0프레임 | 정상 | 정상 | `Lit` | 10 |
| Release +1프레임 | **가짜 null** (`==null` True, `ReferenceEquals` False) | **살아 있음** | **진짜 null**, 접근 시 `NullReferenceException` | **9** |
| 해제된 원본으로 `Instantiate` | `MissingReferenceException: The object of type 'UnityEngine.GameObject' has been destroyed…` | | | |

→ **`Instantiate`한 인스턴스는 참조 카운트에 잡히지 않습니다.** 인스턴스가 살아 있어도 쓰던 에셋이 언로드되고, 머티리얼 슬롯은 **경고 없이 null**이 됩니다. `AssetBundle.Unload(true)`의 분홍 머티리얼 사고와 같은 종류이며, 범위가 번들에서 에셋으로 좁아졌을 뿐입니다. 런타임의 모든 에셋 대입을 추적하는 비용을 피하려는 설계로 보입니다(추정).

### 5.8 기타 관찰 (실측)
- `Fallback fonts are not supported in content directory builds.` 빌드 경고 → UI 폴백 폰트가 빠짐
- FBX 메시(`UnityMaterialBall.fbx`)가 독립 파일이 되지 않고 씬 파일 쪽에 합쳐져 집계됨(배치 규칙은 미확인)
- `Resources.UnloadUnusedAssets()` 비용 예시: `Unloading 13 unused Assets … Total: 23.32 ms (MarkObjects: 22.47 ms)` → 참조 카운트 방식은 이런 전체 스캔이 필요 없음
- 같은 씬 중복 로드 방지는 `GetSceneByLoadableSceneId(id).isLoaded`로 가능하지만 비동기 로드 중에는 `false`라서 연타 시 빈틈이 있음

---

## 6. AssetBundle과의 비교

| 항목 | AssetBundle | Content Directories |
|---|---|---|
| 구성 | 사람이 에셋을 번들에 배정 | Root에서 참조를 따라 자동 수집 |
| 로드 단위 | 번들(의존 번들 전체도 열어야 함) | **에셋**(실선 의존 파일만) |
| 중복 | 배정하지 않은 공용 에셋은 번들마다 복사 | 빌드 내 자동 제거, 빌드끼리는 중복 |
| 참조 | 문자열 경로 `LoadAsset("…")` | 타입 있는 `Loadable<T>` |
| 해제 | `Unload(false)` 중복 위험 / `Unload(true)` 강제 파괴 | 핸들별 참조 카운트, 1프레임 뒤 해제 |
| 원격 배포 | 가능(Addressables 원격 카탈로그) | **불가(로컬 전용)** |
| 코드 스트리핑 | `assetBundleManifestPath` | `ScriptsOnlyCache.yaml` 디렉터리 전달 |

**요약**: 나누는 목적은 같고, Content Directories는 **나누는 단위를 번들에서 에셋으로 바꿔** 중복과 의존성 문제를 줄인 대신 **원격 배포를 포기**했습니다.

---

## 7. 한계점 정리

### 7.1 배포·운영
1. **로컬 전용**: 원격 다운로드, CDN, 카탈로그 업데이트, 패치 기능이 없음(문서). 해시 파일명 덕분에 차분 패치를 직접 만들 수는 있으나 다운로드·저장 위치·정리 로직을 모두 구현해야 함.
2. **코드 변경은 앱 업데이트 필수**: 콘텐츠에는 코드가 들어가지 않으므로 새 Root 클래스, 새 컴포넌트는 Player 재빌드가 필요(문서 + 구조).
3. **Web 플랫폼**: 동기 파일 접근 때문에 비공식 경로(`Library/PlayerDataCache/WebGLPreloadedStreamingAssets`) 우회 필요(문서).
4. **빌드마다 스크립트 컴파일 비용**(약 7초)이 콘텐츠 변경 여부와 무관하게 발생(실측).

### 7.2 중복·의존성
5. **빌드끼리 중복 제거 불가**: 디렉터리끼리, Player와 콘텐츠 사이에 같은 에셋이 각각 들어감(문서). 예: Player의 `SampleScene`과 콘텐츠가 모두 URP Lit을 쓰면 셰이더 2벌.
6. **같은 씬을 Player와 콘텐츠에 동시에 넣으면 어느 쪽이 로드될지 보장 안 됨**(문서).
7. **순환 참조는 한 덩어리로 로드, 64 MB 초과 시 빌드 실패**(문서).
8. **씬 내부 부분 해제 불가**: 씬이 실선으로 끌고 온 에셋은 씬과 함께만 내려감. 부분 해제가 필요하면 프리팹 + `Loadable` 또는 Additive 씬 분리로 설계해야 함.
9. **문자열 기반 로드는 그래프에 안 보임**: `Shader.Find`, `Resources.Load` 등은 추적되지 않음. 에디터에서는 동작하다가 Player에서 깨질 수 있음(문서).

### 7.3 런타임 수명 관리
10. **`Loadable` 인스턴스는 참조 1개만 잡음**: 여러 소비자가 한 인스턴스를 공유하면 첫 `Release()`가 모두에게 영향(문서, 실측 E3).
11. **RAII 아님**: 오브젝트가 파괴돼도 `Release()`를 부르지 않으면 계속 잡고 있음.
12. **인스턴스는 참조 카운트에 포함되지 않음**: 인스턴스가 살아 있어도 마지막 `Release()` 후 에셋이 언로드되고 머티리얼이 **경고 없이 null**이 됨(실측 E1′). → 인스턴스를 모두 파괴한 뒤에 `Release()`할 것.
13. **해제 완료를 알 수 없음**: `Release()`, 씬 언로드 완료 이벤트 모두 실제 해제는 1프레임 뒤(실측). 참조 수를 조회하는 공개 API를 찾지 못함.
14. **씬에는 상태 필드나 참조 카운트가 없음**: Additive 중복 로드 방지가 개발자 몫(문서, 실측).
15. **API 비대칭**: `LoadSceneAsync(LoadableSceneId)`는 있지만 `UnloadSceneAsync(LoadableSceneId)`는 없음(실측, 컴파일 에러 CS1503). 같은 ID로 인스턴스가 여러 개일 수 있으므로 `Scene` 핸들로만 언로드하게 한 것으로 보임(추정).
16. **등록 실패는 예외**: 폴더가 없으면 `FileNotFoundException`(실측). 실서비스 코드에는 존재 확인이나 예외 처리가 필요.
17. **관리 범위는 콘텐츠 에셋까지**: URP 내부 머티리얼, `renderer.material` 복제본 같은 런타임 생성 오브젝트는 별도 관리(실측).

### 7.4 검증 방법
18. **에디터 측정은 절대값을 믿기 어려움**: 프로젝트 에셋과 렌더 파이프라인 내부 오브젝트가 섞여 실행마다 값이 다름(+9, +65 등). 증감과 콘텐츠만 거른 지표로 판단해야 하며, 최종 확인은 Player 빌드에서 해야 함(실측).
19. **폴백 폰트 미지원**: 콘텐츠 디렉터리 빌드에서 폰트 폴백이 제외됨(실측, 빌드 경고).

---

## 8. 미확인 항목과 다음 단계

| 항목 | 상태 |
|---|---|
| Player 빌드에서 M1~M5, E3, E3b, E1′ 재현 여부 | 다음 단계(6단계) |
| `ScriptsOnlyCache.yaml` 전달 시 코드 스트리핑 방지 | 6단계 |
| `UnloadUnusedAssets` 없이 해제되는 것이 Player에서도 같은지 | 6단계 |
| 참조 카운트 단위(파일인지 오브젝트인지) | 비공개 |
| FBX 메시가 씬 파일에 합쳐진 배치 규칙 | 실험 L1 예정 |
| 머티리얼만 `Loadable<Material>`로 로드했을 때 씬의 같은 머티리얼과 공유되는지 | 실험 E2 예정 |
| `Resources.UnloadUnusedAssets()`가 콘텐츠 에셋에 영향을 주는지 | 실험 E4 예정 |
| `Shader.Find`가 콘텐츠 셰이더를 찾는지 | 미확인 |
| 모바일에서 `persistentDataPath`로 등록 가능 여부(직접 패치용) | 미확인 |
| Addressables 4.x 그룹 변환 | 패키지 미설치 |

공식 문서 진행 상황:

| 문서 | 상태 |
|---|---|
| content-directories-introduction | 완료 |
| content-directories-create | 완료 |
| content-directories-references | 완료 |
| content-directories-scenes | 완료 |
| content-directories-load | 에디터 기준 완료, Player 배포 미검증 |
| managed-code-stripping-content | 미진행 |
| assets-avoid-duplication | 빌드 내부만 확인 |
| Addressables convert-content-directories | 미진행 |

---

## 9. 프로젝트 구조와 재현 방법

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
   └─ ContentDirectoryDeployment.cs  Player 빌드 시 StreamingAssets 복사 (공식 문서 코드)
```

**재현 순서**
1. 메뉴 **Build > Content Directories > Content Pack** 실행 → `Builds/ContentDirectories/ContentPack/` 생성
2. `SampleScene`을 열고 Play, **Game 뷰를 클릭**해 포커스를 줌
3. 아래 키로 실험

| 키 | 동작 |
|---|---|
| 키패드 1 | Cube `Load()` + `Instantiate` (M2) |
| 키패드 2 | InDoor 씬 Additive 로드(중복 방지) |
| 키패드 3 | 인스턴스 `Destroy` + `Release()` (M4) |
| 키패드 4 | InDoor 씬 언로드 (M5) |
| 키패드 5 | 콘텐츠 머티리얼 수 출력 |
| 키패드 6 / 7 / 8 | E3: Load ×2 / Release 1회 / Release 2회 |
| Q / W / E | E3b: 두 핸들 Load / 원본만 Release / 두 번째도 Release |
| R / T / Y / U | E1′: Load + Instantiate / Release만 / 해제된 원본으로 Instantiate / 인스턴스 정리 |
| 키패드 0 | 전체 Material/Shader 수 출력 |

빌드 분석 자료 위치: `Library/BuildHistory/<빌드ID>/` (`ContentLayout.json`, `ContentSizeSummary.txt`, `ScriptsOnlyCache.yaml`, `BuildLog.jsonl`)

---

## 10. 참고 문서
- [Content directories introduction](https://docs.unity3d.com/6000.6/Documentation/Manual/content-directories-introduction.html)
- [Create a content directory](https://docs.unity3d.com/6000.6/Documentation/Manual/content-directories-create.html)
- [Load content directories](https://docs.unity3d.com/6000.6/Documentation/Manual/content-directories-load.html)
- [Build Analysis window reference](https://docs.unity3d.com/6000.6/Documentation/Manual/build-analysis-window-reference.html)
- [Addressables 4.1](https://docs.unity3d.com/Packages/com.unity.addressables@4.1/manual/index.html)
- [Convert Addressables projects to content directories](https://docs.unity3d.com/Packages/com.unity.addressables@4.1/manual/convert-content-directories.html)
