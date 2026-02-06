# Changelog

## 1.5.0 - 2026-02-06
- Added UPM install/update flow with version-aware actions, auto-update support, and Update All integration.
- Improved package list UI (version-row actions, UPM badge, required-first sorting, update indicators, git commit message display).
- Enhanced package actions (remove git, uninstall removes meta, open remote from configured repo, UPM-aware open directory/git actions).
- Added UPM detection via Packages/manifest.json fallback when PackageInfo is unavailable.


## 1.4.0 - 2026-02-06
- Added UPM install/update support with version-aware actions and auto-update integration.
- Improved package list UI (version row buttons, UPM badge, required-first sort, update indicators, git commit message).
- Enhanced package actions (remove git, uninstall removes meta, open remote from configured repo).


## 1.3.0 - 2026-02-06
- Support required packages in package.json and UI (no uninstall, auto-update)
- Add Auto Update toggle per package with persisted interval setting
- Improve error messages for install/update/uninstall failures
- Add Update All and auto-refresh after publish/commit/push/create version/uninstall
- Add Git status display and initialize/update Git behavior fixes
- Update README, package metadata, and new editor asmdef setup


## 1.1.0 - 2026-01-29
- Add manifest parsing and GitHub contents installation.

## 1.0.0 - 2025-12-01
- Initial release.
