# Contributing to Eye Break

Thanks for helping. Bug reports, ideas and pull requests are all welcome.

## Reporting a bug

1. In the Eye Break panel, click **Report**. This marks the moment in the log and opens it.
2. [Open a bug report](https://github.com/dev2180/eye-break/issues/new?template=bug_report.yml) and paste the last
   lines of the log (`%APPDATA%\EyeBreak\log.txt`).

## Suggesting a feature

[Open a feature request](https://github.com/dev2180/eye-break/issues/new?template=feature_request.yml) describing what
you'd like and why.

## Working on the code

You need Windows 10 or 11, the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) and the WebView2 runtime
(built into Windows 11).

```
dotnet build EyeBreak.sln -c Release
src\EyeBreak\bin\Release\net9.0-windows\EyeBreak.exe --show-panel
```

Testing flags for the app:

| Flag | What it does |
|---|---|
| `--break-now` | Starts a break right away |
| `--infinite-now` | Starts infinite mode right away |
| `--show-panel` | Opens the tray panel on start |
| `--show-in-capture` | Lets screenshots see the break |
| `--force-dim` | Uses the fullscreen-game dimming even without a game |
| `--pretend-old` | Treats this build as version 0.0.0, so the latest release shows as an update |

Build the installer with:

```
powershell -ExecutionPolicy Bypass -File build\build-installer.ps1
```

### Pull requests

- Keep each pull request to one change, and describe what you tested.
- Match the existing style: small, readable code with comments that explain why.
- Add a line under **Unreleased** in [CHANGELOG.md](CHANGELOG.md).
- CI builds the app and installer on every pull request; it needs to pass.

## Releasing (maintainers)

1. Set the new version in [Directory.Build.props](Directory.Build.props) and move the **Unreleased** notes in
   [CHANGELOG.md](CHANGELOG.md) under that version.
2. Commit, then tag and push: `git tag v1.2.0 && git push origin v1.2.0`.
3. The **Release** workflow builds `EyeBreak-Setup.exe` and publishes the GitHub release with the changelog notes.
   Installed copies offer the update within 12 hours.

By contributing you agree that your work is licensed under the [MIT License](LICENSE), and to follow the
[Code of Conduct](CODE_OF_CONDUCT.md).
