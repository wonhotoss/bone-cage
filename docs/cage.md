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
| 링 | 사각형, **정점 4**. 축 `n`(법선, 몸 바깥), `s`(실루엣 축: 앞/뒤 판의 경계 변이 놓이는 방향), `d`(깊이 축: 앞/뒤 판을 가르는 방향). 코너 = (hi/lo 실루엣 쪽) × (front/back 깊이 쪽). 두 변이 `d`에 평행하므로 변별 `along`으로 기울어도 한 평면 `[N11]`. 깊이 앞/뒤도 각자의 **d 앵커**를 갖는다 — 기본은 링 앵커 전체, toe 링은 뒤(바닥)를 Foot에 건다 `[N14]`. | `cage_ring`, `ring_corners` |
| 기둥(post) | 제어점 하나가 소유하는 **정점 2**(축 `d`의 hi/lo). 손, 정중선(§3b), 골반(§3c). 판 내 위치 = 앵커 관절들의 아핀 결합 + 오프셋. `d` 좌표는 링 변과 같은 규칙 — 양끝이 각자의 d 앵커 `max`/`min` + 여유. | `cage_post`, `post_ends` |
| 판(plate) | 닫힌 제어점 고리를 hi 정점들로 한 번, lo 정점들로 한 번(역순) 채운 면. ladder 삼각화; 홀수 고리는 마지막이 삼각형 하나, 3점 고리는 삼각형 그 자체. 쿼드의 대각선은 **캐릭터 오른쪽 면과 뒷면에서 반대**로 긋는다(거울면 XOR 뒷면) → 좌우 거울 대칭, 앞/뒤 같은 접힘선 `[N3]` | `topology`, `strip` |
| 옆판(wall) | 제어점 사슬. 이웃 쌍마다 쿼드 1(hi–hi–lo–lo). 대각선은 오른쪽 면에서 반대 `[N3]`. | `topology` |
| 여유(reach) | 잰 구간 바깥으로 더하는 상수. 링: `front` `back`(깊이 축, **hi/lo 변별**), `hi`/`lo`(±s 쪽), `outward hi`/`outward lo`(변별 n 방향 이동). 음수 = 안쪽. 모두 씬 단위. | `recipe` |
| 정점 번호 | 링 `i` 코너 `c` → `i·4 + c` (`hi_front 0, hi_back 1, lo_back 2, lo_front 3`). 링 순서 crown, L arm, L elbow, L wrist, R arm, R elbow, R wrist, spine, spine1, spine2, L knee, L ankle, L toe, R knee, R ankle, R toe, head. 기둥 `p` 끝 `e` → `rings·4 + p·2 + e` (`hi 0, lo 1`). 기둥 순서 = 생성 순서(정중선 7: crown·head·neck·sternum·spine·spine1·spine2 → 골반 3: crotch·L hip·R hip → 발끝 4: L tip hi·lo, R tip hi·lo → 어깨 2: L delt, R delt → 왼손 → 오른손, 각 손은 제어점 6 → 엄지…새끼 링). | `cage` 상수 |

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
| `L arm` | LeftArm | LeftShoulder | +side | up | depth | joint | 튠 / 튠 | 튠 / 튠 | 튠(§7) | 튠(§7) | 튠(§7) | 튠(§7) | 몸통 판과 어깨 판의 경계 `[N2]`. `lo`(겨드랑이) 변은 delt 링과 공유 §3e `[N15]`. 모든 여유 튠 중(§7) |
| `L elbow` | LeftForeArm | LeftArm | +side | up | depth | joint | | | 튠(§7, 초기 0.05) | | | | |
| `L wrist` | LeftHand | LeftHand | +side | up | depth | joint | | | | | | | 단면은 손이 덮어씀 §4a |
| `R arm` | RightArm | RightShoulder | −side | up | depth | joint | 튠 / 튠 | 튠 / 튠 | 튠(§7) | 튠(§7) | 튠(§7) | 튠(§7) | `[N2]`. 모든 여유 튠 중(§7) |
| `R elbow` | RightForeArm | RightArm | −side | up | depth | joint | | | 튠(§7, 초기 0.05) | | | | L elbow와 공통 |
| `R wrist` | RightHand | RightHand | −side | up | depth | joint | | | | | | | §4a |
| `spine` | Spine | Hips | +up | side | depth | joint | 0 | 0 | | | | | 몸통 판의 아랫변, 허리. 아래는 골반 기둥 §3c `[N13]`. **두 변이 각각 옆의 고관절 기둥에 걸린다**(`hold_hi` = `L hip`, `hold_lo` = `R hip`) — pelvis를 줄이면 가로일자에서 V로 열린다 `[N16]`. `front`·`back`은 튠 중(§7) |
| `spine1` | Spine1 | Hips | +up | side | depth | joint | 0 | 0 | | | | | 배. 몸통 판의 가로대 하나 `[N10]`. `front`·`back` 튠 중(§7) |
| `spine2` | Spine2 | Hips | +up | side | depth | joint | 0 | 0 | | | | | 아랫가슴. 〃 |
| `L knee` | LeftLeg | LeftUpLeg | −up | side | depth | joint | | 튠(§7, 초기 0.1) | 튠(§7) | | | | 자기 다리 살만 잰다 `[N13]`. `hi` = 바깥쪽 변 |
| `L ankle` | LeftFoot | LeftLeg | −up↷45° | side | depth↷45° | joint | 튠(§7) | 튠(§7) | | | | | 발목. Foot 관절을 지나 뒤로 기울어진 링 — 뒤꿈치에서 발등–정강이 연결부로. 기울기·`front`(발등 쪽)·`back`(뒤꿈치 쪽) 튠 중(§7) `[N14]` |
| `L toe` | LeftToeBase | LeftFoot | +depth | side | up | joint | | (바닥) | | | | | 발볼. 발 방향에 직교하는 세로 링, front = 발등, back = 발바닥. **뒤(바닥)의 d 앵커는 Foot**, 여유 = ankle 링 바닥 높이까지 — 발바닥이 뒤꿈치와 수평 `[N14]` |
| `R knee` | RightLeg | RightUpLeg | −up | side | depth | joint | | 튠(§7, 초기 0.1) | | 튠(§7) | | | `s = side`라 `lo`가 바깥쪽 변; 여유는 L knee와 공통 |
| `R ankle` | RightFoot | RightLeg | −up↷45° | side | depth↷45° | joint | 튠(§7) | 튠(§7) | | | | | `[N14]` |
| `R toe` | RightToeBase | RightFoot | +depth | side | up | joint | | (바닥) | | | | | `[N14]` |

ankle의 `↷`는 `n`의 기준이 `−up`이라 `n = −cos·up + sin·depth`, `d = cos·depth + sin·up` — knee 프레임을 `side` 축으로 돌려 toe 프레임 쪽으로 가는 도중이다. 발끝은 링이 아니라 기둥 §3d. **평평한 발바닥**: `floor = Foot·up − (ankle 링 rest lo_back 코너)·up`. toe 링은 `d_lo_anchor = Foot`, `hi_back = lo_back = floor`; tip 기둥의 아랫끝도 같다(§3d). 위쪽은 살에서 잰다.

