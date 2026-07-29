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

## 채택 방향: Somigliana(품질 목표) + Green(기반 구현)

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
| **Green (Lipman'08)** | 폐형식 | ✓ 준등각 | 보통 | ✗ | **두께 보존, 1차 채택** |
| **Somigliana (Chen'23)** | precompute+corotational | ✓ + 부피제어 | 보통 | ✗ | **최고 품질, 목표** |

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

## Unity 구현 계획

빈 Unity 6 URP 프로젝트(구현 전무)이므로 from scratch. `Assets/Scripts/CageDeform/` 신설.

- **manifest 추가**: `com.unity.mathematics`(float3/float3x3), 선택적으로 `com.unity.burst`, `com.unity.collections`.
- **`CageCoordinates.cs`** — bind 결과 저장용 `ScriptableObject`(φ,ψ 또는 행렬, 케이지 참조 메타).
- **`GreenCoordinatesBaker.cs`** — Editor 스크립트. rest cage + 메시 입력 → 좌표 적분 → 에셋 저장. gptoolbox 이식.
- **`GreenCoordinatesDeformer.cs`** — 런타임. 변형 케이지 정점/법선 → `s_j` 계산 → 정점 재구성 → 메시 갱신.
- (2차) **`SomiglianaCoordinatesBaker.cs` / `Deformer.cs`** — 동일 인터페이스로 Kelvin 커널 + corotational 확장.

### 재사용할 참조 구현 / 자료

> 선행연구 조사 전반(케이지 생성·스켈레톤 결합·인체 응용 포함)은 [cage-refs.md](cage-refs.md)에 모았다.

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
3. **Green은 케이지 경계에 정확히 보간되지 않음**(내부 몸엔 대개 무해). 경계 정확도가 필요하면 exact/normalized 변형 또는 Somigliana.

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
