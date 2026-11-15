# Real account login wait

The **เวลาล็อกอิน Real** control changes the existing Roblox account browser-login wait from 2 to 15 minutes. It does not enter passwords, solve CAPTCHA, retrieve verification codes, or change account records. Complete login in Real normally.

Close Real before applying the change. Nexus never closes Real or Roblox automatically for this operation. Use **คืนค่า 2 นาที** to restore the original executable. A different Real update is rejected until its executable is reviewed; do not copy patched Real binaries to other machines.

## Reviewed artifact

- Installed directory: `real-3.1.1`
- Executable size: 23,497,216 bytes
- SHA-256: `6166e60d3bbd979fe334b9a3d96bbe846f188db1c52e8b40f309879d2276842e`
- Original Authenticode status: NotSigned
- Login polling function VA: `0x140281850` to `0x140282a5d` (exclusive).
- At VA `0x140281ed8`, file offset `0x2812d8`: `48 b8 00 00 00 00 f0 00 00 00` initializes a zero counter and a 240-poll limit. The limit is the four-byte field at file offset `0x2812de`.
- At VA `0x140282018`, the function compares the counter to the limit; the limit branch leads to `0x140282310`, referencing `Login timed out after 2 minutes`.
- The wait at VA `0x140282049` passes 500,000,000 nanoseconds per poll. 240 polls is 120 seconds; 1,800 polls is 900 seconds. Processing time can make the total slightly longer.

The patch changes only the poll-limit immediate, preserving the polling interval, success, cancellation, and normal login logic. The original error text is retained. The full executable hash is checked after normalizing only that field. The installed file is not modified during inspection.

An immutable verified original is retained in the current user's local Nexus data. Application uses a staged file and an atomic file replacement; unknown builds and unrelated modifications are refused. Tests use synthetic executable fixtures and do not log into a real account. Live CAPTCHA/2FA completion is a manual verification step.

## Current validation limit

The 32 isolated transaction checks pass. Windows Security quarantined an **unmodified** temporary copy of this Real executable as `Trojan:Win32/Egairtigado!rfn` before the real-file patch test could run. This is not evidence that the detection is a false positive. The installed original was left unchanged and its original hash verified. No Windows Security settings were changed. The feature is experimental; a successful installed-file patch and live login have not been established.
