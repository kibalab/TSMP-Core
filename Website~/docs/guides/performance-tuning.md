---
title: Performance Tuning
---

# Performance tuning

Tune TSMP by reducing payload first, then adjusting frame rate and texture size.

Visual smoothness alone is not enough to diagnose performance. A payload issue, frame-loss issue, and interpolation issue can look similar.

## Start with the debug canvas

Watch:

- `payload`: how full each frame is.
- `msg`, `var`, `rpc`: how much data is being written.
- `loss`: how many frames the receiver appears to miss.
- `valid` and `header`: whether decode is stable.
- Last error text.

Do not tune by visual smoothness alone. A transport issue and a payload issue can look similar.

## Reduce payload first

| Source | Action |
| --- | --- |
| Transform sync | Sync only objects that need it. |
| Humanoid pose | Select fewer bones when possible. |
| VRChat avatar pose | Use full mode only when the receiver needs full pose data. |
| Blend shapes | Select only visible or gameplay-relevant shapes. |
| Animator | Sync only parameters that affect visible state. |
| Timeline | Sync only directors that need to match the sender. |

Payload reduction is usually the best first optimization because it reduces encode work, transport bandwidth, decode work, and apply work at the same time.

## Then tune frame rate

Higher frame rate can reduce latency, but it increases Udon and texture work.

If the receiver or OBS path cannot keep up, a higher encoder frame rate can make motion look worse by increasing skipped frames.

Recommended approach:

1. Start at 30 FPS.
2. Confirm loss is low.
3. Raise frame rate only if motion needs it.
4. Stop raising frame rate when loss or CPU cost increases.

## Then tune capacity

If `Payload buffer is full` appears:

1. Reduce synchronized data.
2. Increase output texture capacity.
3. Re-run `Apply Setup`.
4. Confirm payload is below usable capacity.

Avoid solving every capacity issue by increasing resolution. Larger textures cost more to render, capture, and decode.

## Interpolation choice

Use per-component receive interpolation intentionally:

| Mode | Cost | Visual result |
| --- | --- | --- |
| None | Lowest | Received values are ignored. |
| Discrete | Low | Values snap to received state. |
| Continuous | Higher | Supported components smooth toward targets. |

Continuous mode can hide transport delay, but it cannot fix missing data. If loss is high, fix loss first.

## Multiple players

When player count increases, payload can grow quickly, especially with avatar pose sync.

If data disappears when another player joins:

- Check payload capacity.
- Check whether each player creates additional avatar pose data.
- Disable sync components that are not needed for remote players.
- Reduce avatar pose details before increasing texture resolution.
- Test with the debug canvas visible before optimizing visuals.

## Practical tuning order

1. Confirm valid decode with a low-bandwidth toggle.
2. Add transform sync.
3. Add one high-bandwidth feature at a time.
4. Watch payload usage after each addition.
5. Tune receive interpolation after transport and capacity are stable.
