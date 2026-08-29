# cage — 뼈 → 케이지 함수 선언

## 0. 목적과 규약

이 프로젝트는 두 과제로 나뉜다.

1. **스켈레톤 → 케이지 함수.** 관절 위치에서 케이지 정점을 정의하고, 그 정점을 잇는 고정 토폴로지를 정의한다. 표준 스켈레톤 + 표준 메시에서 케이지는 메시의 모든 정점을 포함하고 자기 겹침이 없어야 하며, 뼈 길이가 케이지 정점 사이의 관계로 맺어져야 한다.
2. **변형 케이지로의 메시 사상.** 길이가 바뀐 스켈레톤에 1의 함수를 적용해 새 케이지를 만들고, 표준 메시를 그 안으로 사상한다. → [cage-deformation-plan.md](cage-deformation-plan.md)

이 문서는 **1의 선언**이다. 구현은 [cage.cs](../unity/Assets/Scenes/cage.cs)(생성), [mapping_tester.cs](../unity/Assets/Scenes/mapping_tester.cs)(통합·디버그). 여기까지 온 경위와 세션별 결정은 [journal.md](journal.md).

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
| 측정 창 | **cap**: 감쌀 살 전체(평면은 살 끝까지 밀림). **joint**: 링 평면에서 `slab · max(앵커 뼈 rest 길이)` 이내의 살만. **split**: 평면(앵커 + `outward`) 너머(`n` 쪽)의 살 전체 — 그 위의 판이 감싸야 할 것. | `measure`, `fit` |
| inflate | 잰 구간 `[lo, hi]`를 중앙 기준 `(1 + margin)`배로 부풀림. 모든 측정 구간에 적용. | `inflate` |
| 링 | 사각형, **정점 4**. 축 `n`(법선, 몸 바깥), `s`(실루엣 축: 앞/뒤 판의 경계 변이 놓이는 방향), `d`(깊이 축: 앞/뒤 판을 가르는 방향). 코너 = (hi/lo 실루엣 쪽) × (front/back 깊이 쪽). 두 변이 `d`에 평행하므로 변별 `along`으로 기울어도 한 평면 `[N11]`. | `cage_ring`, `ring_corners` |
| 기둥(post) | 제어점 하나가 소유하는 **정점 2**(축 `d`의 hi/lo). 손과 정중선(§3b). 판 내 위치 = 앵커 관절들의 아핀 결합 + 오프셋. `d` 좌표는 링 변과 같은 규칙 — 양끝이 각자의 d 앵커 `max`/`min` + 여유. | `cage_post`, `post_ends` |
| 판(plate) | 닫힌 제어점 고리를 hi 정점들로 한 번, lo 정점들로 한 번(역순) 채운 면. ladder 삼각화. `[N3]` | `topology`, `strip` |
| 옆판(wall) | 제어점 사슬. 이웃 쌍마다 쿼드 1(hi–hi–lo–lo). | `topology` |
| 여유(reach) | 잰 구간 바깥으로 더하는 상수. 링: `front` `back`(깊이 축, **hi/lo 변별**), `hi`/`lo`(±s 쪽), `outward hi`/`outward lo`(변별 n 방향 이동). 음수 = 안쪽. 모두 씬 단위. | `recipe` |
| 정점 번호 | 링 `i` 코너 `c` → `i·4 + c` (`hi_front 0, hi_back 1, lo_back 2, lo_front 3`). 기둥 `p` 끝 `e` → `rings·4 + p·2 + e` (`hi 0, lo 1`). 기둥 순서 = 생성 순서(정중선 7: crown·head·neck·sternum·hip·knee·sole → 왼손 → 오른손, 각 손은 제어점 6 → 엄지…새끼 링). | `cage` 상수 |

## 2. 전역 상수 — `cage` 의 `const`

| 이름 | 값 | 단위 | 의미 |
|---|---|---|---|
| `margin` | 0.05 | 비율 | 모든 측정 구간을 살에서 띄우는 여유 |
| `slab` | 0.25 | 비율 | joint 링의 측정 창 반폭(앵커 뼈 rest 길이 대비) |
| `valley_reach` | 0.01 | 씬 | 손가락 계곡 제어점을 손목 반대 방향으로 미는 거리 |
| `wrist_drop` | 0.01 | 씬 | 손목 링의 손바닥 쪽 변을 손 판 아래로 내리는 거리 `[N5]` |

