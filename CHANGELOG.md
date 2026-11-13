# Changelog

All notable changes to Eye Break are listed here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- Microsoft Store package (MSIX), built with `build/build-msix.ps1`. The Store version starts with Windows through
  its package, gets updates from the Store, and shares settings with the installer version.
- Privacy policy.

### Changed
- Repository reorganized: app in `src/EyeBreak`, installer in `src/EyeBreak.Setup`, build script in `build/`.
- Added a license (MIT), contribution guide, issue templates, and CI that builds the installer on every push.

## [1.1.0] - 2026-11-13

### Added
- Update from inside the app: checks the latest GitHub release at start and every 12 hours, shows a tray notice and an
  **Update to version X** button; one click downloads and installs it, then reopens Eye Break.
- **Check for updates** and the current version in Settings.
- The installer plays ambient music it generates itself, with a mute button.

## [1.0.0] - 2026-11-13

### Added
- One-file installer (`EyeBreak-Setup.exe`): per-user install with no admin rights or .NET needed, Start menu and
  desktop shortcuts, start with Windows, and an uninstaller in Installed apps.
- Session timer for infinite mode, with a chime at the start and end, adjustable during the session and from the panel.
- **Num Lock + numpad 8** stops a break early.
- Error log with a **Report** button in the panel.

### Fixed
- Music no longer keeps playing after a break is stopped early.

[Unreleased]: https://github.com/dev2180/eye-break/compare/v1.1.0...HEAD
[1.1.0]: https://github.com/dev2180/eye-break/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/dev2180/eye-break/releases/tag/v1.0.0
