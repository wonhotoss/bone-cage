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
| 키(stature) | `up·Head − min(up·LeftToeBase, up·RightToeBase)` — Head 관절에서 낮은 쪽 발볼까지의 높이. girth 열에 `키`로 적으면 그 비(현재 / rest)가 실루엣을 곱한다 `[N23]`. | `bake`의 `stature` |
| 서브트리 | 관절 `a`와 그 모든 자손. "`a`의 살" = 서브트리 관절들의 살. | `subtree` |
| 측정 창 | **cap**: 감쌀 살 전체(평면은 살 끝까지 밀림). **joint**: 링 평면에서 `slab · max(앵커 뼈 rest 길이)` 이내의 살만. **split**: 평면(앵커 + `outward`) 너머(`n` 쪽)의 살 전체 — 그 위의 판이 감싸야 할 것. | `measure`, `fit` |
| inflate | 잰 구간 `[lo, hi]`를 중앙 기준 `(1 + margin)`배로 부풀림. 모든 측정 구간에 적용. | `inflate` |
| 링 | 사각형, **정점 4**. 축 `n`(법선, 몸 바깥), `s`(실루엣 축: 앞/뒤 판의 경계 변이 놓이는 방향), `d`(깊이 축: 앞/뒤 판을 가르는 방향). 코너 = (hi/lo 실루엣 쪽) × (front/back 깊이 쪽). 두 변이 `d`에 평행하므로 변별 `along`으로 기울어도 한 평면 `[N11]`. 깊이 앞/뒤도 각자의 **d 앵커**를 갖는다 — 기본은 링 앵커 전체, toe 링은 뒤(바닥)를 Foot에 건다 `[N14]`. | `cage_ring`, `ring_corners` |
| 기둥(post) | 제어점 하나가 소유하는 **정점 2**(축 `d`의 hi/lo). 손, 정중선(§3b), 골반(§3c). 판 내 위치 = 앵커 관절들의 아핀 결합 + 오프셋. `d` 좌표는 링 변과 같은 규칙 — 양끝이 각자의 d 앵커 `max`/`min` + 여유. | `cage_post`, `post_ends` |
| 판(plate) | 닫힌 제어점 고리를 hi 정점들로 한 번, lo 정점들로 한 번(역순) 채운 면. ladder 삼각화; 홀수 고리는 마지막이 삼각형 하나, 3점 고리는 삼각형 그 자체. 쿼드의 대각선은 **캐릭터 오른쪽 면과 뒷면에서 반대**로 긋는다(거울면 XOR 뒷면) → 좌우 거울 대칭, 앞/뒤 같은 접힘선 `[N3]` | `topology`, `strip` |
| 옆판(wall) | 제어점 사슬. 이웃 쌍마다 쿼드 1(hi–hi–lo–lo). 대각선은 오른쪽 면에서 반대 `[N3]`. | `topology` |
| 여유(reach) | 잰 구간 바깥으로 더하는 상수. 링: `front` `back`(깊이 축, **hi/lo 변별**), `hi`/`lo`(±s 쪽), `outward hi`/`outward lo`(변별 n 방향 이동). 음수 = 안쪽. 모두 씬 단위. | `recipe` |
| 보정(gate) | 케이지가 **전부 놓인 뒤** 한 부분을 다른 부분에 비추어 고치는 단계. §6b, 표 순서가 우선순위 `[N20]` | `cage_gate`, `control_points` |
| 정점 번호 | 링 `i` 코너 `c` → `i·4 + c` (`hi_front 0, hi_back 1, lo_back 2, lo_front 3`). 링 순서 crown, L arm, L elbow, L wrist, R arm, R elbow, R wrist, spine, spine1, spine2, L knee, L ankle, L toe, R knee, R ankle, R toe, head. 기둥 `p` 끝 `e` → `rings·4 + p·2 + e` (`hi 0, lo 1`). 기둥 순서 = 생성 순서(정중선 7: crown·head·neck·sternum·spine·spine1·spine2 → 골반 3: crotch·L hip·R hip → 발끝 4: L tip hi·lo, R tip hi·lo → 왼손 → 오른손, 각 손은 제어점 6 → 엄지…새끼 링). | `cage` 상수 |

## 2. 전역 상수 — `cage` 의 `const`

| 이름 | 값 | 단위 | 의미 |
|---|---|---|---|
| `margin` | 0.05 | 비율 | 모든 측정 구간을 살에서 띄우는 여유 |
| `slab` | 0.25 | 비율 | joint 링의 측정 창 반폭(앵커 뼈 rest 길이 대비) |
| `valley_reach` | 튠 중(§7, 초기 0.01) | 씬 | 손가락 계곡 제어점을 손목 반대 방향으로 미는 거리. `cage_tune`으로 옮겨 튠 중 |
| `wrist_drop` | 0.01 | 씬 | 손목 링의 손바닥 쪽 변을 손 판 아래로 내리는 거리 `[N5]` |
| `clearance` | 0.0005 | 씬 | 메시 정점이 케이지 면에서 지켜야 하는 최소 거리. 포함 검사의 기준이며, 이 아래에서 좌표 커널이 무너진다 `[N18]` |

## 3. 몸통 링 — `recipes[...]`

열: **앵커** = 링을 놓는 관절(변별로 분리됨, §6). **감쌀 살** = 서브트리 루트. **종류** cap/joint/split = 측정 창(§1). **hi/lo**가 `A → B 사이`면 그 변은 잰 여유 대신 두 제어점 `A`·`B`를 잇는 선분 위에 놓인다(§6 걸친 변, `[N21]`). 행이 **네 귀퉁이 걸침**이면 네 코너가 각자의 선분과 링 평면의 교점이고 잰 값·여유는 전부 무시된다(§6, `[N27]`). `↷θ` = `side` 축으로 θ만큼 앞으로 기울인 축(`n = cos·up + sin·depth`, `d = cos·depth − sin·up`). 여유는 씬 단위, 빈칸 = 0. `front`/`back`은 hi/lo 변별 — 한 값이면 양 변 공통, `a / b`면 hi 변 / lo 변. **girth** = 실루엣(`s_hi`·`s_lo`·`along_hi`·`along_lo`)을 곱하는 span(§6): 뼈면 그 끝 관절의 이름 — 사지의 링은 전부 그 사지의 뿌리 뼈(쇄골 / 고관절)를 적는다 `[N22]` — 머리 링 둘은 `키`(§1) `[N23]`; 빈칸 = 곱하지 않음.

| 이름 | 앵커 | 감쌀 살 | n | s | d | 종류 | front | back | hi | lo | outward hi | outward lo | girth | 비고 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `crown` | Head | Head | +up | side | depth | cap | 0 | 0 | | | | | 키 | 정수리 캡. 폭·높이가 키를 따른다 `[N23]`. `front`·`back`은 튠 중(§7) — 가슴~배꼽 정중선과 날개뼈가 몸통 판을 뚫음 |
| `head` | Head | Head | up↷25° | side | depth↷25° | split | 0 | 0 | | | 0.023 | 0.023 | 키 | 머리–목 분리 평면. 폭과 오프셋이 키를 따른다 `[N23]`. 기울기·오프셋은 씬의 head splitter에서 읽음; 기울기·오프셋·`front`·`back` 튠 중(§7) `[N12]` |
| `L arm` | LeftArm | LeftShoulder | +side | up | depth | joint | 튠 / 튠 | 튠 / 튠 | — | — | — | — | LeftArm | 몸통 판과 어깨 판의 경계 `[N2]`. 실루엣 변은 재지 않는다 — 정면에서 Arm 관절을 지나는 **라글란 이음선**의 양끝: `arm tilt`(위끝이 안쪽으로, 15°) · `arm length`(rest 0.18), 관절이 중점 `[N11]`. 이음선 길이는 **쇄골**(Shoulder→Arm, 이 링의 앵커 뼈)의 현재/rest 길이 비를 따른다 — `girth` = LeftArm(§6). 상완 판은 여기서 elbow 링으로 바로 간다(§5a). 깊이 여유 튠 중(§7) |
| `L elbow` | LeftForeArm | LeftArm | +side | up | depth | joint | | | 튠(§7, 초기 0.05) | | | | LeftArm | |
| `L wrist` | LeftHand | LeftHand | +side | up | depth | joint | | | | | | | LeftArm | 단면은 손이 덮어씀 §4a |
| `R arm` | RightArm | RightShoulder | −side | up | depth | joint | 튠 / 튠 | 튠 / 튠 | — | — | — | — | RightArm | `[N2]`. L arm과 같은 이음선 |
| `R elbow` | RightForeArm | RightArm | −side | up | depth | joint | | | 튠(§7, 초기 0.05) | | | | RightArm | L elbow와 공통 |
| `R wrist` | RightHand | RightHand | −side | up | depth | joint | | | | | | | RightArm | §4a |
| `spine` | Spine | Hips | +up | side | depth | joint | 0 | 0 | `L hip` → `L arm`·lo 사이 | `R hip` → `R arm`·lo 사이 | | |  | 몸통 판의 아랫변, 허리. 아래는 골반 기둥 §3c `[N13]`. pelvis를 줄이면 링이 고관절 기둥 아래로 내려가는데, 그것은 §6b `spine above hips`가 링째 멈춰 막는다 `[N16]`. `front`·`back`은 튠 중(§7) |
| `spine1` | Spine1 | Hips | +up | side | depth | joint | — | — | 네 귀퉁이 걸침: 코너 c = 그쪽 `arm`·lo 코너 c → `spine` 코너 c 사이 | 〃 | | |  | 배. 몸통 판의 가로대 하나 `[N10]`. 겨드랑이에서 spine 링으로 내려가는 네 직선 위의 중간 링 — 자기 폭·깊이가 없다 `[N27]` |
| `spine2` | Spine2 | Hips | +up | side | depth | joint | — | — | 〃 | 〃 | | |  | 아랫가슴. 〃 |
| `L knee` | LeftLeg | LeftUpLeg | −up | side | depth | joint | | 튠(§7, 초기 0.1) | 튠(§7) | | | | LeftUpLeg | 자기 다리 살만 잰다 `[N13]`. `hi` = 바깥쪽 변 |
| `L ankle` | LeftFoot | LeftLeg | −up↷45° | side | depth↷45° | joint | 튠(§7) | 튠(§7) | | | | | LeftUpLeg | 발목. Foot 관절을 지나 뒤로 기울어진 링 — 뒤꿈치에서 발등–정강이 연결부로. 기울기·`front`(발등 쪽)·`back`(뒤꿈치 쪽) 튠 중(§7) `[N14]` |
| `L toe` | LeftToeBase | LeftFoot | +depth | side | up | joint | | (바닥) | | | | | LeftUpLeg | 발볼. 발 방향에 직교하는 세로 링, front = 발등, back = 발바닥. **뒤(바닥)의 d 앵커는 Foot**, 여유 = ankle 링 바닥 높이까지 — 발바닥이 뒤꿈치와 수평 `[N14]` |
| `R knee` | RightLeg | RightUpLeg | −up | side | depth | joint | | 튠(§7, 초기 0.1) | | 튠(§7) | | | RightUpLeg | `s = side`라 `lo`가 바깥쪽 변; 여유는 L knee와 공통 |
| `R ankle` | RightFoot | RightLeg | −up↷45° | side | depth↷45° | joint | 튠(§7) | 튠(§7) | | | | | RightUpLeg | `[N14]` |
| `R toe` | RightToeBase | RightFoot | +depth | side | up | joint | | (바닥) | | | | | RightUpLeg | `[N14]` |

ankle의 `↷`는 `n`의 기준이 `−up`이라 `n = −cos·up + sin·depth`, `d = cos·depth + sin·up` — knee 프레임을 `side` 축으로 돌려 toe 프레임 쪽으로 가는 도중이다. 발끝은 링이 아니라 기둥 §3d. **평평한 발바닥**: `floor = Foot·up − (ankle 링 rest lo_back 코너)·up`. toe 링은 `d_lo_anchor = Foot`, `hi_back = lo_back = floor`; tip 기둥의 아랫끝도 같다(§3d). 위쪽은 살에서 잰다.

`hi`/`lo` 여유의 방향은 각각 `s`의 +/−쪽: 팔 링(`s = up`)에서는 위/아래, 나머지(`s = side`)에서는 캐릭터 왼쪽/오른쪽. 음수 = 그 변을 살 안쪽으로.

**bake 규칙** (`measure`): 평면 = `max(앵커·n)`. 측정 창의 살로 `s`·`d` 구간을 재고 inflate. 앵커를 `s` 좌표의 중앙값 기준으로 hi/lo 변에 배정(지금은 모든 링이 한 관절 또는 한 사지의 관절들이라 같은 앵커가 양쪽에 `[N1]`). 굽는 값:
`along_hi = (cap ? (max(살·n) − 평면)·(1+margin) : 0) + outward_hi`, `along_lo = 〃 + outward_lo`,
`s_hi = hi_s − max(hi앵커·s) + hi`, `s_lo = min(lo앵커·s) − lo_s + lo`,
코너별 깊이 `{hi,lo}_front = hi_d − max(앵커·d) + front.{hi,lo}`, `{hi,lo}_back = min(앵커·d) − lo_d + back.{hi,lo}`. d 앵커(`d_hi_anchor`/`d_lo_anchor`) = 앵커 전체; toe 링만 뒤(바닥)의 d 앵커를 Foot으로 덮어쓴다(§3d).

**arm 링의 실루엣 변은 잰 값을 덮어쓴다** `[N11]`: `half = arm length / 2`, `θ = arm tilt`로 `s_hi = s_lo = half·cos θ`, `along_hi = −half·sin θ`, `along_lo = +half·sin θ` — 정면에서 Arm 관절을 중점으로 하는 길이 `arm length`의 선분이 위로 갈수록 안쪽(−n)으로 θ만큼 기울어진 것이고, 그 양끝이 승모근 변(hi)과 겨드랑이 변(lo)이다. 깊이는 위 규칙대로 관절 창의 살에서 잰다. 손목 링을 손이 덮어쓰는 것(§4a)과 같은 자리에서 한다.

### 3b. 정중선 기둥 — `midline(slot, joint)`, `post(...)`

앞/뒤 판의 가로대가 정중선을 지나는 자리마다 기둥 하나. 몸통·머리·골반 판을 좌/우 반판으로 가른다 `[N10]`. **띠** = 기둥이 닫는 가로대의 링. 기둥은 두 종류다 — **띠의 깊이를 받는 기둥**(`crown`·`head`·`spine` mid, `neck mid`): `d` 앵커와 깊이 여유가 그 링의 것이라 앞/뒤 정점이 링의 변과 같은 깊이에 놓인다(링 위의 기둥은 hi/lo 코너 여유의 평균, `neck mid`는 arm 링 hi 변의 여유). **교점 기둥**(`sternum`·`spine1`·`spine2` mid): 자기 깊이가 없고, 앞끝 = 두 제어점을 잇는 선분과 정중선 평면의 교점, 뒤끝도 같다(§6 걸친 끝, `[N27]`).

| 이름 | 띠 | 앵커(가중치) | 판 내 위치 | 비고 |
|---|---|---|---|---|
| `crown mid` | crown | Head (1) | crown 앞변 중점 | |
| `head mid` | head | Head (1) | head 앞변 중점 | `d`는 head 링의 기울어진 `d` |
| `neck mid` | L·R arm | Neck (1) | Neck | V넥 바닥. 두 arm 링의 hi 변과 함께 V를 이룬다 `[N11]`. 앞끝만 띠 위에 **자기 여유**를 더한다 — 튠 중(§7) `[N17]` |
| `sternum mid` | L·R arm | Spine3 (1) | 교점: 앞 = `L arm`·lo_front → `R arm`·lo_front, 뒤 = lo_back 둘 | 겨드랑이 선의 정중선 교점. 높이·깊이가 전부 겨드랑이에서 오고 Spine3은 정중선 평면만 준다 — 가슴 띠가 구성상 평면 `[N27]` |
| `spine2 mid` | spine2 | Spine2 (1) | 교점: 앞 = spine2 hi_front → lo_front, 뒤 = hi_back → lo_back | `[N27]` |
| `spine1 mid` | spine1 | Spine1 (1) | 교점: 앞 = spine1 hi_front → lo_front, 뒤 = hi_back → lo_back | `[N27]` |
| `spine mid` | spine | Spine (1) | spine 앞변 중점 | 링 위 정중선 사슬의 끝. 아래로는 골반 반판의 세로 가로대 `spine mid – crotch` |

