# cage — 뼈 → 케이지 함수 선언

## 0. 목적과 규약

이 프로젝트는 두 과제로 나뉜다.

1. **스켈레톤 → 케이지 함수.** 관절 위치에서 케이지 정점을 정의하고, 그 정점을 잇는 고정 토폴로지를 정의한다. 표준 스켈레톤 + 표준 메시에서 케이지는 메시의 모든 정점을 포함하고 자기 겹침이 없어야 하며, 뼈 길이가 케이지 정점 사이의 관계로 맺어져야 한다.
2. **변형 케이지로의 메시 사상.** 길이가 바뀐 스켈레톤에 1의 함수를 적용해 새 케이지를 만들고, 표준 메시를 그 안으로 사상한다. → [cage-deformation-plan.md](cage-deformation-plan.md)

이 문서는 **1의 선언**이다. 구현은 [cage.cs](../unity/Assets/Scenes/cage.cs)(생성), [mapping_tester.cs](../unity/Assets/Scenes/mapping_tester.cs)(통합·디버그).

**편집 규약**

- 문서의 표 한 행은 코드의 선언 한 줄에 대응한다. 표 머리에 대응 심볼을 적는다.
- 개발자는 표의 값과 행을 고치고, 에이전트는 코드를 표에 맞춘다. **코드에만 있고 문서에 없는 결정은 허용하지 않는다** — 발견되면 문서에 먼저 올린다.
- 이름은 이 문서, `cage_constants`의 `name`, 씬 뷰 태그(§7)에서 동일하다.
- 표는 값만 담는다. 근거는 §8 설계 노트에 두고 `[N1]`처럼 참조한다.
- 길이 단위는 두 가지만 쓴다. **씬 단위**(m, 상수 표기) — bake에서 `/ scale`로 rig 단위로 환산. **비율** — rest 길이에 대한 비.

## 1. 어휘

| 용어 | 정의 | 코드 |
|---|---|---|
| 관절 `J` | rig 스켈레톤의 Transform. 뼈는 부모→`J`의 선분이며 `J`의 이름으로 부른다. | `cage_constants.joint_*` |
| FK | `pos[J] = pos[parent] + rest_dir[J] · length[J]`. `rest_dir`는 rest에서 굽고 편집에 불변, `length`는 편집값(편집 대상이 아닌 뼈는 rest 길이). 라이브 스켈레톤과 정확히 일치. `[N8]` | `joint_centers` |
| 케이지 공간 | rig root(`Hips`) 로컬. 케이지 GameObject는 root의 identity 자식(씬 ×100 스케일 상속). | `mapping_tester.ensure_cage_view` |
| 축 `up` `side` `depth` | rest 스켈레톤에서 유도 후 **cardinal 축으로 스냅**: `up = cardinal(Head − Hips)`, `side = cardinal(LeftArm − RightArm)`(캐릭터 왼쪽이 +), `depth = (up × side) · sign(dot(LeftToeBase − LeftFoot, up × side))`(**+depth = 앞**). `[N9]` | `bake` 서두 |
| 살(flesh) | rest 메시의 각 정점을 **지배 본**(최대 가중치)에 배정한 점집합. 케이지 공간. | `gather_flesh` |
| 서브트리 | 관절 `a`와 그 모든 자손. "`a`의 살" = 서브트리 관절들의 살. | `subtree` |
| 측정 창 | **cap**: 감쌀 살 전체. **joint**: 링 평면에서 `slab · max(앵커 뼈 rest 길이)` 이내의 살만. | `measure` |
| inflate | 잰 구간 `[lo, hi]`를 중앙 기준 `(1 + margin)`배로 부풀림. 모든 측정 구간에 적용. | `inflate` |
| 링 | 축 정렬 사각형, **정점 4**. 축 `n`(법선, 몸 바깥), `s`(실루엣 축: 앞/뒤 판의 경계 변이 놓이는 방향), `d`(깊이 축: 앞/뒤 판을 가르는 방향). 코너 = (hi/lo 실루엣 쪽) × (front/back 깊이 쪽). | `cage_ring`, `ring_corners` |
| 기둥(post) | 제어점 하나가 소유하는 **정점 2**(판 축 `d`의 hi/lo). 손 전용. 위치 = 앵커 관절들의 아핀 결합 + 오프셋. | `cage_post`, `post_ends` |
| 판(plate) | 닫힌 제어점 고리를 hi 정점들로 한 번, lo 정점들로 한 번(역순) 채운 면. ladder 삼각화. `[N3]` | `topology`, `strip` |
| 옆판(wall) | 제어점 사슬. 이웃 쌍마다 쿼드 1(hi–hi–lo–lo). | `topology` |
| 여유(reach) | 잰 구간 바깥으로 더하는 상수. 링: `front` `back`(깊이 축), `hi`(+s 쪽), `outward`(n 방향 평면 이동). 모두 씬 단위. | `recipe` |
| 정점 번호 | 링 `i` 코너 `c` → `i·4 + c` (`hi_front 0, hi_back 1, lo_back 2, lo_front 3`). 기둥 `p` 끝 `e` → `rings·4 + p·2 + e` (`hi 0, lo 1`). 기둥 순서 = 생성 순서(왼손 → 오른손, 각 손은 제어점 6 → 엄지…새끼 링). | `cage` 상수 |

