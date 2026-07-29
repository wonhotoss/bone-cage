# cage-refs

cage 기반 인체 변형 선행연구·구현 조사. [making-cage.md](making-cage.md)(케이지 생성)와
[cage-deformation-plan.md](cage-deformation-plan.md)(정점 사상)의 참조 근거를 한곳에 모은다.

> 확인 수준: 초록·서베이 인용·저자 페이지까지. 본문 전체를 읽은 것은 아니며,
> Ju 2008 / PR-Cage는 PDF 접근이 막혀(ACM 403) 초록급 서술에 의존한다.

## 우리 요구 조건에 정확히 대응하는 것

우리 요구는 두 가지다 — **(1) 스켈레톤 구조가 같으면 같은 토폴로지의 케이지**,
**(2) 본별 길이 변화에 케이지 부위가 완전 대응**. 이 둘은 서로 다른 논문에 나뉘어 있다.

### (1) 토폴로지 불변 — Casti et al. 2019
**Skeleton Based Cage Generation Guided by Harmonic Fields** (Casti, Livesu, Mellado, Abu Rumman, Scateni 외,
*Computers & Graphics* 81:140–151). https://inria.hal.science/hal-02113902/ · **코드**: https://github.com/SaraCasti/Skeleton-Based-Cage-Generation

- 생성된 케이지는 "underlying skeleton이 유도하는 구조에 전적으로 부합하며, 사용자가 선택한 bending point의
  semantic으로 보강된다". 서베이 표현으로는 **입력 스켈레톤 구조와 bending node 분포가 최종 케이지의 coarse 토폴로지를 결정**.
  → 토폴로지 결정 인자가 메시가 아니라 스켈레톤. 같은 스켈레톤 + 같은 bending point면 조합구조 동일.
  우리는 본 목록을 상수로 고정하므로 결정론적이다.
- 기여의 핵심이 우리 남은 구멍과 겹친다: 스켈레톤을 임베딩한 내부 하모닉 필드의 **적분선으로 절단면을 피부까지 전파**해
  **비평면 단면(non-planar cross section)** 을 추적. 현재 축직교 링이 못 잡는 골반·사타구니·겨드랑이 wedge가 이 문제다.
- 적용 지점: bake/build 분리를 유지한 채 **bake의 단면 측정만 교체**.

### (2) 본→케이지 대응 — Ju et al. 2008
**Reusable Skinning Templates Using Cage-based Deformations** (Ju, Zhou, van de Panne, Cohen-Or, Neumann, SIGGRAPH Asia 2008 / TOG).
https://www.cs.ubc.ca/labs/imager/tr/2008/Ju_2008_Skinning/ · PDF: https://www.cs.ubc.ca/~van/papers/2008-sigAsia-skinning.pdf

- **관절 타입별 케이지 템플릿**을 정의해 캐릭터 간 공유·재사용. **스켈레톤이 케이지 정점을 구동**하고 케이지가 메시를 변형.
- 케이지가 "캐릭터 모델과 느슨하게 분리(loosely decoupled)" → 케이지가 메시가 아니라 리그에 귀속.
  "케이지 생성 함수는 뼈 길이 목록만 받는다"의 원형.

### 런타임 짝 — Corda et al. 2020
**Real-time Deformation with Coupled Cages and Skeletons** (CGF 2020). https://arxiv.org/pdf/1909.02807 ·
http://pers.ge.imati.cnr.it/livesu/papers/CTLPBS20/CTLPBS20.html
- "skeleton이 cage를 포즈시키고, cage가 skin을 포즈시킨다". 스켈레톤·케이지 변형공간을 결합해 둘 중 하나로는
  못 만드는 포즈까지 커버. 우리 파이프라인 구조와 같다.

### 갭 (우리 고유 영역)
위 셋 모두 **구동 파라미터가 관절 회전(pose)** 이다. **뼈 길이(proportion) 편집을 케이지 구동 신호로 쓰는 선행연구는 찾지 못했다.**
가장 가까운 대체 근거:
- **Adaptive skeleton-driven cages for mesh sequences** (Chen & Feng, CAVW 2014) https://onlinelibrary.wiley.com/doi/abs/10.1002/cav.1577
  — adaptive cross-section 기반 케이지를 **시퀀스 전 프레임에 동일 구조로 전파** 후 자동 refine.
  [cage-lab.md](cage-lab.md)의 다중 포즈 containment와 같은 문제 설정.
- **Neural Cages** (CVPR 2020) https://arxiv.org/pdf/1912.06395 — 템플릿 케이지를 랜드마크 MVC 최적화로 새 체형에 피팅.
  토폴로지 고정 + 비율만 변경의 학습 버전. SMPL 포즈 데이터로 학습.
