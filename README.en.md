[한국어](README.md) | **English** | [日本語](README.ja.md)

---

# VRChat VPM Package Template for KIBALAB

Template for distributing VRChat Creator Companion (VCC) / VRChat Package Manager (VPM) packages.

**When you push a tag (release)**, GitHub Actions automatically:
1. Creates a Release (zip + unitypackage + package.json)
2. Registers package information with the VPM backend
3. Immediately reflects on [vpm.kiba.red](https://vpm.kiba.red)

---

## Requirements

### 1) Package Structure (UPM/VPM Standard)

```
Packages/<PACKAGE_ID>/
├── package.json
├── Runtime/
├── Editor/
└── package-media/        # (Optional) Thumbnail image
    └── thumbnail.png
```

Example:
```
Packages/com.kibalab.mypackage/package.json
```

### 2) Required package.json Fields

```json
{
  "name": "com.kibalab.mypackage",
  "displayName": "My Package",
  "version": "1.0.0",
  "description": "Package description",
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

## Setup Instructions

### 1) Repository Variables

GitHub repository → **Settings** → **Secrets and variables** → **Actions** → **Variables**

| Variable | Description | Example |
|----------|-------------|---------|
| `PACKAGE_NAME` | Package folder name | `com.kibalab.mypackage` |
| `VPM_BACKEND_URL` | VPM backend URL | `https://vpm.kiba.red` |

### 2) Repository Secrets

GitHub repository → **Settings** → **Secrets and variables** → **Actions** → **Secrets**

| Secret | Description |
|--------|-------------|
| `VPM_API_KEY` | VPM backend API key (contact admin) |

---

## Usage

### Creating a New Package

1. Create a new repository using **Use this template**
2. Create a folder under `Packages/` with your package ID
3. Write `package.json`
4. Configure Repository Variables/Secrets

### Deploying a Release

1. Update `version` in `package.json`
2. Commit & push
3. Create and push a tag with the same version

```bash
# After updating version, commit
git add Packages/com.kibalab.mypackage/package.json
git commit -m "Bump version to 1.0.1"
git push

# Create and push tag
git tag 1.0.1
git push origin 1.0.1
```

> Tag version must match package.json version. (Both `v1.0.1` and `1.0.1` formats supported)

---

## Thumbnail Image

You can set a thumbnail to be displayed on the VPM frontend.

### Method 1: Package Thumbnail (Recommended)
```
Packages/<PACKAGE_ID>/package-media/thumbnail.png
```

### Method 2: Repository Root Thumbnail
```
.github/vpm-thumbnail.png
```

**Recommended Specifications:**
- Format: PNG
- Size: 512x512 or 16:9 ratio
- File size: Under 500KB

---

## Workflow Structure

### Reusable Workflow (Centrally Managed)

All package repos reference workflows from `vpm-package-template`.
Modifying the central workflow **automatically applies to all package repos**.

```
vpm-package-template/.github/workflows/
├── vpm-release.yml    # Reusable workflow (actual logic)
└── release.yml        # Usage example

Each package repo/.github/workflows/
└── release.yml        # Calls central workflow (16 lines)
```

### release.yml in Each Package Repo

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

### Workflow Process

1. **Build**
   - Compress `Packages/<PACKAGE_NAME>` folder to ZIP
   - Generate `.unitypackage` file

2. **Create GitHub Release**
   - Attach ZIP, unitypackage, package.json

3. **Register with VPM Backend**
   - Send package information to backend API
   - Automatically detect and register thumbnail URL

---

## Troubleshooting

### Workflow Failed: "Tag does not match version"
- Check that `package.json` `version` matches Git tag
- Tags can be in `1.0.0` or `v1.0.0` format

### Package Not Showing in Listing
- Check backend response in GitHub Actions logs
- Verify `VPM_BACKEND_URL` and `VPM_API_KEY` settings
- Contact backend admin about API key validity

### Thumbnail Not Displayed
- Verify file path is correct
- Check that image is pushed to `main` branch
- Verify Raw URL is accessible

---

## Related Links

- [VPM Package Listing](https://vpm.kiba.red)
- [Add to VCC](https://vpm.kiba.red/vcc)
