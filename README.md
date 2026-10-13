# Optifunity — Unity URP performance evidence

Optifunity is a Unity Editor package for reviewing C# operations, imported assets, effective rendering settings and selected Main Thread profiler data. Its purpose is to help optimize mobile games using explicit evidence and versioned knowledge.

## Compatibility and installation

The milestone A validation baseline is **Unity 6000.0.58f2 / URP 17.0.4 / Windows**. See [acceptance report](docs/MILESTONE_A_ACCEPTANCE.md) for actual results and remaining gates. Settings are stored per project; legacy global presets are not migrated automatically. Other Unity versions and operating systems are untested; Unity 2021.3 is no longer advertised as supported.

Install as a local UPM package using this repository's `package.json`. URP is an explicit dependency. Open the Optifunity dashboard using its existing Unity menu. This repository is a package, not a Unity game project.

## What the plugin does

- Uses Roslyn 3.8 for selected C# allocation, boxing, event subscription and resource operations. Direct callback analysis is not a whole-program call graph or proof of a leak.
- Reviews imported assets and reads platform overrides for texture size and audio settings.
- Reads the effective Quality render pipeline and exposes missing settings as unknown where adapters are unavailable.
- Reviews Main Thread profiler samples (hierarchy depth 0–10). Wait markers alone do not establish a GPU bottleneck; allocation alone is not collection. Heuristic ranking is not a calibrated probability.
- Exports findings with rule/evidence metadata, scan scope and module capture times. Module scans retain other module results; compare capture times before combining them.

Assets, loaded scenes and selected settings are not the entire runtime/build dependency graph. Packages, generated source and Addressables/build dependency coverage are not complete. My Scripts filters code only. A score expresses selected rule compliance and does not certify frame rate or device memory safety.

## Safety and knowledge

**Automatic asset/shader mutation is disabled**, including import hooks, report/dashboard fix actions and the MCP optimization endpoint. Suggestions require developer review of usage, quality and runtime behavior.

[Rule catalog](knowledge/rules.json) records sources, applicability, required evidence, tradeoffs and verification. Legacy detectors without individually verified mappings are explicitly advisory and do not reduce the score. [Reference inventory](docs/REFERENCE_AUDIT.md) retains the 60 original sources and their verification status; inventory does not mean every claim is verified.

The two Vietnamese knowledge documents describe conditional guidance for [mobile performance](Tối%20ưu%20hóa%20hiệu%20năng%20Unity%20URP.md) and [rendering](Sơ%20đồ%20kiến%20trúc%20Unity%20Renderer%20chính%20xác.md).

## MCP connection

The Editor creates a session descriptor at `<project>/Library/OptifunityBridge-<pid>.json`. It contains a loopback endpoint, project path, instance ID and secret session token. It is regenerated on domain reload and removed on normal shutdown. Do not publish it.

Set `OPTIFUNITY_CONNECTION` to the exact descriptor for the intended Editor. There is no fallback to port 9090 or another project. Both `McpServer/index.js` (Node) and `McpServer/unity_mcp_server.py` (Python) read this descriptor for each call. Run `npm ci --prefix McpServer` for the Node client, then launch the chosen client through your MCP host. Stdio is reserved for JSON-RPC.

Read-only endpoints provide status, active scene summary, selected profiler frame and build-target settings. Authentication requires all three identity fields. The optimization tool remains discoverable for compatibility but returns an explicit disabled error. Queued requests expire; no mutation is performed. The descriptor changes after reload, so clients reread the selected file.

## Validation

`Tools~/Validate.ps1 -UnityPath '<path to Unity.exe>'` creates a separate fixture in the operating-system temporary directory, outside this local UPM package and launches EditMode tests. Existing projects without the fixture ownership marker are refused. Nested projects inside this package are refused because they can cause Unity MonoScript import mappings to fail. Pass `-PythonPath` to include the Python live-client test; install Node dependencies before validation. Check the generated `results.xml` for pass/fail and inspect `validation.log` for import/compiler errors. An exit or test count alone does not certify a clean import.

The fixture pins URP 17.0.4 and Test Framework 1.4.5. Roslyn dependencies, hashes and licenses are in `Editor/ThirdParty/Roslyn`. Regression tests are in `Tests/Editor`.

## Roadmap boundaries

Milestone B covers full inventory/build dependencies, deeper lifetime analysis, multiple profiler threads, memory snapshots/diffs and repeatable low-end-device benchmarks. Milestone C covers verified preview/rollback fixes and an AI assistant using the same evidence and write policy. These are planned capabilities, not current features. See [upgrade plan](docs/UPGRADE_PLAN.md).
