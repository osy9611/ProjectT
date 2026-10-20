# Actor 생명주기

Actor는 MonoBehaviour 하나(`ComBaseActor`)이고, 풀에서 꺼낸 뒤 `Spawn()`으로 스폰을 시작해 풀 반납이나 파괴로 스폰을 끝낸다. 상태는 private enum `ActorState` 하나로 관리하며, 각 호출은 허용된 상태에서만 진행되고 나머지 상태에서는 무시되거나 예외를 던진다. 생명주기 단계가 실패하면 첫 예외를 그대로 전파하고 Actor를 `Faulted` 또는 `EndFaulted`로 격리한다. 격리된 Actor는 다시 쓰지 않고 파괴로 정리한다.

기준 커밋은 `7a4f0178`이다. 대상 코드는 다음과 같다.

- `Client/Assets/Script/Actor/ComBaseActor.cs`
- `Client/Assets/Script/Pool/Pool.cs`, `PooledObject.cs`, `GameObjectPool.cs`, `IPoolable.cs`
- `Client/Assets/Script/Managers/PoolManager.cs`, `SceneManager.cs`, `InputManager.cs`
- `Client/Assets/Script/Input/InputContext.cs`, `Controller.cs`

입력 시스템 전체는 [architecture.md](architecture.md)가, 스킬·효과 규칙은 [ability-system.md](ability-system.md)가 다룬다. 이 문서는 Actor와 맞닿는 계약만 적는다.

## 수명은 컴포넌트 수명과 스폰 수명 두 겹이다

```
컴포넌트 수명 (1회)   Awake → OnInit  ...............................  OnDestroy
스폰 수명 (대여마다)  Spawn() → OnEnter → [Enable ⇄ Disable] → OnRelease
프레임 (Active일 때)  Update → (스킬 틱) → OnUpdate,  LateUpdate → OnLateUpdate
```

- 컴포넌트 수명 동안 한 번만 만드는 것(`SkillAgent`, `Status`, 재사용할 `InputContext`)은 `OnInit`에서 만든다.
- 스폰 단위 상태는 `OnEnter`에서 초기화하고 `OnRelease`에서 정리한다. 풀은 GameObject와 `ComBaseActor`를 그대로 재사용하므로 이전 스폰의 값이 남지 않게 하는 책임은 이 두 훅에 있다.
- 이벤트 구독은 `Enable`에서 하고 `Disable`에서 해제한다. `Status.ReleaseInternal`과 `SkillAgent.ReleaseInternal`은 구독자가 `Disable`에서 이미 빠졌다고 보고 알림 없이 값을 비운다.

| 훅 | 호출 시점 |
| --- | --- |
| `OnInit` | `Awake`, 또는 초기화가 끝나지 않은 Actor의 `Spawn()` |
| `OnEnter` | `Spawn()`, 상태 `Entering` |
| `Enable` | `Entered`에서 활성화될 때(스폰 직후, `OnEnable`) |
| `Disable` | `Active`에서 비활성화될 때(`OnDisable`, 반납, 파괴) |
| `OnRelease` | 스폰 종료(반납, 파괴). `OnEnter`를 시작한 스폰에만 호출한다 |
| `OnUpdate`, `OnLateUpdate` | `Active`인 동안 매 프레임 |

## 상태

| 값 | 뜻 |
| --- | --- |
| `None` | 스폰하지 않은 상태. 처음 상태이자 스폰 종료가 성공한 뒤의 상태 |
| `Entering` | `OnEnter` 실행 중 |
| `EnterFailed` | `OnEnter`가 실패한 스폰. 활성화와 재스폰을 하지 않고 반납·파괴에서 정리한다 |
| `Entered` | 스폰했지만 비활성. `OnEnter`가 끝났거나 비활성화가 끝난 상태 |
| `Active` | 스폰했고 활성. 입력·프레임·스킬 틱을 받는다 |
| `Deactivating` | 일반 비활성화(`OnDisable`) 진행 중. 콜백에서의 재활성화는 허용하고 반납만 막는다 |
| `Ending` | 풀 반납에 의한 스폰 종료 진행 중 |
| `Faulted` | 비활성화가 중간에 실패한 Actor. 파괴가 스폰을 끝낸다 |
| `EndFaulted` | 스폰 종료가 중간에 실패한 Actor. 파괴에서 다시 정리하지 않는다 |
| `Destroying` | `OnDestroy`의 스폰 종료 진행 중 |
| `Destroyed` | `OnDestroy`가 끝난 상태 |