`hi`/`lo` 여유의 방향은 각각 `s`의 +/−쪽: 팔 링(`s = up`)에서는 위/아래, 나머지(`s = side`)에서는 캐릭터 왼쪽/오른쪽. 음수 = 그 변을 살 안쪽으로.

**bake 규칙** (`measure`): 평면 = `max(앵커·n)`. 측정 창의 살로 `s`·`d` 구간을 재고 inflate. 앵커를 `s` 좌표의 중앙값 기준으로 hi/lo 변에 배정(지금은 모든 링이 한 관절 또는 한 사지의 관절들이라 같은 앵커가 양쪽에 `[N1]`). 굽는 값:
`along_hi = (cap ? (max(살·n) − 평면)·(1+margin) : 0) + outward_hi`, `along_lo = 〃 + outward_lo` — arm 링은 hi 변만 안쪽으로 들여 승모근 위에 얹는다 `[N11]`,
`s_hi = hi_s − max(hi앵커·s) + hi`, `s_lo = min(lo앵커·s) − lo_s + lo`,
코너별 깊이 `{hi,lo}_front = hi_d − max(앵커·d) + front.{hi,lo}`, `{hi,lo}_back = min(앵커·d) − lo_d + back.{hi,lo}`. d 앵커(`d_hi_anchor`/`d_lo_anchor`) = 앵커 전체; toe 링만 뒤(바닥)의 d 앵커를 Foot으로 덮어쓴다(§3d).
`hold_hi`/`hold_lo`(변별 걸림 기둥)는 기본 빈칸이고, 그 기둥이 만들어진 뒤에 붙인다 — 지금은 spine 링뿐 `[N16]`.

### 3b. 정중선 기둥 — `midline(slot, joint)`, `post(...)`

앞/뒤 판의 가로대가 정중선을 지나는 자리마다 기둥 하나. 몸통·머리·골반 판을 좌/우 반판으로 가른다 `[N10]`. **띠** = 기둥이 닫는 가로대의 링: 기둥의 `d` 앵커와 깊이 여유는 그 링의 것이라 앞/뒤 정점이 링의 변과 같은 깊이에 놓인다 — 링 위의 기둥은 hi/lo 코너 여유의 평균, `neck mid`는 arm 링 **hi 변**의, `sternum mid`는 **lo 변**의 여유.

| 이름 | 띠 | 앵커(가중치) | 판 내 위치 | 비고 |
|---|---|---|---|---|
| `crown mid` | crown | Head (1) | crown 앞변 중점 | |
| `head mid` | head | Head (1) | head 앞변 중점 | `d`는 head 링의 기울어진 `d` |
| `neck mid` | L·R arm | Neck (1) | Neck | V넥 바닥. 두 arm 링의 hi 변과 함께 V를 이룬다 `[N11]`. 앞끝만 띠 위에 **자기 여유**를 더한다 — 튠 중(§7) `[N17]` |
| `sternum mid` | L·R arm | Spine3 (1) | Spine3 | 겨드랑이(arm·lo) 높이의 가로대. 앞끝만 띠 위에 **자기 여유**를 더한다 — 튠 중(§7) `[N17]` |
| `spine2 mid` | spine2 | Spine2 (1) | spine2 앞변 중점 | |
| `spine1 mid` | spine1 | Spine1 (1) | spine1 앞변 중점 | |
| `spine mid` | spine | Spine (1) | spine 앞변 중점 | 링 위 정중선 사슬의 끝. 아래로는 골반 반판의 세로 가로대 `spine mid – crotch` |

**bake 규칙**: 링 위의 기둥(`midline`) — 판 내 위치 = rest 앞변 중점, 앵커 관절과의 차를 오프셋으로 굽는다. 링 사이의 기둥(`post`) — 판 내 위치 = 앵커 관절 그 자리(오프셋 0), 띠 = arm 링(d 앵커 LeftArm·RightArm, 여유 = `L arm`의 해당 변 `front`/`back`). `neck mid`와 `sternum mid`의 앞끝만 그 위에 `neck front`·`sternum front`를 더한다 `[N17]`.

### 3c. 골반 기둥 — `pelvis_post(...)`

골반은 링이 아니라 **손바닥처럼 분기하는 판** `[N13]`. 기둥 3개가 spine 링과 함께 5각형(spine·hi – L hip – crotch – R hip – spine·lo)을 이루고, `crotch – L hip`이 왼다리의, `crotch – R hip`이 오른다리의 **기울어진 고관절 링**이다(손가락 분기 링이 이웃한 손바닥 기둥 둘인 것과 같다). 토폴로지 표에서는 역 `hip`의 `hi`(L hip) · `mid`(crotch) · `lo`(R hip)로 부른다.

| 이름 | 역·변 | 앵커(가중치) | 판 내 위치 | 비고 |
|---|---|---|---|---|
| `crotch` | hip·mid | Hips (1) | Hips − `up`·`crotch drop` | 가랑이 바로 아래. 두 고관절 링이 여기서 만난다 |
| `L hip` | hip·hi | LeftUpLeg, Hips (1+f, −f) | UpLeg + f·(UpLeg − crotch) | `f = hip out`. crotch→UpLeg 직선을 UpLeg 너머로 f배 연장한 고관절 바깥 점. 고관절이 Hips에서 멀어지면 (1+f)배로 따라 나가 링이 옆으로 넓어진다 |
| `R hip` | hip·lo | RightUpLeg, Hips (1+f, −f) | 〃 | |

| 상수 | 값 | 단위 | 의미 |
|---|---|---|---|
| `crotch drop` | 튠 중(§7, 초기 0.15) | 씬 | Hips 관절에서 crotch까지 `−up` 거리 |
| `hip out` | 튠 중(§7, 초기 1) | 비율 | 바깥 고관절 점이 UpLeg에서 더 나가는 crotch→UpLeg 거리의 배수 |
| `pelvis front` / `pelvis back` | 튠 중(§7, 초기 0 / 0) | 씬 | 골반 판 깊이의 여유 |

**깊이(bake 규칙)**: 세 기둥이 **한 깊이를 공유**한다 — 손의 판 두께처럼. Hips·LeftUpLeg·RightUpLeg 본의 살 전체의 `depth` 구간을 inflate하고, `d` 앵커는 셋 다 **Hips**(양끝), 여유 = 구간 − Hips 좌표 + `pelvis front`/`back`. 그래서 골반 판은 허리 링과 허벅지 사이에서 평평한 판이고, 고관절 편집에도 앞뒤가 흔들리지 않는다. 판 내 오프셋: `crotch`는 `−up·drop`, `L/R hip`은 `+up·f·drop`(아핀 결합 `(1+f)·UpLeg − f·Hips`에 crotch의 drop을 f배 더한 것 = `UpLeg + f·(UpLeg − crotch)`).

### 3d. 발끝 기둥 — `foot(prefix, tag, ankle, toe, station)`