## 3. 몸통 링 — `recipes[...]`

열: **앵커** = 링을 놓는 관절(변별로 분리됨, §6). **감쌀 살** = 서브트리 루트. **종류** cap/joint/split = 측정 창(§1). `↷θ` = `side` 축으로 θ만큼 앞으로 기울인 축(`n = cos·up + sin·depth`, `d = cos·depth − sin·up`). 여유는 씬 단위, 빈칸 = 0. `front`/`back`은 hi/lo 변별 — 한 값이면 양 변 공통, `a / b`면 hi 변 / lo 변.

| 이름 | 앵커 | 감쌀 살 | n | s | d | 종류 | front | back | hi | lo | outward hi | outward lo | 비고 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `crown` | Head | Head | +up | side | depth | cap | 0 | 0 | | | | | 정수리 캡. `front`·`back`은 튠 중(§7) — 가슴~배꼽 정중선과 날개뼈가 몸통 판을 뚫음 |
| `head` | Head | Head | up↷25° | side | depth↷25° | split | 0 | 0 | | | 0.023 | 0.023 | 머리–목 분리 평면. 기울기·오프셋은 씬의 head splitter에서 읽음; 기울기·오프셋·`front`·`back` 튠 중(§7) `[N12]` |
| `L arm` | LeftArm | LeftShoulder | +side | up | depth | joint | 튠 / 튠 | 튠 / 튠 | 튠(§7) | 튠(§7) | 튠(§7) | 튠(§7) | 몸통 판과 팔 판의 경계 `[N2]`. 모든 여유 튠 중(§7) |
| `L elbow` | LeftForeArm | LeftArm | +side | up | depth | joint | | | 0.05 | | | | |
| `L wrist` | LeftHand | LeftHand | +side | up | depth | joint | | | | | | | 단면은 손이 덮어씀 §4a |
| `R arm` | RightArm | RightShoulder | −side | up | depth | joint | 튠 / 튠 | 튠 / 튠 | 튠(§7) | 튠(§7) | 튠(§7) | 튠(§7) | `[N2]`. 모든 여유 튠 중(§7) |
| `R elbow` | RightForeArm | RightArm | −side | up | depth | joint | | | 0.05 | | | | |
| `R wrist` | RightHand | RightHand | −side | up | depth | joint | | | | | | | §4a |
| `hip` | LeftUpLeg, RightUpLeg | Hips | +up | side | depth | joint | 0 | 0 | | | | | 몸통 판과 다리 판의 경계. 변별로 자기 쪽 고관절 `[N1]`. `front`·`back`은 튠 중(§7) |
| `knee` | LeftLeg, RightLeg | LeftUpLeg, RightUpLeg | −up | side | depth | joint | | 0.1 | | | | | 양다리 공용 `[N1]` |
| `sole` | LeftFoot, LeftToeBase, RightFoot, RightToeBase | LeftFoot, RightFoot | −up | side | depth | cap | | | | | | | 발바닥 캡 `[N1]` |

`hi`/`lo` 여유의 방향은 각각 `s`의 +/−쪽: 팔 링(`s = up`)에서는 위/아래, 나머지(`s = side`)에서는 캐릭터 왼쪽/오른쪽. 음수 = 그 변을 살 안쪽으로.

**bake 규칙** (`measure`): 평면 = `max(앵커·n)`. 측정 창의 살로 `s`·`d` 구간을 재고 inflate. 앵커를 `s` 좌표의 중앙값 기준으로 hi/lo 변에 배정(단일 사지 링은 같은 앵커가 양쪽에). 굽는 값:
`along_hi = (cap ? (max(살·n) − 평면)·(1+margin) : 0) + outward_hi`, `along_lo = 〃 + outward_lo` — arm 링은 hi 변만 안쪽으로 들여 승모근 위에 얹는다 `[N11]`,
`s_hi = hi_s − max(hi앵커·s) + hi`, `s_lo = min(lo앵커·s) − lo_s + lo`,
코너별 깊이 `{hi,lo}_front = hi_d − max(앵커·d) + front.{hi,lo}`, `{hi,lo}_back = min(앵커·d) − lo_d + back.{hi,lo}`.