## 상태 전이표

| 호출 | 진행하는 상태와 결과 | 무시하는 상태 | 예외 |
| --- | --- | --- | --- |
| `Spawn()` | `None` → `Entering` → `Entered` → (활성 계층이면) `Active`. `OnEnter` 실패 시 `EnterFailed` | 없음 | `Destroying`, `Destroyed`는 `ObjectDisposedException`. `Faulted`, `EndFaulted`와 그 밖의 상태는 `InvalidOperationException` |
| `OnEnable` | `Entered`, `Deactivating` → `Active` → `Enable` | 그 밖의 모든 상태 | 없음 |
| `OnDisable` | `Active` → `Deactivating` → `Entered`. 실패 시 `Faulted` | 그 밖의 모든 상태 | 없음 |
| `IPoolable.OnReturn` | `EnterFailed`, `Entered`, `Active` → `Ending` → `None`. 실패 시 `EndFaulted`. `None`, `Destroyed`는 `SpawnSerial`만 올리고 끝낸다 | 없음 | `Entering`, `Ending`, `Destroying`, `Deactivating`, `Faulted`, `EndFaulted`, 스킬·효과·Status 호출 중은 `InvalidOperationException` |
| `OnDestroy` | `Destroying`을 거쳐 항상 `Destroyed`. `Faulted`와 스폰 중인 상태는 스폰 종료를 실행한다 | `None`, `EndFaulted`는 스폰 종료를 건너뛴다 | 스폰 종료의 예외를 전파한다. 상태는 그래도 `Destroyed`가 된다 |
| `RegisterInputContext` | `Active`이면 Controller에도 추가한다. 그 밖의 상태는 목록에만 기록한다 | 이미 등록한 컨텍스트 | `Destroying`, `Destroyed`는 `ObjectDisposedException`. 다른 Controller에 붙은 컨텍스트는 `InvalidOperationException` |
| `Update`의 자동 틱, `TickSkills`, `OnUpdate`, `OnLateUpdate` | `Active` | 그 밖의 모든 상태 | 없음 |

`Spawn()`은 `OnEnter`가 끝난 뒤 상태가 `Entering`이 아니면 활성화하지 않고 돌아간다.

## 초기화와 스폰

`Awake`는 `protected virtual`이다. 파생 클래스가 재정의할 수 있어야 하기 때문이다. 파생 클래스가 `base.Awake()`를 빠뜨리거나 `OnInit`이 실패하면 `initialized`가 false로 남고, 다음 `Spawn()`이 초기화를 다시 실행한다.

- 초기화(`InitializeInternal`)는 `ComPivotAgent`를 가져오고, `skillAgent`와 `status`를 null로 비운 뒤 `OnInit`을 호출한다. 실패한 `OnInit`이 추가한 에이전트가 재초기화에서 중복 추가로 막히지 않게 하려는 것이다.
- `initialized`는 `OnInit`이 끝난 뒤에 true가 된다. 훅이 실패하면 다음 단계로 넘어가지 않는다.
- 초기화가 안 된 Actor가 비활성 계층에 있으면 `Spawn()`은 `InvalidOperationException`을 던진다. 이 경우 `Awake`가 아직 오지 않았으므로 여기서 초기화하면 나중에 `Awake`가 `OnInit`을 한 번 더 부른다.

`Spawn()`의 순서는 다음과 같다.

1. 상태를 검사하고, 초기화가 안 되었으면 초기화한다.
2. `SpawnSerial`을 1 올린다.
3. 상태를 `Entering`으로 바꾸고 `SkillAgent.OpenInternal()`로 스킬 등록을 연다. 스폰 종료가 해제를 보장하는 상태부터 열어야 `OnEnter`에서 스킬을 등록할 수 있다.
4. `OnEnter`를 호출한다. 실패하면 `EnterFailed`로 두고 예외를 전파한다.
5. `Entered`로 바꾸고, `isActiveAndEnabled`이면 활성화한다.

