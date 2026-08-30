# journal — 케이지 작업 일지

[cage.md](cage.md)는 **지금 무엇인가**의 선언이고, 이 문서는 **어떻게 여기까지 왔나**의 기록이다. 세션마다 항목 하나: 시작 상태, 관찰, 결정과 그 이유, 확정된 수치, 남긴 일. cage.md와 어긋나면 cage.md가 맞다.

---

## 2026-08-29 — 쇄골에서 머리까지

커밋: `168c699` → `e12d603` → `5c31219` → `77974a1`

### 시작 상태
- 링 10개(crown, L/R arm·elbow·wrist, hip, knee, sole) + 손. 176 정점 / 348 삼각형.
- 쇄골~머리 구간의 제어점은 crown 캡과 arm 링뿐. 몸통 8각형의 윗변이 `crown·hi → arm·hi` 대각선 하나.
- **arm 링 윗변(y 0.624)이 Head 관절 높이(0.622)와 같았다** — 어깨 살 실측 8.1 cm + `hi` 여유 5 cm가 겹쳐 어깨 링이 턱 높이까지 올라가 있었다. 목(0.49~0.62)은 그 대각선 벽과 깊이 0.32→0.49로 벌어지는 앞뒤판 사이 공기층에 놓임.
- 목 길이 편집 → 움직이는 제어점은 crown 4개뿐 → 얼굴이 목과 같은 비율로 늘어남.

### 작업 방식 (이 세션에서 정한 것)
- **임시 편집기로 값을 찾고, 확정되면 표로 옮긴다.** `cage_tune`(mapping_tester에 직렬화) 필드 하나 = 인스펙터 슬라이더 하나. 드래그 중에는 재bake + 케이지 갱신만(ms), 놓으면 재bind + deform(초). 확정된 값은 cage.md §3 표와 recipe 리터럴로 옮기고 슬라이더는 지운다 — 이 세션에서는 아직 옮기지 않았다(전부 "튠 중").
- Unity를 밖에서 돌릴 수 없어 컴파일은 `dotnet build unity/Assembly-CSharp.csproj`(임시 출력 경로), 토폴로지는 표를 파이썬으로 재현해 닫힘·Euler 검사. containment·deform은 에디터에서 사람이 확인.

### 진행

**1. arm 링 `hi` 튠 (`168c699`)** — 첫 슬라이더. 어깨 링 윗변을 살 + margin까지 내림(hi −0.0005).

**2. 정중선 (`e12d603`)** — 한쪽 겨드랑이를 당기면 반대편이 끌려오는 비국소성과, 앞으로 넣을 V넥 모두 정중선 정점을 요구했다. 국소로는 불가능: 판 변 위의 정점은 그 변을 공유하는 양쪽 판에 다 들어가야 닫힘 assertion을 통과하므로, 정중선은 crown → hip → knee → sole을 관통하는 사슬이 된다.
- 프리미티브는 6점 링이 아니라 **정중선 기둥(post)**. 손의 `cage_post`가 이미 "아핀 앵커 + d 축 정점 2"였고, 링 번호 규칙·토폴로지 표(링 변과 기둥을 섞어 씀)·옆판이 그대로 남는다.
- 기둥의 `center` 하나를 **d 앵커 두 세트**로 일반화 — 링 변과 같은 규칙(max/min + 여유). sole 앞변은 ToeBase가 정하므로 중심 관절 하나로는 발 길이 편집 때 mid가 변에서 떨어졌다.
- 몸통·허벅지·종아리 판을 좌/우 반판으로, crown·sole 캡을 쿼드 2개로. 정점은 그대로지만 비평면 판의 삼각화가 바뀌어 가슴·배 표면 깊이가 crown↔hip 보간으로 바뀌었다.
- 그 결과 arm 링의 front 0.2 / back 0.1과 crown front 0.1은 가슴을 덮지 못하고 옆만 부풀렸다 → 걷어내고 측정값 + margin으로. 날개뼈·가슴~배꼽 정중선이 새는 것은 crown/hip의 `front`/`back` 튠으로 받았다.
- 비국소성: 반대편으로 어느 정도 전이되지만 자연스러운 수준. MVC 가중치가 전역이라 완화일 뿐 — 해결은 좌표계(Green/Somigliana) 쪽.