**bake 규칙**: 링 위의 기둥(`midline`) — 판 내 위치 = rest 앞변 중점, 앵커 관절과의 차를 오프셋으로 굽는다. `neck mid`(`post`) — 판 내 위치 = Neck 그 자리(오프셋 0), 띠 = arm 링(d 앵커 LeftArm·RightArm, 여유 = `L arm`의 hi 변 `front`/`back`), 앞끝만 그 위에 `neck front`를 더한다 `[N17]`. 교점 기둥(`crossing`) — 앵커 관절, 오프셋 0, `d` 여유 0; `hi_between`·`lo_between`에 두 정점, `between_axis = side`. 구운 상수가 없다.

정중선 기둥은 자기 링의 `girth`를 그대로 받는다(§6) — `crown mid`·`head mid`는 키, spine 셋은 없음. 기둥의 판 내 오프셋이 곧 링의 `n` 방향 reach라, 링만 곱하면 캡의 정중선이 rest 높이에 남아 지붕이 접힌다 `[N23]`.

### 3c. 골반 기둥 — `pelvis_post(...)`

골반은 링이 아니라 **손바닥처럼 분기하는 판** `[N13]`. 기둥 3개가 spine 링과 함께 5각형(spine·hi – L hip – crotch – R hip – spine·lo)을 이루고, `crotch – L hip`이 왼다리의, `crotch – R hip`이 오른다리의 **기울어진 고관절 링**이다(손가락 분기 링이 이웃한 손바닥 기둥 둘인 것과 같다). 토폴로지 표에서는 역 `hip`의 `hi`(L hip) · `mid`(crotch) · `lo`(R hip)로 부른다.

| 이름 | 역·변 | 앵커(가중치) | 판 내 위치 | girth | 비고 |
|---|---|---|---|---|---|
| `crotch` | hip·mid | Hips (1) | Hips − `up`·`crotch drop` | LeftUpLeg → RightUpLeg (`side`) | 가랑이 바로 아래. 두 고관절 링이 여기서 만난다. `crotch drop`이 고관절 너비를 따른다 `[N26]` |
| `L hip` | hip·hi | LeftUpLeg, Hips (1+f, −f) | UpLeg + f·(UpLeg − crotch) | 〃 | `f = hip out`. crotch→UpLeg 직선을 UpLeg 너머로 f배 연장한 고관절 바깥 점. 고관절이 Hips에서 멀어지면 (1+f)배로 따라 나가 링이 옆으로 넓어진다. reach(`f·drop`)가 crotch와 같은 비를 받아 이 직선 위에 남는다 `[N26]` |
| `R hip` | hip·lo | RightUpLeg, Hips (1+f, −f) | 〃 | 〃 | |

| 상수 | 값 | 단위 | 의미 |
|---|---|---|---|
| `crotch drop` | 튠 중(§7, 초기 0.15) | 씬 | Hips 관절에서 crotch까지 `−up` 거리 |
| `hip out` | 튠 중(§7, 초기 1) | 비율 | 바깥 고관절 점이 UpLeg에서 더 나가는 crotch→UpLeg 거리의 배수 |
| `pelvis front` / `pelvis back` | 튠 중(§7, 초기 0 / 0) | 씬 | 골반 판 깊이의 여유 |

**깊이(bake 규칙)**: 세 기둥이 **한 깊이를 공유**한다 — 손의 판 두께처럼. Hips·LeftUpLeg·RightUpLeg 본의 살 전체의 `depth` 구간을 inflate하고, `d` 앵커는 셋 다 **Hips**(양끝), 여유 = 구간 − Hips 좌표 + `pelvis front`/`back`. 그래서 골반 판은 허리 링과 허벅지 사이에서 평평한 판이고, 고관절 편집에도 앞뒤가 흔들리지 않는다. 판 내 오프셋: `crotch`는 `−up·drop`, `L/R hip`은 `+up·f·drop`(아핀 결합 `(1+f)·UpLeg − f·Hips`에 crotch의 drop을 f배 더한 것 = `UpLeg + f·(UpLeg − crotch)`).

### 3d. 발끝 기둥 — `foot(prefix, tag, ankle, toe, station)`

발가락 끝에는 관절이 없으므로 손가락 끝 링(§4c endbone)과 같은 방식: 역 `L tip`/`R tip`의 `hi`(+side)·`lo`(−side) 기둥 둘이 발가락 살 끝을 막는 뚜껑이다 `[N14]`. `d = up`이라 앞끝 = 발등 쪽, 뒤끝 = 발바닥 쪽으로 toe 링과 같다.

| 이름 | 역·변 | 앵커(가중치) | 판 내 위치 | `d` 앵커 · 여유 | girth |
|---|---|---|---|---|---|
| `L tip` | L tip·hi / ·lo | ToeBase, Foot (1+f, −f) | 가상 endbone에서 `side`로 발가락 살 폭(`wide_hi` / `wide_lo`)까지 | 위: ToeBase, 발가락 살 `up` 구간(inflate)의 위끝 − ToeBase. 아래: **Foot**, `floor`(§3) — toe 링 바닥과 같은 높이 | LeftUpLeg |
| `R tip` | R tip·hi / ·lo | 〃 | 〃 | 〃 | RightUpLeg |

**bake 규칙**: 발가락 살 = ToeBase 서브트리의 살. `f = max(살·dir[ToeBase] − ToeBase) · (1+margin) / rest_len(ToeBase)` — 발가락이 ToeBase 너머로 뻗은 길이의 발 뼈 길이 비율. 그래서 발 길이를 늘이면 발끝이 비례해 따라 나간다. 같은 함수가 toe 링의 바닥을 Foot에 건다(§3 평평한 발바닥). 판 내 위치(`reach`, 발가락 폭)는 링의 girth와 같은 규칙으로 고관절 뼈의 비를 곱한다(§6) — 뚜껑이 자기가 닫는 toe 링과 같은 폭으로 남는다 `[N22]`.

### 3e. 어깨 기둥 — 없음

어깨에는 기둥이 없다. 상완 판은 arm 링의 두 변에서 elbow 링의 두 변으로 곧게 간다(§5a). 삼각근 위에 세웠던 `delt` 기둥은 2026-09-09에 지웠다 `[N15]`.

## 4. 손 — `hand(prefix, tag, slot, n, mirror)`

좌우 각각 호출: `("LeftHand", "L", L wrist, +side, mirror)`, `("RightHand", "R", R wrist, −side, 정방향)`. 아래 이름의 `L`은 `R`로도 읽는다.

### 4a. 공통

| 항목 | 정의 |
|---|---|
| 손 축 | `n` = 팔 바깥(±side), `s` = **depth**(엄지 +, 새끼 −), `d` = **up**(판 축). 팔 링과 프리즘 축이 90° 다르다 `[N4]` |
| 판 두께 | 손 서브트리 살 전체의 `d` 구간을 inflate. 손의 모든 기둥이 공유. 모든 기둥의 d 앵커 = **손목**(양끝), 여유 = 판 구간 − 손목 좌표. **`girth_d` = 손목 링의 `girth`(쇄골)** — 손은 자기가 끝내는 팔만큼 두껍다 `[N25]` |
| 손목 링 덮어쓰기 | 실루엣 축(`up`): hi = 판 위, lo = 판 아래 + `wrist_drop`. 깊이 축(`depth`): 손목 평면에서 `rest_len(Middle1)·0.5` 이내 살의 구간을 inflate + 여유 `wrist thumb`(엄지 쪽) / `wrist pinky`(새끼 쪽), 튠 중(§7) `[N5]`. **`girth_d` = Thumb2 → Pinky1 span(`s` 축)** — 손바닥 폭, 첫 마디가 길어지며 벌어지는 만큼 `[N25]` |
| 손 폭 | 손 살 전체의 `s` 구간을 inflate → `wide_hi`(엄지 쪽), `wide_lo`(새끼 쪽) |

### 4b. 손바닥 제어점 6 — `cp[0..5]`

기둥 하나씩. 위치 = 앵커 아핀 결합 + 판 내 오프셋.

| 이름 | 앵커(가중치) | 오프셋 |
|---|---|---|
| `L thumb out` | Thumb2 (1) | `s`로 `wide_hi`까지 + `thumb out`(튠 중 §7) |
| `L thumb\|index` | Thumb2, Index1 (½, ½) | 손목→중점 방향(판 내 투영)으로 `valley_reach`(튠 중 §7) |
| `L index\|middle` | Index1, Middle1 (½, ½) | 〃 |
| `L middle\|ring` | Middle1, Ring1 (½, ½) | 〃 |
| `L ring\|pinky` | Ring1, Pinky1 (½, ½) | 〃 |
| `L pinky out` | Pinky1 (1) | `s`로 `wide_lo`까지 + `pinky out`(튠 중 §7, −s 쪽) |

엄지는 Thumb1이 손안에 묻혀 있으므로 **Thumb2**에서 분기한다. 인접한 두 제어점이 각 손가락의 **분기 링(링 1)** 이다.

### 4c. 손가락 링 — `climb(f)`

분기 링 다음부터, 관절마다 하나 + 마지막 마디 뒤 **가상 endbone**에 하나. 링 하나 = 기둥 2(엄지 쪽 hi, 새끼 쪽 lo), 같은 이름.

| 손가락 | 링 2 | 링 3 | 링 4 |
|---|---|---|---|
| `L thumb` | Thumb3 | endbone | — |
| `L index` `L middle` `L ring` `L pinky` | `{F}2` | `{F}3` | endbone |

규칙:
- **축**: 뼈 방향 `dir[J]`를 판 평면에 투영한 `along`, 그에 직교하는 판 내 축 `perp`(+ = 엄지 쪽). 링은 `s`가 아니라 **자기 뼈에 직교**한다 `[N6]`.
- **반경**: 관절 `J` 서브트리 살의 `perp` 구간을 inflate. 기둥 오프셋 = `perp · (구간 끝 − 관절의 perp 좌표 ± (finger_reach + finger out))` — `finger out`은 모든 손가락 링 양변에 같은 여유를 더하는 knob, 튠 중(§7).
- **endbone**: 마지막 마디 살이 `dir`로 뻗은 최대 거리 ×(1+margin)를 **rest 길이 비율 `f`** 로 굽고, 앵커 `(last, parent(last))`에 가중치 `(1+f, −f)` `[N6]`.
- **girth**(§6): 링의 기둥 오프셋(판 내 폭)에 **자기 마디** — 이전 관절에서 그 링의 관절까지의 뼈 `parent(J) → J` — 의 현재/rest 비를 곱한다. endbone 링은 마지막 마디. 위 표의 링 전부(엄지 Thumb3~endbone, 다른 손가락 `{F}2`~endbone)가 해당하고, 분기 링(손등의 제어점 6)은 곱하지 않는다 `[N24]`.

### 4d. 손가락 링 추가 여유 — `finger_reach`

씬 단위, 좌우 손 공통. 표에 없는 링은 잰 값 그대로. 위에 `finger out`(튠 중 §7)이 모든 링 양변에 일괄로 더해진다 — 표는 링 하나의 예외, knob은 손가락 전체의 바닥 여유.

| 손가락 | 링 | hi(엄지 쪽) | lo(새끼 쪽) |
|---|---|---|---|
| Index | 3 | 0.001 | 0.001 |
| Middle | 2 | 0 | 0.001 |

## 5. 토폴로지

몸통의 "제어점" = 링의 실루엣 변 `(링, hi/lo)` 또는 기둥 `(역, hi/lo/mid)` → 정점 쌍 (front, back). 역(station) = 링 15개 + 링 없이 mid만 가진 `neck`·`sternum` + 기둥 셋(hi/mid/lo)으로 된 `hip`(§3c) + 기둥 둘(hi/lo)로 된 `L tip`·`R tip`(§3d). 손의 제어점 = 기둥 → (hi, lo). 손목 사각형은 양쪽이 공유하되 **역할이 바뀐다**: 팔은 앞/뒤 변을 판에, 위/아래 변을 옆판에 쓰고 손은 반대 `[N4]`.

### 5a. 몸통 판 — `panels` (앞판 + 거울 뒷판)

| 판 | 고리 (링·변) |
|---|---|
| 몸통 왼반판 | L arm·hi → L arm·lo → spine2·hi → spine1·hi → spine·hi → spine·mid → spine1·mid → spine2·mid → sternum·mid → neck·mid |
| 몸통 오른반판 | neck·mid → sternum·mid → spine2·mid → spine1·mid → spine·mid → spine·lo → spine1·lo → spine2·lo → R arm·lo → R arm·hi |
| 목 왼반판 | head·hi → L arm·hi → neck·mid → head·mid (주름을 턱에 둔다 `[N3]`) |
| 목 오른반판 | R arm·hi → head·lo → head·mid → neck·mid (〃) |
| 머리 왼반판 | crown·mid → crown·hi → head·hi → head·mid |
| 머리 오른반판 | head·mid → head·lo → crown·lo → crown·mid |
| 왼 위팔 | L arm·hi → L elbow·hi → L elbow·lo → L arm·lo |
| 왼 아래팔 | L elbow·hi → L wrist·hi → L wrist·lo → L elbow·lo |
| 오른 위팔 | R arm·lo → R elbow·lo → R elbow·hi → R arm·hi |
| 오른 아래팔 | R elbow·lo → R wrist·lo → R wrist·hi → R elbow·hi |
| 골반 왼반판 | spine·mid → spine·hi → hip·hi → hip·mid |
| 골반 오른반판 | hip·mid → hip·lo → spine·lo → spine·mid |
| 왼 허벅지 | hip·mid → hip·hi → L knee·hi → L knee·lo |
| 오른 허벅지 | R knee·hi → R knee·lo → hip·lo → hip·mid |
| 왼 종아리 | L knee·lo → L knee·hi → L ankle·hi → L ankle·lo |
| 오른 종아리 | R ankle·hi → R ankle·lo → R knee·lo → R knee·hi |
| 왼 발 | L ankle·lo → L ankle·hi → L toe·hi → L toe·lo |
| 오른 발 | R toe·hi → R toe·lo → R ankle·lo → R ankle·hi |
| 왼 발가락 | L toe·lo → L toe·hi → L tip·hi → L tip·lo |
| 오른 발가락 | R tip·hi → R tip·lo → R toe·lo → R toe·hi |

골반 두 반판이 앞에서 본 5각형(spine·hi – L hip – crotch – R hip – spine·lo)이고, 윗변 중점 spine·mid에서 갈라진다. 허벅지 판의 윗변 `hip·mid → hip·hi`가 기울어진 고관절 링이다. 다리에서는 링 프레임이 knee(`d = depth`) → ankle(`depth↷45°`) → toe·tip(`d = up`)으로 돌아가므로 **앞판** = 정강이 → 발등 → 발가락 위, **뒷판** = 종아리 → 뒤꿈치 → 발바닥 `[N14]`.

### 5b. 몸통 옆판 — `perimeter` (사슬 3, 손목에서 끊김)