### 3b. 정중선 기둥 — `midline(slot, anchors…)`, `mid_post(...)`

앞/뒤 판의 가로대가 정중선을 지나는 자리마다 기둥 하나. 몸통·머리·다리 판을 좌/우 반판으로 가른다 `[N10]`. **띠** = 기둥이 닫는 가로대의 링: 기둥의 `d` 앵커와 깊이 여유는 그 링의 것이라 앞/뒤 정점이 링의 변과 같은 깊이에 놓인다 — 링 위의 기둥은 hi/lo 코너 여유의 평균, `neck mid`는 arm 링 **hi 변**의, `sternum mid`는 **lo 변**의 여유.

| 이름 | 띠 | 앵커(가중치) | 판 내 위치 | 비고 |
|---|---|---|---|---|
| `crown mid` | crown | Head (1) | crown 앞변 중점 | |
| `head mid` | head | Head (1) | head 앞변 중점 | `d`는 head 링의 기울어진 `d` |
| `neck mid` | L·R arm | Neck (1) | Neck | V넥 바닥. 두 arm 링의 hi 변과 함께 V를 이룬다 `[N11]` |
| `sternum mid` | L·R arm | Spine3 (1) | Spine3 | 겨드랑이(arm·lo) 높이의 가로대 |
| `hip mid` | hip | LeftUpLeg, RightUpLeg (½, ½) | hip 앞변 중점 | 가랑이 |
| `knee mid` | knee | LeftLeg, RightLeg (½, ½) | knee 앞변 중점 | |
| `sole mid` | sole | LeftToeBase, RightToeBase (½, ½) | sole 앞변 중점 | 발바닥 평면을 정하는 앵커 |

**bake 규칙**: 링 위의 기둥(`midline`) — 판 내 위치 = rest 앞변 중점, 앵커 평균과의 차를 오프셋으로 굽는다. 링 사이의 기둥(`mid_post`) — 판 내 위치 = 앵커 관절 그 자리(오프셋 0), 띠 = arm 링(d 앵커 LeftArm·RightArm, 여유 = `L arm`의 해당 변 `front`/`back`).

## 4. 손 — `hand(prefix, tag, slot, n, mirror)`

좌우 각각 호출: `("LeftHand", "L", L wrist, +side, mirror)`, `("RightHand", "R", R wrist, −side, 정방향)`. 아래 이름의 `L`은 `R`로도 읽는다.

### 4a. 공통

| 항목 | 정의 |
|---|---|
| 손 축 | `n` = 팔 바깥(±side), `s` = **depth**(엄지 +, 새끼 −), `d` = **up**(판 축). 팔 링과 프리즘 축이 90° 다르다 `[N4]` |
| 판 두께 | 손 서브트리 살 전체의 `d` 구간을 inflate. 손의 모든 기둥이 공유. 모든 기둥의 d 앵커 = **손목**(양끝), 여유 = 판 구간 − 손목 좌표 |
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

몸통의 "제어점" = 링의 실루엣 변 `(링, hi/lo)` 또는 정중선 기둥 `(역, mid)` → 정점 쌍 (front, back). 역(station) = 링 10개 + 링 없이 mid만 가진 `neck`·`sternum`. 손의 제어점 = 기둥 → (hi, lo). 손목 사각형은 양쪽이 공유하되 **역할이 바뀐다**: 팔은 앞/뒤 변을 판에, 위/아래 변을 옆판에 쓰고 손은 반대 `[N4]`.

### 5a. 몸통 판 — `panels` (앞판 + 거울 뒷판)

