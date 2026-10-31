# Payload diagnostics and default refresh, 2026-11-01

The Encoder now exposes which field was most recently deferred, its network ID
and minimum complete-packet size, whether it fits alone, and how many deferred
fields cannot fit alone. A blocked FIFO RPC exposes its network ID/hash and
minimum complete-packet size. The Inspector compares these with the existing
captured effective budget.

`deferredVariableCount > 0` and `deferredRpcCount > 0` establish whether their
corresponding identities are valid. Diagnostics reset on each real encode
attempt, including early failures. A field that fits alone is described as
deferred by other data; it is not mislabeled permanently oversized. Required
bytes include the eight-byte network header and each message/entry header.
Dynamic fields can become sendable after shrinking or increasing the budget.

The implementation reuses the existing serialized entry length and the RPC
writer's existing ending offset. It adds no serialization pass, queue scan,
per-frame log, automatic budget adjustment or timestamp arrays. Default-zero
Udon RPC handling keeps its previous short-circuit and performs no added UTF-8
size computation. Transmission scheduling, retry defaults and repeat consumption
are unchanged.

## Validation

Evidence: `F:/Unity/TSMP/Validation-Results/next-improvements-20261101/budget-recovery`.

- `native-final`: **12,419 assertions PASS**, including actual Native
  `EncodeNow` diagnostics and six deterministic refresh/application scenarios.
- `vrc-client-final`: **131 assertions PASS**, actual freshly compiled client
  Encoder/source VMs, recompiled after Play entry. Sixteen product programs have
  source and bytecode receipts in `client-programs.csv`.
- `vrc-standard`: **16 product programs PASS, Bindings=True** using the existing
  full compilation/setup check.

The final Encoder bytecode is 139,520 bytes, SHA-256
`90c142e388c90447ec92e9a670c11544034b78ab79a53bf5d13d90c1bf308b8c`.
This size is not a runtime speed measurement. The three captured Native/SDK
configuration files and four edited product sources match before/after each
final targeted run.

## Unchanged-state recovery

All three fields change at simulated time 0.5 seconds and then remain unchanged.
The loss begins at that change. High/medium/slow minimum send intervals are
0.1/0.4/1.5 seconds, with the existing one-second refresh and priorities100/50/0.

| Scenario | Loss ends | First high/medium recovery | First slow recovery | RPC executions |
| --- | ---: | ---: | ---: | ---: |
| 0.1 s loss | 0.6 s | 1.5 s | 1.5 s | 1 |
| 0.4 s loss | 0.9 s | 1.5 s | 1.5 s | 0 |
| 1.5 s loss | 2.0 s | 2.5 s | 3.0 s | 0 |
| 0.4 s held image | 0.9 s | 1.5 s | 1.5 s | 0 |
| 1.5 s held image + new stream at4s | 2.0 s | 2.5 s | 3.0 s | 1 in new stream |
| 0.1 s loss + new stream at4s | 0.6 s | 1.5 s | 1.5 s | 1 per stream,2 total |

Each scenario contains 241 simulated observations through eight seconds. The
first four emit the three fields9/9/6 times, with227 empty output opportunities;
this guards against accidentally treating unchanged fields as continuously due.
The restarted cases emit10/10/6 times with223 empty opportunities. All complete
received state bytes match the zero/42/77 schedule. The trace contains1,446
rows, and nonempty packets plus protected headers are preserved for independent
byte/CRC inspection.

Four consecutive RPC copies span about0.1 seconds at30 Hz. A0.1-second burst
leaves the last copy; a0.4/1.5-second burst covers all four. A later periodic
state refresh restores state, but does not recreate a lost RPC event. In the
short-loss restart case, event501 executes once in the old stream and once in
the new stream despite repeats, demonstrating the application deduplication
boundary with an explicitly supplied stream ID.

No corrupt state, partial array application or duplicate RPC execution was
observed. These are simulated monotonic source times, **not measured live
network recovery latencies**. The refresh test invokes production scheduler/
builder and protected Decoder completion; it bypasses GPU/readback/header
duplicate checks. It does not certify a whole-client stream restart or an
external service reconnect. The diagnostic tests separately use actual Native
Encoder rendering and the compiled Udon Encoder path.

