# Roblox login in Nexus

Open **บัญชี Roblox → เพิ่มบัญชี Roblox · รอ 15 นาที**. Nexus checks the installed Real account store before opening the sign-in page. Complete the normal Roblox website login, CAPTCHA and two-step verification in the embedded browser. Nexus waits up to 15 minutes; it does not solve challenges or retrieve codes.

After Roblox confirms the session, Nexus shows the username, display name and user ID. Review them and click **เพิ่มบัญชีนี้เข้า Real**. Only this click starts import. Nexus checks the session with Roblox again, checks that the local Real data can be read, closes Real, and merges the reviewed account. Open Real afterwards to see the account and launch games normally.

Nexus integrates with the local Real 3.1.1 and 3.2.1 device vault on the same Windows account. The importer requires a supported release and vault format. Read-only preflight passed on installed Real 3.2.1. Automated import tests use synthetic account data, and recognition of an imported live account by Real remains a manual verification step.

## Data handling

- The login uses a separate, temporary WebView2 profile. It does not use Real's browser profile or extend its built-in timer.
- The session is read from the first-party secure Roblox cookie in memory and verified against Roblox's authenticated-user API. The public profile's ID and username must match that identity.
- Passwords and verification codes are entered into Roblox's page. Nexus does not read them or write them to logs, the clipboard, or a package.
- The reviewed session is encrypted with the existing Real device-vault key using AES-256-GCM and a new random nonce. The device key is opened with Windows DPAPI for the current user.
- Existing accounts, IDs, aliases, groups and unknown fields are preserved. Reimporting the same user ID updates the stored session and rotation time.
- Before replacing the account file, Nexus saves a DPAPI-encrypted backup of the complete original JSON in the current user's local Nexus data. This also protects backups of older plaintext-cookie entries.
- Import checks that Real is stopped and that the account file, vault settings and vault key have not changed. Replacement is atomic. Failed verification rolls back only if the current bytes still belong to this transaction, preserving later external edits. Other atomic writers can still create a narrow replacement race; use Real closed and avoid other account editors while importing.
- Browser cookies are cleared when the session has been reviewed or the login window closes. The owned temporary profile is removed after WebView2 shuts down; a forced process termination can interrupt cleanup. No process outside this login is killed for cleanup.

Cleanup is bounded. Cancelling while the browser runtime is still initializing may leave an unused temporary cache directory if its handles remain locked. Nexus does not forcibly terminate other browser processes to remove it.

This workflow does not copy, patch, restore, or execute the Real program binary, and does not change Windows Security settings. A recorded security block on the old timer patch remains in effect independently.

## Distribution

The Microsoft WebView2 SDK wrappers and x86/x64 loader are embedded in the single Nexus executable. The signed PC updater therefore continues to replace only that executable. The SDK files, package URL and hashes are pinned under `lib/WebView2`; `tools/FetchWebView2.py` can reproduce them. The SDK license is embedded and available from the login window and the portable guide.

An installed Microsoft Edge WebView2 Runtime is required. If missing, the login window provides the official Microsoft download link. Runtime DLLs are prepared in the current user's local Nexus data, separate from the temporary login profile.