발가락 끝에는 관절이 없으므로 손가락 끝 링(§4c endbone)과 같은 방식: 역 `L tip`/`R tip`의 `hi`(+side)·`lo`(−side) 기둥 둘이 발가락 살 끝을 막는 뚜껑이다 `[N14]`. `d = up`이라 앞끝 = 발등 쪽, 뒤끝 = 발바닥 쪽으로 toe 링과 같다.

| 이름 | 역·변 | 앵커(가중치) | 판 내 위치 | `d` 앵커 · 여유 |
|---|---|---|---|---|
| `L tip` | L tip·hi / ·lo | ToeBase, Foot (1+f, −f) | 가상 endbone에서 `side`로 발가락 살 폭(`wide_hi` / `wide_lo`)까지 | 위: ToeBase, 발가락 살 `up` 구간(inflate)의 위끝 − ToeBase. 아래: **Foot**, `floor`(§3) — toe 링 바닥과 같은 높이 |
| `R tip` | R tip·hi / ·lo | 〃 | 〃 | 〃 |

**bake 규칙**: 발가락 살 = ToeBase 서브트리의 살. `f = max(살·dir[ToeBase] − ToeBase) · (1+margin) / rest_len(ToeBase)` — 발가락이 ToeBase 너머로 뻗은 길이의 발 뼈 길이 비율. 그래서 발 길이를 늘이면 발끝이 비례해 따라 나간다. 같은 함수가 toe 링의 바닥을 Foot에 건다(§3 평평한 발바닥).

### 3e. 어깨 기둥 — `delt(prefix, tag, station)`

상완 위쪽, 삼각근이 끝나는 자리의 기둥 하나. arm 링의 겨드랑이 변(`arm·lo`)과 함께 **기울어진 세로 링** `arm·lo – delt`를 이루어 상완 판이 여기서 시작한다. 앞에서 보면 arm 링(겨드랑이 → 승모근)과 V, 그 사이 쐐기가 어깨(삼각근) `[N15]`. 역 `L delt`/`R delt`는 `hi`만 갖는다(`lo`는 arm·lo).

| 이름 | 역·변 | 앵커(가중치) | 판 내 위치 | `d` 앵커 · 여유 |
|---|---|---|---|---|
| `L delt` | L delt·hi | LeftArm, LeftForeArm (1−g, g) | 뼈 위 `g` 지점에서 `up`으로 그 자리 상완 살의 위끝 + `delt up` | Arm·ForeArm 양끝. 그 자리 상완 살의 `depth` 구간(inflate) − 앵커 max/min |
| `R delt` | R delt·hi | RightArm, RightForeArm (1−g, g) | 〃 | 〃 |

| 상수 | 값 | 단위 | 의미 |
|---|---|---|---|
| `delt along` (`g`) | 튠 중(§7, 초기 0.4) | 비율 | 어깨 관절 → 팔꿈치 사이 기둥 위치 |
| `delt up` | 튠 중(§7, 초기 0) | 씬 | 상완 살 위끝 너머 여유 |

**bake 규칙**: 살 = LeftArm 본의 살 중 `g` 지점 평면(`dir[ForeArm]`에 직교)에서 `slab · rest_len(ForeArm)` 이내. `up` 구간을 inflate한 위끝, `depth` 구간을 inflate한 앞/뒤. 아핀 앵커라 상완을 늘이면 기둥이 비율대로 미끄러진다.

## 4. 손 — `hand(prefix, tag, slot, n, mirror)`

좌우 각각 호출: `("LeftHand", "L", L wrist, +side, mirror)`, `("RightHand", "R", R wrist, −side, 정방향)`. 아래 이름의 `L`은 `R`로도 읽는다.

### 4a. 공통

| 항목 | 정의 |
|---|---|
| 손 축 | `n` = 팔 바깥(±side), `s` = **depth**(엄지 +, 새끼 −), `d` = **up**(판 축). 팔 링과 프리즘 축이 90° 다르다 `[N4]` |
| 판 두께 | 손 서브트리 살 전체의 `d` 구간을 inflate. 손의 모든 기둥이 공유. 모든 기둥의 d 앵커 = **손목**(양끝), 여유 = 판 구간 − 손목 좌표 |
| 손목 링 덮어쓰기 | 실루엣 축(`up`): hi = 판 위, lo = 판 아래 + `wrist_drop`. 깊이 축(`depth`): 손목 평면에서 `rest_len(Middle1)·0.5` 이내 살의 구간을 inflate + 여유 `wrist thumb`(엄지 쪽) / `wrist pinky`(새끼 쪽), 튠 중(§7) `[N5]` |
| 손 폭 | 손 살 전체의 `s` 구간을 inflate → `wide_hi`(엄지 쪽), `wide_lo`(새끼 쪽) |

### 4b. 손바닥 제어점 6 — `cp[0..5]`

기둥 하나씩. 위치 = 앵커 아핀 결합 + 판 내 오프셋.

| 이름 | 앵커(가중치) | 오프셋 |
|---|---|---|
| `L thumb out` | Thumb2 (1) | `s`로 `wide_hi`까지 + `thumb out`(튠 중 §7) |
| `L thumb\|index` | Thumb2, Index1 (½, ½) | 손목→중점 방향(판 내 투영)으로 `valley_reach` |
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
- **반경**: 관절 `J` 서브트리 살의 `perp` 구간을 inflate. 기둥 오프셋 = `perp · (구간 끝 − 관절의 perp 좌표 ± finger_reach)`.
- **endbone**: 마지막 마디 살이 `dir`로 뻗은 최대 거리 ×(1+margin)를 **rest 길이 비율 `f`** 로 굽고, 앵커 `(last, parent(last))`에 가중치 `(1+f, −f)` `[N6]`.

### 4d. 손가락 링 추가 여유 — `finger_reach`

씬 단위, 좌우 손 공통. 표에 없는 링은 잰 값 그대로.

| 손가락 | 링 | hi(엄지 쪽) | lo(새끼 쪽) |
|---|---|---|---|
| Index | 3 | 0.001 | 0.001 |
| Middle | 2 | 0 | 0.001 |

## 5. 토폴로지

몸통의 "제어점" = 링의 실루엣 변 `(링, hi/lo)` 또는 기둥 `(역, hi/lo/mid)` → 정점 쌍 (front, back). 역(station) = 링 15개 + 링 없이 mid만 가진 `neck`·`sternum` + 기둥 셋(hi/mid/lo)으로 된 `hip`(§3c) + 기둥 둘(hi/lo)로 된 `L tip`·`R tip`(§3d) + 기둥 하나(hi)로 된 `L delt`·`R delt`(§3e). 손의 제어점 = 기둥 → (hi, lo). 손목 사각형은 양쪽이 공유하되 **역할이 바뀐다**: 팔은 앞/뒤 변을 판에, 위/아래 변을 옆판에 쓰고 손은 반대 `[N4]`.

### 5a. 몸통 판 — `panels` (앞판 + 거울 뒷판)

