# 케이지 기반 정점 사상(Cage-based Vertex Mapping) 조사 및 구현 계획

## Context (왜 이 작업인가)

프로젝트 목표: **스켈레톤을 변형(뼈 길이 변경)시키고, 리깅된 메시를 그에 맞춰 변형**시킨다.
중간 매개로 인체를 타이트하게 감싸는 저폴리곤 **cage**를 두고, 케이지를 토폴로지 유지한 채 변형한 뒤,
rest cage 안의 모든 메시 정점을 **변형된 cage 안으로 사상**한다. 이때:

- 위치의 **semantic이 보존**되어야 한다("배꼽은 배꼽 자리").
- 이상적 관계는 **"일정 두께의 옷 → 옷이 바뀌면 안의 몸도 따라 바뀜"** = 국소 **형상/두께 보존(shape-preservation)**.

이 문서는 사용자 지시에 따라 **정점 사상 단계에만 집중**한다(스켈레톤→케이지 구동 단계는 범위 밖).

**결론: 가능하다.** 이는 성숙하게 정립된 분야 — **cage-based deformation + generalized barycentric coordinates**.
핵심 원리는 "좌표를 rest cage에서 한 번 bind → 케이지가 변형될 때마다 재구성"이며,
좌표 벡터 자체가 정점의 불변 "주소" 역할을 하므로 semantic 보존이 자동 충족된다.

### 성능 프로파일 (확정)
- 표준 스켈레톤 → 표준 케이지 → 그 안에 갇힌 표준 메시가 주어진다.
- **precompute(bind)는 무거워도 됨** — 오프라인에서 충분히 수행 가능.
- 변형 케이지로의 deform-time 사상은 **실시간일 필요 없음**.
- → **품질 최대화**가 목표.

---

## 채택 방향: MVC 유지 (2026-09-07 뒤집힘)

**아래의 원래 판단은 뒤집혔다.** 전제가 "형상·두께 보존을 좌표계가 공급한다"였는데, 이 프로젝트에서 **두께는 케이지의 일**이기 때문이다.

이 시스템의 목표는 뼈 길이 → 케이지의 **선언적 스케일링**이다. "허벅지가 길면 굵다"는 **해부학적** 주장이고 부위마다 다르며 근거가 시각적 설득력이라, 두께 driver가 그것을 링·기둥마다 **선언**한다([cage.md](cage.md) §9). 좌표계에 바라는 것은 그 선언을 **정직하게 옮기는 것**뿐이다.

- **MVC는 linear precision을 갖는다** — 케이지가 어떤 아핀 변화를 하든 안쪽이 정확히 그것을 따른다. 케이지가 유일한 저자로 남는다.
- **Green은 닮음만 정확히 재현하고 이방성 stretch를 등방 팽창으로 바꾼다**(2D conformal, 3D quasi-conformal). 즉 좌표계가 **자기 몫의 두께를 얹는다.** 그러면 driver 값은 "원하는 두께 − Green이 이미 준 몫"이 되고, 그 몫은 면의 국소 stretch에 따라 부위마다 다르다 — 선언이 선언이 아니게 된다. **그러므로 이 설계에서 Green/Somigliana는 무거운 선택이 아니라 틀린 선택이다.**

**측정으로 확인**(2026-09-07, `cage_sweep --probe`): 한 구간의 단면을 ×k로 넓히고 rest 메시를 사상해 살을 다시 재면, 그 구간의 제어점이 전부 함께 움직일 때 전달비 `(메시−1)/(케이지−1)`가 **0.92 ~ 1.03**(폭·깊이 모두)이고 k = 0.8·1.2·1.5에서 **같다**. 선언이 손실 없이, 선형으로 도착한다 — MVC는 병목이 아니다.

**다음 후보는 Green이 아니라 PMVC/QMVC다.** MVC의 실제 약점은 두께가 아니라 오목부(겨드랑이·가랑이·손가락 사이)의 **음수 가중치**와 비국소성이다(cage.md `[N18]`: 안 31% · 밖 63%, `Σ|w|` 1 → 7). 그것이 문제로 드러나면 PMVC(Lipman 2007) 또는 QMVC(Thiery·Boubekeur 2018)로 간다 — 음수 가중치만 없애고 **순수 정점 결합의 성질(케이지가 유일한 저자)을 유지**하며 두께를 얹지 않는다.

---

### 원래 판단 (기록으로 남김)

성능 무제약 + 형상 보존 요구 → 순수 정점 선형결합(MVC/Harmonic)은 늘이기(stretch)에서 전단·두께붕괴가 나므로 부적합.
**면 법선 항**을 함께 쓰는 계열이 필수.