풀에서 꺼낸 Actor의 첫 대여 순서는 `GameObjectPool.Get` → `IPoolable.OnGet`(Actor는 할 일 없음) → `SetActive(true)` → `Awake`·`OnInit` → `OnEnable`(상태 `None`이라 무시) → 호출자의 `Spawn()` → `OnEnter` → `Enable`이다. 재대여는 `Awake`·`OnInit`이 빠진다. `OnGet`은 첫 대여 때 `Awake`보다 먼저 오므로 초기화를 두지 않는다.

스폰 데이터는 파생 클래스의 설정 함수로 넘긴다. 스포너가 그 함수를 부른 뒤 `Spawn()`을 호출한다(`TurnBattleRunner.SpawnUnit`의 `unit.Setup(spec)`). 씬에 직접 배치한 Actor도 배치 담당 스크립트가 `Spawn()`을 호출한다. 자동 스폰 옵션은 없다.

## 활성화와 비활성화

활성화(`ActivateInternal`)는 상태를 `Active`로 바꾸고, 입력 컨텍스트를 Controller에 추가한 뒤 `Enable`을 호출한다. 컨텍스트 추가 중 콜백이 Actor를 비활성화하면 `Enable`을 부르지 않는다.

일반 비활성화(`OnDisable`)는 `Deactivating` 상태로 다음을 실행한다.

1. 모든 입력 컨텍스트를 Controller에서 분리한다.
2. 분리한 컨텍스트의 `onReset`을 호출한다.
3. 활성 스킬을 취소한다(`SkillAgent.CancelActiveSkillsInternal`). 파생 클래스가 `Disable`에서 구독을 해제하기 전에 취소 효과를 받게 하려는 순서다.
4. `Disable`을 호출한다.

콜백(`onReset`, 스킬 취소, `Disable`)이 Actor를 다시 활성화하면 활성화 쪽이 등록을 이어받고, 비활성화는 남은 단계를 건너뛰고 `Active`로 끝난다. 콜백이 재활성화한 뒤 다시 비활성화해 `Entered`로 끝났으면 바깥 비활성화가 남은 단계를 이어받고 반납 거부(`Deactivating`)를 다시 건다.

## 스폰 종료

풀 반납(`IPoolable.OnReturn`)과 파괴(`OnDestroy`)가 스폰을 끝낸다. 둘 다 `EndSpawnInternal`을 지나며, `Ending` 또는 `Destroying` 상태로 진행해 콜백에서의 재활성화를 막는다. 순서는 다음과 같다.

1. `SpawnSerial`을 1 올린다. 스폰하지 않은 동안 출처로 잡힌 효과도 반납·파괴 뒤에 이 Actor의 `Status`를 쓰지 않도록 상태와 관계없이 올린다.
2. 이전 상태가 `None`, `EndFaulted`, `Destroyed`이면 끝낸다.
3. 이전 상태가 `Active`이면 비활성화 단계(컨텍스트 분리, `onReset`, 스킬 취소, `Disable`)를 실행한다.
4. 입력 컨텍스트의 `onReset`을 다시 호출한다. 앞 단계에서 받은 바인딩은 `Delivered`가 지워져 있어 다시 호출되지 않는다.
5. `SkillAgent.ReleaseInternal()`, `Status.ReleaseInternal()` 순서로 기반 모듈을 정리한다. 버프가 건 수정자를 `Status`를 비우기 전에 제거해야 하기 때문이다.
6. `OnRelease`를 호출한다.
7. 반납이면 `None`이 된다. 파괴면 `OnDestroy`의 finally가 `Destroyed`로 바꾸고 남은 입력 컨텍스트를 모두 분리한다.

`OnInit`에서 등록한 입력 컨텍스트는 스폰이 끝나도 Actor에 등록된 채 남고 Controller에서만 분리된다. 최종 분리는 `OnDestroy`에서 한다.

## 실패는 첫 예외를 전파하고 Actor를 격리한다

생명주기 함수는 단계마다 try/finally를 겹치지 않는다. 함수마다 `completed` 플래그 하나를 두고, 예외가 나면 남은 단계를 건너뛴 채 finally에서 상태만 바꾸고 원래 예외를 그대로 전파한다. try/finally를 겹치면 앞 단계가 실패한 상태로 뒤 훅이 실행되고, 뒤에서 난 예외가 첫 예외를 가린다.