**3. 라글란 arm 링 + V넥 (`5c31219`)** — arm 링 윗변을 몸통 안쪽·위로 들여 승모근 위에 얹고, 아랫변을 겨드랑이 속으로. 팔 판이 라글란 소매가 되어 삼각근이 소매 안에 들어가고, 두 윗변과 Neck 위의 `neck mid`가 앞뒤로 V를 이룬다.
- 기존 몸통 반판(crown·hi–hip·hi 현이 세로 가로대)은 승모근 점이 현 안쪽에 들어와 **접힌다**(닫힘은 유지, 표면이 겹침). 판을 V에서 잘라 몸통 반판은 `arm·hi → arm·lo → hip·hi → hip·mid → sternum → neck`, 머리 반판은 V → crown. 양쪽 볼록.
- 가로대를 가로로 눕히려면 실루엣 점마다 정중선 점이 필요 → 겨드랑이 높이에 `sternum mid`(Spine3, y 0.402 ≈ 겨드랑이 0.40). 5각형은 ladder가 못 채운다.
- 링은 변별 `along`(outward hi/lo)을 얻었고, 이어서 변별 s 여유(`lo` 신설)와 **코너별** 깊이 여유(`hi_front, hi_back, lo_front, lo_back`)까지. 두 변이 `d`에 평행이라 네 코너는 여전히 한 평면 — N1의 "깊이 공유라 평면" 근거는 "변 평행이라 평면"으로 고쳤다.
- neck/sternum mid는 자기 가로대의 arm 변 깊이를 따른다 → V–겨드랑이 가슴 띠의 앞뒤는 arm 링 hi/lo front/back으로 조절.
- 승모근 점은 어깨 관절에 고정 오프셋 → 쇄골 편집을 100% 따라가는 근사. 절반이어야 하면 아핀 기둥으로.

**4. 머리–목 분리 링 (`77974a1`)** — 턱끝이 Head 관절보다 앞·아래에 있어 분리 평면은 기울어져야 한다. 씬에 놓아둔 **head splitter** 평면을 Hips 공간으로 환산: 타깃 rig root 월드 (2,0,0)·−90° x·×100, Hips 로컬 +90° x → Hips 공간 = 월드 축. 평면 법선 (0, .906, .423) = `side` 축 25°, Head에서 법선 방향 0.023 m.
- `head` 링: `n = up↷25°`, `d = depth↷25°`, `s = side`, outward 0.023. 링 코드는 프레임이 직교면 되므로 기울어진 링도 같은 코드.
- 측정 종류 **split** 신설(`bool terminal` → `enum fit{ joint, cap, split }`): 평면은 앵커 + outward에 고정, 단면은 평면 너머 Head 살 전체 — 머리 판이 감싸야 할 것. crown과 비슷한 값이 나오지만 종속은 아니다.
- 판: 목 반판(V → head), 머리 반판(head → crown). 목 길이 편집이 목 판만 늘이고 머리는 rigid하게 오른다 — 에디터에서 확인.

### 씬에 저장된 튠 값 (2026-08-29 종료 시점, 씬 단위 m)

| 대상 | 값 |
|---|---|
| arm `hi` / `lo` | 0.002 / 0 |
| arm `outward hi` / `outward lo` | −0.05 / 0.02 |
| arm hi `front` / `back` | 0.03 / 0.02 |
| arm lo `front` / `back` | 0.055 / 0.02 |
| head `tilt` / `offset` | 25° / 0.023 |
| head `front` / `back` | 0.005 / −0.04 |
| crown `front` / `back` | 0.01 / 0.004 |
| hip `front` / `back` | 0.02 / 0.004 |

종료 시 containment 통과. 194 정점 / 384 삼각형.

