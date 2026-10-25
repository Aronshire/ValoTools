# ValoTools

**English** · [Türkçe](README.tr.md)

ValoTools is a VALORANT-themed desktop app built with C# and .NET 10 WPF. It loads playable agents from the [Valorant API](https://valorant-api.com/v1/agents?isPlayableCharacter=true). When Riot Client and VALORANT are running, it detects the region from local session data and attempts to lock your chosen agent during agent select.

You can filter agents by role or search by name. The connection card in the lower-left corner displays your Riot name and tag when it detects a VALORANT session. The editable logo source is at `assets/ValoTools.svg`, and the app icon is at `assets/ValoTools.ico`.

The interface uses Figtree for body text and Barlow Condensed for headings. Both fonts are bundled with the app; their license texts are in `assets/fonts`.

## Run from source

Windows and the .NET 10 SDK are required.

```powershell
git clone https://github.com/Aronshire/ValoTools.git
cd ValoTools
dotnet run --project ValoTools.csproj
```

Select an agent and click **İZLEMEYİ BAŞLAT** (Start Monitoring). The app's button labels are currently in Turkish. There is no manual region selector. The app checks the VALORANT session's GLZ address first, Riot Client region data next, and the current ShooterGame log last. If it cannot determine the region reliably, it displays an error instead of silently choosing a default.

The built app is at `dist\ValoTools.exe`. It requires the .NET 10 Desktop Runtime on the computer; keep the other files in `dist` alongside it. On first launch, ValoTools reads existing settings from an older ValoInsta installation.

## Windows installer

`dist\ValoTools-Setup-1.2.2.exe` installs ValoTools on x64 Windows 10/11. It includes the .NET 10 Desktop Runtime, installs for the current user without administrator privileges, adds a Start menu shortcut, offers an optional desktop shortcut, and can be removed through Windows Apps settings. It also upgrades an older ValoInsta installation using the same app ID.

The installed app checks [GitHub Releases](https://github.com/Aronshire/ValoTools/releases/latest) for updates at startup and every 30 minutes while running. The sidebar shows the current version and a **GÜNCELLEMEYİ KONTROL ET** (Check for updates) button. When a new version is available, the app downloads `ValoTools-Setup-<version>.exe` in the background, verifies it against the SHA-256 digest published by GitHub, and shows a **GÜNCELLEMEYİ KUR** (Install update) button. Installation starts when you click that button. Builds run from source do not check automatically, but you can use the manual button.

To rebuild the installer, install the .NET 10 SDK and Inno Setup 7, then run:

```powershell
./installer/build.ps1
```

The installer script is at `installer/ValoTools.iss`. The current distribution does not use a code-signing certificate, so Windows cannot verify its publisher.

See the [code-signing policy](CODE_SIGNING_POLICY.md) for the current signing status and planned process, and the [privacy policy](PRIVACY.md) for the local data and network connections the app uses.

## Publish a new version

Keep `Version` in `ValoTools.csproj` and `AppVersion` in `installer/ValoTools.iss` in sync. Push your changes to GitHub, then push a `vX.Y.Z` tag pointing to the same commit. `.github/workflows/release.yml` builds the Windows installer and publishes it as a GitHub Release asset. Installed clients download the new version the next time they check for updates. You can also build the installer locally with `./installer/build.ps1`.

## Verification

```powershell
dotnet build ValoTools.csproj
dotnet run --project tests\RegionResolverCheck\RegionResolverCheck.csproj
dotnet run --project tests\UpdateCheck\UpdateCheck.csproj
```

This app uses unofficial game-client endpoints. The actual agent-lock flow must be tested with an active VALORANT session and match. The lockfile, token, and pregame methods in [Valorant.js](https://github.com/aronshire/Valorant.js) served as references; ValoTools does not depend on that project's fixed client version.

## License

ValoTools source code is released under the [MIT License](LICENSE). The bundled Figtree and Barlow Condensed fonts have their own SIL Open Font License 1.1 terms, available in `assets/fonts`.