| 실패 지점 | 결과 상태 | 정리 |
| --- | --- | --- |
| `OnInit` | 상태 변화 없음, `initialized`는 false | 다음 `Spawn()`이 다시 초기화한다 |
| `OnEnter` | `EnterFailed` | 반납하면 스폰 종료를 실행해 `None`이 된다. 재스폰·활성화는 거부·무시한다 |
| `Enable` | `Active` 유지 | 격리하지 않는다 |
| 비활성화 단계 | `Faulted`. 콜백이 재활성화한 뒤 실패했으면 다시 등록된 입력도 분리한다 | 반납을 거부하고, 파괴가 스폰을 끝낸다 |
| 스폰 종료 단계 | `EndFaulted` | 반납을 거부하고, 파괴에서 다시 정리하지 않는다 |

격리된 Actor는 상태가 오염되었을 수 있으므로 다시 활성화·스폰·반납하지 않는다. 반납이 실패한 객체는 `Pool<T>`의 활성 목록에 남고(`OnReturn` 성공 뒤에만 비활성 목록으로 옮긴다), `PooledObject`도 콜백이 모두 성공해야 GameObject를 비활성화한다. 남은 Actor는 다음 경로에서 파괴된다.

- `Global.Pool.Destroy(obj)`. `Pool<T>.Remove`가 `OnReturn` 예외와 관계없이 finally에서 파괴한다.
- Scope 정리(`PoolManager.ClearScope`, `PoolManager.Clear`). `Pool<T>.ClearInternal`이 객체마다 `OnReturn`과 파괴를 시도하고 예외를 모아 마지막에 던진다.

Actor 생명주기에는 롤백이나 오류 집계용 catch를 두지 않는다. 오류 보고는 Unity 실행 경계나 씬 종료 경계가 한 번 한다. 오류를 모으는 `ErrorCollector`는 풀·Scope 정리처럼 여러 객체를 한꺼번에 정리하는 경계에서만 쓴다.

## 호출 중 반납은 거부한다

다음 상황에서 `IPoolable.OnReturn`은 `InvalidOperationException`을 던진다. 진행 중인 처리가 반납된 객체나 다음 스폰에 상태를 남기기 때문이다.

- 스폰 전환 중(`Entering`, `Ending`, `Destroying`). 예를 들어 `OnEnter`나 `OnRelease` 안에서 자기 자신을 반납하는 경우다.
- 일반 비활성화 중(`Deactivating`). `Disable`이나 `onReset` 안에서 반납하는 경우다.
- 스킬·효과 호출 중(`SkillAgent.IsInCall`). 스킬 틱, 스킬 취소, 효과 콜백이 여기에 든다.
- Status 알림 중(`Status.IsInCall`). `OnStatChanged`, `OnResourceChanged` 구독자가 반납하는 경우다.

반납은 호출이 끝난 뒤에 한다. 스킬 틱 뒤에 오는 `OnUpdate`나, 스킬 호출이 반환된 뒤가 그 자리다.

## 틱과 OnUpdate의 실패는 격리하지 않는다

`ComBaseActor.Update`는 `Active`일 때 스킬을 먼저 틱하고(`AutoTickSkills`가 true일 때), 상태가 여전히 `Active`이면 `OnUpdate`를 호출한다. 만료와 쿨다운이 반영된 뒤 `OnUpdate`가 판단하게 하려는 순서다. 틱 중 비활성화·반납되면 `OnUpdate`를 호출하지 않는다.

틱과 `OnUpdate`가 실패해도 Actor는 `Active`로 남는다. 다른 Actor에 건 효과의 실패도 이 경로로 올라오므로, 여기서 격리하면 실패하지 않은 Actor가 격리된다. 남은 태그와 수정자는 반납·파괴의 해제가 정리하고, 보고는 Unity 실행 경계가 한다.

턴제처럼 프레임 시간과 다른 단위로 효과와 쿨다운을 진행하려면 `AutoTickSkills`를 false로 재정의하고 `TickSkills(deltaTime)`을 직접 호출한다(`TurnUnit`). `TickSkills`도 `Active`일 때만 진행하므로 비활성·반납된 Actor의 효과와 쿨다운은 흐르지 않는다.

## 스킬 시스템과의 연결 지점