| 판 | 고리 (링·변) |
|---|---|
| 몸통 왼반판 | L arm·hi → L arm·lo → hip·hi → hip·mid → sternum·mid → neck·mid |
| 몸통 오른반판 | neck·mid → sternum·mid → hip·mid → hip·lo → R arm·lo → R arm·hi |
| 목 왼반판 | head·mid → head·hi → L arm·hi → neck·mid |
| 목 오른반판 | neck·mid → R arm·hi → head·lo → head·mid |
| 머리 왼반판 | crown·mid → crown·hi → head·hi → head·mid |
| 머리 오른반판 | head·mid → head·lo → crown·lo → crown·mid |
| 왼 위팔 | L arm·hi → L elbow·hi → L elbow·lo → L arm·lo |
| 왼 아래팔 | L elbow·hi → L wrist·hi → L wrist·lo → L elbow·lo |
| 오른 위팔 | R arm·lo → R elbow·lo → R elbow·hi → R arm·hi |
| 오른 아래팔 | R elbow·lo → R wrist·lo → R wrist·hi → R elbow·hi |
| 왼 허벅지 | hip·mid → hip·hi → knee·hi → knee·mid |
| 오른 허벅지 | knee·mid → knee·lo → hip·lo → hip·mid |
| 왼 종아리 | knee·mid → knee·hi → sole·hi → sole·mid |
| 오른 종아리 | sole·mid → sole·lo → knee·lo → knee·mid |

### 5b. 몸통 옆판 — `perimeter` (사슬 3, 손목에서 끊김)

| 사슬 | 경로 |
|---|---|
| 1 | crown·hi → head·hi → L arm·hi → L elbow·hi → L wrist·hi |
| 2 | L wrist·lo → L elbow·lo → L arm·lo → hip·hi → knee·hi → sole·hi → **sole·mid → sole·lo** → knee·lo → hip·lo → R arm·lo → R elbow·lo → R wrist·lo |
| 3 | R wrist·hi → R elbow·hi → R arm·hi → head·lo → crown·lo → **crown·mid → crown·hi** |

같은 링을 따라가는 구간(sole·hi→mid→lo, crown·lo→mid→hi)이 그 링 자신의 사각형(쿼드 2) = **캡**이다.

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
- 반판 고리는 실루엣에서 출발해 정중선으로 돌아오며, 실루엣 제어점 수 = 정중선 기둥 수라 ladder 가로대가 가로로 눕는다(몸통: arm·hi–neck, arm·lo–sternum, hip·hi–hip·mid).
- 결과: **194 정점 / 384 삼각형**, Euler = 2. (링 11×4 + 정중선 7×2 + 손 2×(제어점 6 + 엄지 2링·2 + 손가락 4×3링·2)×2)

## 6. 런타임 재배치 — `points(lengths, k)`

순수 함수: 편집 길이 → FK 관절 `jc` → 제어점.

**링** (`ring_corners`): 변별로 자기 앵커만 본다.
`plane_hi = n·(max(hi앵커·n) + along_hi)`, `plane_lo = n·(max(lo앵커·n) + along_lo)`,
`edge_hi = s·(max(hi앵커·s) + s_hi)`, `edge_lo = s·(min(lo앵커·s) − s_lo)`,
깊이는 양쪽 앵커 전체의 구간에 코너별 여유: `front = d·(max(앵커·d) + c_front)`, `back = d·(min(앵커·d) − c_back)` (c = hi, lo 변).
코너 = plane + edge + 깊이. 좌우 변이 독립이라 공용 링은 기울 수 있고, 두 변이 `d`에 평행이라 네 점은 항상 한 평면 `[N1]`.

**기둥** (`post_ends`): `at = Σ weight·jc[anchor] + reach`를 `d`에 직교 투영, `d` 좌표는 `max(d_hi앵커·d) + d_hi` / `min(d_lo앵커·d) − d_lo` (손: 양쪽 다 손목, 정중선: 링의 앵커).

## 7. 검증·디버그 — `mapping_tester`