- **1차 구현 = Green Coordinates** (Lipman·Levin·Cohen-Or, SIGGRAPH 2008)
  - 정점항 + 면법선항, 2D 등각 / 3D 준등각 → **두께 보존**. 폐형식 → 검증 쉬움, 참조 코드 존재.
  - 파이프라인 전체(bind→deform)를 먼저 이걸로 완성해 시각적 baseline 확보 및 리스크 제거.
- **품질 상한 = Somigliana Coordinates** (Chen·de Goes·Desbrun, SIGGRAPH 2023)
  - Green을 탄성역학(Somigliana 항등식, Kelvin 기본해)으로 **일반화**. 행렬 가중 + corotational.
  - Green의 전단 아티팩트를 우회하고 **부피/불룩함(bulge)·강성(Poisson비 ν) 제어** 추가 → 물리적으로 가장 그럴듯.
  - precompute가 더 무겁고 deform도 비실시간이지만, 요구 프로파일상 문제없음.
  - **Green과 동일한 "정점+면법선" 골격을 재사용**하므로 Green 구현 위에 증분 확장이 자연스럽다.

두 방법 모두 **표면 경계적분** 방식 → 볼륨 테셀레이션(테트라 메시) 불필요. (반면 Harmonic/BBW는 볼륨 격자 solve 필요.)

### 방법 비교 (정점 사상 관점)