- `AddSkillAgent()`와 `AddStatus()`는 `OnInit`에서 한 번 호출한다. 두 번 호출하면 `InvalidOperationException`이다.
- `ComBaseActor`는 `IAbilityOwner`를 구현해 `SkillAgent`, `Status`, `SpawnSerial`을 노출한다. 효과는 잡아 둔 시전자가 반납·재스폰·파괴되었는지 `SpawnSerial`의 변화로 판단하므로, 이 값은 스폰 시작과 모든 스폰 종료(파괴 포함)에서 오른다.
- 스킬 등록은 `Spawn()`에서 열리고 `SkillAgent.ReleaseInternal()`에서 닫힌다. 닫힌 동안의 `GiveSkill`, `ActivateSkill`, `ApplyEffect` 처리는 ability-system.md를 따른다.

자세한 내용은 [ability-system.md](ability-system.md)를 본다.

## 입력 컨텍스트

Actor는 `RegisterInputContext`, `UnregisterInputContext`로 컨텍스트를 등록한다. 컨텍스트가 실제로 입력을 받는 것은 Actor가 `Active`이고 Controller에 붙어 있는 동안이다.

- 활성화는 `Global.Input.AddContext(context)`로 추가한다. `InputManager`는 첫 비동기 대기 전에 Ready가 되므로 Actor 활성화 시점에는 이미 Ready다.
- 비활성화와 스폰 종료는 `Global.Input`을 조회하지 않고 `context.Owner`(컨텍스트가 붙은 Controller)에서 제거한다. 종료 중 `InputManager`가 먼저 Controller를 해제했으면 컨텍스트가 이미 분리되어 있어 아무 일도 하지 않는다.
- `InputManager.Controller`와 `InputContext.Owner`는 internal이다. 게임 코드는 `Global.Input.AddContext`, `Global.Input.RemoveContext`, `InputContext.Bind`, `InputContext.Unbind`만 쓴다.
- 등록은 Controller에 먼저 추가한 뒤 Actor 목록에 기록한다. Controller의 검증이 실패해도 Actor에 등록 흔적이 남지 않는다.
- 다른 Controller에 붙은 컨텍스트(`context.Owner != null`)는 거부한다. 비활성 Actor가 가진 컨텍스트는 Controller에 붙어 있지 않으므로 공유를 거부하지 못한다. 컨텍스트를 Actor끼리 공유하지 않는다.
- Actor의 컨텍스트 목록은 수정하지 않고 교체한다(copy-on-write). Controller나 `onReset` 콜백 중에 등록·해제해도 순회 중인 스냅샷이 유효하다.

컨텍스트를 어디서 만드느냐에 따라 등록 수명이 정해진다.

| 만드는 곳 | 등록 수명 | 해제 |
| --- | --- | --- |
| `OnInit` | 컴포넌트 수명. 스폰이 끝나면 Controller에서만 분리된다 | `OnDestroy`가 자동으로 분리한다 |
| `OnEnter` | 그 스폰 | 같은 스폰의 `OnRelease`에서 `UnregisterInputContext`를 호출한다. 같은 인스턴스의 중복 등록은 무시되지만, 스폰마다 새로 만든 컨텍스트는 누적된다 |

### 상태 변경을 먼저 끝내고 콜백은 마지막에 실행한다

분리·등록·플래그 같은 상태 변경을 모든 대상에 먼저 끝내고, 사용자 콜백(`onReset`, 훅)은 마지막에 실행한다. 첫 예외는 원래 타입과 스택 그대로 전파하고 남은 콜백은 건너뛴다. Actor 비활성화는 모든 컨텍스트를 분리한 뒤 리셋하고, `Controller.Release`는 모든 컨텍스트를 분리한 뒤 리셋한다. 그래서 한 컨텍스트의 `onReset`이 실패해도 나머지 컨텍스트가 비활성화된 Actor에 입력을 전달하지 않는다.

### onReset은 멱등이어야 한다

`onReset`은 입력으로 쌓인 값을 지우는 용도다(이동 벡터를 0으로, 차지 여부를 false로). 예외로 건너뛴 바인딩은 `Delivered`가 남아 그 컨텍스트의 다음 전체 리셋에서 `onReset`을 받으므로, 늦게 한 번 더 호출되어도 안전해야 한다. Actor 해제나 등록 해제로 버려진 컨텍스트는 늦은 리셋을 보장하지 않는다.

