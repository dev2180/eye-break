# Security Policy

## Supported versions

Only the [latest release](https://github.com/dev2180/eye-break/releases/latest) gets security fixes. Eye Break can
update itself: open the panel, then **Settings › Check for updates**.

## Reporting a vulnerability

Please don't open a public issue. Instead, use
[**Report a vulnerability**](https://github.com/dev2180/eye-break/security/advisories/new) on the Security tab, which
reaches the maintainer privately. Include what you found, how to reproduce it, and the version you tested.

You'll get a reply within a week. Once a fix is released, you'll be credited in the advisory unless you'd rather not be.

## What Eye Break touches

So you know what to look at:

- It runs per user, with no admin rights.
- It installs a low-level keyboard hook during breaks, to block the Windows key and Alt+Tab. The hook is removed when
  the break ends.
- It changes the display gamma during breaks only when a game is in exclusive fullscreen, and always restores it.
- It downloads updates only from this repository's GitHub releases, over HTTPS.
- Its settings and log stay on your PC, in `%APPDATA%\EyeBreak`. The only network traffic is GitHub (update checks),
  YouTube (music in infinite mode) and ZenQuotes (only if you pick it).
