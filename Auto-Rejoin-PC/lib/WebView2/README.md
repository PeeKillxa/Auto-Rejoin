# Microsoft WebView2 build inputs

These files come from the official `Microsoft.Web.WebView2` NuGet package, pinned to `1.0.4314.35`:

- [NuGet package](https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.4314.35)
- [Microsoft WebView2 documentation](https://learn.microsoft.com/microsoft-edge/webview2/)

`package.json` records package and member SHA-256 digests. Run `python tools/FetchWebView2.py` to restore these exact inputs from NuGet. The application embeds the managed SDK assemblies, x86/x64 loader, and license; the versioned native loader is verified and extracted into the current user's Nexus runtime cache. The evergreen WebView2 browser runtime itself is provided by Microsoft and is not bundled or installed by this script.

Redistribution terms are in [LICENSE.txt](LICENSE.txt), embedded in Nexus, and available from its login window. No Real executable or account information is included.
