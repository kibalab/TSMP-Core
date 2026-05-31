**한국어** | [English](README.en.md) | [日本語](README.ja.md)

---

# VRChat VPM Package Template for KIBALAB

VRChat Creator Companion(VCC) / VRChat Package Manager(VPM) 패키지 배포를 위한 템플릿입니다.

**태그(릴리스)를 푸시하면** GitHub Actions가 자동으로:
1. Release 생성 (zip + unitypackage + package.json)
2. VPM 백엔드에 패키지 정보 등록
3. 즉시 [vpm.kiba.red](https://vpm.kiba.red)에 반영

---

## 요구 사항

### 1) 패키지 구조 (UPM/VPM 표준)

```
Packages/<PACKAGE_ID>/
├── package.json
├── Runtime/
├── Editor/
└── package-media/        # (선택) 썸네일 이미지
    └── thumbnail.png
```

예시:
```
Packages/com.kibalab.mypackage/package.json
```

### 2) package.json 필수 필드

```json
{
  "name": "com.kibalab.mypackage",
  "displayName": "My Package",
  "version": "1.0.0",
  "description": "패키지 설명",
  "author": {
    "name": "Your Name",
    "email": "your@email.com",
    "url": "https://your-site.com"
  },
  "vpmDependencies": {
    "com.vrchat.worlds": "3.x.x"
  }
}
```

---

## 설정 방법

### 1) Repository Variables

GitHub 저장소 → **Settings** → **Secrets and variables** → **Actions** → **Variables**

| Variable | 설명 | 예시 |
|----------|------|------|
| `PACKAGE_NAME` | 패키지 폴더 이름 | `com.kibalab.mypackage` |
| `VPM_BACKEND_URL` | VPM 백엔드 URL | `https://vpm.kiba.red` |

### 2) Repository Secrets

GitHub 저장소 → **Settings** → **Secrets and variables** → **Actions** → **Secrets**

| Secret | 설명 |
|--------|------|
| `VPM_API_KEY` | VPM 백엔드 API 키 (관리자에게 문의) |

---

## 사용 방법

### 새 패키지 생성

1. **Use this template**로 새 저장소 생성
2. `Packages/` 폴더 아래에 패키지 ID로 폴더 생성
3. `package.json` 작성
4. Repository Variables/Secrets 설정

### 릴리스 배포

1. `package.json`의 `version` 업데이트
2. 커밋 & 푸시
3. 같은 버전으로 태그 생성 & 푸시

```bash
# 버전 업데이트 후 커밋
git add Packages/com.kibalab.mypackage/package.json
git commit -m "Bump version to 1.0.1"
git push

# 태그 생성 및 푸시
git tag 1.0.1
git push origin 1.0.1
```

> 태그 버전과 package.json 버전이 일치해야 합니다. (`v1.0.1` 또는 `1.0.1` 형식 모두 지원)

---

## 썸네일 이미지

VPM 프론트엔드에 표시될 썸네일을 설정할 수 있습니다.

### 방법 1: 패키지 내 썸네일 (권장)
```
Packages/<PACKAGE_ID>/package-media/thumbnail.png
```

### 방법 2: 저장소 루트 썸네일
```
.github/vpm-thumbnail.png
```

**권장 사양:**
- 형식: PNG
- 크기: 512x512 또는 16:9 비율
- 용량: 500KB 이하

---

## 워크플로우 구조

### Reusable Workflow (중앙 관리)

모든 패키지 레포가 `vpm-package-template`의 워크플로우를 참조합니다.
중앙 워크플로우를 수정하면 **모든 패키지 레포에 자동 적용**됩니다.

```
vpm-package-template/.github/workflows/
├── vpm-release.yml    # 재사용 가능한 워크플로우 (실제 로직)
└── release.yml        # 호출 예시

각 패키지 레포/.github/workflows/
└── release.yml        # 중앙 워크플로우 호출 (16줄)
```

### 각 패키지 레포의 release.yml

```yaml
name: Build Release

on:
  workflow_dispatch:
  push:
    tags:
      - '*'

permissions:
  contents: write

jobs:
  release:
    uses: kibalab/vpm-package-template/.github/workflows/vpm-release.yml@main
    with:
      package_name: ${{ vars.PACKAGE_NAME }}
      vpm_backend_url: ${{ vars.VPM_BACKEND_URL || 'https://vpm.kiba.red' }}
    secrets:
      VPM_API_KEY: ${{ secrets.VPM_API_KEY }}
```

### 워크플로우 동작

1. **빌드**
   - `Packages/<PACKAGE_NAME>` 폴더를 ZIP으로 압축
   - `.unitypackage` 파일 생성

2. **GitHub Release 생성**
   - ZIP, unitypackage, package.json 첨부

3. **VPM 백엔드 등록**
   - 패키지 정보를 백엔드 API로 전송
   - 썸네일 URL 자동 감지 및 등록

---

## 문제 해결

### 워크플로우 실패: "Tag does not match version"
- `package.json`의 `version`과 Git 태그가 일치하는지 확인
- 태그는 `1.0.0` 또는 `v1.0.0` 형식 모두 가능

### 패키지가 목록에 표시되지 않음
- GitHub Actions 로그에서 백엔드 응답 확인
- `VPM_BACKEND_URL`과 `VPM_API_KEY` 설정 확인
- 백엔드 관리자에게 API 키 유효성 문의

### 썸네일이 표시되지 않음
- 파일 경로가 정확한지 확인
- 이미지가 `main` 브랜치에 푸시되어 있는지 확인
- Raw URL 접근 가능 여부 확인

---

## 관련 링크

- [VPM 패키지 목록](https://vpm.kiba.red)
- [VCC에 추가하기](https://vpm.kiba.red/vcc)