### 남긴 일
- 튠 값을 cage.md §3 표와 recipe로 옮기고 슬라이더 정리. head splitter 오브젝트 삭제.
- 승모근 능선에 딱 맞추는 변별 측정 창(지금은 어깨 관절 평면에서 잰 삼각근 정점 높이).
- `neck mid`가 Neck 관절 자리라 V가 얕다(arm·hi와 2 cm) — 내리는 여유가 필요할 수 있다.
- 두께 driver, 척추 세부 링(정중선 사슬에 가로대 하나씩), 자기겹침 스윕 — cage.md §9.

---

## 2026-08-30 — 골반에서 발끝까지

커밋: `1eee5a8` → `a982e22` → `954124f`(씬) → `53f226d` → `bd5a6e7`

### 시작 상태
- 링 11 + 정중선 기둥 7 + 손, 194 정점 / 384 삼각형. 쇄골~머리 완료.
- 다리는 **스커트 프리즘 하나**: `hip`·`knee`·`sole` 링이 양다리 공용이고 정중선 기둥이 앞뒤 판을 좌/우로 가를 뿐, 다리 사이 안쪽 벽이 없다. 가랑이·안쪽 허벅지가 공기층. 공용 링은 한 다리 편집에 반대 다리를 끌어간다(`[N1]`의 대가).
- 발은 `sole` 캡 하나 — 종아리 프리즘의 바닥면. 발등·뒤꿈치·발가락이 전부 밖.
- 지난 세션 저널의 `hip front` 0.02는 씬에서는 0.04로 저장돼 있었다(저널 뒤에 더 튠). 이 세션에서 hip 링이 없어져 값 자체는 소멸.

### 진행

**1. 골반 분기 (`1eee5a8`)** — 손의 §4 구조를 골반에.
- `hip` 링 → **`spine` 링**(Spine 관절, joint, 허리). 몸통 반판의 아랫변이 `spine·hi → spine·mid`.
- 골반은 링이 아니라 **기둥 3**: `crotch`(Hips − up·drop), `L/R hip`(앵커 (UpLeg, Hips) 가중치 (1+f, −f) + up·f·drop = `UpLeg + f·(UpLeg − crotch)`). 손가락 endbone과 같은 수법이라 고관절 폭 편집에 (1+f)배로 따라 나간다. 셋이 골반 살(Hips·양 UpLeg 본)의 depth 구간 하나를 공유(d 앵커 Hips) — 손의 판 두께처럼.
- 왜 링이 아니라 기둥인가: 두 고관절 링이 가랑이에서 **만나야** 하는데 링(정점 4)은 정점을 공유할 수 없다. 손가락 분기 링이 이웃한 손바닥 기둥 둘인 것과 같이 `crotch – L hip`이 왼다리의 기울어진 고관절 링. 앞에서 보면 두 링이 V, spine 링과 함께 손등 같은 5각형 → 정중선 규약대로 `spine·mid – crotch`에서 반판 둘.
- 토폴로지 표에서 역 `hip`의 `hi`/`mid`/`lo`가 기둥. 정중선 기둥 헬퍼를 `at[(역, 변)]` 사전 + 가중치 있는 `post()`로 일반화 — 이후 발끝 기둥도 이 사전을 그대로 쓴다.
- `knee`/`sole` → `L/R knee`, `L/R sole` 단일 사지 링(자기 다리 살만). 정중선 사슬은 crotch에서 끝나고 그 아래 두 다리는 각자 닫힌 관 — `[N10]` 갱신, `[N1]`은 역사로.
- 튠 신설: `crotch drop`, `hip out`(비율), `pelvis front/back`, `spine front/back`(hip 것 대체), `knee out`(양 링 바깥 변)·`knee back`(고정 0.1 → 슬라이더). 204 정점 / 404 삼각형.