## 2. 전역 상수 — `cage` 의 `const`

| 이름 | 값 | 단위 | 의미 |
|---|---|---|---|
| `margin` | 0.05 | 비율 | 모든 측정 구간을 살에서 띄우는 여유 |
| `slab` | 0.25 | 비율 | joint 링의 측정 창 반폭(앵커 뼈 rest 길이 대비) |
| `valley_reach` | 0.01 | 씬 | 손가락 계곡 제어점을 손목 반대 방향으로 미는 거리 |
| `wrist_drop` | 0.01 | 씬 | 손목 링의 손바닥 쪽 변을 손 판 아래로 내리는 거리 `[N5]` |

## 3. 몸통 링 — `recipes[...]`

열: **앵커** = 링을 놓는 관절(변별로 분리됨, §6). **감쌀 살** = 서브트리 루트. **종류** cap/joint = 측정 창(§1). 여유는 씬 단위, 빈칸 = 0.

| 이름 | 앵커 | 감쌀 살 | n | s | d | 종류 | front | back | hi | outward | 비고 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `crown` | Head | Head | +up | side | depth | cap | 0.1 | | | | 정수리 캡 |
| `L arm` | LeftArm | LeftShoulder | +side | up | depth | joint | 0.2 | 0.1 | 0.05 | 0.05 | 몸통 판과 팔 판의 경계 `[N2]`. `hi`는 튠 중(§7) |
| `L elbow` | LeftForeArm | LeftArm | +side | up | depth | joint | | | 0.05 | | |
| `L wrist` | LeftHand | LeftHand | +side | up | depth | joint | | | | | 단면은 손이 덮어씀 §4a |
| `R arm` | RightArm | RightShoulder | −side | up | depth | joint | 0.2 | 0.1 | 0.05 | 0.05 | `[N2]`. `hi`는 튠 중(§7) |
| `R elbow` | RightForeArm | RightArm | −side | up | depth | joint | | | 0.05 | | |
| `R wrist` | RightHand | RightHand | −side | up | depth | joint | | | | | §4a |
| `hip` | LeftUpLeg, RightUpLeg | Hips | +up | side | depth | joint | | | | | 몸통 판과 다리 판의 경계. 변별로 자기 쪽 고관절 `[N1]` |
| `knee` | LeftLeg, RightLeg | LeftUpLeg, RightUpLeg | −up | side | depth | joint | | 0.1 | | | 양다리 공용 `[N1]` |
| `sole` | LeftFoot, LeftToeBase, RightFoot, RightToeBase | LeftFoot, RightFoot | −up | side | depth | cap | | | | | 발바닥 캡 `[N1]` |

`hi` 여유의 방향은 `s`의 +쪽: 팔 링(`s = up`)에서는 위, 나머지(`s = side`)에서는 캐릭터 왼쪽.

**bake 규칙** (`measure`): 평면 = `max(앵커·n)`. 측정 창의 살로 `s`·`d` 구간을 재고 inflate. 앵커를 `s` 좌표의 중앙값 기준으로 hi/lo 변에 배정(단일 사지 링은 같은 앵커가 양쪽에). 굽는 값:
`along = (cap ? (max(살·n) − 평면)·(1+margin) : 0) + outward`,
`s_hi = hi_s − max(hi앵커·s) + hi`, `s_lo = min(lo앵커·s) − lo_s`,
`d_hi = hi_d − max(앵커·d) + front`, `d_lo = min(앵커·d) − lo_d + back`.