| 기능 | 동작 |
|---|---|
| 슬라이더 | 편집 대상 뼈 53개(몸통 23 + 손가락 2×5×3), 범위 rest × [0.5, 1.5]. 변경마다 `update_body()` = 케이지 재생성 → deform → rest pose 재바인딩. |
| 이름 태그 (씬 뷰, 선택 시) | 이름 그룹마다 중심에 태그 하나(흰색). 화면 크기 `tag_min_px` 미만 그룹은 숨김 → 전신에선 링 이름만, 손 줌에서 손가락 이름. **클릭 → 그 그룹만 펼침**(노랑): 정점 번호(시안), 놓는 관절 태그(오렌지) + 관절→대상 점선. 가상 endbone은 `Joint ×1.40`처럼 가중치 표기. |
| 와이어 | 라이브 케이지, 시안. |
| `check containment` | 소스 지오메트리를 타깃 본에 LBS → 현재 케이지에 대해 **광선 패리티** 판정. 바깥 정점을 빨간 큐브로. rest에서만 의미 있음. |
| `check self-collision` | 정점을 공유하지 않는 삼각형 쌍의 관통 검출, 빨간 외곽선. 손가락 길이를 크게 바꾼 뒤 먼저 볼 것. |
| `rebuild cage` | 재bake + 케이지 갱신 + 재bind. 이 문서의 상수를 바꾸면 누른다. |
| 튠 슬라이더 (`cage_tune`) | 아직 확정 안 된 §3 값을 인스펙터에서 찾는 임시 편집기. 현재: arm 링 `hi`(기본 0.05) · `lo`(0, 범위 −0.1..0.1), arm 링 `outward hi` · `outward lo`(0.05, 범위 −0.15..0.1; 음수로 hi는 승모근 위까지, lo는 겨드랑이 속까지 들인다), arm 링 hi/lo 변별 `front`·`back`(0, 범위 −0.1..0.1), crown·hip 링 `front`·`back`(0, 범위 −0.05..0.1), head 링 `tilt`(25°, 0..45) · `offset`(0.023, −0.02..0.06) · `front`·`back`(0, −0.05..0.1). 드래그 중엔 재bake + 케이지 갱신만(와이어가 바로 따라옴), 놓으면 재bind + deform. 값이 정해지면 표와 recipe로 옮기고 슬라이더는 지운다. |
| import 시 | `bake` → `bind` → 케이지 자식 생성 → `update_cage`. FBX는 Read/Write 활성 필요. |

## 8. 설계 노트