| 사슬 | 경로 |
|---|---|
| 1 | crown·hi → head·hi → L arm·hi → L elbow·hi → L wrist·hi |
| 2 | L wrist·lo → L elbow·lo → L arm·lo → spine2·hi → spine1·hi → spine·hi → hip·hi → L knee·hi → L ankle·hi → L toe·hi → **L tip·hi → L tip·lo** → L toe·lo → L ankle·lo → L knee·lo → **hip·mid** → R knee·hi → R ankle·hi → R toe·hi → **R tip·hi → R tip·lo** → R toe·lo → R ankle·lo → R knee·lo → hip·lo → spine·lo → spine1·lo → spine2·lo → R arm·lo → R elbow·lo → R wrist·lo |
| 3 | R wrist·hi → R elbow·hi → R arm·hi → head·lo → crown·lo → **crown·mid → crown·hi** |

같은 역을 따라가는 구간(L/R tip·hi→lo, crown·lo→mid→hi)이 그 역 자신의 사각형 = **캡**이다(tip은 쿼드 1, crown은 mid를 지나 쿼드 2). `L knee·lo → hip·mid → R knee·hi`는 두 허벅지 **안쪽 벽**으로, crotch에서 만난다.

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
- **거울 대칭**: 오른쪽 면의 삼각형 집합 = 왼쪽 면의 거울(winding 반전). 면의 좌/우는 rest 제어점 중심의 `side` 부호로 판정하며, 정중선에 걸치는 면은 없다(assertion) `[N3]`.
- 반판 고리는 실루엣에서 출발해 정중선으로 돌아오며, 실루엣 제어점 수 = 정중선 기둥 수라 ladder 가로대가 가로로 눕는다(몸통: arm·hi–neck, arm·lo–sternum, spine2·hi–spine2·mid, spine1·hi–spine1·mid, spine·hi–spine·mid; 골반: spine·hi–L hip, spine·mid–crotch).
- **표가 선언하는 것은 고리와 사슬뿐이다**(목 반판 둘의 시작점이 예외로 대각선까지 정한다, `[N3]`)**.** 판을 채우는 ladder의 가로대(`v[i]`–`v[j]` 짝)와 쿼드를 가르는 대각선은 `strip`의 선택이며 어느 행도 이름하지 않는다. 그래서 사슬의 연속 기둥 쌍 — 그것이 곧 껍질의 링이고, 기울어진 고관절 링(`crotch` 옆 `L/R hip`)이 여기서 나온다 — 을 `cage_constants.grid`에 정점 쌍으로 남긴다. 삼각화는 이 구분을 지우므로 `tris`에서 되돌릴 수 없다. 디버그 와이어(§7)가 읽는다.
- 결과: **236 정점 / 468 삼각형**, Euler = 2. (링 17×4 + 기둥 16×2 + 손 2×(제어점 6 + 엄지 2링·2 + 손가락 4×3링·2)×2)

## 6. 런타임 재배치 — `points(lengths, k)`

순수 함수: 편집 길이 → FK 관절 `jc` → 제어점.

**링** (`ring_corners`): 변별로 자기 앵커만 본다. 링과 기둥 모두 관절 중심만 읽으므로 서로를 기다리지 않는다 — 배열 순서는 토폴로지 표가 쓰는 정점 순서일 뿐이다 `[N16]`.
`plane_hi = n·(max(hi앵커·n) + along_hi·g)`, `plane_lo` 도 같다,
`edge_hi = s·(max(hi앵커·s) + s_hi·g)`, `edge_lo = s·(min(lo앵커·s) − s_lo·g)`,
여기서 `g`는 링의 **girth span**의 현재 길이 / rest 길이 — 실루엣 네 값이 그것을 따라 커지고 줄어든다. span(`cage_span`) = `(a[], b[], axis, rest)`, 길이 = `max(a·axis) − min(b·axis)`, 관절만 읽는다. **뼈**는 `a = 관절, b = 부모, axis = rest 방향`이라 FK 아래서 정확히 그 뼈의 길이다; **키**는 `a = Head, b = L/R ToeBase, axis = up`(§1). girth가 없는 링은 `g = 1`. 사지의 링은 전부 그 사지의 **뿌리 뼈**를 girth로 갖는다 — arm·elbow·wrist는 쇄골(LeftArm·RightArm 관절의 뼈) `[N11]`, knee·ankle·toe는 고관절(LeftUpLeg·RightUpLeg 관절의 뼈) — 그래서 뿌리의 비가 말단 링까지 그대로 내려간다 `[N22]`. crown·head 링은 키를 갖는다 `[N23]`. 몸통 링은 girth가 없다.
깊이는 d 앵커의 구간에 코너별 여유: `front = d·(max(d_hi앵커·d) + c_front·g_d)`, `back = d·(min(d_lo앵커·d) − c_back·g_d)` (c = hi, lo 변; d 앵커는 보통 양 변 앵커 전체). `g_d`는 링의 **`girth_d` span**의 비 — 깊이가 무엇을 따르는지를 실루엣과 따로 선언한다. 없으면 `g_d = 1`. 지금은 손목 링만 갖는다(§4a, Thumb2 → Pinky1) `[N25]`. 몸의 깊이가 실루엣을 따라야 할 때는 `girth_d = girth`로 적는다 — 별도의 복원 패스는 없다.
코너 = plane + edge + 깊이. 좌우 변이 독립이라 공용 링은 기울 수 있고, 두 변이 `d`에 평행이라 네 점은 항상 한 평면 `[N1]`.

**기둥** (`post_ends`): `at = Σ weight·jc[anchor] + reach·g`를 `d`에 직교 투영, `d` 좌표는 `max(d_hi앵커·d) + d_hi` / `min(d_lo앵커·d) − d_lo` (손: 양쪽 다 손목, 정중선: 링의 앵커). `g`는 링의 것과 같은 girth 비이고 `reach`에 곱한다; `d` 끝(`d_hi`·`d_lo`)에는 기둥의 **`girth_d`** 비 `g_d`가 곱해진다 — 손의 모든 기둥이 손목 링의 `girth`(쇄골)를 `girth_d`로 갖고(§4a) `[N25]`, 나머지 기둥은 없다. 골반 기둥 셋의 `girth`(reach — crotch의 drop과 고관절 기둥의 `f·drop`)는 고관절 너비 LeftUpLeg → RightUpLeg(`side` 축)다(§3c) `[N26]`. `L/R tip`은 고관절을 girth로 갖고(§3d) `[N22]`, 정중선 기둥은 자기 링의 girth를 받는다(§3b) — `crown mid`·`head mid`는 키 `[N23]`, 손가락 링의 기둥은 자기 마디 `[N24]`. 나머지(손바닥 제어점 6, `neck mid`·`sternum mid`, 골반 기둥)는 없다.

**걸침** (`between`): 링과 기둥이 다 놓인 뒤, 다른 제어점 사이에 놓이는 것들. 공통 연산은 **교점** `crossing(A, B, axis, plane) = lerp(A, B, clamp01((plane − A·axis) / ((B − A)·axis)))` — 선분 A→B가 `axis` 방향 `plane`을 지나는 점, 선분 밖이면 가까운 끝. **평가 중 제어점이 다른 제어점을 읽는 유일한 자리**이고 한 방향이다 — 세 단계가 각각 앞 단계까지만 읽는다:
1. **걸친 변**: §3에서 `A → B 사이`로 선언된 변은 교점의 `s` 좌표만 받는다(`axis = n`, `plane` = 자기 평면). 평면과 깊이는 그대로. spine 링의 양 변이 `L/R hip`(기둥 앞끝)과 `L/R arm`의 lo 변 앞 코너(겨드랑이) 사이 `[N21]`.
2. **네 귀퉁이 걸침**: `between`(코너마다 정점 둘)이 선언된 링은 네 코너가 각자의 교점 그 자체다 — 링은 평면만 남긴다. spine1·spine2의 코너 c = 그쪽 arm 링 lo 변 코너 c → spine 링 코너 c(1에서 놓인 뒤) `[N27]`.
3. **걸친 끝**: `hi_between`·`lo_between`이 선언된 기둥의 끝은 교점(`axis = between_axis`, `plane` = 앵커에서 온 자기 좌표) — 정중선 기둥은 `side`, 즉 정중선 평면. `sternum mid` = arm 링 lo 변 앞 코너 둘 / 뒤 코너 둘, `spine1 mid`·`spine2 mid` = 자기 링의 앞변 / 뒷변(2에서 놓인 뒤) `[N27]`.

### 6b. 보정 — `control_points`의 마지막 단계

링과 기둥과 걸친 변이 **전부 놓인 뒤** 도는 단계. 평가 중 제어점이 서로를 읽는 것은 걸친 변 하나뿐이고 그것은 위치이지 판단이 아니다 — 케이지의 한 부분을 다른 부분에 비추어 **판단**하는 일은 전부 여기로 온다 `[N20]`. **표의 순서가 우선순위다**: 뒤의 보정이 앞의 보정이 옮긴 정점을 다시 옮길 수 있다.

`lift = max(0, max(바닥·축) − min(재는 곳·축) − 여유)`를 **올리는 것 전체에 같은 값으로** 더한다 — 뭉치가 통째로 움직이므로 그것이 이루는 모양은 그대로다. 여유는 바닥 아래로 얼마까지 봐줄 것인가이며, 0이면 닿는 순간 멈춘다.

| 이름 | 올리는 것 | 재는 곳 | 바닥 | 여유 | 축 | 비고 |
|---|---|---|---|---|---|---|
| `head above arms` | crown 링, head 링, `crown mid`, `head mid` (정점 12) | head 링 네 코너 중 최하단 | L·R arm 링의 hi 변 (정점 4) | 튠 중(§7, 초기 0.038) | `up` | 목을 줄이면 파팅 평면이 이음선 아래로 내려가 목 판이 머리를 뚫는다 — 머리 뭉치를 어깨 위로 되올린다 `[N20]` |
| `L arm beside head` / `R arm beside head` | 그쪽 arm 링 전체 (정점 4) | 그 링의 hi 변 (정점 2) | head 링 네 코너 중 그쪽으로 가장 바깥 | 튠 중(§7, 초기 0) | `+side` / `−side` | 쇄골을 줄이면 arm 링이 관절을 따라 정중선 쪽으로 오다 hi 변(승모근 점)이 머리 실루엣 안에 선다 — 옆벽이 안쪽으로 눕고 목 반판이 머리 반판을 뚫는다. 재는 곳은 hi 변, 세우는 것은 **링 전체**라 라글란 기울기가 남는다. head gate와 축이 직교해 순서 무관 `[N20]` |
| `spine above hips` | spine 링, `spine mid` (정점 6) | 그 여섯 중 최하단 | L·R `hip` 기둥의 네 끝 | 튠 중(§7, 초기 −0.005) | `up` | pelvis를 줄이면 spine 링이 고관절 기둥 아래로 내려가고 골반 판이 위로 접혀 몸통 판을 뚫는다 — 링과 그 정중선 기둥을 함께 멈춰 가로일자를 유지한다. 옆변만 붙들면 정중선이 계속 내려가 V가 열리고, 고관절 기둥까지 안으로 걸어 들어오면 허벅지 판이 그 V를 가로지른다 `[N16]`. **여유는 음수여야 한다** — 고관절 기둥과 같은 높이에 서면 그 사이 골반 판이 납작해져 몸통 판과 스친다 `[N20]` |
| `L knee beside crotch` / `R knee beside crotch` | 그쪽 knee 링 전체 (정점 4) | 그 링의 안쪽 변 (정점 2; `L knee`는 `lo`, `R knee`는 `hi`) | `crotch` 기둥 두 끝 | 튠 중(§7, 초기 −0.01) | `+side` / `−side` | 고관절 뼈가 순수 측방이라 그 길이가 골반 반폭이다 — 줄이면 다리가 링째 안으로 걸어 들어와 무릎 링 안쪽 변이 정중선을 넘고 두 다리의 안쪽 벽이 서로를 통과한다. 재는 곳은 안쪽 변, 세우는 것은 **링 전체**라 허벅지가 얇아지지 않는다. 바닥이 어느 보정도 옮기지 않는 `crotch`라 좌우가 서로를 읽지 않는다. **여유는 음수여야 한다** — 0이면 두 링이 정중선에 겹쳐 서고 그 사이 판이 여전히 스친다 `[N20]`. 발목은 별도 보정이 필요 없다 |

rest에서는 머리가 이미 어깨 위에 있고, arm 링 hi 변도 머리 폭 바깥(Arm 관절 side 0.16 대 머리 반폭 약 0.09), spine 링 바닥도 고관절 기둥보다 3.7 cm 위, 무릎 링 안쪽 변도 가랑이에서 3.98 cm 떨어져 있어 **네 보정**(거울쌍 둘이 각각 두 줄이라 선언은 여섯) 모두 `lift = 0`이므로 **rest 케이지는 보정되지 않는다**(분기 없이 성립).

## 7. 검증·디버그 — `mapping_tester`