- **Roblox Layered Clothing** — 고정 리그(R15)에 대해 정점 순서·UV까지 규정된 **고정 토폴로지 케이지 템플릿**을 전 아바타가 공유.
  요구 조건을 프로덕션에서 그대로 만족시킨 사례.
  https://create.roblox.com/docs/art/accessories/caging-best-practices ·
  https://github.com/Roblox/creator-docs/blob/main/content/en-us/art/accessories/clothing-specifications.md

## 케이지 생성 (containment·tightness·워터타이트)

- **PR-Cage: Progressive Feasibility Relaxation for Tight Bounding Cage Generation** (TOG 45(4), 2026) https://doi.org/10.1145/3811300
  — 면 수 최소화 ↔ tightness 최대화의 균형. 현재 outside 정점 문제에 직접 대응하나 **스켈레톤 인식은 아님**
  → Casti의 토폴로지 안에서 tightness 기준으로만 참고.
- **Automatic cage generation by improved OBBs for mesh deformation** (Visual Computer 2011) https://link.springer.com/article/10.1007/s00371-011-0595-6
  — OBB 트리 → boolean union → 외곽면 추출. 지금의 접합부 박스 겹침을 워터타이트로 용접하는 접근이 이것.
- **Automatic generation of coarse bounding cages from dense meshes** (SMI 2009) http://www.cad.zju.edu.cn/home/hwlin/pdf_files/Automatic-generation-of-coarse-bounding-cages-from-dense-meshes.pdf
- **Interactive cage generation for mesh deformation** (I3D 2017) https://dl.acm.org/doi/10.1145/3023368.3023369
  — 병렬 복셀화 + coarsening, 영역별 정밀도 브러시. 골반·굽힘부만 링 밀도를 올리는 계획과 맞음.
- **\*Cages: A Multilevel, Multi-Cage-Based System** — 계층·다중 케이지. 부위별 세그먼트 케이지의 선행연구.
- **A Survey on Cage-based Deformation of 3D Models** (Ströter et al., CGF 2024) https://www.inf.usi.ch/hormann/papers/Stroter.2024.ASO.pdf
  — 최신 서베이. **여러 방법을 통합한 애플리케이션을 함께 공개** → 대조군 구현을 대체할 수 있다.

## 좌표계 (정점 사상)

계획서의 Green → Somigliana 축은 [cage-deformation-plan.md](cage-deformation-plan.md) 참조. 그 이후 진전:

- **Flexible 3D Cage-based Deformation via Green Coordinates on Bézier Patches** (SIGGRAPH 2025)
  https://arxiv.org/html/2501.14068v3 · **코드**: https://github.com/Submanifold/BezierGreen
  — Green을 베지에 패치로 확장, **훨씬 적은 케이지 정점**으로 매끄러운 변형. 삼각 Green 구현의 상위 호환 경로.
- **QMVC 레퍼런스 구현** https://github.com/superboubek/QMVC — 쿼드 케이지용 MVC.
  우리 케이지가 quad-tube라 대조군 이상의 후보.
- **Shape-Deformation-with-Cages** https://github.com/Junyu-Liu-Nate/Shape-Deformation-with-Cages — Green+MVC, 2D/3D, 부분 케이지.
- **maya_greenCageDeformer** https://github.com/ryusas/maya_greenCageDeformer — DCC 통합 사례.
- Polynomial 2D Green / Biharmonic Coordinates for High-order Cages (2024/TOG 2025) — 아직 2D, 참고용.

## 인체 대상 케이지 응용 (참고)

- **D3GA: Drivable 3D Gaussian Avatars** https://arxiv.org/abs/2311.08581 · https://github.com/facebookresearch/D3GA
  — 인체·얼굴·의상별 **테트라 케이지**를 프록시로 LBS 대체. 케이지 변형기울기가 프리미티브를 늘려준다는 논지가
  Green/Somigliana를 쓰는 이유와 같다. 부위별 케이지 분해의 실제 구현.
- **Intersection-Free Garment Retargeting** (SIGGRAPH 2025) https://dl.acm.org/doi/10.1145/3721238.3730590
  — 수동 케이징을 대체하며 교차 없음 보장. self-collision 제거 과제의 최신 참조.
- **CageNeRF** (NeurIPS 2022) https://papers.neurips.cc/paper_files/paper/2022/file/cb78e6b5246b03e0b82b4acc8b11cc21-Paper-Conference.pdf ·
  **CAGE-GS** (2025) https://arxiv.org/pdf/2504.12800 · **AniArtAvatar** https://arxiv.org/pdf/2403.17631
  — 뉴럴 표현 + 케이지. 직접 관련은 낮음.
- **Spatial Deformation Transfer** (Ben-Chen, Weber, Gotsman, SCA 2009) https://dl.acm.org/doi/10.1145/1599470.1599479
  — 케이지를 매개로 변형을 다른 형상에 전이. 하모닉 기저 + 비선형 최적화.