**2. 발 (`a982e22`)** — sole 삭제, 다리별 `knee → ankle → toe → tip`.
- **ankle**: Foot 관절을 지나되 **뒤로 기울어진** 링(`n = −up↷tilt`, `d = depth↷tilt`, 초기 45°). 수평이면 뒤꿈치 아래와 발등 위를 동시에 자르지만, 뒤꿈치 바닥에서 발등–정강이 연결부로 기울이면 종아리 관과 발 관을 가르는 자연스러운 단면. wrap = LeftLeg 서브트리, joint slab.
- **toe**: ToeBase에서 `n = depth, d = up` 세로 링 — front = 발등, back = 발바닥.
- **tip**: 관절 없는 endbone 위의 **기둥 쌍**(`(1+f, −f)`·(ToeBase, Foot), f = 발가락 살이 ToeBase 너머로 뻗은 길이 ÷ 발 뼈) — 발 길이에 비례해 따라 나가는 뚜껑. 캡 = `tip·hi → tip·lo` 쿼드 1.
- 프레임의 `d`가 knee(depth) → ankle(depth↷45°) → toe·tip(up)으로 연속해서 돌아 **앞판 = 정강이 → 발등 → 발가락 위, 뒷판 = 종아리 → 뒤꿈치 → 발바닥**. 링 코드는 그대로(직교 프레임이면 됨). `[N14]`.
- **평평한 발바닥**: 링에도 기둥처럼 **변별 깊이 앵커**(`d_hi_anchor`/`d_lo_anchor`, 기본 = 앵커 전체)를 두고, toe 링의 아랫변과 tip의 아랫끝을 **Foot에 걸어 ankle 링 바닥(뒤꿈치) 높이**에 둔다. 위쪽만 살에서 재고, ankle `back` 하나가 발 전체의 바닥을 정한다. 발 뼈를 늘여도 발바닥은 뒤꿈치와 한 평면.
- 튠: `ankle tilt`, `ankle front`(발등 쪽, −0.1까지)·`back`(뒤꿈치 쪽). 220 정점 / 436 삼각형.

**3. 씬 (`954124f`)** — 옛 constants(링에 d 앵커 없음)는 `rebuild cage` 전에 `ring_corners`에서 예외 — 의도된 fail-loud, 방어 코드 없음.

**4. 척추 세부 링 (`53f226d`)** — Spine(허리)과 Spine3(sternum mid) 사이에 가로대가 없어 배·아랫가슴 깊이가 보간이었다. Spine1·Spine2에 `spine`과 같은 가로 joint 링 + 정중선 기둥 하나씩.
- 몸통 반판 10점: `arm·hi → arm·lo → spine2·hi → spine1·hi → spine·hi → spine·mid → spine1·mid → spine2·mid → sternum → neck` — 가로대가 sternum·spine2·spine1·spine 관절마다 하나. 정중선 사슬은 crown → head → neck → sternum → spine2 → spine1 → spine → crotch. 척추 마디 길이 편집이 그 구간만 늘인다.
- 튠 `spine1/2 front/back`(초기 0). 232 정점 / 460 삼각형. 슬롯 번호가 바뀌어 `rebuild cage` 필수.
- 확인할 것: Spine2 관절이 겨드랑이(arm·lo ≈ Spine3 높이) 아래인지 — 위라면 spine2·hi–arm·lo 옆판이 접힌다.

