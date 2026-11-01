# Main integration validation

## Reliability integration, 2026-11-01

The validated reliability work is merged into local main in all five TSMP
repositories. Each integration is a two-parent merge of the freshly fetched
origin/main and codex/tsmp-reliability, with exactly the same tree as the tested
reliability revision. No conflict resolution or product edits were needed.

| Repository | Integrated local main | New working branch |
| --- | --- | --- |
| Core | `42bca3d` | `codex/tsmp-optimization` |
| Luma4 | `a1be05e` | `codex/tsmp-optimization` |
| Color256 | `acc6865` | `codex/tsmp-optimization` |
| RGB16 | `e53bc76` | `codex/tsmp-optimization` |
| RGB20 | `cf44add` | `codex/tsmp-optimization` |

Core's prior local main (`b205273`) has unrelated legacy history. It is preserved
as `archive/legacy-main-before-reliability-20261101`; the current main starts from
the canonical origin/main (`180646f`) before integrating reliability. Existing
reliability branches and other worktrees are retained. No push or release was
performed; live remote main refs remain unchanged.

The independent merge audit passes **56 checks across five repositories**:
parent order, source-tree equality, branch origins, preserved reliability refs,
legacy-main backup and unchanged live remote refs. Evidence is retained under
`F:/Unity/TSMP/Validation-Results/optimization-focus-20261101`, including
`git-before.json`, `git-after.json` and `independent-merge-audit.json`.

Because all merged trees are byte-identical to the previously tested revisions,
the merge does not claim a new runtime performance result. Current pre-integration
automatic checks and their scope are recorded in
[WorkloadFollowup](WorkloadFollowup/RESULTS.md#product-finishing-follow-up).
New optimization experiments and any accepted changes stay on the new branches.

## Earlier integration, 2026-09-11

Validated on 2026-09-11 while merging `release` at `7d0b352` into `main` at `820715a` (PR #1, humanoid Continuous interpolation).

## Merge resolution

- Retained PR #1's world-to-local rotation targets and direct/indirect received-ancestor lookup.
- Retained release's Animator/Avatar replacement and same-length bone selection cache invalidation.
- Regenerated the humanoid Udon field metadata from the merged source through full UdonSharp compilation. Kept the original source and serialized program asset references; discarded validation-only generated GUID changes elsewhere.
- Added a regression group to `ConfigurationValidation` for local targets, root rotation ownership, unchanged ancestor cache reuse, same-size received bone set changes, skipped parents and rotated replacement rigs.

## Environment

- Core: actual `F:/Unity/TSMP/TSMP-Core-main/Packages/com.kibalab.tsmp.core` worktree, `0.2.0-beta.1` unchanged.
- Unity 2022.3.22f1; real Luma4 0.0.3-beta.3 from `TSMPCodec-Luma4-UnitySupport`.
- Native project: `F:/Unity/TSMP/Validation-NoSDK`, without VRCSDK/UdonSharp.
- SDK project: `F:/Unity/TSMP/Validation-VRC`, Worlds 3.10.4-beta.2 and bundled UdonSharp.
- Windows x64 Player: Mono, managed stripping disabled; RTX 4090, Direct3D11.

## Results

Evidence is under `F:/Unity/TSMP/Validation-Results/merge-main-20260911/`.

| Check | Result | Evidence |
| --- | --- | --- |
| Native configuration and merge regressions | PASS, nine groups | `native/20260911-175318-Configuration-result.txt` |
| Full Udon client compilation, VM codec/lookup checks and configuration regressions | PASS | `vrc/20260911-175337-Configuration-result.txt` |
| Editor GPU loopback | PASS, nine frames | `native/20260911-175415-Play-result.txt` |
| Windows x64 Mono build | Succeeded, zero errors, one warning | `native/20260911-175437-Build.log` |
| Built Player GPU loopback | PASS, nine frames | `native/20260911-175504-Player-result.txt` |

Matching `.log` files accompany the result files. The executable is `F:/Unity/TSMP/Validation-NoSDK/Build/Mono/TSMPValidation.exe`, with its sibling `TSMPValidation.build-report.txt`. The loopback checks Transform, AnimationClip-driven humanoid bones/root, Component int/Unicode fields, repeated/cross-stream RPCs, and blank input rejection.

The added humanoid configuration tests execute the C# component path in both projects; they are not a VRChat client playback test. Full Udon client bytecode compilation passes, but live VRChat playback, SDK world build and IL2CPP were not repeated for this merge. The one Player build warning is the existing `SampleBlockLuma` shader warning. No package version, release tag or deployment was created.

## Reproduce

Use `Validation~/Run-Validation.ps1` from the merged worktree with `-UnityEditor`, `-Project` and `-Results`:

- Native: `-Step Configuration`, `-Step Play`, `-Step Build`, `-Step Player`.
- SDK: `-Step Configuration` (includes full compilation with `IsEditorBuild=false`).

Both validation manifests reference the merged main package, not the release worktree or an edited copy.
