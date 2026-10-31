# FEC feasibility measurements

This directory contains **measurement-only** Python references and fixture tools.
No runtime package imports these files. No production protocol, codec palette,
CRC policy or package version is changed by this experiment.

The shortened Reed–Solomon reference uses GF(256), primitive polynomial `0x11d`,
and consecutive generator roots alpha^0 through alpha^(parity-1). Two independent
parity constructions and deterministic corruption tests check its implementation.
This is a specific experimental codeword convention, not a claim of byte-level
interoperability with another RS implementation.

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
python Validation~/FecFeasibility/test_reference.py
python Validation~/FecFeasibility/test_envelope.py
python Validation~/FecFeasibility/test_analysis.py --results '<new evidence folder>'
```

`analyze_residuals.py` consumes `raw-dumps.csv` exported by the RefineRobustness
harness with `dumpRawBytes=true`, plus the matching production verdict CSVs. It
requires independent protected source bins and verifies every decoded dump SHA.
Only mode two is selected by default. It records error positions, consecutive
byte runs, affected symbols and raster tiles for the block-eight fixtures.

Its parity screen assumes **perfect parity and unchanged old payload errors**.
The round-robin variant assigns those old payload errors to different codewords.
The resulting counts are conditional correction bounds; they are not new video
decodes, observed delivery gains or a prediction of parity survival. Header-invalid
forced payload probes remain diagnostic and are separately counted. Sampled
unknown-error decoding replays check the reference against those same assumptions.

## Actual encoded-raster experiment

`prepare_envelopes.py` wraps independent source payload bins before any raster call.
The experimental packet has twenty metadata bytes (including metadata CRC),
shortened RS codewords, and optional column-wise codeword interleaving. The metadata
contains the original length and original CRC. The original source bytes are never
recovered from measured images or replaced with decoded bytes.

Packets use **new payload type `0xF100` and layout `0xFEC1`**, with valid ordinary
header and outer packet CRCs. Existing TSMP receivers reject the payload type.
These identifiers are measurement markers, not allocated production protocol IDs.
Production use needs an explicit protocol design and compatibility tests.

The focused v2 experiment protects the twenty metadata bytes with a fixed sixteen
RS parity bytes before body parsing. It uses payload type `0xF101`, layout `0xFEC2`
and envelope version two. The thirty-six-byte bootstrap needs no untrusted parity
or length value before correction. `prepare_envelopes.py --protect-metadata` selects
this variant. Initial v1 files, outputs and source snapshots remain preserved.

`FecRasterFixtures.cs` runs in a dedicated SDK-free Unity project. Environment
variables `TSMP_FEC_FIXTURE_PLAN` and `TSMP_FEC_FIXTURE_RESULTS` select the prepared
`raster-plan.json` and a fresh output folder; execute method `FecRasterFixtures.Run`
calls each actual codec prefab's production `TryWriteFrame`. Ninety native 720p,
block-eight/sample-four rasters per fixture preserve the independent reference bins.
The CPU generator can run with `-batchmode -nographics`.

`encode_prototype.py` uses the exact existing VideoTransport FFmpeg profile builders
to encode and restore these rasters. It retains arguments, process exits, packet
sizes/PTS, video hashes and source hashes. Normal baseline source payloads are the
same originals, rasterized by the same production codecs. New and old formats
receive the same nominal encoder settings; exact measured packet rates can differ.

The ordinary RefineRobustness GPU harness then exports Header mode zero and Payload
modes zero/one/two for all PNG controls and compressed inputs. Its raw probes use
independent reference geometry. `analyze_encoded.py` accepts an observation only
when the actual decoded header is CRC-valid and byte-identical to the reference;
therefore the accepted probe's geometry also equals the received geometry.

The Python reference receiver requires metadata integrity, RS correction, original
payload CRC and the protected outer packet CRC after complete packet reconstruction.
Every accepted original and reconstructed packet must also match independent source
bytes. A valid CRC with wrong bytes fails the analysis. This tests a reference FEC
receiver with actual raster/compression/GPU corruption; it does not demonstrate a
TSMP runtime FEC path, application/state/RPC acceptance, Udon cost, live FPS, network
delivery, VRChat or VRCDN operation. Python correction timings are diagnostic only.

The paired baseline in the raw experiment is the frozen prior receiver's three
global decoding modes, without the new sparse candidate mixing. Optional
`analyze_encoded.py --current-production-csv '<fresh CSV>' --current-variant SparseRecovery`
also reports paired gains/losses against actual current production verdicts on the
same original inputs. These baselines must be named separately in any report.

Evidence for this run is under
`F:/Unity/TSMP/Validation-Results/fec-feasibility-20261031`.