| 기능 | 동작 |
|---|---|
| 슬라이더 | 편집 대상 뼈 53개(몸통 23 + 손가락 2×5×3), 범위 rest × [0.5, 1.5]. 변경마다 `update_body()` = 케이지 재생성 → deform → rest pose 재바인딩. |
| 이름 태그 (씬 뷰, 선택 시) | 이름 그룹마다 중심에 태그 하나(흰색). 화면 크기 `tag_min_px` 미만 그룹은 숨김 → 전신에선 링 이름만, 손 줌에서 손가락 이름. **클릭 → 그 그룹만 펼침**(노랑): 정점 번호(시안), 놓는 관절 태그(오렌지) + 관절→대상 점선. 가상 endbone은 `Joint ×1.40`처럼 가중치 표기. |
| 와이어 (`all_edges`) | 라이브 케이지, 시안. 기본은 **이 문서가 선언하는 변만**(`cage.frame`): 링마다 사각형 하나(손가락·발가락 링은 이름을 공유하는 기둥 쌍), 기둥마다 선분 하나, 그리고 §5d의 `grid` — 사슬의 연속 기둥 쌍, 즉 분기 링(고관절·어깨·손가락 갈래)과 실루엣을 따라가는 링 사이 연결. 판을 채우는 ladder의 가로대와 쿼드 대각선은 표에 없으므로 빠지고, 그러면 와이어가 메시가 아니라 레시피로 읽힌다 — 튠 슬라이더가 움직이는 것이 정확히 링 변과 기둥 끝이다. 켜면 삼각형 전체를 그린다: 포함·자기겹침은 표면에 관한 것이므로 그때는 이쪽. |
| | 정중선에서 갈린 링(crown·head·spine·spine1·spine2)의 앞·뒤 변과 정중선 기둥의 선분은 **껍질의 간선이 아니다** — 판이 그 자리에서 반으로 갈리므로 정중선 기둥을 거쳐 돌아간다. 선언된 대상이라 그대로 그린다. |
| `check containment` | rest 메시를 현재 케이지로 사상한 결과(`mapped`)가 그 케이지에 **여유를 갖고 담기는지** 판정. **광선 패리티**로 안팎을, 최근접 면까지의 거리로 여유를 함께 재고, 바깥이거나 `clearance`보다 가까운 정점을 빨간 큐브로 표시한다. 면에 얹힌 정점 하나가 좌표를 깨뜨리는데 편집 중에 그 한 경우만 노려볼 방법이 없으므로, 지키는 것은 안팎이 아니라 여유다 `[N18]`. 스키닝은 이 파이프라인에 없으므로 어느 길이에서나 의미가 있다 — 몸은 언제나 "케이지가 사상한 메시"다. |
| `check self-collision` | 정점을 공유하지 않는 삼각형 쌍의 관통 검출, 빨간 외곽선. 손가락 길이를 크게 바꾼 뒤 먼저 볼 것. |
| `rebuild cage` | 재bake + 케이지 갱신 + 재bind. 이 문서의 상수를 바꾸면 누른다. |
| `export sweep data` | 스윕(§7b)이 읽을 rest 쪽을 `tools/cage_sweep/data/`에 기록: 구운 상수(JSON), rest 메시(rig 공간)와 정점별 지배 관절, 슬라이더가 편집하는 본 목록. 재bake 뒤에 다시 누른다. |
| 체형 슬라이더 (`cage_shape`) | 뼈 무리를 한꺼번에 스케일해 비례를 만드는 시험용 손잡이 다섯. **torso**(9: 척추·목·머리 + 어깨밑동) · **arms**(36: 쇄골 이하 양팔 + 손가락) · **legs**(8: 고관절 이하 양다리) · **left**(22) · **right**(22), 각각 rest 대비 비 `[0.5, 1.5]`. 앞 셋이 53개를 **분할**하고 뒤 둘이 사지를 다시 좌우로 가른다 — 어깨밑동은 좌우에서 빠지므로 몸통은 어떤 비대칭에서도 통째로 남는다. 뼈 하나가 받는 값은 **덮는 슬라이더의 곱**이다(왼팔 = arms × left). 무리는 나열하지 않고 스켈레톤에서 읽는다: 팔 = 쇄골 서브트리, 다리 = 고관절 서브트리, 몸통 = 나머지. 슬라이더를 옮기면 **지난번 값과의 비만** 곱하므로 그 아래의 개별 본 편집이 살아남고, 두 절을 어느 순서로 써도 된다. `reset bone lengths`가 다섯을 1로 되돌린다. |
| 튠 슬라이더 (`cage_tune`) | 아직 확정 안 된 §3 값을 인스펙터에서 찾는 임시 편집기. 현재: arm 링 `tilt`(라글란 이음선의 기울기, 7°, 범위 −30..45) · `length`(이음선 길이, 0.16, 0.05..0.3), arm 링 hi/lo 변별 `front`·`back`(0, 범위 −0.1..0.1), crown·spine·spine1·spine2 링 `front`·`back`(0, 범위 −0.05..0.1), head 링 `tilt`(25°, 0..45) · `offset`(0.023, −0.02..0.06) · `front`·`back`(0, −0.05..0.1), `head gate slack`(§6b 머리 보정의 여유, 0.038, 0..0.1) · `arm gate slack`(§6b 어깨 보정의 여유: arm hi 변이 머리 폭 안쪽으로 들어와도 봐주는 거리, 0, −0.05..0.05; 음수는 간격 요구) · `spine gate slack`(§6b 허리 보정의 여유: spine 링 바닥이 고관절 기둥 아래로 내려가도 봐주는 거리, −0.005, −0.035..0.01; 이 보정도 음수여야 한다) · `knee gate slack`(§6b 무릎 보정의 여유: 무릎 링 안쪽 변이 가랑이 너머로 들어와도 봐주는 거리, −0.01, −0.035..0.01; 음수는 간격 요구이고 이 보정은 음수여야 한다), `neck front`·`sternum front`(두 정중선 기둥의 앞끝만 띠 너머로, 0, −0.05..0.1), 골반 `crotch drop`(0.15, 0..0.3) · `hip out`(비율 1, 0..2) · `pelvis front`·`back`(0, −0.05..0.1), knee 링 `out`(양 링의 바깥쪽 변 s 여유, 0, −0.1..0.1) · `back`(0.1, −0.05..0.2), ankle 링 `tilt`(45°, 0..80) · `front`(0, −0.1..0.1) · `back`(0, −0.05..0.1; 발바닥 높이도 정한다), elbow 링 `hi`(양 링 윗변, 0.05, −0.05..0.1), 손 `wrist thumb`·`wrist pinky`(손목 링 폭 여유, 0, −0.05..0.05) · `thumb out`·`pinky out`(8각형 바깥 기둥 여유, 0, −0.05..0.05; 양손 공통, 음수 = 살 쪽으로), `finger out`(모든 손가락 링 양변의 perp 여유 일괄, 0, −0.005..0.01; §4c) · `valley reach`(계곡 제어점을 손목 반대 방향으로 미는 거리, 0.01, 0..0.03; §2·§4b). 드래그 중엔 재bake + 케이지 갱신만(와이어가 바로 따라옴), 놓으면 재bind + deform. 값이 정해지면 표와 recipe로 옮기고 슬라이더는 지운다. |
| import 시 | `bake` → `bind` → 케이지 자식 생성 → `update_cage`. FBX는 Read/Write 활성 필요. |

### 7b. 길이 스윕 — `tools/cage_sweep`

버튼 둘(`check containment`·`check self-collision`)을 길이 범위 전체에 자동으로 돌리는 Unity 밖 도구. 한 케이스는 길이만의 순수 함수 — 스켈레톤이 케이지를 몰고 케이지가 메시를 몰 뿐 **스키닝이 개입하지 않으므로** 에디터가 필요 없다. csproj가 `cage.cs`·`cage_deform.cs`를 **소스째 컴파일**하므로 재구현이 없고, 도구와 에디터 버튼이 같은 코드를 돈다(`UnityEngine.CoreModule`의 `Vector3`·`Mathf`는 엔진 없이 도는 순수 관리 코드다).

| 층 | 케이스 | 묻는 것 |
|---|---|---|
| 1 single | 본 하나를 rest × {0.5 … 1.5} 8단으로, 나머지는 rest | 각 본이 홀로 어디까지 가는가 |
| 2 pair | 본 쌍을 네 모서리(0.5·1.5 조합)로 | 이웃 본 사이 상호작용 |
| 3 whole | 전 본을 `[0.5, 1.5]`에서 무작위로 뽑은 전신 | 전조합(2⁵³)의 몬테카를로 대역 |
| 4 proportion | 체형 슬라이더 다섯 축의 격자 — torso·arms·legs를 {0.7, 0.85, 1, 1.2, 1.4}, left·right를 {0.9, 1, 1.1}로 (1,125) | **비례**가 바뀔 때. 3층은 본을 서로 독립으로 흔들므로 "키가 크다" 같은 상관된 변화가 사실상 나오지 않는다 — 두께 driver가 겨냥하는 것이 그것이라 자기 층이 필요하다 |

실제로 일어나지 않는 조합도 일부러 남긴다 — 인구를 모형화하는 것이 아니라 레시피가 깨지는 자리를 찾는 것이다. **판정은 자기겹침뿐이다** — 변형 후 케이지 밖으로 나간 정점은 실패가 아니라 통계로 남긴다 `[N18]`. rest 기준선은 별도로 **0 / 0**이어야 하고, 그것이 깨지면 그게 곧 레시피의 실패다(§9 여유). `--skip hand`처럼 이름 조각을 주면 해당 본은 rest에 묶인다 — 손가락을 풀어 두면 3층이 전부 손에서 깨져 몸통에 대해 아무 말도 하지 않으므로, 한 번에 한 부위씩 묻는 손잡이다. 결과는 `out/results.csv`(전 케이스)와 `out/report.md`(케이지 그룹별 자기겹침, 탈출 분포와 신체 부위별 최악, 본별 안전 범위와 그 구간의 최대 탈출).

**전달비 probe** — `--probe k`는 스윕 대신 다른 것을 묻는다: **선언한 단면이 살에 얼마나 도착하는가.** 뼈를 전부 rest에 두고 한 구간의 단면 여유를 ×k 한 케이지로 rest 메시를 사상한 뒤, bake가 쓰는 그 측정 창에서 살의 폭·깊이를 다시 잰다. `전달비 = (메시 비 − 1) / (케이지 비 − 1)`이고 1.00이면 선언이 그대로 도착한 것이다. 두께 driver의 값을 정하려면 이 비를 먼저 알아야 한다(§9).

```
Unity 인스펙터 [export sweep data]        # 또는 -executeMethod mapping_tester.export_headless
dotnet run -c Release --project tools/cage_sweep
dotnet run -c Release --project tools/cage_sweep -- --probe 1.2    # 전달비만
```

## 8. 설계 노트

