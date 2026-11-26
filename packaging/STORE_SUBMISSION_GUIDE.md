# Publishing Eigen to the Microsoft Store (Free Individual Account)

## 1. Register Your Free Developer Account
1. Go to [storedeveloper.microsoft.com](https://storedeveloper.microsoft.com) and select **Individual developer (free)** — there is no \$19 fee and no credit card required.
2. Complete identity verification (government-issued photo ID + selfie).
3. Reserve your app name (`Eigen` or your chosen store listing name) in Partner Center and copy your **App Identity** values (`Package/Identity/Name`, `Package/Identity/Publisher`, and `Package/Properties/PublisherDisplayName`).

## 2. Build the Store `.msix` Package
Run from PowerShell (using your Partner Center values):

```powershell
powershell -ExecutionPolicy Bypass -File "d:\Win Explowrer\packaging\build-msix.ps1" `
  -Version "1.0.0.0" `
  -IdentityName "YOUR_PARTNER_CENTER_NAME" `
  -Publisher "CN=YOUR_PARTNER_CENTER_PUBLISHER_GUID" `
  -PublisherDisplayName "Your Name"
```

The script:
1. Publishes `Eigen.csproj` as self-contained `win-x64` (`ReadyToRun=true`, so end users don't need `.NET 9 Desktop Runtime` installed separately).
2. Stamps `Package.appxmanifest` and copies `packaging\Assets\*.png`.
3. Builds `d:\Win Explowrer\packaging\out\Eigen_<Version>_x64.msix` using `makeappx.exe` (automatically restoring `Microsoft.Windows.SDK.BuildTools` if Windows SDK isn't installed locally).

Note: Do **not** self-sign with a test certificate for Store upload — Microsoft Partner Center signs the uploaded `.msix` / `.msixupload` package automatically.

## 3. Restricted Capability Justification (`runFullTrust`)
When submitting in Partner Center, declare why Eigen uses `runFullTrust`:

> **Eigen** is a desktop file manager and Windows Explorer replacement. Full-trust access (`runFullTrust`) is required so users can browse local drives and user folders, move items to the Recycle Bin, launch files with default desktop applications, and invoke installed Windows Explorer shell context menu extensions (`IContextMenu` / `CNewMenu`) directly from the UI.

## 4. Store Listing Checklist
- Privacy Policy URL (state clearly that Eigen operates offline locally on the user's files and collects/transmits no personal data or telemetry).
- Age Rating questionnaire.
- Screenshots showing Details view, Tiles/Compact view, Skins/Appearance drawer, and Dual-pane mode.
