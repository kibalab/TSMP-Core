---
title: TSMP 동작 방식
---

# TSMP 동작 방식

TSMP는 씬 데이터를 픽셀로 바꾸고, 그 픽셀을 텍스처 또는 영상 경로로 보낸 뒤, 다시 씬 변경으로 되돌립니다.

같은 구조가 Unity 에디터, Play 모드, 업로드된 VRChat 월드에서 모두 사용됩니다. 위험한 부분은 전송 경로입니다. 픽셀이 바뀌면 데이터도 바뀝니다.

## 송신자

송신자 쪽에서 `TSMPEncoder`는 활성화된 TSMP 네트워크 컴포넌트를 찾습니다. 각 컴포넌트는 인코더가 읽기 전에 현재 상태를 하나 이상의 `[TransSync]` 필드에 복사합니다.

인코더는 다음 작업을 합니다.

1. Variable state message와 queued RPC message로 네트워크 페이로드를 만듭니다.
2. Frame index, codec ID, payload size, CRC가 포함된 56바이트 TSMP 헤더를 씁니다.
3. 선택된 코덱에 헤더와 페이로드를 전달합니다.
4. 결과 픽셀을 출력 렌더 텍스처에 씁니다.

일반적으로 이 과정을 직접 수정할 필요는 없습니다. Sync 컴포넌트를 추가하거나 제거한 뒤 `Apply Setup`을 실행하세요.

## 전송 경로

인코딩된 텍스처는 표시, 캡처, 스트리밍, 루프백될 수 있습니다.

일반적인 전송 경로:

- VRChat 카메라가 보는 screen-space TSMP 출력.
- Spout에서 OBS로 전달.
- OBS 출력을 다시 수신 텍스처로 입력.
- 테스트용 로컬 렌더 텍스처 경로.

전송 경로는 TSMP 영역을 보존해야 합니다. 색 보정, 압축, 스케일링, 필터링, 안티앨리어싱, 포스트 프로세싱은 인코딩 영역에 적용하지 마세요.

## 수신자

수신자 쪽에서 `TSMPDecoder`는 입력 텍스처를 읽습니다. 먼저 프레임 헤더를 검증하고, 헤더가 유효하며 CRC가 일치하면 페이로드 바이트를 디코딩해 메시지를 전달합니다.

수신자는 다음 정보를 사용합니다.

- Network ID로 올바른 `TSMPNetworkBehaviour`를 찾습니다.
- Variable hash로 올바른 `[TransSync]` 필드를 찾습니다.
- RPC hash와 method name으로 TSMP RPC 이벤트를 실행합니다.

따라서 송신자와 수신자 씬은 일관되게 설정되어야 합니다. `TSMPSetup`이 이 ID들을 맞추는 런타임 바인딩 테이블을 만듭니다.

## 실패 경계

TSMP는 잘못된 데이터를 안전하게 버리도록 설계되어 있습니다.

- Invalid header: 프레임을 버립니다.
- CRC mismatch: 프레임을 버리고 경고 로그를 남깁니다.
- Malformed payload: 페이로드 처리를 중단하고 diagnostics를 갱신합니다.
- Missing binding: 해당 값이나 RPC를 적용할 수 없습니다.

이 때문에 debug canvas가 중요합니다. 오브젝트 움직임만 보고 추측하지 않고 어느 단계가 실패했는지 볼 수 있습니다.
