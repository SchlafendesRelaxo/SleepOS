# SleepOS installer and updates

The first install is `SleepOS-Setup.exe`. Updates are checked from the public
`SchlafendesRelaxo/SleepOS` GitHub Releases page in Settings. Users who run a
portable build need to install the setup once; later stable releases can be
downloaded and applied inside SleepOS.

The public repository already exists. To publish an update:

1. Push the current source, including `.github/workflows/release.yml`, to its
   `main` branch.
2. Push a new, higher semantic-version tag such as `v1.0.8`.
3. GitHub Actions runs tests, builds the x64 installer and update packages,
   and attaches the required files to that GitHub Release.

Do not upload only the setup executable. The generated full package and
`releases.win.json` are required by the in-app updater.