## 4. 손 — `hand(prefix, tag, slot, n, mirror)`

좌우 각각 호출: `("LeftHand", "L", L wrist, +side, mirror)`, `("RightHand", "R", R wrist, −side, 정방향)`. 아래 이름의 `L`은 `R`로도 읽는다.

### 4a. 공통

| 항목 | 정의 |
|---|---|
| 손 축 | `n` = 팔 바깥(±side), `s` = **depth**(엄지 +, 새끼 −), `d` = **up**(판 축). 팔 링과 프리즘 축이 90° 다르다 `[N4]` |
| 판 두께 | 손 서브트리 살 전체의 `d` 구간을 inflate. 손의 모든 기둥이 공유. 기둥의 `d` 좌표는 **손목 관절**의 `d` 좌표 + 이 오프셋 |
| 손목 링 덮어쓰기 | 실루엣 축(`up`): hi = 판 위, lo = 판 아래 + `wrist_drop`. 깊이 축(`depth`): 손목 평면에서 `rest_len(Middle1)·0.5` 이내 살의 구간을 inflate `[N5]` |
| 손 폭 | 손 살 전체의 `s` 구간을 inflate → `wide_hi`(엄지 쪽), `wide_lo`(새끼 쪽) |

### 4b. 손바닥 제어점 6 — `cp[0..5]`

기둥 하나씩. 위치 = 앵커 아핀 결합 + 판 내 오프셋.

| 이름 | 앵커(가중치) | 오프셋 |
|---|---|---|
| `L thumb out` | Thumb2 (1) | `s`로 `wide_hi`까지 |
| `L thumb\|index` | Thumb2, Index1 (½, ½) | 손목→중점 방향(판 내 투영)으로 `valley_reach` |
| `L index\|middle` | Index1, Middle1 (½, ½) | 〃 |
| `L middle\|ring` | Middle1, Ring1 (½, ½) | 〃 |
| `L ring\|pinky` | Ring1, Pinky1 (½, ½) | 〃 |
| `L pinky out` | Pinky1 (1) | `s`로 `wide_lo`까지 |

엄지는 Thumb1이 손안에 묻혀 있으므로 **Thumb2**에서 분기한다. 인접한 두 제어점이 각 손가락의 **분기 링(링 1)** 이다.

### 4c. 손가락 링 — `climb(f)`

분기 링 다음부터, 관절마다 하나 + 마지막 마디 뒤 **가상 endbone**에 하나. 링 하나 = 기둥 2(엄지 쪽 hi, 새끼 쪽 lo), 같은 이름.

| 손가락 | 링 2 | 링 3 | 링 4 |
|---|---|---|---|
| `L thumb` | Thumb3 | endbone | — |
| `L index` `L middle` `L ring` `L pinky` | `{F}2` | `{F}3` | endbone |

규칙:
- **축**: 뼈 방향 `dir[J]`를 판 평면에 투영한 `along`, 그에 직교하는 판 내 축 `perp`(+ = 엄지 쪽). 링은 `s`가 아니라 **자기 뼈에 직교**한다 `[N6]`.
- **반경**: 관절 `J` 서브트리 살의 `perp` 구간을 inflate. 기둥 오프셋 = `perp · (구간 끝 − 관절의 perp 좌표 ± finger_reach)`.
- **endbone**: 마지막 마디 살이 `dir`로 뻗은 최대 거리 ×(1+margin)를 **rest 길이 비율 `f`** 로 굽고, 앵커 `(last, parent(last))`에 가중치 `(1+f, −f)` `[N6]`.

### 4d. 손가락 링 추가 여유 — `finger_reach`

씬 단위, 좌우 손 공통. 표에 없는 링은 잰 값 그대로.

| 손가락 | 링 | hi(엄지 쪽) | lo(새끼 쪽) |
|---|---|---|---|
| Index | 3 | 0.001 | 0.001 |
| Middle | 2 | 0 | 0.001 |

## 5. 토폴로지