- **[N1] 공용 링의 변은 각자의 앵커로.** 두 변이 한 평면(`n`으로 전체 최댓값)을 공유하면 한쪽 다리만 **줄일** 때 링이 반대쪽 무릎에 붙잡혀 따라오지 않는다(늘일 때만 따라옴). 변을 갈라 두면 링이 기울면서 양다리를 추적하고 포함도 유지한다. 대가는 비대칭 편집 시 축 정렬이 풀리는 것 — 두 변이 `d`에 평행이라 네 점은 한 평면에 남는다. 단일 사지 링은 같은 앵커가 양쪽에 들어가 축 정렬 그대로.
- **[N2] 여유는 그 자리에서 몸통 판을 경계 짓는 링에.** 얼굴·가슴·배·어깨는 링 자신의 살 측정에 안 들어오므로 여유로 덮는다. 어깨·고관절 링이 들어오면서 몸통 판의 변이 팔꿈치·무릎에서 어깨·고관절로 옮겨갔으므로 가슴·어깨 여유도 팔꿈치 링에서 arm 링으로 옮겼다. 팔꿈치 링에 남기면 팔 판만 앞으로 20cm 부푼다. **정중선 반판(N10) 이후**에는 몸통 판의 깊이가 crown↔hip 보간이 되어 arm 링의 front/back 여유는 가슴·등을 덮지 못하고 몸통 옆만 크게 부풀렸다. 그래서 arm front 0.2 / back 0.1과 crown front 0.1을 걷어 측정값 + margin으로 되돌렸다. 가슴·등은 crown/hip의 `back` 튠과 앞으로 들어올 어깨 링이 맡는다.
- **[N3] fan이 아니라 ladder.** 링마다 깊이가 달라 fan은 판 전체를 첫 제어점 기준으로 비튼다 — 몸통 중앙선이 정수리에서 고관절로 직행하며 어깨 링을 건너뛰어 어깨 앞 여유가 가슴에 반영되지 않았다. ladder는 좌우 대칭이고 가슴 띠가 어깨 깊이로 평평하다.
- **[N4] 손은 링이 아닌 기둥, 프리즘 축은 팔과 90°.** 인접 분기 링이 **제어점을 공유**해야 손등이 한 장의 폴리곤으로 남고, 손가락 링이 자기 뼈에 직교할 수 있다 — 링(정점 4)은 그걸 못 한다. T-pose에서 손바닥이 아래를 보므로 손가락은 `depth`로 벌어지고 두께는 `up`이다. 손목 사각형의 네 변은 팔과 손이 역할을 바꿔 각각 두 번씩 쓰이므로 껍질은 닫힌 채로 남고, 닫힘 assertion이 그것을 검증한다.
- **[N5] 손목 링만 손 쪽에서 잰다.** 두께는 손 살 전체에서 — 모든 손 기둥이 이 두께를 공유하므로 손목 단면만 보면 손가락이 판을 뚫는다. 폭은 중수골 절반 이내 살에서 — 링 자신의 slab은 아래팔 길이에 비례해 너무 넓어 벌어진 손가락까지 폭으로 잡는다. 단 아래팔이 손보다 훨씬 굵어 그대로 두면 팔 판이 손목에서 손바닥 두께로 잘록해지므로, 손목 링의 **손바닥 쪽 변만** `wrist_drop`만큼 내린다. 손 기둥들은 판을 지키므로 손 전체가 두꺼워지지 않고 손바닥 판이 손목에서 손 쪽으로 비스듬히 올라온다.
- **[N6] 손가락 링은 자기 뼈에 직교, endbone은 비율.** `s`에 직교시키면 벌어진 손가락이 판을 뚫는다. rig에 endbone이 없으므로 마지막 마디 살이 뼈 방향으로 뻗은 만큼을 rest 길이 비율로 굳혀 `(1+f, −f)` 아핀 결합으로 놓는다 — 그래서 마디를 늘리면 끝 링이 따라 나간다. 현재 케이지에서 **길이에 비례해 굵기·길이가 따라가는 유일한 부위**다(§9 참고).
- **[N7] winding은 부피로, 왼손은 역추적.** 판 방향은 일관되게 추적하되 어느 쪽이 바깥인지는 rig 축에 달렸으므로 부호 있는 부피로 판정해 필요 시 전체를 뒤집는다. 좌우 손은 프레임 손대칭이 반대라 왼손만 추적 순서를 뒤집는데, 닫힘 assertion이 그 판정을 검증한다.
- **[N8] 케이지는 길이만의 함수.** 매 프레임 메시를 읽지 않는다. rest 방향이 편집에 불변이므로 FK가 라이브 스켈레톤을 정확히 재현하고, 살 측정은 bake 1회에 상수로 굳는다. 현재 vicon 메시의 rest 케이지가 조건을 만족하면 비율이 바뀐 pose의 케이지도 만족한다고 본다.
- **[N10] 정중선은 관통한다.** 판 변 위의 정점은 그 변을 공유하는 양쪽 판에 모두 들어가야 닫힘 assertion을 통과한다. 그래서 정중선 정점은 한 구간에만 둘 수 없고 crown → hip → knee → sole을 잇는 사슬이 된다. 반판의 ladder는 정중선 기둥에서 출발해 실루엣 사슬로 돌아오므로 가로대가 세로로 선다(crown·hi–hip·hi 현). 정점은 그대로지만 비평면 판의 삼각화가 바뀌므로 가슴·배의 표면 깊이는 달라진다 — 팔 링 깊이의 가로 띠가 사라지고 crown↔hip 깊이의 보간이 된다(N3의 "가슴 띠"는 어깨 링이 들어오면 그 링의 여유가 맡는다). 한쪽 편집은 그쪽 반판만 움직이지만 MVC 가중치는 전역이라 비국소성은 완화될 뿐 사라지지 않는다.
- **[N11] 라글란 arm 링과 V넥.** arm 링의 hi 변을 안쪽·위로 들여 승모근 위에 얹으면(변별 `along`) 팔 판이 라글란 소매가 되어 삼각근이 소매 안에 들어가고, 두 hi 변과 Neck 위의 `neck mid`가 앞뒤로 V를 이룬다. 그러면 지금까지의 몸통 반판(crown·hi–hip·hi 현이 세로 가로대)은 승모근 점이 현 안쪽에 들어와 **접힌다** — 닫힘은 깨지지 않지만 표면이 겹친다. 그래서 판을 V에서 자른다: 몸통 반판은 arm·hi에서 출발해 정중선(hip·mid → sternum → neck)으로 돌아오고, 머리 반판은 V에서 crown까지. 양쪽 다 볼록. 가로대를 가로로 눕히려면 실루엣 점(arm·hi, arm·lo, hip·hi)마다 정중선 점이 있어야 하므로 겨드랑이 높이에 `sternum mid`(Spine3)를 둔다 — 5각형은 ladder가 못 채운다. 두 기둥의 깊이는 arm 링 것이라 V–겨드랑이 사이 가슴 띠가 arm 링 깊이로 평평하다(N3의 가슴 띠가 여기로 돌아옴). 승모근 점은 어깨 관절에 고정 오프셋이라 쇄골 편집을 100% 따라간다; 절반만 따라가야 하면 아핀 기둥으로 바꾼다.
- **[N12] 머리–목 분리 평면은 기울어진다.** 턱끝이 목 꼭대기(Head 관절)보다 앞·아래에 있어 머리(턱·귀·뒤통수와 그 위)와 목을 가르는 평면은 수평일 수 없다. 씬의 head splitter 평면(Hips 공간에서 법선 (0, .906, .423), Head에서 법선 방향 0.023 m)을 그대로 읽어 `side` 축 25° 기울기 + 오프셋으로 굽는다. 링 프레임(n, s, d)은 직교만 하면 되므로 기울어진 링도 같은 코드로 놓인다. 단면은 **split**: 평면 너머의 Head 살 전체 — 그 위의 머리 판이 감싸야 하는 것이 그것이고, 결과적으로 crown과 비슷한 폭·깊이가 나오지만 종속은 아니다. 목 길이를 늘이면 V–head 사이 목 판만 늘고 head–crown 사이 머리 판은 Head에 함께 실려 rigid하게 오른다.
- **[N9] cardinal 스냅과 발가락 부호.** rig root 로컬은 월드 정렬이 아니므로 스켈레톤에서 축을 유도하되 cardinal로 스냅해 링을 축 정렬로 유지한다. 외적은 깊이 축만 정하고 앞뒤는 못 정하므로 발가락 방향으로 부호를 정한다.

