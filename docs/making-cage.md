# making-cage

본 길이(스켈레톤 변형)에 대응하는 저폴리곤 cage 생성. mesh deformation의 전 단계.

## 의도 (goal)

- `mapping_tester`의 `import()` 시점에 rest pose에 맞춘 rest cage를 생성하고, 본 길이를 조정할 때마다 본 위치에 맞춰 cage를 업데이트한다.
- 이를 위한 cage 생성 함수는 **편집 가능한 상수 외엔 뼈 길이 목록만**을 파라미터로 받는다(매 프레임 메시 버퍼를 읽지 않음). cage가 갖춰야 할 조건:
    - 메시를 완전히 포함한다.
    - 메시에 가급적 fit해야 한다.
    - 지나치게 복잡해선 안 된다. 모든 기둥은 사각이면 충분하다.
    - 제어점(cage 버텍스)이 본 길이를 반영한다. 1단계는 t-pose 팔다리의 시작·끝 정도면 충분(길이 반영 목적).
- 알고리즘 최초 작성 시 rest 메시 정보(머리 크기, 팔다리 굵기 등)를 참조해 상수화한다. 현재 vicon 메시에 대한 rest cage가 조건을 만족하면, 변형된 비율의 pose에 대한 cage도 만족할 것으로 본다.
- mesh deformation은 이 단계에서 다루지 않는다.

## 구현 (implemented)

파일: [cage.cs](../unity/Assets/Scenes/cage.cs), [mapping_tester.cs](../unity/Assets/Scenes/mapping_tester.cs).

- **bake(에디터, 1회)** — `cage.bake(source)`가 rest 스켈레톤 + rest 메시에서 상수(`cage_constants`)를 산출해 직렬화. `import()`에서 호출.
    - 각 정점을 지배 본이 fold되는 관절 그룹에 배치(손가락 등 미열거 본은 가장 가까운 상위 관절로 fold → 살이 누락되지 않음).
    - 정점 스캔으로 세그먼트 **시작/끝 링 단면**을 개별 측정(테이퍼 반영), margin(5%)으로 완전 포함 보장.
    - Head/손끝(HandMiddle1)/발끝(ToeBase) 등 leaf 살은 **말단 캡 링**으로 확장.
- **build(런타임, 순수)** — `cage.build(lengths, k)`. 본 길이로 FK 재구성 후 baked 단면을 얹어 **고정 토폴로지 quad-tube 메시**를 생성. 단면은 상수라 길이가 늘면 튜브만 늘어남.
    - FK: `pos[j] = pos[parent] + rest_dir[j] * length[j]`. rest 방향은 길이 편집에 불변이므로 라이브 스켈레톤과 정확히 일치.
    - 조립: 체인별(척추 / 목+머리 / 좌·우 팔 / 좌·우 다리) 닫힌 사각 튜브. 관절 링 공유, 접합부(어깨·골반)는 박스 겹침. 프레임은 parallel-transport로 twist 억제.
- **통합** — `mapping_tester`: `import()`가 bake + cage 자식 생성 + `update_cage()`. 슬라이더/리셋 시 `update_cage()`로 재생성. cage는 rig root의 identity-local 자식이라 씬 스케일(x100)을 상속.
- **읽기 설정** — 정점 스캔을 위해 `ViconActorFingers_orient_finger_fixed.fbx`의 Read/Write(`isReadable`)를 활성화.
- **디버그(에디터)** — 인스펙터 버튼 + `OnDrawGizmosSelected`(선택 시), 모두 변형된 cage 공간에서:
    - `check containment` → 소스 지오메트리를 **타깃 본에 LBS 스킨**한 정점을 **현재 cage**와 비교, 벗어난 정점을 빨간 큐브로.
    - `check self-collision` → 세그먼트를 OBB로 근사해 **OBB-OBB SAT**로 겹침 검출(같은 체인 인접 세그먼트 제외), 겹친 세그먼트를 빨간 박스로.
    - 지오메트리는 항상 readable한 소스에서, 본은 타깃에서 읽음(둘은 동일 토폴로지·본 순서 클론).

현재 rest 기준 outside ≈ 2371/36426. 본 길이 대응은 정상 동작.

## 향후 과제 (future)

> 아래 항목 중 cage 생성 알고리즘 자체의 개선은 유니티 밖 샌드박스에서 반복 실험으로 진행했다 —
> [cage-lab.md](cage-lab.md). 단일 워터타이트 사각 파이프, 가랑이/뒤꿈치 포함, 다중 포즈 containment
> 학습까지 도달했고 유니티 역이식은 아직 하지 않았다.

- **접합부 박스 겹침 해소 / self-collision 제거**: 현재 팔·다리·머리 뿌리가 몸통에 박스로 겹침. 워터타이트 단일 케이지로 용접 필요. (self-collision이 없어야 한다는 것과 같은 문제.)
- **포함 갭 보완**: Hips 아래(골반·사타구니)와 팔꿈치·무릎 굽힘부 바깥 wedge의 미포함 정점. margin·링 밀도·골반 전용 처리로 개선.
- **mesh deformation**: cage 기반 정점 사상(별도 조사 [cage-deformation-plan.md](cage-deformation-plan.md)). 이것이 들어오면 메시가 cage를 따라가 `check containment`의 outside가 수렴한다.
- (선택) 링 밀도 상향, 굽힘부 miter 프레임, self-collision을 삼각형 단위 정밀 검출로 승급.