- **[N1] 공용 링의 변은 각자의 앵커로.** 양다리 공용 링(옛 hip·knee·sole)이 있던 때의 규칙: 두 변이 한 평면(`n`으로 전체 최댓값)을 공유하면 한쪽 다리만 **줄일** 때 링이 반대쪽 무릎에 붙잡혀 따라오지 않으므로(늘일 때만 따라옴) 변을 갈라 링이 기울며 양다리를 추적하게 했다. 골반이 기둥으로 분기하고(N13) 무릎·발바닥이 다리별 링이 된 뒤로는 공용 링이 없다 — 변별 앵커 구조는 코드에 남아 있고(§6), 지금은 모든 링이 같은 앵커를 양쪽에 두어 축 정렬 그대로다.
- **[N2] 여유는 그 자리에서 몸통 판을 경계 짓는 링에.** 얼굴·가슴·배·어깨는 링 자신의 살 측정에 안 들어오므로 여유로 덮는다. 어깨·고관절 링이 들어오면서 몸통 판의 변이 팔꿈치·무릎에서 어깨·고관절로 옮겨갔으므로 가슴·어깨 여유도 팔꿈치 링에서 arm 링으로 옮겼다. 팔꿈치 링에 남기면 팔 판만 앞으로 20cm 부푼다. **정중선 반판(N10) 이후**에는 몸통 판의 깊이가 crown↔hip 보간이 되어 arm 링의 front/back 여유는 가슴·등을 덮지 못하고 몸통 옆만 크게 부풀렸다. 그래서 arm front 0.2 / back 0.1과 crown front 0.1을 걷어 측정값 + margin으로 되돌렸다. 가슴·등은 crown/spine(당시 hip) 링의 `front`/`back` 튠과 어깨 링이 맡는다.
- **[N3] fan이 아니라 ladder.** 링마다 깊이가 달라 fan은 판 전체를 첫 제어점 기준으로 비튼다 — 몸통 중앙선이 정수리에서 고관절로 직행하며 어깨 링을 건너뛰어 어깨 앞 여유가 가슴에 반영되지 않았다. ladder는 좌우 대칭이고 가슴 띠가 어깨 깊이로 평평하다. 고리가 홀수면 두 절반이 만나는 곳이 삼각형 하나로 끝난다 — 어깨 쐐기(N15)의 3점 판이 그것. **대각선 규칙**: ladder의 가로대(정점 짝)는 고리를 역순으로 돌려도 같지만 쿼드의 대각선(`v[i]–v[j−1]`)은 다른 코너를 잇는다. 오른쪽 판은 왼쪽 판의 거울상을 역순으로 추적하고(거울은 방향을 뒤집으므로 winding을 지키려면 역순), 뒷면도 앞면 고리의 역순이라, 그대로 두면 케이지가 거울 대칭이 아니라 up 축 180° 회전 대칭(왼 앞판 ≡ 오른 뒷판의 거울)이 된다 — 비평면 쿼드에서는 표면이 달라 대칭 메시의 containment와 MVC 가중치가 좌우 다르게 나온다. 그래서 오른쏙 면과 뒷면(XOR)은 대각선을 반대로 긋는다: 좌우가 정확한 거울이 되고, 쿼드는 앞/뒤 같은 두 제어점을 잇는 선으로 접힌다. 좌/우 판정은 면의 rest 중심 `side` 부호 — 모든 판이 정중선에서 갈라지므로(N10) 자기 자신이 거울상인 면은 없다. **고리의 시작점이 대각선을 정한다**: 사다리는 `v[0]`을 `v[2]`에 잇는다. 같은 고리를 한 칸 돌려 추적하면 winding과 가로대는 그대로이고 대각선만 반대 코너로 간다. 쿼드가 접힌 곳에서는 두 대각선의 표면이 다르므로 이것이 선택이 된다 — 목 반판이 그 유일한 자리이고, §5a의 두 행이 실루엣(턱 옆코너)에서 출발하는 것이 그 선언이다. 목을 줄이면 턱선이 이음선 아래로 내려가 이 쿼드는 어느 쪽으로 갈라도 비틀리는데, 주름을 턱에 두면 그 삼각형이 머리 판을 파고들지 않는다(측정: `chest=0.5` 단독 4 → 0 삼각형).
- **[N4] 손은 링이 아닌 기둥, 프리즘 축은 팔과 90°.** 인접 분기 링이 **제어점을 공유**해야 손등이 한 장의 폴리곤으로 남고, 손가락 링이 자기 뼈에 직교할 수 있다 — 링(정점 4)은 그걸 못 한다. T-pose에서 손바닥이 아래를 보므로 손가락은 `depth`로 벌어지고 두께는 `up`이다. 손목 사각형의 네 변은 팔과 손이 역할을 바꿔 각각 두 번씩 쓰이므로 껍질은 닫힌 채로 남고, 닫힘 assertion이 그것을 검증한다.
- **[N5] 손목 링만 손 쪽에서 잰다.** 두께는 손 살 전체에서 — 모든 손 기둥이 이 두께를 공유하므로 손목 단면만 보면 손가락이 판을 뚫는다. 폭은 중수골 절반 이내 살에서 — 링 자신의 slab은 아래팔 길이에 비례해 너무 넓어 벌어진 손가락까지 폭으로 잡는다. 단 아래팔이 손보다 훨씬 굵어 그대로 두면 팔 판이 손목에서 손바닥 두께로 잘록해지므로, 손목 링의 **손바닥 쪽 변만** `wrist_drop`만큼 내린다. 손 기둥들은 판을 지키므로 손 전체가 두꺼워지지 않고 손바닥 판이 손목에서 손 쪽으로 비스듬히 올라온다.
- **[N6] 손가락 링은 자기 뼈에 직교, endbone은 비율.** `s`에 직교시키면 벌어진 손가락이 판을 뚫는다. rig에 endbone이 없으므로 마지막 마디 살이 뼈 방향으로 뻗은 만큼을 rest 길이 비율로 굳혀 `(1+f, −f)` 아핀 결합으로 놓는다 — 그래서 마디를 늘리면 끝 링이 따라 나간다. 현재 케이지에서 **길이에 비례해 굵기·길이가 따라가는 유일한 부위**다(§9 참고).
- **[N7] winding은 부피로, 왼손은 역추적.** 판 방향은 일관되게 추적하되 어느 쪽이 바깥인지는 rig 축에 달렸으므로 부호 있는 부피로 판정해 필요 시 전체를 뒤집는다. 좌우 손은 프레임 손대칭이 반대라 왼손만 추적 순서를 뒤집는데, 닫힘 assertion이 그 판정을 검증한다. 뒤집힌 추적은 대각선을 바꾸므로 손도 N3의 대각선 규칙(오른손 면 반대)으로 거울 대칭이 된다.
- **[N8] 케이지는 길이만의 함수.** 매 프레임 메시를 읽지 않는다. rest 방향이 편집에 불변이므로 FK가 라이브 스켈레톤을 정확히 재현하고, 살 측정은 bake 1회에 상수로 굳는다. 현재 vicon 메시의 rest 케이지가 조건을 만족하면 비율이 바뀐 pose의 케이지도 만족한다고 본다.
- **[N10] 정중선은 관통한다.** 판 변 위의 정점은 그 변을 공유하는 양쪽 판에 모두 들어가야 닫힘 assertion을 통과한다. 그래서 정중선 정점은 한 구간에만 둘 수 없고 crown → head → neck → sternum → spine2 → spine1 → spine → crotch를 잇는 사슬이 된다(처음에는 crown → hip → knee → sole이었고, 다리가 분기하면서 crotch에서 끝난다 — 그 아래 두 다리는 각자 닫힌 관이다). 척추 관절마다 링 + 기둥 하나 = 몸통 판의 가로대 하나: 배·아랫가슴 단면이 살에서 잡히고 척추 마디 길이 편집이 그 구간만 늘인다. 반판의 ladder는 정중선 기둥에서 출발해 실루엣 사슬로 돌아오므로 가로대가 세로로 선다(crown·hi–hip·hi 현). 정점은 그대로지만 비평면 판의 삼각화가 바뀌므로 가슴·배의 표면 깊이는 달라진다 — 팔 링 깊이의 가로 띠가 사라지고 crown↔hip 깊이의 보간이 된다(N3의 "가슴 띠"는 어깨 링이 들어오면 그 링의 여유가 맡는다). 한쪽 편집은 그쪽 반판만 움직이지만 MVC 가중치는 전역이라 비국소성은 완화될 뿐 사라지지 않는다.
- **[N11] 라글란 arm 링과 V넥.** arm 링의 hi 변을 안쪽·위로 들여 승모근 위에 얹으면(변별 `along`) 팔 판이 라글란 소매가 되어 삼각근이 소매 안에 들어가고, 두 hi 변과 Neck 위의 `neck mid`가 앞뒤로 V를 이룬다. 그러면 지금까지의 몸통 반판(crown·hi–hip·hi 현이 세로 가로대)은 승모근 점이 현 안쪽에 들어와 **접힌다** — 닫힘은 깨지지 않지만 표면이 겹친다. 그래서 판을 V에서 자른다: 몸통 반판은 arm·hi에서 출발해 정중선(hip·mid → sternum → neck)으로 돌아오고, 머리 반판은 V에서 crown까지. 양쪽 다 볼록. 가로대를 가로로 눕히려면 실루엣 점(arm·hi, arm·lo, hip·hi)마다 정중선 점이 있어야 하므로 겨드랑이 높이에 `sternum mid`(Spine3)를 둔다 — 당시 ladder는 홀수 고리를 못 채웠고, 지금(N3)은 채우더라도 5각형이면 마지막 가로대가 삼각형으로 기울어 가슴 띠가 평평하지 않다. 두 기둥의 깊이는 arm 링 것이라 V–겨드랑이 사이 가슴 띠가 arm 링 깊이로 평평하다(N3의 가슴 띠가 여기로 돌아옴). 승모근 점은 어깨 관절에 고정 오프셋이라 쇄골 편집을 100% 따라간다; 절반만 따라가야 하면 아핀 기둥으로 바꾼다. **2026-09-09**: 이 링의 실루엣은 더 이상 살을 재고 여유 넷(`hi`·`lo`·`outward hi`·`outward lo`)으로 다듬지 않는다 — 정면에서 Arm 관절을 중점으로 지나는 **이음선 하나**(`arm tilt`·`arm length`)의 양끝이 두 변이다(§3 bake 규칙). 두께 driver가 이 링을 쇄골 비로 키울 때 곱할 것이 길이 하나가 되도록 단순화한 것이고, 절대 여유가 비율 아래서 어긋나던 문제(§9 자기겹침 지도의 쇄골 무리)도 여유가 없어져 뿌리째 사라진다. 깊이 여유 넷(hi/lo 변별 `front`·`back`)은 남는다.
- **[N12] 머리–목 분리 평면은 기울어진다.** 턱끝이 목 꼭대기(Head 관절)보다 앞·아래에 있어 머리(턱·귀·뒤통수와 그 위)와 목을 가르는 평면은 수평일 수 없다. 씬의 head splitter 평면(Hips 공간에서 법선 (0, .906, .423), Head에서 법선 방향 0.023 m)을 그대로 읽어 `side` 축 25° 기울기 + 오프셋으로 굽는다. 링 프레임(n, s, d)은 직교만 하면 되므로 기울어진 링도 같은 코드로 놓인다. 단면은 **split**: 평면 너머의 Head 살 전체 — 그 위의 머리 판이 감싸야 하는 것이 그것이고, 결과적으로 crown과 비슷한 폭·깊이가 나오지만 종속은 아니다. 목 길이를 늘이면 V–head 사이 목 판만 늘고 head–crown 사이 머리 판은 Head에 함께 실려 rigid하게 오른다.
- **[N13] 골반은 손바닥처럼 분기한다.** 양다리를 한 프리즘에 넣고 정중선으로만 가르면 가랑이와 안쪽 허벅지가 공기층에 놓이고, 공용 링은 한 다리 편집에 반대 다리를 끌어간다. 두 다리가 각자 링을 가지되 가랑이에서 **만나야** 하므로 고관절 링은 링(정점 4, 공유 불가)이 아니라 손의 분기 링처럼 **이웃한 기둥 둘**이다: `crotch`를 양쪽이 공유하고 바깥 점 `L/R hip`은 각자. crotch→UpLeg 직선을 UpLeg 너머로 `hip out`배 연장하면 고관절 바깥 실루엣 근처에 닿고, 이 세 점과 depth가 한 평면이라 고관절 링은 사타구니 주름처럼 안쪽 아래(crotch)에서 바깥 위(hip)로 기울어 다리를 감싼다. 앞에서 보면 두 링이 V, 위의 spine 링과 함께 손등 같은 5각형 = 골반 판(정중선 규약대로 spine·mid–crotch에서 반판 둘). 바깥 점을 `(1+f)·UpLeg − f·Hips`의 아핀 결합으로 두는 것은 손가락 endbone과 같은 수법이라, 고관절 폭 편집에 링이 옆으로 넓어진다. 몸통 판의 아랫변은 hip 링 대신 **spine 링**(Spine 관절, 허리)이 되어 sternum·arm과 이어진다. 세 기둥은 손의 판 두께처럼 골반 살의 depth 구간 하나를 공유해 골반 판이 평평한 판으로 남는다. 무릎·발바닥 링은 자기 다리 살만 재므로(`wrap` = 그 다리의 UpLeg/Foot) 두 다리가 붙어 서도 안쪽 변이 서로를 넘지 않는다 — 극단 길이에서의 자기겹침은 `check self-collision`으로 본다.
- **[N14] 발은 프레임이 돌아가는 관이다.** 발바닥 캡 하나로는 발이 종아리 프리즘의 바닥면일 뿐이라 발등·뒤꿈치·발가락이 전부 밖에 놓였다. 발을 다리와 90° 꺾인 사지로 보아 링을 셋 둔다: **ankle**은 Foot 관절을 지나되 수평이 아니라 뒤로 기울어진 링 — 수평이면 뒤꿈치 아래와 발등 위를 동시에 자르지만, 뒤꿈치 바닥에서 발등–정강이 연결부로 기울이면 종아리 관과 발 관을 가르는 자연스러운 단면이 된다(기울기는 튠). **toe**는 ToeBase에서 발 방향(`depth`)에 직교하는 세로 링으로 발볼을 감싼다. **tip**은 손가락 끝처럼 관절 없는 endbone 위의 기둥 쌍(`(1+f, −f)`·(ToeBase, Foot))이라 발 길이에 비례해 따라 나가는 뚜껑이다. 프레임의 `d`가 knee의 `depth` → ankle의 `depth↷tilt` → toe·tip의 `up`으로 연속해서 돌므로 링 코드는 그대로이고, 앞판이 정강이에서 발등으로, 뒷판이 종아리에서 뒤꿈치·발바닥으로 이어진다 — 옆판은 발의 안·바깥 측면. ankle의 측정 창은 joint slab(종아리 뼈 길이 × 0.25)이라 기울어진 평면 근처의 정강이·발 살을 함께 잡는다; 좁혀야 하면 ankle 전용 창을 둔다. **발바닥은 평평하다**: toe 링의 아랫변과 tip의 아랫끝은 살을 재지 않고 ankle 링의 바닥(뒤꿈치, `back` 여유 포함) 높이를 따른다 — d 앵커를 Foot으로 두고 그 높이 차를 여유로 굽는다. 그래서 발 뼈를 늘이거나 기울여도 발바닥은 뒤꿈치와 한 평면이고, ankle `back` 하나가 발 전체의 바닥을 정한다.
- **[N15] 어깨는 팔 링과 겨드랑이를 공유하는 V.** arm 링의 윗변이 승모근 위로 들어간 뒤(N11) 상완 판의 윗변은 승모근 점에서 팔꿈치 위까지 한 직선이 되어, 삼각근 너머 상완 위에 빈 공간이 컸다. 어깨와 상완을 가르는 링을 넣되, 겨드랑이 정점을 arm 링과 **공유**해야 어깨 쐐기가 닫힌다 — 골반의 crotch(N13)와 같은 이유로 링이 아니라 **기둥 하나**(`delt`) + arm 링의 lo 변이 새 링이다. 상완 위 삼각근 끝에 앉힌 `delt`에서 겨드랑이로 내려오는 기울어진 세로 링이 되고, 앞에서 보면 arm 링과 겨드랑이에서 만나는 V. 그 사이는 **삼각형 판** 둘(앞/뒤: arm·hi – delt – arm·lo) + 위쪽 벽 쿼드(arm·hi → delt) = 어깨 쐐기이며, ladder가 홀수 고리를 삼각형으로 끝내도록 넓혔다. `delt`는 (Arm, ForeArm)의 아핀 점이라 상완 길이에 비율로 따라간다. **2026-09-09: 지웠다.** arm 링이 이음선 하나(`arm tilt`·`arm length`)가 되어 쇄골을 따르게 되자, 링 위 삼각근 자리를 따로 세운 기둥은 그 비를 한 번 더 받아야 하는 이웃(§9 두께 driver의 "소속" 문제)이었고, 상완 판이 arm 링에서 elbow 링으로 곧게 가도 승모근 변이 이미 위로 올라가 있어 삼각근 위 빈 공간은 이음선의 길이가 덮는다. 어깨 쐐기 삼각형 둘과 기둥 둘, 튠 둘(`delt along`·`delt up`)이 함께 사라졌다.
- **[N16] 허리는 고관절 위에 있다 — 걸림에서 보정으로.** spine 링은 Spine 관절에 걸려 있고 pelvis 뼈(Hips→Spine)가 그것을 내린다. 반면 `L/R hip` 기둥은 UpLeg와 Hips의 아핀 점이라 그 뼈에 움직이지 않는다. 그래서 pelvis를 줄이면 링이 옆의 고관절 기둥 아래로 내려가고, 골반 판이 위로 접혀 몸통 판을 뚫는다 — 길이 스윕이 가장 먼저 찾은 실패이며 교차는 pelvis 0.625에서 시작한다(측정).
    - **처음 답은 변별 걸림이었다**(`hold_hi`/`hold_lo`): 그 변의 평면은 자기 앵커 + 여유이거나, 그보다 `n`으로 덜 나갔다면 걸림 기둥의 높이. 정중선 기둥(`spine mid`)은 걸리지 않고 계속 관절을 따르므로 앞에서 본 링은 가로일자 → 고관절 높이에서 평평 → 정중선을 바닥으로 하는 V로 열렸다. 높이에 대해서는 성립했다: 걸림 높이가 pelvis·lumbar와 무관한 상수이므로 spine 변이 spine1 변 위로 밀려 올라갈 수는 없다(최악 pelvis 0.5·lumbar 0.5에서 여유 +0.00036 rig 단위, 측정).
    - **그러나 고관절 뼈가 rest에 있을 때만 성립한다.** 걸림 기둥은 고관절 뼈를 (1+f)배로 따라가므로 그 뼈를 줄이면 기둥이 안쪽으로 걸어 들어온다. `pelvis 0.5 + L hip 0.5`에서 기둥이 side 18.1 → 9.0으로 오는데 무릎 링 바깥은 12.9에 남아, 열린 V를 허벅지 판이 가로지른다 — 뚫리는 쌍이 `spine mid+spine+spine1` 대 `L hip+L hip+L knee`이고 서로의 평면까지 센티미터 단위로 들어간다(측정).
    - **그래서 걸림을 버리고 보정으로 옮겼다**(§6b `spine above hips`). 변별로 붙드는 대신 링 네 코너와 `spine mid`를 **한 덩어리로** 멈춘다 — 가로일자가 유지되므로 V 자체가 생기지 않는다. 여유는 음수여야 한다: 고관절 기둥과 같은 높이에 세우면 그 사이 골반 판이 납작해져 `L hip+crotch+spine mid`가 `spine+spine1`과 스친다(측정: 여유 0에서 `pelvis 0.5` 단독이 0 → 2 삼각형으로 나빠진다). −0.5 cm부터 −3 cm까지 전부 0이다.
    - **이로써 `hold` 기구는 사라졌다.** spine 링이 유일한 사용처였으므로 `cage_ring`의 두 필드, `ring_corners`의 `held` 인자와 `outermost()`가 함께 없어진다. 케이지를 자기 자신에 비추어 **판단**하는 일은 전부 보정에 있다 `[N20]`. 같은 날 걸친 변(`[N21]`)이 제어점 간 읽기를 한 곳 되살렸지만 그것은 판단(clamp)이 아니라 위치이고, 걸림처럼 어느 값이 이기는지를 따지지 않는다.
    - **높이는 닫혔고 폭은 열려 있다.** 이 보정은 접힘을 막을 뿐 고관절 기둥이 안으로 걸어 들어오는 것을 되돌리지 않는다 — 골반 살은 계속 껍질 밖으로 나간다(`pelvis 0.5 + L hip 0.5`에서 `Hips` 44 → 39). 그쪽은 두께 driver의 몫이다(§9).
- **[N17] 정중선 기둥의 앞끝은 자기 띠에서 풀린다.** 정중선 기둥은 자기 띠(닫는 가로대의 링)의 깊이를 그대로 쓴다(§3b) — `neck mid`의 앞끝은 arm 링 hi 변의 `front` 여유가 정한 깊이에 놓이고, rest에서 두 값은 같다(둘 다 0.1186). 그런데 목·얼굴을 조이려면 arm 링의 hi `front`를 줄여야 하고, 그러면 V 바닥이 함께 뒤로 끌려 목 앞·쇄골 사이로 파고든다. 두 점이 요구하는 깊이가 다르다: arm·hi는 가슴 띠의 깊이(승모근 위), `neck mid`는 목 앞(울대) 깊이. 그래서 앞끝에만 자기 여유 `neck front`를 띠 위에 더한다 — 뒤끝은 목 뒤가 가슴 띠와 같은 깊이라 그대로 둔다. `sternum mid`도 같다: arm 링의 lo 변이 정하는 깊이는 겨드랑이의 것이고 흉골은 그보다 앞이므로 `sternum front`로 민다. 이 둘로 arm 링의 두 변을 살까지 당기고도 목 앞과 윗가슴이 덮인다 — rest 실측 0 정점 탈출. 대가는 가슴 띠의 두 가로대가 깊이로 13 cm 벌어진 것이고, chest 뼈를 줄이면 그 사각형이 안장이 되어 접힌다(§9 자기겹침 지도 B). 필요해지면 뒤끝도 같은 방식으로 푼다. **2026-09-09**: `sternum front`는 없어졌다 — sternum이 겨드랑이 선의 교점이 되어(`[N27]`) 흉골 앞은 arm 링 lo 변의 `front` 자체로 덮는다(0.055 → 0.08, 겨드랑이 앞 코너도 그만큼 나온다). `neck front`만 남는다.
- **[N18] 케이지 밖에 있다는 것이 무엇을 뜻하는가.** 두 질문을 갈라야 한다 — rest 케이지가 정점을 담고 있는가, 변형된 케이지가 담고 있는가.
    - **좌표는 케이지 밖에서도 유효하다.** MVC의 정의식은 안이라는 조건을 쓰지 않고, partition of unity와 linear precision이 공간 전체에서 성립한다. 케이지를 정확한 아핀 변환(×1.2 + 이동)으로 옮기면 6 cm 밖의 점도 0.33 mm 안에서 따라온다(측정). 실제 길이 편집에서도 밖의 점은 자기 아래 표면을 타고 간다 — 1 cm 밖 표본의 이탈이 중앙 0.00 cm, 95번째 0.08 cm(pelvis·양 고관절 0.5).
    - **다만 조건수가 나빠진다.** 음수 가중치가 안에서 31%, 밖에서 63%로 두 배가 되고 Σ|w|가 1 → 7로 커진다. 케이지의 작은 오차가 그만큼 증폭되므로 거리와 함께 최악값이 자란다(6 cm 밖, 최악 5.85 cm). 그래서 **rest 케이지의 포함은 지켜야 하는 기준**이다 — bind의 조건수를 그것이 정한다.
    - **면 위는 안 된다.** 정점이 케이지 면에 정확히 얹히면 커널의 "이 면 위에 있다" 분기가 부동소수 때문에 빗나가고 일반식이 0에 가까운 값으로 나눠 최악 8 mm가 난다. 면에서 **0.5 mm** 안쪽이면 오차가 평평해진다(중앙 0.0007 mm, 최악 0.076 mm, 1 mm를 넘는 면 0개). 0.1 mm에서는 아직 최악 5.9 mm다. 그래서 규칙은 **여유를 갖고 담긴다**이고, 그 값이 §2의 `clearance`이며, 포함 검사가 그것을 잰다(§7). 메시 정점은 이산적이라 케이지 면을 스쳐 지나도 마침 얹히는 정점이 없을 수 있지만, 그것은 운이지 안전이 아니다 — 튠을 한 틱 옮길 때마다 사람이 그 한 경우를 확인할 방법이 없으므로 지키기 쉽고 결과가 확실한 쪽을 규칙으로 삼는다.
    - **변형 후에 밖으로 나가는 것은 보장할 수 없고, 보장할 필요도 없다.** 오목한 케이지에서 가중치가 음수이므로 사상은 볼록결합이 아니고, 따라서 안의 점이 안에 남는다는 보장이 없다(측정: `양 고관절 0.5`에서 444 정점). 그러나 좌표는 bind 시점에 고정되므로 그 점이 새 껍질 안에 떨어지는지는 식에 들어가지 않는다 — 나갔다는 것이 잘못 사상됐다는 뜻이 아니다. 껍질이 살을 더 이상 감싸지 않는다는 신호일 뿐이라, 진단으로 남기고 판정에서는 내린다(§7b). 이가 있는 기준은 rest 포함과 자기겹침 둘이다 — 접힌 케이지는 사상이 국소적으로 뒤집혀 실제로 깨진다.