### 게임 입력 차단도 onReset으로 온다

`InputManager`는 Controller의 입력 허용을 다음 식으로 정한다.

```
게임 입력 = 씬 허용 && !UI 포커스 && !장치 분실 && !장치 전환 프레임
```

허용이 false가 되면 Controller가 액션을 끄고 등록된 모든 컨텍스트를 리셋한다. Actor는 `Active`인 채로 `onReset`을 받는다. 장치 분실은 페어링된 장치가 하나도 남지 않았을 때만이고, 분실 중에는 게임 입력만 막히고 UI 조작은 유지된다. 분실과 복구는 `NotificationId.InputDeviceLost`, `NotificationId.InputDeviceRegained`로 알린다.

## 풀과 씬 Scope

씬 수명 Actor는 씬 Scope 풀에서 빌린다. `SceneManager`는 모든 씬 종료 경로(`StopEntryInternal`)에서 `SceneBase.Stop()` 직후 `Global.Pool.ClearScope(scope)`를 호출해 그 씬 Scope의 풀을 동기적으로 정리한다. 씬 Scope는 언로드 뒤에 해제되므로, 이 정리가 없으면 그 사이 씬 Scope 풀의 Actor가 입력과 프레임 처리를 계속한다. `Stop()`이 실패해도 풀 정리는 실행하고 두 예외를 씬 종료 경계로 함께 전달한다.

`ClearScope`의 동작은 다음과 같다.

1. Scope를 닫힘으로 표시한다. Scope가 해제될 때까지 그 Scope로의 새 풀 생성과 대여는 `InvalidOperationException`으로 거부된다. 정리 중 `OnRelease`에서의 대여도 여기에 든다.
2. 풀마다 목록에서 뺀 직후 `GameObjectPool.Dispose()`를 호출한다. 대여 중인 Actor는 `OnReturn`(스폰 종료) 뒤 파괴되고, 대기 중인 Actor는 바로 파괴된다.
3. 이미 해제된 Scope면 닫지 않고 남은 풀만 정리한다.

App Scope(`Global.Resource.AppScope`) 풀에서 빌린 Actor는 씬이 정지해도 반납되지 않는다. 소유자가 직접 반납하거나(`TurnBattleRunner.ClearUnits`) `PoolManager` 종료 때 정리된다. 씬 수명 Actor를 App Scope로 빌려 생기는 문제는 사용 코드의 버그로 본다. 씬에 직접 배치한 Actor는 씬 언로드 때 파괴되며, 씬 정지부터 언로드까지 멈추는 계약은 없다.

### 풀 콜백 안에서 같은 풀을 바꾸지 않는다

`Pool<T>`는 `Get`, `Return`, `ReturnAll`, `Remove`, `Clear`, `Dispose` 실행 중 같은 풀을 다시 바꾸면 `InvalidOperationException("Pool callbacks cannot change the same pool.")`을 던진다. Actor 쪽에서는 반납 경로의 `Disable`, `onReset`, `OnRelease`가 이 범위에 든다. 이 안에서 같은 원본의 같은 Scope 풀에 속한 다른 객체를 `Return`, `Get`, `Destroy`하면 재진입 예외가 난다. 다른 풀은 바꿀 수 있다. 연쇄 반납이 필요해지면 지연 반납을 따로 설계한다.

`GameObjectPool.Get`은 `Pool<T>.Get`이 끝난 뒤 `SetActive(true)`를 호출하므로 `Awake`, `OnInit`, `OnEnable`은 이 제약 밖이다. 대신 활성화 중 그 객체가 반납·파괴되면 `Get`이 `InvalidOperationException`을 던진다.

### 종료 순서

매니저는 등록의 역순으로 종료한다. 등록 순서는 `Global.Managers.cs`의 `Input`, `Patch`, `Resource`, `Notification`, `Data`, `Pool`, `Scene`, `UI`, `Sound`다. Actor에 관련된 순서는 다음과 같다.