| 판 | 고리 (링·변) |
|---|---|
| 몸통 왼반판 | L arm·hi → L arm·lo → spine2·hi → spine1·hi → spine·hi → spine·mid → spine1·mid → spine2·mid → sternum·mid → neck·mid |
| 몸통 오른반판 | neck·mid → sternum·mid → spine2·mid → spine1·mid → spine·mid → spine·lo → spine1·lo → spine2·lo → R arm·lo → R arm·hi |
| 목 왼반판 | head·mid → head·hi → L arm·hi → neck·mid |
| 목 오른반판 | neck·mid → R arm·hi → head·lo → head·mid |
| 머리 왼반판 | crown·mid → crown·hi → head·hi → head·mid |
| 머리 오른반판 | head·mid → head·lo → crown·lo → crown·mid |
| 왼 어깨 | L arm·hi → L delt → L arm·lo (삼각형) |
| 오른 어깨 | R arm·lo → R delt → R arm·hi (삼각형) |
| 왼 위팔 | L delt → L elbow·hi → L elbow·lo → L arm·lo |
| 왼 아래팔 | L elbow·hi → L wrist·hi → L wrist·lo → L elbow·lo |
| 오른 위팔 | R arm·lo → R elbow·lo → R elbow·hi → R delt |
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
| 1 | crown·hi → head·hi → L arm·hi → L delt → L elbow·hi → L wrist·hi |
| 2 | L wrist·lo → L elbow·lo → L arm·lo → spine2·hi → spine1·hi → spine·hi → hip·hi → L knee·hi → L ankle·hi → L toe·hi → **L tip·hi → L tip·lo** → L toe·lo → L ankle·lo → L knee·lo → **hip·mid** → R knee·hi → R ankle·hi → R toe·hi → **R tip·hi → R tip·lo** → R toe·lo → R ankle·lo → R knee·lo → hip·lo → spine·lo → spine1·lo → spine2·lo → R arm·lo → R elbow·lo → R wrist·lo |
| 3 | R wrist·hi → R elbow·hi → R delt → R arm·hi → head·lo → crown·lo → **crown·mid → crown·hi** |

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
- **표가 선언하는 것은 고리와 사슬뿐이다.** 판을 채우는 ladder의 가로대(`v[i]`–`v[j]` 짝)와 쿼드를 가르는 대각선은 `strip`의 선택이며 어느 행도 이름하지 않는다. 그래서 사슬의 연속 기둥 쌍 — 그것이 곧 껍질의 링이고, 기울어진 고관절 링(`crotch` 옆 `L/R hip`)과 어깨 링(`L/R delt` 옆 arm·lo)이 여기서 나온다 — 을 `cage_constants.grid`에 정점 쌍으로 남긴다. 삼각화는 이 구분을 지우므로 `tris`에서 되돌릴 수 없다. 디버그 와이어(§7)가 읽는다.
- 결과: **236 정점 / 468 삼각형**, Euler = 2. (링 17×4 + 기둥 16×2 + 손 2×(제어점 6 + 엄지 2링·2 + 손가락 4×3링·2)×2)

## 6. 런타임 재배치 — `points(lengths, k)`

순수 함수: 편집 길이 → FK 관절 `jc` → 제어점.

**링** (`ring_corners`): 변별로 자기 앵커만 본다. 기둥이 링보다 먼저 놓인다 — 기둥은 관절 중심만 읽고 링 변은 기둥에 걸릴 수 있으므로 의존이 한 방향이다.
`plane_hi = n·max(max(hi앵커·n) + along_hi, max_{p ∈ hold_hi} max(p의 두 끝·n))`, `plane_lo` 도 같다 — 걸림 기둥이 없으면 앞 항만 남는다 `[N16]`,
`edge_hi = s·(max(hi앵커·s) + s_hi)`, `edge_lo = s·(min(lo앵커·s) − s_lo)`,
깊이는 d 앵커의 구간에 코너별 여유: `front = d·(max(d_hi앵커·d) + c_front)`, `back = d·(min(d_lo앵커·d) − c_back)` (c = hi, lo 변; d 앵커는 보통 양 변 앵커 전체).
코너 = plane + edge + 깊이. 좌우 변이 독립이라 공용 링은 기울 수 있고, 두 변이 `d`에 평행이라 네 점은 항상 한 평면 `[N1]`.

**기둥** (`post_ends`): `at = Σ weight·jc[anchor] + reach`를 `d`에 직교 투영, `d` 좌표는 `max(d_hi앵커·d) + d_hi` / `min(d_lo앵커·d) − d_lo` (손: 양쪽 다 손목, 정중선: 링의 앵커).

## 7. 검증·디버그 — `mapping_tester`