- **[N19]** 는 `try-neck-fit` 브랜치가 쓴 번호다 — 이 줄기에서는 비어 있다.
- **[N20] 맞추는 대신 지킨다 — 보정(gate).** 케이지의 모양은 **주어진 메시에 의존적**이다. 망치상어처럼 머리가 좌우로 긴 모델, 어깨에 돌출부가 있는 모델을 주면 "살에 꼭 맞춰 자기겹침을 없앤다"는 목표 자체가 도달 불가능해진다 — 어떤 메시는 언제나 여유를 빼앗아 간다. 그래서 목표를 바꾼다: **비상식적인 스켈레톤 변화에서 케이지를 지킨다.** 그런 변화가 실제로 요구되면 그때 케이지를 고도화해도 늦지 않다.
    - **왜 평가 뒤인가.** 보정은 케이지의 한 부분을 다른 부분에 비추어 판단한다. 링이 기둥에 걸리는 것(`[N16]`)은 기둥이 링보다 먼저 놓인다는 한 방향 의존이 있어 평가 중에 풀렸지만, 링 대 링에는 그런 순서가 없다. 그래서 스켈레톤 → 케이지 평가가 **전부 끝난 뒤** 보정으로 돈다(§6b). 보정이 늘면 서로가 옮긴 것을 다시 옮기게 되므로 **표 순서를 우선순위로** 정해 둔다.
    - **첫 보정: 머리는 어깨 위에 있다.** 목뼈를 줄이면 파팅 평면이 가라앉아 그 최하단(기울기 때문에 앞 두 코너)이 arm 링 hi 변 아래로 내려가고, 이음선이 턱선을 넘어 목 판이 머리 판을 뚫는다(§9 지도 A). 부족분만큼 머리 뭉치(crown 링·head 링과 그 정중선 기둥 둘)를 통째로 올린다. `[N16]`의 거울상 — arm 링 윗변에 턱선 상한을 두는 안 — 은 "턱이 어깨를 내린다"는 원격 인과라 측정만 남기고 되돌렸는데(journal 2026-09-04), 이쪽은 **"어깨가 머리를 올린다"**로 인과가 바로 선다.
    - **여유가 없으면 목이 짧아지지 않는다.** 닿는 즉시 올리면 목뼈를 줄인 만큼 머리가 그대로 따라 올라와 편집이 상쇄된다(목뼈 셋 0.5에서 88.6 mm). 판이 실제로 접히기 시작하는 것은 링이 이음선 아래로 자기 높이의 5분의 2쯤 가라앉은 뒤다 — 측정: 여유 0·24·38·45 mm에서 chest+목 계열이 전부 0이고 **50 mm에서 4 삼각형**. 그래서 여유를 두고 경계 안쪽에서 튠한다(초기 0.038 = 목 3개 축소 시 들리는 높이 88.6 → 50.6 mm).
    - **rest는 보정하지 않는다.** rest에서 머리는 이미 어깨 위이므로 `lift = 0`이고 보정이 no-op이다 — 특별 분기 없이 성립한다(실측 이동량 0.000 mm). bake의 측정도 rest 살에서 하므로 영향이 없다.
    - **보정은 살을 뼈에서 떼어 놓는다.** 머리를 들면 머리 살이 스켈레톤이 말하는 자리보다 위에 놓인다. 그래도 모션에는 지장이 없다 — 스키닝은 rest → 애니메이션 포즈의 **델타**를 rest 메시에 적용하고 정점의 리깅(본 가중치)은 건드리지 않으므로, 보정된 메시가 그 캐릭터의 새 rest가 될 뿐이다.
    - **둘째 보정: 어깨는 머리 옆에 있다.** 쇄골을 줄이면 arm 링이 Arm 관절을 따라 정중선 쪽으로 오고, 승모근에 얹힌 hi 변이 머리 실루엣 안에 서면 head·hi→arm·hi 옆벽이 안쪽으로 눕고 목 반판이 머리 반판을 뚫는다(§9 지도의 쇄골 무리). 재는 곳은 hi 변이지만 세우는 것은 **링 전체**다 — 변 하나만 밀면 라글란 기울기가 풀려 링이 반대로 기울므로, 머리를 상자째 올리듯 링을 통째로 멈춘다. `arm outward hi`가 "비율이어야 할 절대값"이라는 진단(journal 2026-09-01)을 비율로 고치는 대신 하한으로 닫는 것이고, 09-04에 되돌린 턱선 상한("턱이 어깨를 내린다")과 달리 "어깨 윗변은 머리 폭 안으로 들어올 수 없다"는 몸의 불변식이라 인과가 바로 선다. head gate와는 축이 직교해(up 대 side) 서로가 읽는 좌표를 바꾸지 않으므로 순서가 결과에 들어오지 않는다. elbow 링은 자기 뼈에 남는다. rest에서 Arm 관절은 side 0.16, 머리 반폭은 0.09 근처라 여유 7 cm, 쇄골 0.5에서 0.08로 들어와 정확히 머리 폭에 닿는다.
    - **셋째 보정: 무릎은 가랑이 옆에 있다.** 이 rig의 고관절 뼈는 순수 측방(rest 10.05 cm)이라 그 길이가 곧 골반 반폭이다. 줄이면 다리 전체가 FK로 안쪽으로 평행이동하고, 무릎 링은 자기 폭을 지킨 채 통째로 따라 들어와 안쪽 변이 정중선을 넘는다 — 두 다리의 안쪽 벽이 서로를 통과한다(무릎 링 안쪽 변 사이 간격 rest +7.97 cm → 고관절 ×0.6 −0.08 cm → ×0.5 −2.09 cm). 재는 곳은 안쪽 변, 세우는 것은 링 전체다 — 안쪽 변만 밀면 바깥 변이 계속 들어와 허벅지가 얇아진다. 바닥은 `crotch` 기둥이고, 어느 보정도 그것을 옮기지 않으므로 좌우가 서로가 옮긴 것을 읽지 않는다.
        - **여기서는 여유가 음수여야 한다.** 앞의 둘과 달리 이 보정은 좌우가 같은 바닥을 공유한다. 여유 0으로 닿는 순간 멈추면 두 링의 안쪽 변이 **정확히 정중선에 겹쳐 서고**, 그 사이를 잇는 판이 여전히 서로를 스친다 — 측정: `양 고관절 0.5`에서 충돌 13 → **16**으로 늘어난다. 간격을 요구하면 −0.5 cm에서 이미 0이고 −3.5 cm까지 평평하다. rest 여유가 3.98 cm이므로 그 안쪽이면 rest는 no-op이고, −1 cm에서 보정이 물리기 시작하는 값은 고관절 **×0.70**이다.
        - **발목은 별도 보정이 필요 없다.** 발목 링 안쪽 변은 고관절 0.5에서도 정중선을 넘지 않고(−1.0 cm), 뚫리던 것은 정강이 판의 무릎 쪽 끝이다. 측정: 발목 링을 함께 옮긴 경우와 무릎 링만 옮긴 경우가 음수 여유 전 구간에서 결과가 같다.
        - **이것은 케이지를 지킬 뿐이다.** 같은 편집에서 **살이 먼저 겹친다** — rest에서 좌우 안쪽 허벅지가 정중선에서 각 1.52 cm이므로 고관절 ×0.85부터 메시 자체가 서로를 통과하고, ×0.5에서 7.0 cm 겹친다. 그 범위를 지원할 것인가는 §9 길이 범위 확정이 받는다.
    - **넷째 보정: 허리는 고관절 위에 있다.** `[N16]`이 걸림에서 옮겨 온 것이다. 앞의 셋과 달리 이것은 원래 평가 안에 있던 규칙이라, 보정으로 오면서 **재는 단위가 변에서 링으로 커졌다** — 그것이 정확히 고치는 것이다. 여유가 음수여야 하는 이유는 셋째 보정과 같다: 바닥과 같은 높이에 세우면 그 사이 판이 납작해진다.
- **[N21] 몸통 옆판은 겨드랑이에서 고관절로 곧다 — 첫 두께 룰, 그런데 driver가 아니다.** 몸통 링(`spine`·`spine1`·`spine2`)의 양 변은 자기 살을 재지 않고, 그쪽 **고관절 기둥**(`L/R hip`)과 **arm 링의 lo 변 앞 코너**(겨드랑이)를 잇는 선분 위에 놓인다 — 링 평면이 그 선분을 지나는 높이에서(§6 걸친 변). 상수도 튠도 없다. 앞판은 겨드랑이와 고관절을 위아래 변으로 하는 사다리꼴이 되고, 이 메시에서는 거의 직사각형이다(고관절 36.19 cm, 겨드랑이 36.94 cm).
    - **왜 담기는가.** 옆구리가 오목해서 직선이 살 바깥으로 남는다 — 측정(rest): spine 살 폭 28.2 → 선분 폭 36.26(한쪽 여유 4.0 cm), spine1 23.7 → 36.48(**6.4 cm**), spine2 27.3 → 36.69(4.7 cm). 메시가 바뀌어도 옆구리가 고관절·겨드랑이 선을 넘는 몸은 드물다. 깊이(`d`)는 여전히 잰 값이다.
    - **얻는 것.** 어깨나 고관절이 넓어지면 몸통이 **통째로** 그 사이를 잇는 판으로 따라간다. 아래 링은 고관절을, 위 링은 어깨를 더 따르는 것이 높이에서 저절로 나온다 — 가중치를 둘 곳이 없다. 척추 마디가 길어져 링이 오르내려도 그 높이의 선분 위에 놓이므로 판이 곧게 남는다.
    - **버리는 것.** **케이지가 허리를 모른다.** 허리에서 살과 껍질 사이가 0.6 → 6.4 cm로 벌어지고, 잘록함은 케이지의 모양이 아니라 그 안의 MVC가 만드는 결과가 된다. 몸통이 넓어지면 허리도 같은 비로 넓어지지만, "허리만 죄기" 같은 편집은 이 규칙 아래서는 표현할 수 없다. 지금 메시에서 시각적으로 문제가 없다고 보고 받아들인다 — 필요해지면 그때 허리에 자기 규칙을 준다.
    - **driver가 아닌 이유.** rest 케이지가 바뀐다(spine1 24.88 → 36.48 cm) — 잰 살이 아니라 케이지 자기 기하로 폭을 정하는 **레시피**다. §9의 driver 불변식(rest는 항등)은 driver에 대한 것이므로 모순은 아니지만, 이 룰은 그 틀 밖에 있다. 그래서 §3 표의 `hi`/`lo` 열에 "사이"로 쓰고, 재bind가 따른다.
    - **걸친 변은 제어점을 읽는다 — 관절로 풀지 않은 이유.** 고관절 기둥의 side는 `(1+f)·UpLeg.side`, 겨드랑이는 `Arm.side + (arm length / 2)·sin(arm tilt)`로 관절과 상수로 다시 쓸 수 있지만, 그러면 같은 식이 두 곳에 생겨 `hip out`이나 이음선 튠이 바뀔 때 몸통 규칙이 소리 없이 어긋난다. 규칙의 본질이 "케이지 자기 기하에 대한 문장"이므로 놓인 점을 읽는다. 읽히는 점은 관절만 읽으므로 순환이 없다(§6).
    - **지나온 길.** 같은 날 먼저 들어간 것은 비율형 driver였다 — 잰 폭에 `lerp(고관절너비/rest, 어깨너비/rest, w)`를 곱하고 `w`를 링마다 튠(§3 `width` 열, 검산 rest ×1.000 · 양 고관절 ×1.2 → ×1.100 · 둘 다 → ×1.200 · lumbar ×1.5 → 폭 불변). rest를 지키고 허리를 남기는 대신 튠 셋과 관절 쌍·가중치·rest 거리의 열이 필요했다. 사다리꼴은 그 전부를 없애므로 바꿨고, `width` 열은 사용자가 없어 같이 걷어냈다 — 사지 룰(팔꿈치 ← 상완 길이)이 그 형태를 다시 필요로 하면 이력에서 되살린다.