## Preserved preliminary runs

`native-initial` is **superseded and not accepted refresh evidence**: its harness
created a new metadata cache each frame, invalidating field identity and making
unchanged data eligible every frame. Self-review found this; `native-calibrated`
used a persistent cache and explicit emission-cadence/first-recovery guards.
`native-final` adds raw packets, source/send masks and the deduplication restart
control. Product scheduling did not change to obtain the result.

`vrc-initial` retains an Editor harness generic-type compilation error.
`vrc-corrected` passes113 assertions but is provisional because client compilation
was only explicitly requested before Play entry. `vrc-client-final` repeats
compilation after Play entry, reads the serialized client programs and preserves
their source/bytecode hashes. The final product sources are the same across
these SDK attempts.

## Actual monotonic-clock Encoder follow-up

`realtime-native-playmode` completes the actual-clock check, complementing the
deterministic scheduler cases above. Seven Native Encoders call production
`EncodeNow` for **8.50315 actual seconds**, sharing255 thirty-Hz deadline
opportunities. Production `GetSendTime` uses
`Time.realtimeSinceStartupAsDouble` in PlayMode; no clock or successful-send
timestamp is injected. Defaults remain refresh1s, repeats4, target0.

**19,721 assertions PASS**, with1,785 observations,115 original packets and
protected headers, zero missed deadline slots and maximum tick-entry gap33.429ms.
All115 emitted packets use the actual GPU Luma4 Encoder path. This is local
scheduling validation; it does not measure GPU completion or receiver readback.

Every actual opportunity checks field eligibility against the Encoder's own
`_sendTime` and preceding successful `Entry.LastSent`, including send-on-change,
minimum intervals and refresh. The separate Python audit reparses the protected
packet/header bytes, CRCs, whole32-byte values, RPC method/event501 envelopes,
queue repeats and visible originals, and reconstructs all1,785 receiver states.
`realtime-audit.json` reports PASS. Source/configuration hashes match before/after.

The first source change occurs near0.5s; times below are actual seconds from
the shared clock origin and include the real scheduling phase.

| Case | Actual loss ends | First high/medium application | First slow application | Events |
| --- | ---: | ---: | ---: | ---: |
| 0.1s loss | 0.6063 | 1.5344 | 1.5344 | 0 |
| 0.4s loss | 0.9000 | 1.5346 | 1.5346 | 0 |
| 1.5s loss | 2.0030 | 2.5673 | 3.0673 | 0 |
| 0.4s held image | 0.9033 | 1.5350 | 1.5350 | 0 |
| 1.5s hold + new stream near4s | 2.0035 | 2.5340 | 3.0677 | 1 in new stream |
| 0.1s loss + new stream near4s | 0.6038 | 1.5340 | 1.5670 | 1 in new stream |
| Intact restart control | no loss | 0.5058 | 1.5672 | 1 per stream,2 total |

The real0.1-second loss covers **all four RPC copies** in both short-loss cases.
For example, one stream sends copies at0.50628,0.53712,0.56766 and0.60083s;
its loss window is0.50626–0.60626s. This preserves the observed missing event.
The deterministic grid above leaves its fourth copy exactly on the recovery
boundary; that grid result is not a guarantee that a100ms burst preserves an
event under actual scheduling. The intact restart control confirms the old
stream already executed event501 once, then the new stream executes the reused
ID once despite four visible copies.

All first recoveries obey their actual eligible refresh/minimum-interval
opportunities. No corrupt or partial array, duplicate event execution or
unexpected continuous unchanged-field emission occurs. These are **actual local
clock times with an injected loss/hold policy**, not network latency measurements.
Decoder completion receives the original protected packet bytes directly;
header/readback duplicate checks, video compression and external services remain
outside this experiment.

`realtime-native-initial` and `realtime-native-debug` retain failed EditMode
fixtures. Editor automatic scene binding rebuilds expanded the intended isolated
source tables to scene-wide bindings, which the duplicate-field guard rejected.
The final fixture creates cases **after Play entry**, where that automatic
Editor rebuilding is inactive. It changes no production code, Editor defaults
or event subscriptions to obtain stable bindings. The debug run preserves the
offending original packet for review.