**5. 어깨–상완 분리 (`bd5a6e7`)** — arm 링 윗변이 승모근 위로 들어간 뒤(N11) 상완 판 윗변이 승모근 점 → 팔꿈치 위 직선이 되어, 앞에서 보면 삼각근 너머 상완 위에 빈 공간이 컸다.
- 골반(N13)과 같은 답: 어깨와 상완을 가르는 링은 **겨드랑이 정점(arm·lo, R쪽 v18–v19)을 arm 링과 공유**해야 하므로 링(정점 4)이 아니라 **기둥 하나 `delt`** + arm·lo 변. 상완 위 삼각근 끝에서 겨드랑이로 내려오는 기울어진 세로 링이고, 앞에서 arm 링과 겨드랑이에서 만나는 V.
- 그 사이 어깨 쐐기 = 앞/뒤 **삼각형 판**(arm·hi – delt – arm·lo) + 위쪽 벽 쿼드(arm·hi → delt). `strip`이 홀수 고리를 삼각형 하나로 끝내도록 넓혔다(짝수 고리 결과는 불변 — 옛 표 194/384 재현으로 확인). 상완 판은 `delt → elbow·hi → elbow·lo → arm·lo`.
- `delt` 위치: 앵커 (Arm, ForeArm) 가중치 (1−g, g), `g = delt along`(초기 0.4) — 상완을 늘이면 비율로 미끄러진다. 그 지점 평면 ±slab 안의 LeftArm 살에서 up 위끝(+`delt up`)과 depth 앞/뒤를 잰다. 살 창이 LeftArm 본만 보므로 삼각근이 LeftShoulder 본에 지배되면 낮게 잡힐 수 있다.
- 같은 커밋에 elbow 링 `hi`(고정 0.05) → 슬라이더. 씬에서 0.01로 내려왔다. 236 정점 / 468 삼각형.

### 작업 방식
- 지난 세션과 같다(슬라이더 → 표). 토폴로지는 매번 파이썬으로 재현해 닫힘·Euler 검사(열린 간선 = 손목 사각형 2개만이면 손이 닫음), 컴파일은 `dotnet build`. containment·self-collision·deform은 에디터에서 사람이.

### 씬에 저장된 튠 값 (2026-08-30 종료 시점, 씬 단위 m)

| 대상 | 값 |
|---|---|
| arm `hi` / `lo` | 0.002 / 0 |
| arm `outward hi` / `outward lo` | −0.05 / 0.02 |
| arm hi `front` / `back` | 0.03 / 0.02 |
| arm lo `front` / `back` | 0.055 / 0.02 |
| head `tilt` / `offset` | 25° / 0.023 |
| head `front` / `back` | 0.005 / −0.04 |
| crown `front` / `back` | 0.01 / 0.004 |
| spine `front` / `back` | 0.01 / 0 |
| spine1 · spine2 `front` / `back` | 0 / 0 (미튠) |
| `crotch drop` / `hip out` | 0.08 / 0.8 |
| pelvis `front` / `back` | 0 / 0.006 |
| knee `out` / `back` | 0.005 / 0.02 |
| ankle `tilt` / `front` / `back` | 45° / −0.08 / 0.005 |
| `delt along` / `delt up` | 0.4 / 0 (미튠) |
| elbow `hi` | 0.01 |

전부 "튠 중" — 표로 옮긴 값은 아직 없다. 고정값에서 풀린 것: knee `back` 0.1 → 0.02, elbow `hi` 0.05 → 0.01.

### 남긴 일
- 튠 값을 cage.md §3 표와 recipe로 옮기고 슬라이더 정리(두 세션 분). head splitter 오브젝트 삭제.
- ankle의 측정 창이 joint slab(종아리 뼈 × 0.25)이라 정강이·발볼 살까지 단면에 들어온다 — 헐거우면 전용 창. toe·tip 위쪽·폭에는 여유 열 없음. tip 위끝이 발가락 살 전체의 up 최대라 뚜껑이 발볼 높이.
- 골반 판 깊이가 Hips·UpLeg 살 전체 구간이라 crotch 정점도 엉덩이 깊이 — 안쪽 허벅지 벽이 헐거우면 crotch만의 깊이로.
- `L/R hip` 높이는 `hip out` 하나로 옆·위가 함께 정해진다 — 따로 필요하면 up 오프셋 열.
- spine1·spine2 링 튠(배·아랫가슴 깊이), spine2와 arm·lo의 높이 순서 확인.
- `delt` 튠(along·up); 삼각근 살이 LeftShoulder 본이면 살 창 넓히기; delt 링 앞/뒤 여유 열 없음.
- 이전 항목 그대로: 승모근 능선 측정 창, `neck mid` 깊이, 두께 driver, 자기겹침 스윕 — cage.md §9.