| 방법 | 계산 | 형상보존("두께") | 오목부(겨드랑이·가랑이·손가락) | 케이지 경계 보간 | 비고 |
|---|---|---|---|---|---|
| MVC (Ju/Floater 2005) | 폐형식, 매우 빠름 | ✗ (전단) | ✗ 음수가중치 | ✓ | 가장 쉬운 baseline |
| PMVC/QMVC (Lipman'07 / Thiery'18) | 수치/폐형식 | ✗ | ✓ 음수 제거 | ✓ | 오목부 보완, 쿼드케이지(QMVC) |
| Harmonic (Pixar, Joshi'07) | 볼륨 격자 solve | ✗ | ✓ 강한 오목부 견고 | ✓ | 애니 산업 표준, precompute 무거움 |
| **Green (Lipman'08)** | 폐형식 | ✓ 준등각 | 보통 | ✗ | 두께를 **좌표계가** 얹는다 → 이 설계에는 부적합 |
| **Somigliana (Chen'23)** | precompute+corotational | ✓ + 부피제어 | 보통 | ✗ | 〃, 게다가 가중치가 ×9 |

"형상보존" 열이 이 설계에서는 **장점이 아니라 간섭**이다. 두께는 케이지가 선언하고 좌표계는 그것을 옮긴다 — 그래서 MVC의 ✗(전단)가 곧 "케이지에 정직하다"는 뜻이고, 채택 근거가 된다. 오목부의 ✗만이 남은 약점이며 PMVC/QMVC가 그 열을 ✓로 바꾼다.

---

## 정점 사상 파이프라인 (핵심)

### A. Bind (오프라인, rest cage 기준, 1회)
케이지 정점 `{v_i}`, 삼각형 면 `{f_j}`(외향 법선 `{n_j}`, 면적 `{A_j}`)에 대해,
각 메시 정점 η마다 좌표를 적분으로 산출·저장:
- Green: 정점 가중치 `φ_i(η)`, 면 가중치 `ψ_j(η)` (폐형식, 논문 부록 `GCTriInt`).
- Somigliana: 행렬값 가중치 `Φ_i(η)`(3×3), `Ψ_j(η)`(3×3) (Kelvin 커널 경계적분).
- 저장 규모 ~ `N_meshVerts × (N_cageVerts + N_faces)` (Somigliana는 ×9). 케이지가 저폴리곤이라 감당 가능.
- 결과를 **에셋으로 직렬화**(런타임은 로드+재구성만).

### B. Deform (변형 케이지 `{v'_i}`, `{n'_j}` 주어질 때)
- **Green**: `η' = Σ_i φ_i v'_i + Σ_j ψ_j · s_j · n'_j`
  - `s_j` = 면별 **stretch(등각) 스케일 인자** — rest 대비 변형 삼각형의 변 배치로 계산(참조 코드 그대로 이식).
- **Somigliana**: `η' = Σ_i Φ_i · v'_i + Σ_j Ψ_j · n'_j` 를 corotational 스킴으로(영역별 회전 추출) + ν로 부피 거동 조절.
- 비실시간 허용 → CPU(가능하면 Burst/Jobs) 재구성으로 충분. 필요 시 이후 compute shader로 이관.

---

## 구현 현황 (implemented)

파일: [cage_deform.cs](../unity/Assets/Scenes/cage_deform.cs), [mapping_tester.cs](../unity/Assets/Scenes/mapping_tester.cs).

파이프라인 전체가 인스펙터 버튼으로 연결되어 있다. **본 길이 슬라이더 → 케이지 갱신 → `deform` → `refresh rest pose`.**

- **좌표 교체 지점** — `cage_deform.bind(coords, pts, rest_cage, tris)` → `cage_bind`, `cage_deform.map(bind, live_cage)` 둘. `cage_coords` 열거형에 값을 추가하고 두 switch만 넓히면 방법을 갈아끼운다. 인스펙터의 `coords` 드롭다운으로 선택.
- **1차 방법 = MVC** (Ju/Schaefer/Warren 2005 폐형식). 계획상 baseline. `cage_bind`는 메시 정점당 케이지 코너 하나씩의 가중치(point-major `float[]`)를 들고 있고, Green/Somigliana는 여기에 면 항을 더해 넓히면 된다.
- **bind / deform 분리** — 좌표는 (rest 메시, rest 케이지, 방법)만의 함수, 즉 import 시점 상수다. `mapping_tester.bind()`가 import와 "rebuild cage"에서 한 번 풀고, `deform`은 라이브 케이지에 대한 가중합만 남는다. 36,426 정점 / 40 코너 · 76 면 기준으로 **~500 ms → ~1 ms**(.NET 10 Release 측정; Unity Mono는 배수만큼 느리다). 가중치 5 MB는 씬에 직렬화하지 않으므로 씬 리로드 후 첫 deform이 한 번 다시 bind한다 — Green/Somigliana의 무거운 bind는 계획대로 별도 에셋으로 굽는다.
- **`deform`** — 항상 **원본 소스 지오메트리**에 대한 bind에서 읽어 rest cage → 현재 cage로 사상하고 타깃 메시 버퍼에 쓴다. 자기 출력에 누적되지 않으므로 길이 편집·방법 변경 후 몇 번이든 다시 눌러도 된다(슬라이더는 원본 rest 기준 절대값이라 드리프트가 없다). `coords` 드롭다운은 훅이 없는 평범한 필드이므로 `deform`이 스스로 불일치를 보고 재바인딩한다.
    - 공간: 메시 버퍼(bind space) ↔ 케이지 공간(rig root local)은 `bind_to_rig = root.W2L · bone[0].L2W · bindpose[0]`로 오간다. bind pose 정의상 모든 본이 같은 행렬을 주며, 소스 스켈레톤이 그 pose에 그대로 서 있다.
- **`refresh rest pose`** — 현재 본 transform 기준으로 bindpose를 다시 굽는다(`bindpose[b] = bone[b].W2L · root.L2W · bind_to_rig`). 그러면 현재 pose에서의 스키닝이 항등이 되어 버퍼가 화면에 그대로 나오고, 그 위에 얹는 애니메이션이 새 몸을 변형한다. 본은 움직일 필요가 없다 — 슬라이더가 이미 변형된 스켈레톤에 세워 두었고, 길이만 바뀌었으므로 회전은 불변이다.
    - 합성 결과가 `root.L2W · p_deformed`이므로, **deform + refresh 후 메시는 변형된 케이지 안에 rest 메시가 rest 케이지 안에 있던 것과 정확히 같은 자리에 놓인다** → 눈으로 검증 가능.
- **중간 상태** — `deform`만 누른 직후 뷰포트는 (구) rest pose로 스키닝된 결과, 즉 길이 변화가 두 번 적용된 모습이다. `refresh rest pose`를 누르면 해소된다.

### 검증 (수행)

MVC 커널은 Unity 밖에서 수치 검증했다(단위 큐브 케이지 + 실제 7링 케이지):
partition of unity 1e-16, **linear precision `Σ w_i p_i = p` 1e-15**, affine 재현 1e-15, identity 1e-15, 크라운 링 stretch에 대해 국소·단조 응답.
알려진 한계도 확인 — 이 plus 자형 케이지는 오목해서 표본 대부분에 **음수 가중치**가 나온다(겨드랑이·가랑이). 두께 붕괴와 함께 Green으로 넘어갈 이유.

## Unity 구현 계획 (철회)

아래는 Green 이식 계획이었다. 채택이 MVC 유지로 뒤집혔으므로 **하지 않는다.** 기록으로 남긴다 — PMVC/QMVC로 갈 때 bind 결과를 에셋으로 굽는 부분은 그대로 쓸 수 있다.

빈 Unity 6 URP 프로젝트(구현 전무)이므로 from scratch. `Assets/Scripts/CageDeform/` 신설.

- **manifest 추가**: `com.unity.mathematics`(float3/float3x3), 선택적으로 `com.unity.burst`, `com.unity.collections`.
- **`CageCoordinates.cs`** — bind 결과 저장용 `ScriptableObject`(φ,ψ 또는 행렬, 케이지 참조 메타).
- **`GreenCoordinatesBaker.cs`** — Editor 스크립트. rest cage + 메시 입력 → 좌표 적분 → 에셋 저장. gptoolbox 이식.
- **`GreenCoordinatesDeformer.cs`** — 런타임. 변형 케이지 정점/법선 → `s_j` 계산 → 정점 재구성 → 메시 갱신.
- (2차) **`SomiglianaCoordinatesBaker.cs` / `Deformer.cs`** — 동일 인터페이스로 Kelvin 커널 + corotational 확장.

**bind 크기**(참고, 제어점 236 · 면 468 · 정점 36,426, float32): MVC **34 MB** · Green **103 MB** · Somigliana **920 MB**. Somigliana는 이 케이지 해상도로는 그대로 갈 수 없다.

### 재사용할 참조 구현 / 자료
- **Green 이식 원본**: gptoolbox `green_coordinates.m` (Alec Jacobson, libigl 기반) — 3D `s_j`·`GCTriInt` 폐형식 포함.
  https://github.com/alecjacobson/gptoolbox/blob/master/mesh/green_coordinates.m
- **Green 원 논문 테크리포트**(부록 수식): https://www.wisdom.weizmann.ac.il/~ylipman/GC/gc_techrep.pdf
- **Somigliana 공개 코드**: https://github.com/jiongchen · **supplemental**: https://www.geometry.caltech.edu/pubs/CdGD23_supp.pdf · **논문**: https://pages.saclay.inria.fr/mathieu.desbrun/pubs/CdGD23.pdf
- **종합 서베이 + 통합 앱**(방법 비교/구현 대조군): Ströter et al. CGF 2024 https://www.inf.usi.ch/hormann/papers/Stroter.2024.ASO.pdf
- **libigl**(C++, MPL-2.0, 참조/대조): harmonic·biharmonic coordinates 등.
- (대조군) MVC: Ju/Schaefer/Warren 2005 · PMVC: Lipman 2007 · QMVC(쿼드케이지): Thiery/Boubekeur 2018 · Harmonic: Joshi/Pixar 2007.

---

## 한계 & 완화 (정직한 평가)

1. **단일 전역 케이지는 스켈레톤을 모른다.** 팔이 몸통에 접히면 케이지가 겹쳐 서로 다른 부위가 섞이거나 self-intersection 가능. → 케이지를 몸에 타이트 fit, 필요 시 부위별 세그먼트 케이지 또는 스키닝 병용. (범위 밖이나 케이지 설계에 반영 권고.)
2. **오목부**(겨드랑이·가랑이·손가락 사이)는 인체 케이지에 필연. Green/Somigliana는 강한 오목부에서 국소 아티팩트 가능 — Somigliana의 corotational이 완화. baseline 대조로 PMVC/Harmonic도 참고.
3. ~~**Green은 케이지 경계에 정확히 보간되지 않음**~~ — 채택하지 않으므로 해당 없음. MVC는 경계에 보간되지만 면 위에서 조건수가 무너지므로 **여유를 갖고 담는다**는 규칙으로 피한다(cage.md `[N18]`, `clearance` 0.5 mm).
4. **두께는 좌표계가 주지 않는다.** 뼈를 늘여도 단면은 케이지가 선언한 만큼만 두꺼워진다 — 그 선언이 두께 driver이고 아직 없다(cage.md §9). 지금은 길이만 반영된 상태다.

---

## 검증 (end-to-end)

간단한 케이지+내부 메시(예: 실린더 케이지 + 내부 캡슐)로 단계별 확인:
1. **Identity**: 변형 케이지 == rest → 메시 불변 (Green은 `s_j=1`에서 재현).
2. **Similarity 재현**: 케이지에 균등 스케일/회전/이동 → 메시가 동일 변환.
3. **Stretch(핵심)**: 한 축으로 케이지 늘이기(뼈 길이 증가 모사) → Green/Somigliana는 **단면 두께 보존**, MVC 대조군은 두께 붕괴 확인(단면 측정 + 시각 비교).
4. **Bend/Concavity**: 사지 굽힘 → 심한 self-intersection·음수가중치 아티팩트 없는지.
5. **수치 검증**: 동일 입력에 대해 gptoolbox 참조 결과와 좌표/재구성 좌표 대조.
6. Unity Test Framework(이미 포함)로 1~2·5를 자동화 테스트로 고정.

**후속(범위 밖)**: 검증 통과 후 스켈레톤→케이지 구동, 스키닝 병용, GPU 이관 순으로 확장.