몸통의 "제어점" = 링의 실루엣 변 `(링, hi/lo)` → 정점 쌍 (front, back). 손의 제어점 = 기둥 → (hi, lo). 손목 사각형은 양쪽이 공유하되 **역할이 바뀐다**: 팔은 앞/뒤 변을 판에, 위/아래 변을 옆판에 쓰고 손은 반대 `[N4]`.

### 5a. 몸통 판 — `panels` (앞판 + 거울 뒷판)

| 판 | 고리 (링·변) |
|---|---|
| 몸통 8각형 | crown·hi → L arm·hi → L arm·lo → hip·hi → hip·lo → R arm·lo → R arm·hi → crown·lo |
| 왼 위팔 | L arm·hi → L elbow·hi → L elbow·lo → L arm·lo |
| 왼 아래팔 | L elbow·hi → L wrist·hi → L wrist·lo → L elbow·lo |
| 오른 위팔 | R arm·lo → R elbow·lo → R elbow·hi → R arm·hi |
| 오른 아래팔 | R elbow·lo → R wrist·lo → R wrist·hi → R elbow·hi |
| 허벅지 | hip·hi → knee·hi → knee·lo → hip·lo |
| 종아리 | knee·hi → sole·hi → sole·lo → knee·lo |

### 5b. 몸통 옆판 — `perimeter` (사슬 3, 손목에서 끊김)

| 사슬 | 경로 |
|---|---|
| 1 | crown·hi → L arm·hi → L elbow·hi → L wrist·hi |
| 2 | L wrist·lo → L elbow·lo → L arm·lo → hip·hi → knee·hi → sole·hi → **sole·lo** → knee·lo → hip·lo → R arm·lo → R elbow·lo → R wrist·lo |
| 3 | R wrist·hi → R elbow·hi → R arm·hi → crown·lo → **crown·hi** |

같은 링을 잇는 쌍(sole·hi→sole·lo, crown·lo→crown·hi)이 그 링 자신의 사각형 = **캡**이다.

### 5c. 손 — `loops`, `outline`

`wrist_front` / `wrist_back` = 손목 링의 앞·뒤 변을 (위, 아래) 쌍으로 읽은 것.

| 면 | 고리 / 사슬 |
|---|---|
| 손등·손바닥 8각형 | wrist_front → cp0 … cp5 → wrist_back |
| 손가락 `f` 판 (5장) | cp[f] → 링 2..끝의 hi 기둥 → 링 끝..2의 lo 기둥 → cp[f+1] |
| 옆판 (1사슬) | wrist_front → (손가락 0..4마다: cp[f] → hi 기둥들 → lo 기둥들 역순) → cp5 → wrist_back |

왼손(`mirror`)은 고리·사슬을 모두 **역순**으로 추적한다 `[N7]`.

### 5d. 불변식

- 모든 방향 간선이 정확히 한 번 나타나고 그 반대 간선이 존재 → **닫힘 + 일관된 방향**. `Debug.Assert`로 bake마다 검사.
- 부호 있는 부피가 음수면 전체 winding 반전 → 법선이 바깥 `[N7]`.
- 결과: **176 정점 / 348 삼각형**, Euler = 2. (링 10×4 + 손 2×(제어점 6 + 엄지 2링·2 + 손가락 4×3링·2)×2)

## 6. 런타임 재배치 — `points(lengths, k)`

순수 함수: 편집 길이 → FK 관절 `jc` → 제어점.

**링** (`ring_corners`): 변별로 자기 앵커만 본다.
`plane_hi = n·(max(hi앵커·n) + along)`, `plane_lo = n·(max(lo앵커·n) + along)`,
`edge_hi = s·(max(hi앵커·s) + s_hi)`, `edge_lo = s·(min(lo앵커·s) − s_lo)`,
깊이는 양쪽 앵커 전체에서 `lo_d = d·(min(앵커·d) − d_lo)`, `hi_d = d·(max(앵커·d) + d_hi)`.
코너 = plane + edge + 깊이. 좌우 변이 독립이라 공용 링은 기울 수 있고, 깊이가 공유라 네 점은 항상 한 평면 `[N1]`.

**기둥** (`post_ends`): `at = Σ weight·jc[anchor] + reach`를 판 평면에 투영, `d` 좌표는 `jc[손목]·d + d_lo/d_hi`.

