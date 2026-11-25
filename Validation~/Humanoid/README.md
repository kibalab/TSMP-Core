# Production rig regressions

In a disposable Unity 2022.3 project without the Worlds SDK, install the working Core package, copy the two scripts in `Editor/` into `Assets/Editor`, then run:

```text
Unity.exe -batchmode -quit -projectPath <project> -executeMethod HumanoidReceiveValidation.RunBatch -logFile <log>
```

The cases exercise partial rig selections, corrupt records, animator-relative root motion, alternate finger/wrist receipts, and interpolation. The fixed-rig and missing-sender-bone checks instantiate the Shapell model from the Core sample. The binding test checks both explicit invalidation and a replacement setup table.

For bytecode execution in a separate Worlds SDK project, run `Validation~/Run-Validation.ps1` with `-Step ReceivePoliciesVm`. It compiles the client programs and exercises the fixed-rig hand updates and receive resets in the editor Udon VM. Neither suite measures VRChat client frame time.