## 9. 미결

- **두께 driver — "키가 크면 두꺼워진다".** 현재 단면은 rest 살 측정값 + 절대 여유이고 앵커 spread만 길이를 따른다. 단일 사지 링(팔꿈치·손목)은 spread가 0이라 전신을 1.2배 늘여도 팔 굵기가 그대로다. 링마다 단면을 구동하는 뼈(또는 전신 척도)를 선언하는 열이 필요하다: `단면 = rest 단면 × f(driver 길이 / rest)`. §4c endbone이 이 형태의 선례.
- **V넥 다듬기.** (1) arm·hi의 높이는 아직 어깨 관절 평면에서 잰 삼각근 정점 높이 — 승모근 능선에 딱 맞추려면 변별 측정 창이 필요. (2) 가슴 띠(V–겨드랑이)가 arm 링 깊이로 정해지므로 가슴이 새면 arm 링 `front`/`back` 튠을 되살린다. (3) `neck mid`가 Neck 관절 그 자리라 V가 얕다(arm·hi와 2 cm 차) — 내리는 여유가 필요할 수 있다. (4) head 링의 tilt·offset은 head splitter에서 읽은 초기값(25°, 0.023) — 튠 확정 후 표로 옮기고 씬의 splitter 오브젝트는 지운다.
- **포함률.** 이 정도로 조악한 몸통 케이지는 어깨·배·엉덩이·팔 위아래가 판을 뚫는다. 의도된 1단계 상태이며 `check containment`가 양을 보여준다. 필요 시 링 추가(어깨·골반·가슴)로 조인다 — 토폴로지 표만 늘리면 되고 assertion이 오추적을 막는다.
- **자기겹침 스윕 테스트.** 실제로 지원할 길이 범위가 정해진 뒤, 그 범위의 극단·조합에 대해 `self_overlaps`를 자동화한다.
- **bind 비용.** 제어점 40 → 188로 늘면서 MVC bind가 그만큼 무거워졌다(import 1회). Green/Somigliana로 갈 때 먼저 부딪히는 벽 → [cage-deformation-plan.md](cage-deformation-plan.md).
