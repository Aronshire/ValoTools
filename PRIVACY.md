# ValoTools Privacy Policy

Last updated: 25 October 2026

ValoTools is a local Windows application. It has no developer-operated server, account system, advertising, or analytics. It does not send Riot credentials, Riot ID, match data, or logs to the ValoTools maintainer.

## Data used on your computer

- ValoTools reads the Riot Client lockfile and, when necessary to determine the game region, the recent `ShooterGame.log` file. It communicates with the Riot Client through its local HTTPS interface (`127.0.0.1`).
- It reads the current Riot ID for display and obtains temporary session tokens from the local Riot Client. These tokens are kept in application memory and used only to make the requested VALORANT pregame API calls. They are not written to ValoTools settings.
- The selected agent ID is saved in `%APPDATA%\ValoTools\settings.json`. On first use, an older `%APPDATA%\ValoInsta\settings.json` may be read to restore the previous selection.
- Downloaded installers are stored in `%LOCALAPPDATA%\ValoTools\Updates` until removed by the user. The application verifies their SHA-256 digest before offering installation.

## Network connections

- `valorant-api.com` supplies the public agent list. Agent artwork is loaded from the image URLs supplied by that API. No Riot token is sent with these requests.
- Riot Games' VALORANT game service receives authenticated pregame requests to detect agent selection and, when the user starts monitoring, lock the selected agent.
- `api.github.com` is queried for new ValoTools releases when the installed app starts, every 30 minutes while it runs, or when the user presses the update button. If a newer release is available, its installer is downloaded from GitHub. No Riot token or Riot ID is sent to GitHub.

These services receive the normal network information required for a request, such as the device's public IP address. Their handling of that information is governed by their own policies: [Riot Games Privacy Notice](https://www.riotgames.com/en/privacy-notice) and [GitHub Privacy Statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement). The community-run `valorant-api.com` service is independent of ValoTools and Riot Games.

## Control and deletion

Agent monitoring starts only after the user selects an agent and presses **İzlemeyi Başlat**. The update installer starts only after the user presses **Güncellemeyi kur**. Closing ValoTools stops its monitoring and update checks. To remove locally stored ValoTools data, delete the two ValoTools folders named above after closing the application.

For questions or corrections, open an issue in the [ValoTools repository](https://github.com/Aronshire/ValoTools/issues).
