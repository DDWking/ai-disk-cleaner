# c_cleaner_plus (github.com/Kiowx/c_cleaner_plus, main) — evidence review

## A. SAFETY COVERAGE
- `tests/test_safety_regressions.py` = 46,056 B, one class `SafetyRegressionTests`, **46 `test_*` methods**. Repo tree (`git/trees/main?recursive=1`) lists only 2 test files (`test_safety_regressions.py`, `test_clean_table_interaction.py` = 7 tests) → whole suite ≈ **53 tests** for a 654,154 B `main.py`.
- Protected paths: only `test_sensitive_descendants_are_blocked_but_vetted_cache_children_are_allowed` asserts `main.is_protected_system_path`: **True** for `%SystemRoot%\WinSxS\payload.dll`, `%ProgramFiles%\Example\app.exe`, `%SystemRoot%\Temp`; **False** for `%SystemRoot%\Temp\cleanup.tmp` and `%LOCALAPPDATA%\Temp\cleanup.tmp`.
- **No** assertion for `C:\` root, `pagefile.sys`, user-profile root, reparse points, or "file in use" during cleanup. Closest: `test_partial_commit_never_overwrites_a_new_target` → message `"目标文件在提交前被其他程序占用"` (migration path, not cleanup).
- Path re-validation before delete: `test_duplicate_candidate_is_revalidated_before_delete` (expectation = `{"reference","size","digest":sha256}`; after content change returns False + `"不再相同"`); `test_duplicate_delete_worker_skips_failed_revalidation` (`main.delete_path` `assert_not_called()`).
- Path escape: `test_rule_pack_filename_rejects_path_escape` — `..\main.py`, `../main.py`, `C:\tmp\payload.json`, `/tmp/payload.json`, `payload.txt`, `..` → `normalize_rule_pack_filename(name) == ""`.
- Rule-pack download: `download_rule_pack` bounded by `RULE_PACK_MAX_BYTES` **before** write, invalid JSON raises `ValueError` and file is not persisted, output contained in `base_dir`.
- Registry: `test_scheduled_registry_check_never_deletes` — `force_delete_registry.assert_not_called()`, log must contain `"仅报告"` and `"删除 0 项"`.
- **Recycle bin vs permanent delete: NOT tested.** No test mentions recycle/shell API/RecycleBin; only `main.delete_path` is mocked. `README.en.md` claims "Supports recycle bin deletion and permanent deletion".
- Glob/wildcard expansion rules: no test; only rule-data patterns (below).

## B. DELETE MECHANICS (as asserted)
- Tests never call real Windows delete APIs: `main.delete_path` and `main.force_delete_registry` are mock targets; no assertion forbids whole-directory deletes or specific extensions.
- Only real delete-adjacent exercise is migration/link logic on `tempfile` dirs (symlink created via mocked `os.symlink(target, source, target_is_directory=True)`), plus `test_incremental_directory_move_*`, `test_undo_*` on real temp files.
- `test_clean_table_interaction.py` does exercise real Qt (`QTest.mouseClick`, offscreen): shift-range selection across hidden rows, `moveRows`, `sort_by_mode(3)`, model reload invalidating anchor, interactive column resize.

## C. RUST HELPER `tools/fast_large_files`
- Read-only raw-NTFS MFT scanner; **zero dependencies** (Cargo.lock contains only `fast_large_files 0.1.0`; Cargo.toml: edition 2021, `lto=true`, `strip=true`, `publish=false`).
- Non-Windows `main()` prints `"fast_large_files only supports Windows NTFS volumes"`, exit 1.
- Opens `\\.\{drive}:` with `share_mode 0x7` (FILE_SHARE_ALL); validates boot sig `b"NTFS    "`; reads MFT record 5, data runs, `apply_fixup` (USA) per 512-B sector; parses `ATTR_FILE_NAME 0x30`, `ATTR_DATA 0x80`, `ATTR_REPARSE_POINT 0xC0`; merges extension records; `build_path` with cycle guard (`test build_path_rejects_parent_cycles`). Buffered `SectorReader` (1 MiB chunks, `CHUNK_RECORDS = 16*1024`).
- Filtering: files only (`!is_dir`), **not** reparse points, `size >= min_bytes`; skips `$*` metadata and root `PAGEFILE.SYS/SWAPFILE.SYS/HIBERFIL.SYS/DUMPSTACK.LOG`; absolute skip of `*.sys`; skips prefixes `SYSTEM VOLUME INFORMATION`, `RECOVERY`, `$RECYCLE.BIN`, `WINDOWS\SYSTEM32\{CONFIG,TASKS,SRU,WDI}`, `WINDOWS\SYSWOW64\{...}`, `WINDOWS\SYSTEM\WIM`; dedupes normalized paths.
- CLI: `--root` (repeatable/positional, default `C:\`), `--exclude` (repeatable), `--min-bytes` (default `300*1024*1024`; 0 = error), `--limit` (default 300; 0 = unlimited), `--skip-optional 1|true|yes|y` (adds pagefile/hiberfil/swapfile/memory.dmp + `.vhd/.vhdx/.vmdk/.vdi/.qcow2/.ova/...`), `--help`; unknown `-flag` → error.
- Output: **JSON Lines to stdout**, `{"size":N,"path":"C:\\..."}`, sorted size-desc then path, truncated to limit. No write/delete/rename anywhere.
- Invocation: README — "only used for drive-root scans such as `C:\` when the compiled executable is available", else fallback to `os.scandir()`. `test_background_scan_methods_use_snapshotted_arguments` shows `BigFilePage._scan_w` takes snapshot args instead of `self.sp_mb/self.sp_mx/self.drive_sel/self.chk_skip_special` → maps to `--min-bytes/--limit/--root/--skip-optional`.
- README speed claim: **no numbers at all** — only "Optional Windows helper for accelerating large-file scans on NTFS drives".

## D. RULE STORE `config_store.json`
- `{"items":[...]}`, 10 packs: `common_custom_rules.json`, `rules_cn_apps.json`, `rules_dev_tools.json`, `rules_media_creation.json`, `rules_mobile_dev_tools.json`, `rules_design_3d_cad.json`, `rules_ai_tools.json`, `rules_game_platforms.json`, `winapp2.json`, `bleachbit.json`.
- Per-item fields: `title`, `filename`, `source` (`官方` ×8, `兼容` ×2), `summary`, `detail`. **No version, no URL, no sha256/hash, no category key.**
- Integrity: none. Only size cap (`RULE_PACK_MAX_BYTES`) + JSON validity + filename containment. Only versioned remote artifact is `i18n/manifest.json`: `{"version":9,"updated_at":"2026-08-02","languages":{"en_us":{"label":"English","url":"https://raw.githubusercontent.com/.../refs/heads/main/i18n/en_us.json","version":9,"updated_at":"2026-08-02"}}}`; test asserts `payload["version"] == manifest["languages"]["en_us"]["version"]`.

## E. RULE FORMAT `config/rules_ai_tools.json`
- Positional JSON **arrays**, not objects; 7 slots align with the `main.CleanRuleRow` ctor in tests (`(idx, title, path, kind, False, "", False, "")`): `[title, path, kind, bool#1, description, bool#2, glob_pattern]`. `kind` values seen: `"dir"`, `"file"`, `"glob"`; paths use `%LOCALAPPDATA%/%APPDATA%/%USERPROFILE%/%PROGRAMDATA%/%TEMP%`.
- Verbatim: `["Ollama 日志","%LOCALAPPDATA%\\Ollama\\logs","dir",false,"Ollama 本地运行日志"]`; `["Claude Code IDE 锁文件","%USERPROFILE%\\.claude\\ide","glob",false,"Claude Code 与编辑器通信的端口锁文件，退出后常有残留",false,"*.lock"]`.
- Other glob patterns: `.claude.json.backup.*`, `rollout-*.jsonl`. **No risk level, no `protected`, no `recycle`, no exclude patterns**; every sampled rule in 4 packs has bool#1 `false` and bool#2 `false`/absent. Per-rule enable flag is UI-side (`CleanRuleRow.checked`).

## F. I18N
- `i18n/` contains exactly `en_us.json` (83,830 B) and `manifest.json` (274 B) — **no other locales on disk**; manifest lists only `en_us`.
- Flat zh→en string map; API used by tests: `main._normalize_language_pack`, `main._runtime_tr`, `main.bundled_language_file("en_us.json"|"manifest.json")`. Required keys include `"源文件在半成品校验过程中发生变化，已保留源文件"`, `"目标文件在提交前被其他程序占用"`, `"重复文件复核"`, `"已暂停（可继续）"`, `"尚未运行"`.

## G. PACKAGING
- `.iss`: `AppId {{ACF190D8-8DC8-4EC6-9F6E-DB4FD898F296}`, `AppName "C Cleaner Plus"`, `AppDisplayName "C盘强力清理工具"`, `DefaultDirName={autopf}\C Cleaner Plus`, `ArchitecturesAllowed/InstallIn64BitMode=x64compatible`, `PrivilegesRequired=admin`, `CloseApplications=yes`, lzma2/max. Files: `Source: "..\dist\c_cleaner_plus\*"` recursesubdirs; `[InstallDelete] filesandordirs {app}\_internal`; start-menu icon + optional `desktopicon` (unchecked); **one** `[Run]` entry with `nowait postinstall skipifsilent runascurrentuser` (test asserts no `runasoriginaluser`); `[UninstallRun]` PowerShell `Get-ScheduledTask ... Unregister-ScheduledTask`, `RunOnceId: "RemoveScheduledTasks"`; `[UninstallDelete] {app}\cdisk_cleaner_bootstrap.json`; `[Code] EnsureBootstrapConfig` writes `{"config_dir":"%LOCALAPPDATA%\\C Cleaner Plus\\configs","skip_legacy_migration":false,"legacy_migration_acknowledged":false}` at `ssPostInstall`. Languages: chinesesimp + english.
- **No autostart registry key in the installer** (autostart is in-app: `global_settings["auto_start"]`, `_refresh_auto_start_state_async`). **No code-signing directive** (`SignTool` absent; spec `codesign_identity=None`).
- `.spec`: PyInstaller **onedir** (`exclude_binaries=True` + `bundle = COLLECT(...)`), `contents_directory="_internal"`, `upx=False`, `uac_admin=True`, `console=False`, `optimize=2`. Datas: app/icon.ico, `i18n`, `config`, `configs`, `config_store.json`, `common_custom_rules.json`, `rules_cn_apps.json`, `rules_dev_tools.json`, `rules_game_platforms.json`. Binary: `tools/fast_large_files/target/{release,debug}/fast_large_files.exe` (else "未找到…将使用普通扫描回退路径"). `hiddenimports = collect_submodules("qfluentwidgets")` (minus multimedia); excludes ~40 PySide6 modules; custom `should_keep` filter drops Qt6Quick/Qml/WebEngine/3D/Charts/... plus `qml/` and `translations/`; `upx_exclude` includes `fast_large_files.exe`. `requirements.txt`: PySide6==6.10.2, PySide6-Fluent-Widgets==1.11.1, PySideSix-Frameless-Window==0.8.0, darkdetect==0.8.0, pyinstaller==6.18.0, pywin32==311 (all pinned).

## H. NOTABLE WEAKNESSES (evidenced)
- **Rule packs trusted without signature/hash**: `config_store.json` has no sha256/version, and no test verifies integrity — only size cap, JSON parse, filename sanitation.
- **Update channel is mutable and unhashed**: i18n `url` points at `raw.githubusercontent.com/.../refs/heads/main/...`; version-only check, no digest.
- **Delete behavior effectively untested**: `delete_path`/`force_delete_registry` are mocks; no recycle-bin vs permanent-delete test, no dry-run test, and no test that a protected path is refused *at delete time*.
- **Brittle source-text assertions instead of behavior**: `inspect.getsource` on `UninstallPage._trigger_leftover_scan` (`assertNotIn("threading.Thread")`), `CleanPage._cln_w`, `BigFilePage._scan_w`, `MainWindow.__init__`, `_register_nav_items` (lazy-page strings) — refactor guards, not runtime safety.
- **Rust helper duplicates protection logic** (hardcoded Windows\System32 prefixes) that can drift from Python's `is_protected_system_path`; helper is optional, so packaged behavior differs by build.
- **Coverage scale**: 53 tests vs 654 KB single-module `main.py`; disk scan/delete workers are covered only via mocks or source inspection. Test import `os.environ.setdefault("QT_QPA_PLATFORM","offscreen")` means GUI tests run headless.

UNVERIFIED: `main.py` internals (654,154 B, not fetched) — exact Rust-helper argv/call site, whether `delete_path` uses `SHFileOperation`/recycle vs `os.remove`, the Python protected-path list, existence of a dry-run mode, the rule-pack download base URL/update channel; semantics of rule slots `bool#1`/`bool#2`; `docs.cus.cc.cd/guide/config.html` (unreachable in this run); `.github/workflows/build.yml` content beyond strings asserted by the test; whether releases are actually code-signed. Some fetches needed mirrors (cdn.jsdelivr.net / api.github.com) because raw.githubusercontent was intermittently failing.