| 기능 | 동작 |
|---|---|
| 슬라이더 | 편집 대상 뼈 53개(몸통 23 + 손가락 2×5×3), 범위 rest × [0.5, 1.5]. 변경마다 `update_body()` = 케이지 재생성 → deform → rest pose 재바인딩. |
| 이름 태그 (씬 뷰, 선택 시) | 이름 그룹마다 중심에 태그 하나(흰색). 화면 크기 `tag_min_px` 미만 그룹은 숨김 → 전신에선 링 이름만, 손 줌에서 손가락 이름. **클릭 → 그 그룹만 펼침**(노랑): 정점 번호(시안), 놓는 관절 태그(오렌지) + 관절→대상 점선. 가상 endbone은 `Joint ×1.40`처럼 가중치 표기. |
| 와이어 (`all_edges`) | 라이브 케이지, 시안. 기본은 **이 문서가 선언하는 변만**(`cage.frame`): 링마다 사각형 하나(손가락·발가락 링은 이름을 공유하는 기둥 쌍), 기둥마다 선분 하나, 그리고 §5d의 `grid` — 사슬의 연속 기둥 쌍, 즉 분기 링(고관절·어깨·손가락 갈래)과 실루엣을 따라가는 링 사이 연결. 판을 채우는 ladder의 가로대와 쿼드 대각선은 표에 없으므로 빠지고, 그러면 와이어가 메시가 아니라 레시피로 읽힌다 — 튠 슬라이더가 움직이는 것이 정확히 링 변과 기둥 끝이다. 켜면 삼각형 전체를 그린다: 포함·자기겹침은 표면에 관한 것이므로 그때는 이쪽. |
| | 정중선에서 갈린 링(crown·head·spine·spine1·spine2)의 앞·뒤 변과 정중선 기둥의 선분은 **껍질의 간선이 아니다** — 판이 그 자리에서 반으로 갈리므로 정중선 기둥을 거쳐 돌아간다. 선언된 대상이라 그대로 그린다. |
| `check containment` | rest 메시를 현재 케이지로 사상한 결과(`mapped`)를 그 케이지에 대해 **광선 패리티** 판정. 바깥 정점을 빨간 큐브로. 스키닝은 이 파이프라인에 없으므로 어느 길이에서나 의미가 있다 — 몸은 언제나 "케이지가 사상한 메시"다. |
| `check self-collision` | 정점을 공유하지 않는 삼각형 쌍의 관통 검출, 빨간 외곽선. 손가락 길이를 크게 바꾼 뒤 먼저 볼 것. |
| `rebuild cage` | 재bake + 케이지 갱신 + 재bind. 이 문서의 상수를 바꾸면 누른다. |
| `export sweep data` | 스윕(§7b)이 읽을 rest 쪽을 `tools/cage_sweep/data/`에 기록: 구운 상수(JSON), rest 메시(rig 공간)와 정점별 지배 관절, 슬라이더가 편집하는 본 목록. 재bake 뒤에 다시 누른다. |
| 튠 슬라이더 (`cage_tune`) | 아직 확정 안 된 §3 값을 인스펙터에서 찾는 임시 편집기. 현재: arm 링 `hi`(기본 0.05) · `lo`(0, 범위 −0.1..0.1), arm 링 `outward hi` · `outward lo`(0.05, 범위 −0.15..0.1; 음수로 hi는 승모근 위까지, lo는 겨드랑이 속까지 들인다), arm 링 hi/lo 변별 `front`·`back`(0, 범위 −0.1..0.1), crown·spine·spine1·spine2 링 `front`·`back`(0, 범위 −0.05..0.1), head 링 `tilt`(25°, 0..45) · `offset`(0.023, −0.02..0.06) · `front`·`back`(0, −0.05..0.1), `neck front`·`sternum front`(두 정중선 기둥의 앞끝만 띠 너머로, 0, −0.05..0.1), 골반 `crotch drop`(0.15, 0..0.3) · `hip out`(비율 1, 0..2) · `pelvis front`·`back`(0, −0.05..0.1), knee 링 `out`(양 링의 바깥쪽 변 s 여유, 0, −0.1..0.1) · `back`(0.1, −0.05..0.2), ankle 링 `tilt`(45°, 0..80) · `front`(0, −0.1..0.1) · `back`(0, −0.05..0.1; 발바닥 높이도 정한다), 어깨 기둥 `delt along`(비율 0.4, 0.1..0.9) · `delt up`(0, −0.05..0.1), elbow 링 `hi`(양 링 윗변, 0.05, −0.05..0.1), 손 `wrist thumb`·`wrist pinky`(손목 링 폭 여유, 0, −0.05..0.05) · `thumb out`·`pinky out`(8각형 바깥 기둥 여유, 0, −0.05..0.05; 양손 공통, 음수 = 살 쪽으로). 드래그 중엔 재bake + 케이지 갱신만(와이어가 바로 따라옴), 놓으면 재bind + deform. 값이 정해지면 표와 recipe로 옮기고 슬라이더는 지운다. |
| import 시 | `bake` → `bind` → 케이지 자식 생성 → `update_cage`. FBX는 Read/Write 활성 필요. |

### 7b. 길이 스윕 — `tools/cage_sweep`

버튼 둘(`check containment`·`check self-collision`)을 길이 범위 전체에 자동으로 돌리는 Unity 밖 도구. 한 케이스는 길이만의 순수 함수 — 스켈레톤이 케이지를 몰고 케이지가 메시를 몰 뿐 **스키닝이 개입하지 않으므로** 에디터가 필요 없다. csproj가 `cage.cs`·`cage_deform.cs`를 **소스째 컴파일**하므로 재구현이 없고, 도구와 에디터 버튼이 같은 코드를 돈다(`UnityEngine.CoreModule`의 `Vector3`·`Mathf`는 엔진 없이 도는 순수 관리 코드다).

| 층 | 케이스 | 묻는 것 |
|---|---|---|
| 1 single | 본 하나를 rest × {0.5 … 1.5} 8단으로, 나머지는 rest | 각 본이 홀로 어디까지 가는가 |
| 2 pair | 본 쌍을 네 모서리(0.5·1.5 조합)로 | 이웃 본 사이 상호작용 |
| 3 whole | 전 본을 `[0.5, 1.5]`에서 무작위로 뽑은 전신 | 전조합(2⁵³)의 몬테카를로 대역 |

실제로 일어나지 않는 조합도 일부러 남긴다 — 인구를 모형화하는 것이 아니라 레시피가 깨지는 자리를 찾는 것이다. 판정은 둘 다 rest 기준선 대비이고, 그 기준선은 **0 / 0**이다(§9 포함률). `--skip hand`처럼 이름 조각을 주면 해당 본은 rest에 묶인다 — 손가락을 풀어 두면 3층이 전부 손에서 깨져 몸통에 대해 아무 말도 하지 않으므로, 한 번에 한 부위씩 묻는 손잡이다. 결과는 `out/results.csv`(전 케이스)와 `out/report.md`(케이지 그룹별 자기겹침, 신체 부위별 탈출, 본별 안전 범위).

```
Unity 인스펙터 [export sweep data]        # 또는 -executeMethod mapping_tester.export_headless
dotnet run -c Release --project tools/cage_sweep
```

## 8. 설계 노트