## 7. 검증·디버그 — `mapping_tester`

| 기능 | 동작 |
|---|---|
| 슬라이더 | 편집 대상 뼈 53개(몸통 23 + 손가락 2×5×3), 범위 rest × [0.5, 1.5]. 변경마다 `update_body()` = 케이지 재생성 → deform → rest pose 재바인딩. |
| 이름 태그 (씬 뷰, 선택 시) | 이름 그룹마다 중심에 태그 하나(흰색). 화면 크기 `tag_min_px` 미만 그룹은 숨김 → 전신에선 링 이름만, 손 줌에서 손가락 이름. **클릭 → 그 그룹만 펼침**(노랑): 정점 번호(시안), 놓는 관절 태그(오렌지) + 관절→대상 점선. 가상 endbone은 `Joint ×1.40`처럼 가중치 표기. |
| 와이어 | 라이브 케이지, 시안. |
| `check containment` | 소스 지오메트리를 타깃 본에 LBS → 현재 케이지에 대해 **광선 패리티** 판정. 바깥 정점을 빨간 큐브로. rest에서만 의미 있음. |
| `check self-collision` | 정점을 공유하지 않는 삼각형 쌍의 관통 검출, 빨간 외곽선. 손가락 길이를 크게 바꾼 뒤 먼저 볼 것. |
| `rebuild cage` | 재bake + 케이지 갱신 + 재bind. 이 문서의 상수를 바꾸면 누른다. |
| 튠 슬라이더 (`cage_tune`) | 아직 확정 안 된 §3 값을 인스펙터에서 찾는 임시 편집기. 현재: arm 링 `hi`(기본 0.05, 범위 −0.05..0.1). 드래그 중엔 재bake + 케이지 갱신만(와이어가 바로 따라옴), 놓으면 재bind + deform. 값이 정해지면 표와 recipe로 옮기고 슬라이더는 지운다. |
| import 시 | `bake` → `bind` → 케이지 자식 생성 → `update_cage`. FBX는 Read/Write 활성 필요. |

## 8. 설계 노트