1. `SceneManager`가 씬을 정지하고 씬 Scope 풀을 정리한다. 이때 `PoolManager`는 아직 Ready라 `Global.Pool`로 조회한다.
2. `PoolManager.Clear()`가 남은 모든 풀(App Scope 포함)을 해제한다.
3. `InputManager`가 Controller를 해제한다. 모든 컨텍스트를 분리한 뒤 리셋한다.

`Object.Destroy`는 프레임 끝에 실행되므로 Actor의 `OnDestroy`는 Controller 해제 뒤에 올 수 있다. 그래서 Actor는 종료 경로에서 `Global.Input`을 조회하지 않는다. 종료 토큰이 취소된 뒤에는 새 풀 생성과 대여를 시작하지 않는다.

## 설계 결정

### 로직 객체와 컴포넌트를 나누지 않는다

예전 구조는 POCO 로직(`BaseActor`)과 MonoBehaviour(`ComBaseActor`)를 나눴다. 분리가 주던 이점이 실제로 쓰이지 않아 하나로 합쳤다.

| 분리 구조의 이점 | 버린 근거 |
| --- | --- |
| 같은 몸체에서 로직 교체 | 실제 요구가 없었다. 샘플의 교체 버튼에만 쓰였다 |
| Unity와 무관한 로직 실행 | 로직이 이미 `Global.Input`과 `SceneBase`에 의존했다 |
| 서버 통신 경계 | 참고 프로젝트(UC, R.O.A.D) 모두 서버 동기화는 데이터 클래스와 매니저 계층이 맡았고, 로직 객체는 경계가 아니었다 |
| 입력 스키마 분리 | project-wide Input Actions 하나를 공유하고 `"Map/Action"` 경로로 바인딩하므로 GameObject 활성 수명을 따르는 편이 맞다 |
| 대여마다 새 상태 | 유일한 실질 이점이었다. `OnEnter`/`OnRelease`에서 스폰 단위 상태를 초기화하는 계약으로 대신한다 |

합친 뒤로 소유자 재검사(`IsCurrentInternal`)가 사라지고, 훅 뒤의 확인은 `ActorState` 비교만 남았다.

### 참고 프로젝트에서 가져온 규칙

- 하위 시스템은 소유 Actor만 안다. 형제 시스템 참조는 Actor가 연결한다.
- 캐릭터별 차이는 서브클래스와 데이터로 표현한다. 공통 코드에 타입·이름 분기를 두지 않는다.
- 테이블 행은 읽기 전용이다. 런타임 값은 Actor가 소유한다.
- 이벤트 구독과 해제는 `Enable`/`Disable`에서 짝으로 처리한다.
- Actor 클래스는 리플렉션 문자열(`Type.GetType`)이 아니라 프리팹에 붙은 컴포넌트로 정한다.
- 프레임 처리는 GameObject별 `ComBaseActor.Update`가 한다.
- 검증 실패는 예외로 전파한다. 로그 후 진행하거나 null 체크 후 조용히 돌아가지 않는다.

### 입력과 서버 명령은 행동 함수로 모은다

InputContext 콜백은 Actor의 행동 함수(이동, 점프 같은)를 호출하는 데서 끝낸다. AI나 서버 명령도 같은 함수를 호출하게 해 입력 경로가 늘어도 Actor 구조가 바뀌지 않게 한다. 서버 동기화는 아직 없다. 들어오면 동기화 대상 상태는 Actor를 참조하지 않는 C# 데이터 클래스로 두고, 처리는 매니저 계층(패킷 → 매니저 → Actor의 데이터 갱신 또는 행동 함수 호출)에 둔다. 개별 Actor는 패킷을 직접 처리하지 않는다.

### 하지 않을 것

- 공통 모듈 인터페이스나 등록 목록 같은 모듈 프레임워크를 먼저 만들지 않는다. 모듈은 Actor가 필드로 소유한다(`SkillAgent`, `Status`).
- 스킬 실행, 데미지, 연출, UI를 Actor 한 클래스에 모으지 않는다.
- 서버가 들어오기 전에 동기화용 데이터 클래스나 빙의(Controller) 계층을 추가하지 않는다.
- `SceneBase`나 Actor에 `Global.Pool`, `Global.Resource` 기능을 다시 노출하는 전달 함수를 두지 않는다.
- 스폰 데이터용 제네릭 `ComBaseActor<TData>.Spawn(TData)`는 사용 근거가 없어 두지 않는다.
