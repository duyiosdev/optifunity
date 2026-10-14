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

**Automatic asset/shader mutation remains disabled**, including import hooks, report/dashboard fix actions and the MCP optimization endpoint. Milestone C adds a separate, explicitly approved Unity UI transaction for platform Texture Max Size reductions, with preview, backup, verification and guarded rollback. Suggestions require developer review of usage, quality and runtime behavior.

[Rule catalog](knowledge/rules.json) records sources, applicability, required evidence, tradeoffs and verification. Legacy detectors without individually verified mappings are explicitly advisory and do not reduce the score. [Reference inventory](docs/REFERENCE_AUDIT.md) retains the 60 original sources and their verification status; inventory does not mean every claim is verified.

The two Vietnamese knowledge documents describe conditional guidance for [mobile performance](Tối%20ưu%20hóa%20hiệu%20năng%20Unity%20URP.md) and [rendering](Sơ%20đồ%20kiến%20trúc%20Unity%20Renderer%20chính%20xác.md).

## MCP connection

The Editor creates a session descriptor at `<project>/Library/OptifunityBridge-<pid>.json`. It contains a loopback endpoint, project path, instance ID and secret session token. It is regenerated on domain reload and removed on normal shutdown. Do not publish it.

Set `OPTIFUNITY_CONNECTION` to the exact descriptor for the intended Editor. There is no fallback to port 9090 or another project. Both `McpServer/index.js` (Node) and `McpServer/unity_mcp_server.py` (Python) read this descriptor for each call. Run `npm ci --prefix McpServer` for the Node client, then launch the chosen client through your MCP host. Stdio is reserved for JSON-RPC.

Read-only endpoints provide status, active scene summary, selected profiler frame and build-target settings. Authentication requires all three identity fields. The optimization tool remains discoverable for compatibility but returns an explicit disabled error. Queued requests expire; no mutation is performed. The descriptor changes after reload, so clients reread the selected file.

## Validation

`Tools~/Validate.ps1 -UnityPath '<path to Unity.exe>'` creates a separate fixture in the operating-system temporary directory, outside this local UPM package and launches EditMode tests. Existing projects without the fixture ownership marker are refused. Nested projects inside this package are refused because they can cause Unity MonoScript import mappings to fail. Pass `-PythonPath` to include the Python live-client test; install Node dependencies before validation. Check the generated `results.xml` for pass/fail and inspect `validation.log` for import/compiler errors. An exit or test count alone does not certify a clean import.

The fixture pins URP 17.0.4 and Test Framework 1.4.5. Roslyn dependencies, hashes and licenses are in `Editor/ThirdParty/Roslyn`. Regression tests are in `Tests/Editor`.

## Milestone B evidence tools

Open **Tools > Optifunity > Evidence and Benchmarks**, also linked from the Dashboard Build tab.

- Inventory imported main/sub-assets and direct dependencies under Assets and optionally Packages. The latest BuildReport is separate, dated build evidence; serialized packed bytes are not runtime memory. Addressables 2.3.16 is optional: persisted settings roots are inspected without invoking the migration-capable default-settings getter. Bundle layouts and remote catalogs remain outside this adapter.
- Inspect saved scenes/prefabs for Camera, Canvas/CanvasRenderer, particles, material/shader keywords and configured URP renderer features. These are usage/configuration records, not measured overdraw or final stripped shader variants.
- Scan resolved helper call paths and event cleanup evidence. Traversal is bounded to static, same-compilation method bodies; dynamic dispatch and whole-program leak proof remain unsupported. Findings are advisory.
- Read all captured Profiler threads (up to 256) and their top 20 inclusive markers. A per-thread sample cap is explicit. Concurrent thread times must not be summed into frame wall time.
- Capture Editor object aggregates and signed type diffs; capture native .snap files separately. Open the optional Unity Memory Profiler (fixture: 1.1.11) to import and compare native/managed graphs. Aggregate diffs are not native heap diffs and do not prove leaks.
- Add DeviceEvidenceRecorder explicitly in a measurement scene, configure scenario/target/warmup, call Begin and StopAndSave. It saves under Application.persistentDataPath. No recorder is injected automatically. Compare two JSON captures for frame-time percentiles, hitches, sampled memory peaks and named loading intervals. RSS/thermal data require a measured external source; missing values remain unknown.

See [B acceptance](docs/MILESTONE_B_ACCEPTANCE.md) for test results and [device protocol](docs/DEVICE_BENCHMARK_PROTOCOL.md) before recording. The user has not yet supplied a game/device/scenario: **mobile before/after acceptance remains pending**. Windows fixture results do not satisfy it.

## Milestone C optimization review

Open **Tools > Optifunity > Optimization Review**. Review evidence-first priorities, estimated impact/cost, sources and tradeoffs; save scoped baselines and reasoned suppression without deleting findings or changing compliance scores.

The controlled change workflow supports Default 2D textures under Assets only: preview Android/iPhone Max Size reduction, review usage/quality, explicitly apply, then verify visual/device behavior. Source/meta hashes and GUID checks reject stale previews; rollback refuses to overwrite newer edits. Metadata backups and atomic transaction journals are under Library/OptifunityChanges, so deleting Library removes rollback history. Interrupted transactions remain visible for manual recovery; power-loss recovery is not certified.

Both MCP clients expose **unity_get_assistant_context** with the authenticated project/instance, scoped findings, catalog sources, review decisions and change history. Share B captures explicitly through Evidence and Benchmarks; session captures clear on reload. This is an evidence interface for your chosen AI host, not an embedded model or automatic cloud upload. MCP writes remain disabled.

See [C workflow](docs/OPTIMIZATION_REVIEW_WORKFLOW.md) and [C acceptance](docs/MILESTONE_C_ACCEPTANCE.md). Technical import verification does not certify texture quality or device performance; AI response quality still depends on the connected model.

## Roadmap boundaries

Milestone B tooling is available within the documented scope above; full device acceptance and the listed unsupported contexts remain open. Milestone C now supplies the bounded workflow above. Other automatic fix families, embedded AI providers and full mobile acceptance remain outside the implemented scope. See [upgrade plan](docs/UPGRADE_PLAN.md).