- **[N1] 공용 링의 변은 각자의 앵커로.** 양다리 공용 링(옛 hip·knee·sole)이 있던 때의 규칙: 두 변이 한 평면(`n`으로 전체 최댓값)을 공유하면 한쪽 다리만 **줄일** 때 링이 반대쪽 무릎에 붙잡혀 따라오지 않으므로(늘일 때만 따라옴) 변을 갈라 링이 기울며 양다리를 추적하게 했다. 골반이 기둥으로 분기하고(N13) 무릎·발바닥이 다리별 링이 된 뒤로는 공용 링이 없다 — 변별 앵커 구조는 코드에 남아 있고(§6), 지금은 모든 링이 같은 앵커를 양쪽에 두어 축 정렬 그대로다.
- **[N2] 여유는 그 자리에서 몸통 판을 경계 짓는 링에.** 얼굴·가슴·배·어깨는 링 자신의 살 측정에 안 들어오므로 여유로 덮는다. 어깨·고관절 링이 들어오면서 몸통 판의 변이 팔꿈치·무릎에서 어깨·고관절로 옮겨갔으므로 가슴·어깨 여유도 팔꿈치 링에서 arm 링으로 옮겼다. 팔꿈치 링에 남기면 팔 판만 앞으로 20cm 부푼다. **정중선 반판(N10) 이후**에는 몸통 판의 깊이가 crown↔hip 보간이 되어 arm 링의 front/back 여유는 가슴·등을 덮지 못하고 몸통 옆만 크게 부풀렸다. 그래서 arm front 0.2 / back 0.1과 crown front 0.1을 걷어 측정값 + margin으로 되돌렸다. 가슴·등은 crown/spine(당시 hip) 링의 `front`/`back` 튠과 어깨 링이 맡는다.
- **[N3] fan이 아니라 ladder.** 링마다 깊이가 달라 fan은 판 전체를 첫 제어점 기준으로 비튼다 — 몸통 중앙선이 정수리에서 고관절로 직행하며 어깨 링을 건너뛰어 어깨 앞 여유가 가슴에 반영되지 않았다. ladder는 좌우 대칭이고 가슴 띠가 어깨 깊이로 평평하다. 고리가 홀수면 두 절반이 만나는 곳이 삼각형 하나로 끝난다 — 어깨 쐐기(N15)의 3점 판이 그것. **대각선 규칙**: ladder의 가로대(정점 짝)는 고리를 역순으로 돌려도 같지만 쿼드의 대각선(`v[i]–v[j−1]`)은 다른 코너를 잇는다. 오른쪽 판은 왼쪽 판의 거울상을 역순으로 추적하고(거울은 방향을 뒤집으므로 winding을 지키려면 역순), 뒷면도 앞면 고리의 역순이라, 그대로 두면 케이지가 거울 대칭이 아니라 up 축 180° 회전 대칭(왼 앞판 ≡ 오른 뒷판의 거울)이 된다 — 비평면 쿼드에서는 표면이 달라 대칭 메시의 containment와 MVC 가중치가 좌우 다르게 나온다. 그래서 오른쏙 면과 뒷면(XOR)은 대각선을 반대로 긋는다: 좌우가 정확한 거울이 되고, 쿼드는 앞/뒤 같은 두 제어점을 잇는 선으로 접힌다. 좌/우 판정은 면의 rest 중심 `side` 부호 — 모든 판이 정중선에서 갈라지므로(N10) 자기 자신이 거울상인 면은 없다.
- **[N4] 손은 링이 아닌 기둥, 프리즘 축은 팔과 90°.** 인접 분기 링이 **제어점을 공유**해야 손등이 한 장의 폴리곤으로 남고, 손가락 링이 자기 뼈에 직교할 수 있다 — 링(정점 4)은 그걸 못 한다. T-pose에서 손바닥이 아래를 보므로 손가락은 `depth`로 벌어지고 두께는 `up`이다. 손목 사각형의 네 변은 팔과 손이 역할을 바꿔 각각 두 번씩 쓰이므로 껍질은 닫힌 채로 남고, 닫힘 assertion이 그것을 검증한다.
- **[N5] 손목 링만 손 쪽에서 잰다.** 두께는 손 살 전체에서 — 모든 손 기둥이 이 두께를 공유하므로 손목 단면만 보면 손가락이 판을 뚫는다. 폭은 중수골 절반 이내 살에서 — 링 자신의 slab은 아래팔 길이에 비례해 너무 넓어 벌어진 손가락까지 폭으로 잡는다. 단 아래팔이 손보다 훨씬 굵어 그대로 두면 팔 판이 손목에서 손바닥 두께로 잘록해지므로, 손목 링의 **손바닥 쪽 변만** `wrist_drop`만큼 내린다. 손 기둥들은 판을 지키므로 손 전체가 두꺼워지지 않고 손바닥 판이 손목에서 손 쪽으로 비스듬히 올라온다.
- **[N6] 손가락 링은 자기 뼈에 직교, endbone은 비율.** `s`에 직교시키면 벌어진 손가락이 판을 뚫는다. rig에 endbone이 없으므로 마지막 마디 살이 뼈 방향으로 뻗은 만큼을 rest 길이 비율로 굳혀 `(1+f, −f)` 아핀 결합으로 놓는다 — 그래서 마디를 늘리면 끝 링이 따라 나간다. 현재 케이지에서 **길이에 비례해 굵기·길이가 따라가는 유일한 부위**다(§9 참고).
- **[N7] winding은 부피로, 왼손은 역추적.** 판 방향은 일관되게 추적하되 어느 쪽이 바깥인지는 rig 축에 달렸으므로 부호 있는 부피로 판정해 필요 시 전체를 뒤집는다. 좌우 손은 프레임 손대칭이 반대라 왼손만 추적 순서를 뒤집는데, 닫힘 assertion이 그 판정을 검증한다. 뒤집힌 추적은 대각선을 바꾸므로 손도 N3의 대각선 규칙(오른손 면 반대)으로 거울 대칭이 된다.
- **[N8] 케이지는 길이만의 함수.** 매 프레임 메시를 읽지 않는다. rest 방향이 편집에 불변이므로 FK가 라이브 스켈레톤을 정확히 재현하고, 살 측정은 bake 1회에 상수로 굳는다. 현재 vicon 메시의 rest 케이지가 조건을 만족하면 비율이 바뀐 pose의 케이지도 만족한다고 본다.
- **[N10] 정중선은 관통한다.** 판 변 위의 정점은 그 변을 공유하는 양쪽 판에 모두 들어가야 닫힘 assertion을 통과한다. 그래서 정중선 정점은 한 구간에만 둘 수 없고 crown → head → neck → sternum → spine2 → spine1 → spine → crotch를 잇는 사슬이 된다(처음에는 crown → hip → knee → sole이었고, 다리가 분기하면서 crotch에서 끝난다 — 그 아래 두 다리는 각자 닫힌 관이다). 척추 관절마다 링 + 기둥 하나 = 몸통 판의 가로대 하나: 배·아랫가슴 단면이 살에서 잡히고 척추 마디 길이 편집이 그 구간만 늘인다. 반판의 ladder는 정중선 기둥에서 출발해 실루엣 사슬로 돌아오므로 가로대가 세로로 선다(crown·hi–hip·hi 현). 정점은 그대로지만 비평면 판의 삼각화가 바뀌므로 가슴·배의 표면 깊이는 달라진다 — 팔 링 깊이의 가로 띠가 사라지고 crown↔hip 깊이의 보간이 된다(N3의 "가슴 띠"는 어깨 링이 들어오면 그 링의 여유가 맡는다). 한쪽 편집은 그쪽 반판만 움직이지만 MVC 가중치는 전역이라 비국소성은 완화될 뿐 사라지지 않는다.
- **[N11] 라글란 arm 링과 V넥.** arm 링의 hi 변을 안쪽·위로 들여 승모근 위에 얹으면(변별 `along`) 팔 판이 라글란 소매가 되어 삼각근이 소매 안에 들어가고, 두 hi 변과 Neck 위의 `neck mid`가 앞뒤로 V를 이룬다. 그러면 지금까지의 몸통 반판(crown·hi–hip·hi 현이 세로 가로대)은 승모근 점이 현 안쪽에 들어와 **접힌다** — 닫힘은 깨지지 않지만 표면이 겹친다. 그래서 판을 V에서 자른다: 몸통 반판은 arm·hi에서 출발해 정중선(hip·mid → sternum → neck)으로 돌아오고, 머리 반판은 V에서 crown까지. 양쪽 다 볼록. 가로대를 가로로 눕히려면 실루엣 점(arm·hi, arm·lo, hip·hi)마다 정중선 점이 있어야 하므로 겨드랑이 높이에 `sternum mid`(Spine3)를 둔다 — 당시 ladder는 홀수 고리를 못 채웠고, 지금(N3)은 채우더라도 5각형이면 마지막 가로대가 삼각형으로 기울어 가슴 띠가 평평하지 않다. 두 기둥의 깊이는 arm 링 것이라 V–겨드랑이 사이 가슴 띠가 arm 링 깊이로 평평하다(N3의 가슴 띠가 여기로 돌아옴). 승모근 점은 어깨 관절에 고정 오프셋이라 쇄골 편집을 100% 따라간다; 절반만 따라가야 하면 아핀 기둥으로 바꾼다.
- **[N12] 머리–목 분리 평면은 기울어진다.** 턱끝이 목 꼭대기(Head 관절)보다 앞·아래에 있어 머리(턱·귀·뒤통수와 그 위)와 목을 가르는 평면은 수평일 수 없다. 씬의 head splitter 평면(Hips 공간에서 법선 (0, .906, .423), Head에서 법선 방향 0.023 m)을 그대로 읽어 `side` 축 25° 기울기 + 오프셋으로 굽는다. 링 프레임(n, s, d)은 직교만 하면 되므로 기울어진 링도 같은 코드로 놓인다. 단면은 **split**: 평면 너머의 Head 살 전체 — 그 위의 머리 판이 감싸야 하는 것이 그것이고, 결과적으로 crown과 비슷한 폭·깊이가 나오지만 종속은 아니다. 목 길이를 늘이면 V–head 사이 목 판만 늘고 head–crown 사이 머리 판은 Head에 함께 실려 rigid하게 오른다.
- **[N13] 골반은 손바닥처럼 분기한다.** 양다리를 한 프리즘에 넣고 정중선으로만 가르면 가랑이와 안쪽 허벅지가 공기층에 놓이고, 공용 링은 한 다리 편집에 반대 다리를 끌어간다. 두 다리가 각자 링을 가지되 가랑이에서 **만나야** 하므로 고관절 링은 링(정점 4, 공유 불가)이 아니라 손의 분기 링처럼 **이웃한 기둥 둘**이다: `crotch`를 양쪽이 공유하고 바깥 점 `L/R hip`은 각자. crotch→UpLeg 직선을 UpLeg 너머로 `hip out`배 연장하면 고관절 바깥 실루엣 근처에 닿고, 이 세 점과 depth가 한 평면이라 고관절 링은 사타구니 주름처럼 안쪽 아래(crotch)에서 바깥 위(hip)로 기울어 다리를 감싼다. 앞에서 보면 두 링이 V, 위의 spine 링과 함께 손등 같은 5각형 = 골반 판(정중선 규약대로 spine·mid–crotch에서 반판 둘). 바깥 점을 `(1+f)·UpLeg − f·Hips`의 아핀 결합으로 두는 것은 손가락 endbone과 같은 수법이라, 고관절 폭 편집에 링이 옆으로 넓어진다. 몸통 판의 아랫변은 hip 링 대신 **spine 링**(Spine 관절, 허리)이 되어 sternum·arm과 이어진다. 세 기둥은 손의 판 두께처럼 골반 살의 depth 구간 하나를 공유해 골반 판이 평평한 판으로 남는다. 무릎·발바닥 링은 자기 다리 살만 재므로(`wrap` = 그 다리의 UpLeg/Foot) 두 다리가 붙어 서도 안쪽 변이 서로를 넘지 않는다 — 극단 길이에서의 자기겹침은 `check self-collision`으로 본다.
- **[N14] 발은 프레임이 돌아가는 관이다.** 발바닥 캡 하나로는 발이 종아리 프리즘의 바닥면일 뿐이라 발등·뒤꿈치·발가락이 전부 밖에 놓였다. 발을 다리와 90° 꺾인 사지로 보아 링을 셋 둔다: **ankle**은 Foot 관절을 지나되 수평이 아니라 뒤로 기울어진 링 — 수평이면 뒤꿈치 아래와 발등 위를 동시에 자르지만, 뒤꿈치 바닥에서 발등–정강이 연결부로 기울이면 종아리 관과 발 관을 가르는 자연스러운 단면이 된다(기울기는 튠). **toe**는 ToeBase에서 발 방향(`depth`)에 직교하는 세로 링으로 발볼을 감싼다. **tip**은 손가락 끝처럼 관절 없는 endbone 위의 기둥 쌍(`(1+f, −f)`·(ToeBase, Foot))이라 발 길이에 비례해 따라 나가는 뚜껑이다. 프레임의 `d`가 knee의 `depth` → ankle의 `depth↷tilt` → toe·tip의 `up`으로 연속해서 돌므로 링 코드는 그대로이고, 앞판이 정강이에서 발등으로, 뒷판이 종아리에서 뒤꿈치·발바닥으로 이어진다 — 옆판은 발의 안·바깥 측면. ankle의 측정 창은 joint slab(종아리 뼈 길이 × 0.25)이라 기울어진 평면 근처의 정강이·발 살을 함께 잡는다; 좁혀야 하면 ankle 전용 창을 둔다. **발바닥은 평평하다**: toe 링의 아랫변과 tip의 아랫끝은 살을 재지 않고 ankle 링의 바닥(뒤꿈치, `back` 여유 포함) 높이를 따른다 — d 앵커를 Foot으로 두고 그 높이 차를 여유로 굽는다. 그래서 발 뼈를 늘이거나 기울여도 발바닥은 뒤꿈치와 한 평면이고, ankle `back` 하나가 발 전체의 바닥을 정한다.
- **[N15] 어깨는 팔 링과 겨드랑이를 공유하는 V.** arm 링의 윗변이 승모근 위로 들어간 뒤(N11) 상완 판의 윗변은 승모근 점에서 팔꿈치 위까지 한 직선이 되어, 삼각근 너머 상완 위에 빈 공간이 컸다. 어깨와 상완을 가르는 링을 넣되, 겨드랑이 정점을 arm 링과 **공유**해야 어깨 쐐기가 닫힌다 — 골반의 crotch(N13)와 같은 이유로 링이 아니라 **기둥 하나**(`delt`) + arm 링의 lo 변이 새 링이다. 상완 위 삼각근 끝에 앉힌 `delt`에서 겨드랑이로 내려오는 기울어진 세로 링이 되고, 앞에서 보면 arm 링과 겨드랑이에서 만나는 V. 그 사이는 **삼각형 판** 둘(앞/뒤: arm·hi – delt – arm·lo) + 위쪽 벽 쿼드(arm·hi → delt) = 어깨 쐐기이며, ladder가 홀수 고리를 삼각형으로 끝내도록 넓혔다. `delt`는 (Arm, ForeArm)의 아핀 점이라 상완 길이에 비율로 따라간다.
- **[N16] 링 변은 기둥에 걸릴 수 있다 — 가로일자에서 V로.** spine 링은 Spine 관절에 걸려 있고 pelvis 뼈(Hips→Spine)가 그것을 내린다. 반면 `L/R hip` 기둥은 UpLeg와 Hips의 아핀 점이라 그 뼈에 움직이지 않는다. 그래서 pelvis를 줄이면 링의 두 변이 옆의 고관절 기둥 아래로 내려가고, 골반 판이 위로 접혀 몸통 판을 뚫는다 — 길이 스윕이 가장 먼저 찾은 실패이며 교차는 pelvis 0.625에서 시작한다(측정). 답은 변별 걸림(`hold_hi`/`hold_lo`): 그 변의 평면은 자기 앵커 + 여유이거나, 그보다 `n`으로 덜 나갔다면 걸림 기둥의 높이. 정중선 기둥(`spine mid`)은 걸리지 않고 계속 관절을 따르므로, 앞에서 본 링은 가로일자 → 고관절 높이에서 평평 → **정중선을 바닥으로 하는 V**로 열린다. 고관절 기둥 자체는 손대지 않는다. 걸림 높이는 pelvis·lumbar와 무관한 상수이므로 이 clamp가 spine 변을 spine1 변 위로 밀어올릴 수는 없다(최악 pelvis 0.5·lumbar 0.5에서 여유 +0.00036 rig 단위, 측정). 기둥 두 끝의 최댓값을 쓰므로 기울어진 기둥은 먼 끝으로 건다.
- **[N17] 정중선 기둥의 앞끝은 자기 띠에서 풀린다.** 정중선 기둥은 자기 띠(닫는 가로대의 링)의 깊이를 그대로 쓴다(§3b) — `neck mid`의 앞끝은 arm 링 hi 변의 `front` 여유가 정한 깊이에 놓이고, rest에서 두 값은 같다(둘 다 0.1186). 그런데 목·얼굴을 조이려면 arm 링의 hi `front`를 줄여야 하고, 그러면 V 바닥이 함께 뒤로 끌려 목 앞·쇄골 사이로 파고든다. 두 점이 요구하는 깊이가 다르다: arm·hi는 가슴 띠의 깊이(승모근 위), `neck mid`는 목 앞(울대) 깊이. 그래서 앞끝에만 자기 여유 `neck front`를 띠 위에 더한다 — 뒤끝은 목 뒤가 가슴 띠와 같은 깊이라 그대로 둔다. `sternum mid`도 같다: arm 링의 lo 변이 정하는 깊이는 겨드랑이의 것이고 흉골은 그보다 앞이므로 `sternum front`로 민다. 이 둘로 arm 링의 두 변을 살까지 당기고도 목 앞과 윗가슴이 덮인다 — rest 실측 0 정점 탈출. 필요해지면 뒤끝도 같은 방식으로 푼다.
- **[N9] cardinal 스냅과 발가락 부호.** rig root 로컬은 월드 정렬이 아니므로 스켈레톤에서 축을 유도하되 cardinal로 스냅해 링을 축 정렬로 유지한다. 외적은 깊이 축만 정하고 앞뒤는 못 정하므로 발가락 방향으로 부호를 정한다.