- **[N1] 공용 링의 변은 각자의 앵커로.** 두 변이 한 평면(`n`으로 전체 최댓값)을 공유하면 한쪽 다리만 **줄일** 때 링이 반대쪽 무릎에 붙잡혀 따라오지 않는다(늘일 때만 따라옴). 변을 갈라 두면 링이 기울면서 양다리를 추적하고 포함도 유지한다. 대가는 비대칭 편집 시 축 정렬이 풀리는 것 — 깊이 범위는 공유라 네 점은 한 평면에 남는다. 단일 사지 링은 같은 앵커가 양쪽에 들어가 축 정렬 그대로.
- **[N2] 여유는 그 자리에서 몸통 판을 경계 짓는 링에.** 얼굴·가슴·배·어깨는 링 자신의 살 측정에 안 들어오므로 여유로 덮는다. 어깨·고관절 링이 들어오면서 몸통 판의 변이 팔꿈치·무릎에서 어깨·고관절로 옮겨갔으므로 가슴·어깨 여유도 팔꿈치 링에서 arm 링으로 옮겼다. 팔꿈치 링에 남기면 팔 판만 앞으로 20cm 부푼다.
- **[N3] fan이 아니라 ladder.** 링마다 깊이가 달라 fan은 판 전체를 첫 제어점 기준으로 비튼다 — 몸통 중앙선이 정수리에서 고관절로 직행하며 어깨 링을 건너뛰어 어깨 앞 여유가 가슴에 반영되지 않았다. ladder는 좌우 대칭이고 가슴 띠가 어깨 깊이로 평평하다.
- **[N4] 손은 링이 아닌 기둥, 프리즘 축은 팔과 90°.** 인접 분기 링이 **제어점을 공유**해야 손등이 한 장의 폴리곤으로 남고, 손가락 링이 자기 뼈에 직교할 수 있다 — 링(정점 4)은 그걸 못 한다. T-pose에서 손바닥이 아래를 보므로 손가락은 `depth`로 벌어지고 두께는 `up`이다. 손목 사각형의 네 변은 팔과 손이 역할을 바꿔 각각 두 번씩 쓰이므로 껍질은 닫힌 채로 남고, 닫힘 assertion이 그것을 검증한다.
- **[N5] 손목 링만 손 쪽에서 잰다.** 두께는 손 살 전체에서 — 모든 손 기둥이 이 두께를 공유하므로 손목 단면만 보면 손가락이 판을 뚫는다. 폭은 중수골 절반 이내 살에서 — 링 자신의 slab은 아래팔 길이에 비례해 너무 넓어 벌어진 손가락까지 폭으로 잡는다. 단 아래팔이 손보다 훨씬 굵어 그대로 두면 팔 판이 손목에서 손바닥 두께로 잘록해지므로, 손목 링의 **손바닥 쪽 변만** `wrist_drop`만큼 내린다. 손 기둥들은 판을 지키므로 손 전체가 두꺼워지지 않고 손바닥 판이 손목에서 손 쪽으로 비스듬히 올라온다.
- **[N6] 손가락 링은 자기 뼈에 직교, endbone은 비율.** `s`에 직교시키면 벌어진 손가락이 판을 뚫는다. rig에 endbone이 없으므로 마지막 마디 살이 뼈 방향으로 뻗은 만큼을 rest 길이 비율로 굳혀 `(1+f, −f)` 아핀 결합으로 놓는다 — 그래서 마디를 늘리면 끝 링이 따라 나간다. 현재 케이지에서 **길이에 비례해 굵기·길이가 따라가는 유일한 부위**다(§9 참고).
- **[N7] winding은 부피로, 왼손은 역추적.** 판 방향은 일관되게 추적하되 어느 쪽이 바깥인지는 rig 축에 달렸으므로 부호 있는 부피로 판정해 필요 시 전체를 뒤집는다. 좌우 손은 프레임 손대칭이 반대라 왼손만 추적 순서를 뒤집는데, 닫힘 assertion이 그 판정을 검증한다.
- **[N8] 케이지는 길이만의 함수.** 매 프레임 메시를 읽지 않는다. rest 방향이 편집에 불변이므로 FK가 라이브 스켈레톤을 정확히 재현하고, 살 측정은 bake 1회에 상수로 굳는다. 현재 vicon 메시의 rest 케이지가 조건을 만족하면 비율이 바뀐 pose의 케이지도 만족한다고 본다.
- **[N9] cardinal 스냅과 발가락 부호.** rig root 로컬은 월드 정렬이 아니므로 스켈레톤에서 축을 유도하되 cardinal로 스냅해 링을 축 정렬로 유지한다. 외적은 깊이 축만 정하고 앞뒤는 못 정하므로 발가락 방향으로 부호를 정한다.

## 9. 미결

- **두께 driver — "키가 크면 두꺼워진다".** 현재 단면은 rest 살 측정값 + 절대 여유이고 앵커 spread만 길이를 따른다. 단일 사지 링(팔꿈치·손목)은 spread가 0이라 전신을 1.2배 늘여도 팔 굵기가 그대로다. 링마다 단면을 구동하는 뼈(또는 전신 척도)를 선언하는 열이 필요하다: `단면 = rest 단면 × f(driver 길이 / rest)`. §4c endbone이 이 형태의 선례.
- **몸통 정중선 정점.** MVC의 음수 가중치(겨드랑이·가랑이 오목부)로 한쪽 편집이 반대편에 새는 비국소성. 정중선 제어점을 더해 케이지 쪽에서 완화할 예정.
- **포함률.** 이 정도로 조악한 몸통 케이지는 어깨·배·엉덩이·팔 위아래가 판을 뚫는다. 의도된 1단계 상태이며 `check containment`가 양을 보여준다. 필요 시 링 추가(어깨·골반·가슴)로 조인다 — 토폴로지 표만 늘리면 되고 assertion이 오추적을 막는다.
- **자기겹침 스윕 테스트.** 실제로 지원할 길이 범위가 정해진 뒤, 그 범위의 극단·조합에 대해 `self_overlaps`를 자동화한다.
- **bind 비용.** 제어점 40 → 176으로 늘면서 MVC bind가 그만큼 무거워졌다(import 1회). Green/Somigliana로 갈 때 먼저 부딪히는 벽 → [cage-deformation-plan.md](cage-deformation-plan.md).