- **[N22] 사지 링은 뿌리의 비를 그대로 받는다 — 룰 3.** 사지 하나의 굵기는 그 뿌리 뼈 하나가 정한다: 팔은 쇄골(어깨 폭), 다리는 고관절 뼈(골반 반폭). 뿌리 링·기둥은 이미 그 뼈를 따르므로(arm 링의 이음선 `[N11]`, `L/R hip`의 `(1+f)` `[N13]`), 그 아래 링에 **같은 비를 그대로** 넘기면 사지가 한 배율로 굵어지고 판이 링 사이에서 꺾이지 않는다 — 원본 비율에 대한 확장비를 그대로 전달하는 것으로 충분하다고 보았고, 링마다 다른 곡선을 둘 근거가 아직 없다. 열은 `girth` 하나라 새 튠이 없고 rest에서 비가 1이라 driver 불변식(§9)이 그대로 성립한다. **다리는 폭**(knee·ankle·toe의 `s = side`, tip 기둥의 `reach = side·폭)이 hip → knee → ankle → toe → tip으로 내려가고, **팔은 높이**(arm·elbow·wrist의 `s = up`)가 arm → elbow → wrist까지 내려간다. 깊이(`front`·`back`)는 어느 쪽도 곱하지 않는다 — 깊이 복원 패스의 몫(§9). 손목 링의 실루엣은 손이 덮어쓴 값(§4a)이지만 그것도 곱하므로 손목은 팔과 함께 굵어지고, **손등 이후(손 기둥)는 따르지 않는다** — 손은 따로 본다. tip 기둥은 `reach`만 곱한다: 위끝(`d_hi`)은 높이라 발가락 폭과 무관하고, 아래끝은 평평한 발바닥 `[N14]`이다.
- **[N23] 머리는 키를 따른다 — 룰 4.** 머리 크기는 키에 비례한다고 놓는다. 키 = Head 관절에서 발볼까지의 `up` 거리(§1)이고, 두 발 중 **낮은 쪽**을 쓴다 — 서 있는 키가 그것이고, 한쪽 다리만 편집해도 좌우가 대칭으로 응답한다. rest 대비 비를 그대로 곱한다(선형). 소아 비례처럼 비선형이 맞겠지만 지금은 단순하게 두고, 곡선이 필요해지면 `stature`의 비에 f를 씌운다. 곱하는 자리는 기존 `girth` 열 그대로다: `head` 링의 폭(`s`)과 오프셋(`along`, 2.3 cm), `crown` 링의 폭과 높이(`along` = 관절 위 두개골 높이). 요청은 head 폭·crown 높이 둘이었지만 crown 폭을 빼면 머리가 위로 갈수록 좁아지는 사다리꼴이 되므로 함께 두었고, head 오프셋은 작아서 분리하지 않았다 — 축별로 갈라야 하면 그때 열을 나눈다. 깊이는 곱하지 않는다. **구간 단위**: `crown mid`·`head mid` 기둥이 같은 비를 받는다(§3b) — 기둥의 오프셋은 링의 `n` reach 그 자체(crown: 두개골 높이, head: 오프셋)라 링만 곱하면 정중선이 rest에 남아 캡이 접힌다. 이것을 위해 girth를 뼈에서 **span**(관절 쌍 + 축 + rest, §6)으로 일반화했다 — 키는 뼈가 아니기 때문이다; 뼈는 같은 형식으로 정확히 같은 값을 준다. `head above arms`·`L/R arm beside head` 두 gate가 커진 머리를 그대로 받는다 — driver는 gate보다 앞(§9).
- **[N24] 손가락 링은 자기 마디를 따른다 — 룰 5.** 사지(`[N22]`)처럼 뿌리 하나의 비를 내려보내지 않고 **링마다 자기 마디**의 비를 곱한다 — 이전 관절에서 그 링의 관절까지의 뼈, endbone 링은 마지막 마디. 손가락은 마디마다 따로 편집되는 사지이고 마디 하나가 길어지면 그 마디만 굵어지는 것이 맞다; 손과 팔 사이에 뿌리 비를 이어 줄 뼈(손목 → 손)는 손 전체의 척도이지 손가락의 것이 아니다. 곱하는 것은 판 내(`xz`, `perp`) 폭만이다 — 손의 두께(`d = up`, 판 두께)는 모든 기둥이 공유하는 하나라 링별로 곱할 수 없고, 손등에 속한 분기 링(제어점 6)은 판의 일부라 건드리지 않는다 `[N4]`. 그래서 링 2(엄지는 Thumb3 위의 링)에서 마디가 굵어지면 분기 링과의 사이 판이 사다리꼴로 벌어진다 — 손등이 손가락을 따라 굵어지지 않는 대가이고, 받아들였다. 기둥이 곧 링이라 `cage_post.girth`가 그대로 자리다(§6).
- **[N25] 손목과 손 — 축마다 근원 하나, 그리고 `girth_d`.** 손목 링의 두 축은 뜻이 다르다: `s = up`은 팔의 두께, `d = depth`는 손의 폭(엄지↔새끼). 팔의 두께는 룰 3으로 이미 쇄골을 타고 내려오고, 손의 폭은 손가락 첫 마디(중수골)가 길어지며 벌어지는 만큼이어야 하므로 **깊이에도 자기 driver를 선언하는 열**을 둔다 — `girth_d`(§6): 링에서는 `front`·`back` 여유 넷에, 기둥에서는 `d_hi`·`d_lo`에 곱한다. 손목의 `girth_d`는 Thumb2 → Pinky1의 `s` 축 거리(엄지가 손바닥 아래로 내려가 있어 유클리드가 아니라 손목 여유가 놓인 그 축으로 잰다). 손 기둥 전부의 `girth_d`는 손목 링의 `girth`(쇄골) — 판 두께가 손목 관절 높이 기준이라 판이 손목을 중심으로 두꺼워지고 손목 링의 `s`가 같은 비라 둘이 맞닿은 채 간다; 룰 3에서 받아들였던 손목의 두께 단이 사라진다. 그래서 손목 이후는 세 축이 전부 선언된다 — y는 쇄골, xz는 손목이 손바닥 폭, 손가락 링이 자기 마디(`[N24]`), 손바닥 제어점은 앵커가 따라간다. **대가**: 손가락 두께는 마디 길이를 모른다 — 판이 손 전체에 하나(`[N4]`)라 마디를 늘이면 폭만 넓어진다. 분기점의 `thumb out`·`pinky out` 여유는 곱하지 않았다 — 뿌리 관절에서 가장자리까지 몇 mm라 뿌리가 벌어지면 가장자리가 따라오고, 손목과의 차이는 눈에 들 크기가 아니다. `girth_d`는 §9가 말하던 깊이 복원 패스의 자리이기도 하다: 몸의 링이 실루엣과 같은 비로 깊어져야 하면 `girth_d = girth`를 적으면 되고, `neck mid`·`sternum mid`의 소속 문제는 그 기둥의 `girth_d`에 무엇을 적느냐가 된다.
- **[N26] 가랑이는 고관절 너비만큼 내려온다 — 룰 7.** `crotch`의 drop(Hips에서 `−up`으로 내려오는 reach)이 고관절 너비를 따른다: 골반 기둥 셋의 `girth` = LeftUpLeg → RightUpLeg의 `side` 축 span. 고관절 뼈 둘은 순수 측방(`[N13]`)이라 이 span은 두 뼈의 합이고, rest 대비 비는 **두 뼈의 비의 평균**이다 — 한쪽 고관절만 편집하면 절반만 움직여 비대칭에서 좌우가 서로 잡아 준다. 그래서 "평균"을 따로 계산하지 않고 두 관절의 거리 하나로 적는다. **`L/R hip`도 같은 비를 받는다** — 그 reach `f·drop`은 crotch→UpLeg 직선을 f배 연장한 것이라(§3c) drop만 곱하고 이것을 두면 고관절 기둥이 그 직선에서 벗어나 기울어진 고관절 링의 정의가 깨진다; 같은 비를 곱하면 `(1+f)·UpLeg − f·Hips + f·g·drop = UpLeg + f·(UpLeg − crotch')`로 선언이 그대로 성립한다. 깊이(`d` 끝, 골반 앞뒤)는 셋 다 그대로다 — 세 기둥이 한 깊이를 공유한다는 §3c의 규칙도 그대로. 가랑이가 내려가면 `spine above hips`의 바닥(고관절 기둥 네 끝)이 f배 올라가고 `knee beside crotch`의 바닥이 내려가므로 두 gate의 여유를 스윕에서 다시 본다.
- **[N27] 독립 정의를 가진 정중선 기둥을 걷어냈다 — 깊이 복원 전에.** 두께 driver가 구간 단위로 도착해야 하는데(§9 "driver의 단위는 구간"), `sternum mid`와 spine1·spine2의 mid는 자기 링과 따로 구운 깊이를 갖고 있어 곱할 때마다 소속을 따져야 하는 이웃이었다. 둘을 없앤다. (1) **sternum은 겨드랑이 선의 정중선 교점이다.** 앞끝 = `L arm`·lo_front와 `R arm`·lo_front를 잇는 선분이 정중선 평면(`side` = Spine3·side)을 지나는 점, 뒤끝은 lo_back 둘. 높이·깊이가 전부 겨드랑이에서 오므로 Spine3은 정중선만 주고, 가슴 띠(겨드랑이–sternum 가로대)는 **구성상 평면**이다 — chest 뼈만 편집해도 sternum은 Spine3이 아니라 겨드랑이를 따른다. `sternum front` 튠은 지우고 그만큼을 arm 링 lo 변 `front`에 올린다(`[N17]`). (2) **spine 링 위의 몸통은 절두체다.** 양 겨드랑이 앞뒤 네 코너에서 spine 링의 네 코너로 직선 넷이 내려가고, spine1·spine2는 자기 관절 높이의 평면이 그 넷을 자르는 중간 링이다 — 자기 폭·깊이가 없고 `front`·`back` 튠 넷이 사라진다. 옆변은 이미 `L/R hip → 겨드랑이` 선 위였고(`[N21]`) spine 링이 그 선 위에 있으므로 폭은 그대로, 깊이만 측정값에서 보간값으로 바뀐다. 그 mid 기둥도 자기 링 앞변·뒷변의 정중선 교점이 되어 구운 오프셋이 없다. 기구는 걸친 변의 일반화다(§6 걸침 세 단계): 공통 연산이 교점 하나이고, 변은 `s`만, 코너는 전부, 기둥 끝은 전부 받는다. 읽기는 여전히 한 방향(관절 → 링·기둥 → 변 → 코너 → 끝)이라 `[N16]`의 규칙은 그대로다. **gate 뒤의 어긋남**: gate가 arm 링을 옆으로 세우거나 spine 링을 올리면 그 전에 계산된 걸침은 옛 자리를 읽은 것이다 — 걸친 변이 이미 같은 조건으로 돌고 있어 새 문제는 아니고 rest에서는 lift가 0이다. `--probe`에서 spine1·spine2 행은 곱할 자기 값이 없어 전달비 0이 정상이다. `spine mid`·`crown mid`·`head mid`는 아직 구운 오프셋 방식이다 — 같은 교점으로 바꾸면 `midline()`과 그 girth가 함께 사라진다.
- **[N9] cardinal 스냅과 발가락 부호.** rig root 로컬은 월드 정렬이 아니므로 스켈레톤에서 축을 유도하되 cardinal로 스냅해 링을 축 정렬로 유지한다. 외적은 깊이 축만 정하고 앞뒤는 못 정하므로 발가락 방향으로 부호를 정한다.

## 9. 미결

- **자기겹침 지도 — 전부 닫혔다.** 2026-09-07, 네 보정이 모두 든 케이지로 잰 스윕(§7b, 손을 rest에 묶고 seed 1):

    | | rest | 단독 184 | 쌍 1,012 | 전신 20,000 |
    |---|---|---|---|---|
    | 보정 전 (09-04) | 310 밖 | 0 | — | 5,277 |
    | 보정 1 (`512d81a`) | 310 밖 | 0 | 7 | 2,837 |
    | 보정 2 (`1614055`) | 310 밖 | 0 | 3 | 2,099 |
    | **보정 4 (09-07)** | **0 밖 · 겹침 0** | **0** | **0** | **0** |
    | **룰 3~7 + 정중선 정리 (09-09)** | **0 밖 · 겹침 0** | **0** | **0** | 3층 안 돌림 · 4층 1,125 **0** |

    판정 둘(rest 포함, 자기겹침)이 모두 0이다. 아래 표는 무엇이 있었고 무엇이 닫았는지의 이력이다 — 새 기전이 나오면 여기에 이어 적는다. 09-09 스윕(`out_mid_body`)의 탈출은 새는 케이스 466 → 266으로 줄고 최악은 27 → **57**로 올랐다 — 한쪽 고관절·쇄골 0.5 같은 극단 조합에서 사지 링이 절반으로 죄어진 자리(룰 3)이며, 비례 4층에서는 실패 0. 손 뼈까지 훑은 7,061 케이스(`out_mid`)에서는 중수골 0.5에서 손가락 링끼리 겹치는 실패 1,771이 나왔다 — 손은 이전에 훑은 적이 없어 비교 대상이 없고, 두께 driver의 자기겹침 상한이 손에서는 아직 안 잡힌 것이다.

    | 기전 | 그룹(케이스) | 방아쇠 | 뿌리 |
    |---|---|---|---|
    | **D1 다리 교차** → **0** | `ankle+knee` 762 · `knee+crotch` 484 · `L ankle+L toe` 2 | 양 고관절 0.5(단독은 무사) | 고관절 뼈가 순수 측방이라 그 길이가 골반 반폭이다 — 줄이면 다리가 링째 안으로 평행이동해 무릎 링 안쪽 변이 정중선을 넘고 두 다리의 안쪽 벽이 서로를 통과한다. `L 고관절 0.5 + R 고관절 0.5`의 16 삼각형이 **전부 좌↔우 교차**다(쌍으로 갈라 확인). §6b `L/R knee beside crotch`가 닫는다 — 이 보정만 넣고 1·2층을 다시 돌려 실패 11 → 10건, 네 그룹(`L/R ankle+knee`, `L/R knee+crotch`)이 표에서 사라진다. 다만 **살이 먼저 겹친다**: 고관절 ×0.85부터 메시 자체가 서로를 통과하므로(×0.5에서 7.0 cm) 이 범위는 보정이 아니라 길이 범위가 받을 몫이다 `[N20]` |
    | **D2 골반 판 꺾임** → **0** | `spine+spine mid+spine1` 1,670 · `L/R hip+L/R knee` 1,023·1,058 | pelvis 0.5 + 고관절 0.5(각각 단독은 0) | 고관절 기둥만 뼈를 (1+f)배로 따라간다. `L hip=0.5`에서 그 기둥이 side 0.1809 → 0.0905로 걸어 들어오는데 spine 링의 변은 0.1635에 남아, 걸림이 열어 둔 V를 허벅지 판이 가로지른다. §6b `spine above hips`가 걸림을 링째 멈춤으로 바꿔 닫는다 — 1·2층 실패 10 → 8, 세 그룹 모두 표에서 사라진다. **남은 것은 접힘이 아니라 폭이다**: 골반 살은 계속 샌다(`Hips` 44 → 39) — 두께 driver의 몫이며 `[N18]`상 판정은 아니다 |
    | **A 이음선 대 턱선** | `head+head mid+neck mid` 822 → **0** (`crown+head` 3,036 → **0**) | 목뼈 셋 중 하나 0.5, 또는 어깨밑동 1.5 | arm 링 hi 변이 목뼈를 따라가지 않는다. 보정이 머리를 어깨 위로 되올려 대부분 닫혔고 `[N20]`, 남은 쇄골 무리는 둘째 보정 `L/R arm beside head`가 닫았다(2026-09-07 스윕) |
    | **C 어깨 쐐기** (기전 자체가 없어졌다 — `delt` 삭제 2026-09-09) | `L/R arm+L/R delt` 381·421 → **0** | 쇄골 0.5 + 어깨밑동 1.5, 또는 chest 0.5 | 쇄골이 짧아지면 이음선이 정중선 쪽으로 오는데 `delt`는 상완에 매달려 밖에 남아 쐐기 삼각형이 뒤집힌다 `[N15]`. 둘째 보정이 arm 링을 머리 옆에 세워 닫았다(2026-09-07 스윕) |
    | **B 가슴 띠 뒤틀림** | `L/R arm+sternum mid` 207·206 → **0** (3그룹 짜리는 833·838 → **0**) | chest 0.5 + 쇄골 | `[N17]`의 두 여유가 두 가로대의 깊이를 13 cm 벌려 놓아 사각형이 안장이 된다. 그 사이에 가로대를 하나 넣으면 닫힌다고 보았으나, 둘째 보정 뒤 0이 되어(2026-09-07 스윕) 가로대는 필요 없어졌다 |
    | **E 머리 판 안장** | **0** (`try-neck-fit`에서 1,451·1,445) | — | head 링을 목에 맞춰 좁히면 crown 캡과의 사이가 안장이 되던 것. 이 줄기는 링을 좁히지 않고 보정으로 지키므로 기전 자체가 없다 |

    **D1·D2의 그룹 배분은 쌍 케이스에서 갈라 본 것이고, 전신 3층 숫자는 아직 그 기준으로 다시 세지 않았다** — 네 보정이 모두 든 케이지로 스윕을 다시 돌려야 확정된다(§7b `export sweep data`부터).

    **쇄골이 방아쇠인 넷**(A·B·C에 걸쳐 있다)은 하나의 뿌리를 공유한다 — `[N11]`의 `arm outward hi = −0.05`가 **절대 거리**라, 쇄골이 짧아지면 그 5 cm가 `neck mid`를 넘는다. 비율이어야 할 값이 절대값인 것이고 §9 두께 driver와 같은 종류다(씬은 지금 0으로 물렸다). §6b `L/R arm beside head`가 이것을 닫는다(2026-09-06). 2026-09-07 스윕으로 확인: 1·2층 쇄골이 든 행 188개 전부 충돌 0, 전신 3층에서 A·B·C 그룹 0. lo 변(겨드랑이)의 `outward lo`도 같은 절대값이지만 B 무리가 함께 0이 되어 따로 볼 것이 없어졌다.
- **두께 driver — "키가 크면 두꺼워진다". 룰을 하나씩 세우는 중.**
    - **룰 1 — 몸통 옆판은 겨드랑이에서 고관절로 곧다** `[N21]`. driver의 틀(rest 항등, 잰 단면 × 비)에 들어가지 않는 **레시피**로 들어갔다 — spine 세 링의 변이 고관절 기둥과 겨드랑이 사이에 걸친다(§6 걸친 변). 두께 룰이 반드시 driver 형태일 필요는 없다는 첫 예다. 비율형 driver 열(`width`)은 만들었다가 걷어냈고, 사지 룰이 오면 되살린다. 현재 단면은 rest 살 측정값 + 절대 여유이고 앵커 spread만 길이를 따른다. 단일 사지 링(팔꿈치·손목)은 spread가 0이라 전신을 1.2배 늘여도 팔 굵기가 그대로다. 링마다 단면을 구동하는 뼈(또는 전신 척도)를 선언하는 열이 필요하다: `단면 = rest 단면 × f(driver 길이 / rest)`. §4c endbone이 이 형태의 선례.
    - **룰 2 — arm 링의 이음선은 쇄골을 따른다** (2026-09-09). 그 열이 `girth`(§6)로 들어왔다: 링의 실루엣 네 값(`s_hi`·`s_lo`·`along_hi`·`along_lo`)에 girth 뼈의 현재/rest 비를 곱한다. arm 링은 실루엣이 이음선 하나(`arm tilt`·`arm length`, `[N11]`)라 곱할 것이 길이 하나이고, 그 뼈는 쇄골이다 — 어깨 폭이 넓으면 어깨가 굵다. 걸친 변(§6)이 읽는 겨드랑이 코너가 함께 내려가므로 몸통 옆판의 시작점도 따라온다. **깊이는 아직 따르지 않는다** — 그것은 이 틀의 다음 단계, rest 비율을 되돌리는 깊이 복원 패스의 몫이다(`neck mid`·`sternum mid`가 그때 함께 닥친다). 단일 사지 링에 뿌리의 비를 넘기는 것은 룰 3이 했다.
    - **룰 3 — 사지 링은 뿌리의 비를 그대로 받는다** (2026-09-09, `[N22]`). 같은 `girth` 열: elbow·wrist는 쇄골, knee·ankle·toe와 `L/R tip` 기둥(`reach`)은 고관절 뼈. 다리는 폭이 hip → knee → ankle → toe → tip으로, 팔은 높이가 arm → elbow → wrist로 내려간다. 손등 이후는 따르지 않는다 — 손은 따로 본다. 깊이는 여전히 따르지 않는다. 씬 재bake·스윕은 아직이다.
    - **룰 4 — 머리는 키를 따른다** (2026-09-09, `[N23]`). `crown`·`head` 링과 그 정중선 기둥 둘의 girth = `키`(§1: Head에서 낮은 발볼까지의 `up` 거리, rest 대비, 선형). 정면 실루엣만 — head 폭·오프셋, crown 폭·높이; 깊이는 아니다. 이를 위해 `girth`가 뼈에서 span으로 일반화됐다(§6). 비선형(소아 비례)은 미결. 씬 재bake·스윕은 아직이다.
    - **룰 5 — 손가락 링은 자기 마디를 따른다** (2026-09-09, `[N24]`). §4c의 링 전부(엄지 Thumb3~endbone, 다른 손가락 `{F}2`~endbone)의 기둥 girth = 그 링의 관절로 들어오는 뼈. 판 내 폭만 — 판 두께와 손등의 분기 링은 그대로. 씬 재bake·스윕은 아직이다.
    - **룰 6 — 손목은 손바닥 폭을, 손은 팔의 두께를 따른다** (2026-09-09, `[N25]`). 새 열 `girth_d`(§6, 깊이의 driver): 손목 링의 `girth_d` = Thumb2 → Pinky1 span(`s` 축), 손 기둥 전부의 `girth_d` = 손목 링의 `girth`(쇄골). 손목 이후는 세 축이 전부 선언되어 깊이 복원에서 볼 것이 없다. 몸 링의 깊이 복원은 `girth_d = girth`를 적는 일로 남는다. 씬 재bake·스윕은 아직이다.
    - **깊이 복원 전 정리** (2026-09-09, `[N27]`): 독립 깊이를 갖던 정중선 기둥 셋을 걷어냈다 — `sternum mid`는 겨드랑이 선의 교점, spine1·spine2와 그 mid는 겨드랑이 → spine 링 절두체의 중간 링. 남은 독립 정의는 `neck mid`(Neck 관절 + arm hi 변 깊이 + `neck front`)와 구운 오프셋 방식의 `spine`·`crown`·`head` mid.
    - **룰 7 — 가랑이는 고관절 너비만큼 내려온다** (2026-09-09, `[N26]`). 골반 기둥 셋의 `girth` = LeftUpLeg → RightUpLeg(`side`): crotch의 drop과 고관절 기둥의 `f·drop`이 같은 비를 받아 기울어진 고관절 링의 정의가 유지된다. 두 고관절 뼈의 합이라 비는 둘의 평균 — 비대칭 편집에서 절반씩. 깊이(앞뒤)는 그대로. 씬 재bake·스윕은 아직이다.
    - **선언은 손실 없이 도착한다 — 다만 구간 단위로.** `--probe`(§7b)로 잰 것(2026-09-07):
        - **구간의 제어점이 전부 함께 움직이면 전달비 0.92 ~ 1.03**(폭·깊이 모두). 팔꿈치 1.00 / 1.00, 무릎 1.03 / 0.99, spine 0.95 / 0.99, spine1 0.99 / 1.01. **MVC는 병목이 아니다** — 좌표계 교체는 이 줄기에서 필요 없다([cage-deformation-plan.md](cage-deformation-plan.md)).
        - **k에 대해 선형이다.** 0.8 · 1.2 · 1.5에서 같은 전달비가 나오므로 driver는 보정표 없이 비율을 그대로 선언하면 된다.
        - **driver의 단위는 링이 아니라 구간이다.** 링 네 코너와 **그 위에 선 기둥**이 한 배율을 받아야 한다. rest에서 spine 링의 앞변과 `spine mid`의 앞끝은 정확히 같은 깊이인데(§3b), 링만 ×1.2 하면 정중선이 3.14 cm, ×1.5에서 7.84 cm 함몰해 위에서 본 단면이 **나비**가 된다 — 그것은 두꺼운 몸이 아니다. 기둥의 깊이는 §3b가 링에서 유도한다고 선언하지만 bake는 독립 상수로 굽는다; **driver가 구간의 제어점을 전부 곱하는 쪽으로 간다**(런타임에 다시 유도하면 방금 `[N16]`에서 걷어낸 "제어점이 서로를 읽는다"가 되살아난다).
        - **아직 따라오지 않는 이웃들**(그 구간 소속인데 이름·구조가 링에 매이지 않은 것): 손은 처음부터 끝까지 기둥이라 손목 깊이가 0.05, `tip` 기둥과 평평한 발바닥 구속 `[N14]` 때문에 발목 0.54 · 발볼 1.41(과응답), arm 링의 띠에 선 `neck mid`·`sternum mid`와 당시의 `delt` 기둥 때문에 arm 0.86(`delt`는 그 뒤 지웠다). **전부 "구간에 무엇이 속하는가"의 문제이지 좌표계 손실이 아니다** — §3b·§3e·§4가 이미 소속을 선언해 두었으므로 driver 열은 그것을 따라간다.
    - **룰 하나씩 세운다 — 무엇이 판정하는가.** driver는 gate와 달리 "고칠 실패"가 없는 자리에서 출발한다(판정 둘이 이미 0). 그래서 룰을 넣기 전에 기준을 먼저 둔다.
        - **rest는 항등이다.** 모든 driver는 `f(1) = 1`이어야 한다 — 그러면 rest 케이지가 손대지 않은 채로 남아 bind와 지금까지의 측정이 그대로 유효하다. 보정이 rest에서 `lift = 0`인 것과 같은 성질이고, 분기 없이 성립한다.
        - **자기겹침 0이 상한이다.** 단면을 키우면 팔 링이 몸통에, 무릎 링이 서로 닿는다. "살을 얼마나 따라가게 할 수 있는가"의 한계를 gate와 같은 기준이 정하므로, 룰마다 스윕을 다시 돌린다.
        - **신호는 변형 후 탈출이다.** 판정은 아니지만(정합성이 아니라 설득력의 문제 `[N18]`) driver가 겨냥하는 것이 정확히 "껍질이 몸을 더 이상 감싸지 않는다"이다. 룰마다 이 수치가 내려가는지를 본다.
        - **마지막은 눈이다.** 목표가 설득력이므로 에디터에서 확인한다. 스윕이 이것을 대신하지 못한다.
        - **driver는 gate보다 앞이다.** gate는 자기가 판정한 좌표가 그 뒤로 바뀌지 않는다는 전제 위에 선다. `양 고관절 0.5 + 양 허벅지 1.5`에서 무릎 링을 ×1.5로 구동해 재면, driver → gate는 안쪽 변이 +0.50 cm에 서지만 gate → driver는 **−2.97 cm**로 다시 정중선을 넘는다(측정). 단면 배율은 배치의 항이고(§6 `s_hi` 등에 곱해진다) 보정은 그 뒤에 온다.
        - **지금 gate 여유 넷은 driver가 없는 폭에 대해 튠된 값**이므로, driver가 들어오면 함께 다시 본다.
    - 판정이 전부 0이 된 지금, 남은 신호는 **변형 후 탈출**뿐이고(`[N18]`상 진단이지 실패가 아니다) 그것이 이 항목을 가리킨다. 3층(무작위 20,000)에서는 11,448 케이스가 새고 중앙값 8정점 · 90번째 30 · 최악 96이며, 부위별 최악이 `Hips` +96 · `Spine3` +48 · `L/R UpLeg` +43·+41 · `L/R Shoulder` +13·+12 — 고관절 기둥만 뼈를 (1+f)배로 따라가고 링 폭이 따라가지 않는 그 자리다(§9 지도 D2 말미).
    - **그러나 4층(비례 1,125)에서는 그 신호가 거의 평평하다**: 실패 0, 새는 케이스 274 / 1,125, 중앙값 8 · 90번째 15 · **최악 22**. 균등 1.4배는 11정점, torso·arms 단독 1.4는 **0**, legs 1.4가 14로 가장 크다(측정 2026-09-07). 즉 3층의 최악값은 **어떤 몸도 도달하지 않는 조합**이 만든 것이고, 실제 비례에서는 재서 고칠 실패가 없다. **그러므로 두께 driver는 수치를 고치는 작업이 아니라 설득력을 바꾸는 작업이다** — 룰의 근거는 탈출 수치가 아니라 "전신을 1.2배 늘여도 팔 굵기가 그대로"라는 눈에 보이는 사실이고, 탈출은 상한을 넘지 않았는지 보는 보조 지표로만 쓴다.
- **V넥 다듬기.** (1) arm·hi의 높이는 아직 어깨 관절 평면에서 잰 삼각근 정점 높이 — 승모근 능선에 딱 맞추려면 변별 측정 창이 필요. (2) 가슴 띠(V–겨드랑이)가 arm 링 깊이로 정해지므로 가슴이 새면 arm 링 `front`/`back` 튠을 되살린다. 반대로 목·얼굴을 조이려고 arm 링의 hi `front`를 줄이면 `neck mid`가 딸려 들어오므로 `neck front`로 앞끝을 되민다 `[N17]`; sternum은 lo 변의 교점이라 lo `front`가 곧 흉골 앞이다 `[N27]`. (3) `neck mid`가 Neck 관절 그 자리라 V가 얕다(arm·hi와 2 cm 차) — 내리는 여유가 필요할 수 있다. (4) head 링의 tilt·offset은 head splitter에서 읽은 초기값(25°, 0.023) — 튠 확정 후 표로 옮기고 씬의 splitter 오브젝트는 지운다. (5) 목을 줄일 때 이음선이 턱선을 넘는 것은 아직 열려 있다(§9 자기겹침 지도 A).
- **여유(clearance) — 닫혔다, 다만 튠으로.** rest에서 **36,426 정점 전부가 `[N18]`의 한계값 0.5 mm 안쪽**이다(2026-09-07 스윕, `outside` 0). 09-04에는 310 정점이 한계값 아래였고 가장 가까운 것이 0.045 mm였다 — 손가락 마디 전부, 머리 판(0.260 mm), 골반 판(0.298 mm). 원인은 `margin`이 **비율**이라는 것이었다(반폭 7 mm인 손가락에 5%는 0.35 mm뿐이고, 둥근 단면을 감싼 사각 링은 변의 중앙에서 살에 가장 가깝다). 닫은 것은 `finger out`·`valley reach`와 머리·골반 쪽 여유 튠이다.
    - **그래서 규칙이 아니라 값으로 닫혔다.** `inflate`에 절대 하한을 두자던 안은 지금 필요가 없어졌으므로 보류한다 — 다만 다른 메시를 물리거나 `margin`을 건드리면 같은 자리가 다시 열린다. 그때는 하한을 규칙으로 두는 쪽을 먼저 본다.
- **골반 다듬기.** (0) spine 링이 고관절을 넘던 자기겹침은 §6b `spine above hips`가 닫는다 `[N16]`; 그 바닥이 `hip out`·`crotch drop`에 딸리므로 그 둘을 튠하면 §7b 스윕을 다시 돌린다. (1) `crotch drop`·`hip out`·`pelvis front/back`·`spine front/back`은 초기값 — 에디터에서 튠 뒤 표로. (2) 골반 판 깊이가 Hips·UpLeg 살 전체의 구간이라 crotch 정점도 엉덩이 깊이를 갖는다; 안쪽 허벅지 벽이 헐거우면 crotch만의 깊이(가랑이 근처 살)로 좁힌다. (3) 골반 판이 허리 링 깊이 ↔ 골반 깊이 보간이라 엉덩이 최대 돌출이 새면 `pelvis back`으로 받는다. (4) `L/R hip` 높이는 `hip out` 하나로 옆·위가 함께 정해진다 — 따로 필요하면 up 오프셋 열 추가.
- **어깨 다듬기 — 닫혔다.** `delt` 기둥을 지워(2026-09-09, `[N15]`) 항목 자체가 없어졌다. 상완은 arm 링의 이음선(`arm tilt`·`arm length`)과 elbow 링만으로 정해진다. 삼각근이 새면 이음선 튠으로 받는다.
- **발 다듬기.** (1) `ankle tilt` 45°·`front`·`back` 0은 초기값. (2) ankle 링의 joint slab이 넓어 발볼·정강이 살까지 단면에 들어올 수 있다 — 단면이 헐거우면 ankle 전용 측정 창(예: 평면 ± 발 두께)으로. (3) toe·tip의 위쪽·폭에는 아직 여유 열이 없다(잰 값 + margin) — 발등이나 발가락 끝이 새면 `front`/`out` 튠 추가. (4) tip의 위끝이 발가락 살 전체의 `up` 최대라 뚜껑이 발볼 높이다; 끝만큼 낮추려면 tip 근처 살로 잰다.
- **길이 범위 확정.** 스윕(§7b)은 슬라이더 범위인 rest × [0.5, 1.5] 전체를 훑는다. 실제로 지원할 범위가 정해지면 그에 맞춰 좁히고, 범위 밖으로 밀려난 실패는 기록만 남긴다.
- **bind 비용.** 제어점 40 → 188로 늘면서 MVC bind가 그만큼 무거워졌다(import 1회). Green/Somigliana로 갈 때 먼저 부딪히는 벽 → [cage-deformation-plan.md](cage-deformation-plan.md).