## 9. 미결

- **두께 driver — "키가 크면 두꺼워진다".** 현재 단면은 rest 살 측정값 + 절대 여유이고 앵커 spread만 길이를 따른다. 단일 사지 링(팔꿈치·손목)은 spread가 0이라 전신을 1.2배 늘여도 팔 굵기가 그대로다. 링마다 단면을 구동하는 뼈(또는 전신 척도)를 선언하는 열이 필요하다: `단면 = rest 단면 × f(driver 길이 / rest)`. §4c endbone이 이 형태의 선례.
- **V넥 다듬기.** (1) arm·hi의 높이는 아직 어깨 관절 평면에서 잰 삼각근 정점 높이 — 승모근 능선에 딱 맞추려면 변별 측정 창이 필요. (2) 가슴 띠(V–겨드랑이)가 arm 링 깊이로 정해지므로 가슴이 새면 arm 링 `front`/`back` 튠을 되살린다. 반대로 목·얼굴을 조이려고 arm 링의 `front`를 줄이면 두 정중선 기둥이 딸려 들어오므로 `neck front`·`sternum front`로 앞끝을 되민다 `[N17]`. (3) `neck mid`가 Neck 관절 그 자리라 V가 얕다(arm·hi와 2 cm 차) — 내리는 여유가 필요할 수 있다. (4) head 링의 tilt·offset은 head splitter에서 읽은 초기값(25°, 0.023) — 튠 확정 후 표로 옮기고 씬의 splitter 오브젝트는 지운다.
- **포함률.** rest에서는 **36,426 정점 전부가 케이지 안**이고 자기겹침도 없다(2026-09-01 §7b 스윕 측정). 제어점이 40 → 236으로 늘면서 초기의 "어깨·배·팔 위아래가 판을 뚫는다" 상태는 해소됐다. 남은 것은 길이 편집 아래의 포함률이고, 그것이 스윕이 재는 값이다. 조여야 할 자리가 나오면 링 추가로 — 토폴로지 표만 늘리면 되고 assertion이 오추적을 막는다.
- **골반 다듬기.** (0) spine 링이 고관절을 넘던 자기겹침은 변별 걸림으로 닫았다 `[N16]`; 걸림 높이가 `hip out`·`crotch drop`에 딸리므로 그 둘을 튠하면 §7b 스윕을 다시 돌린다. (1) `crotch drop`·`hip out`·`pelvis front/back`·`spine front/back`은 초기값 — 에디터에서 튠 뒤 표로. (2) 골반 판 깊이가 Hips·UpLeg 살 전체의 구간이라 crotch 정점도 엉덩이 깊이를 갖는다; 안쪽 허벅지 벽이 헐거우면 crotch만의 깊이(가랑이 근처 살)로 좁힌다. (3) 골반 판이 허리 링 깊이 ↔ 골반 깊이 보간이라 엉덩이 최대 돌출이 새면 `pelvis back`으로 받는다. (4) `L/R hip` 높이는 `hip out` 하나로 옆·위가 함께 정해진다 — 따로 필요하면 up 오프셋 열 추가.
- **어깨 다듬기.** (1) `delt along` 0.4·`delt up` 0은 초기값. (2) `delt`의 살 창은 LeftArm 본의 살만 본다 — 삼각근이 LeftShoulder 본에 지배되면 위끝이 낮게 잡힌다; 그러면 창을 LeftShoulder 살까지 넓힌다. (3) delt 링의 앞/뒤 여유 열 없음(잰 값 + margin).
- **발 다듬기.** (1) `ankle tilt` 45°·`front`·`back` 0은 초기값. (2) ankle 링의 joint slab이 넓어 발볼·정강이 살까지 단면에 들어올 수 있다 — 단면이 헐거우면 ankle 전용 측정 창(예: 평면 ± 발 두께)으로. (3) toe·tip의 위쪽·폭에는 아직 여유 열이 없다(잰 값 + margin) — 발등이나 발가락 끝이 새면 `front`/`out` 튠 추가. (4) tip의 위끝이 발가락 살 전체의 `up` 최대라 뚜껑이 발볼 높이다; 끝만큼 낮추려면 tip 근처 살로 잰다.
- **길이 범위 확정.** 스윕(§7b)은 슬라이더 범위인 rest × [0.5, 1.5] 전체를 훑는다. 실제로 지원할 범위가 정해지면 그에 맞춰 좁히고, 범위 밖으로 밀려난 실패는 기록만 남긴다.
- **bind 비용.** 제어점 40 → 188로 늘면서 MVC bind가 그만큼 무거워졌다(import 1회). Green/Somigliana로 갈 때 먼저 부딪히는 벽 → [cage-deformation-plan.md](cage-deformation-plan.md).
