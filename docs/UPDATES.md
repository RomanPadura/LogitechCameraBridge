# Velopack releases and updates

The WPF tray application checks `https://github.com/RomanPadura/LogitechCameraBridge`
only when the user selects **About > Check for updates...**. It downloads a newer
stable release, releases the camera/controller, flushes logs, launches the silent
updater and exits. Velopack replaces the installed files and restarts the app.
App updates are silent when the required .NET Desktop Runtime is already installed.
If it is missing, Velopack may prompt to install it before applying an update.
Checks and downloads that fail leave the running bridge in place; retry the menu action later.

This updates the installed tray app on the user's Windows machine (including a
Windows server running it in an interactive user session). It does not deploy a
Windows service. The first installation uses Setup.exe.

## 1. Choose repository visibility

The repository is now public, as confirmed during setup. The application can
read releases anonymously. You do not need a GitHub token on client machines or a
`RELEASES_TOKEN` Actions secret.

Alternatively, keep the repository private and follow the private-access section
below. The implementation supports either visibility; it does not change it for you.

## 2. Push the implementation

Commit and push the updater code, tests, and `.github/workflows/release.yml`.
The workflow must be present in the commit you tag.

In **Settings > Actions > General**, enable GitHub Actions and allow the workflow
to use `contents: write`. The workflow requests this permission and uses the
automatically supplied `GITHUB_TOKEN` to create releases in this same repository.
Unlike the ShortcutDeck example, a separate public releases repository is not used.

Before release, review `src/LogitechCameraBridgeConsole/appSettings.json`:
set `UseControllerStub` to `false` for physical Logitech controller deployment.
This JSON is shared by both hosts and is included in the installer.

## 3. Create the first release

From your checked-out repository after committing the changes:

```powershell
git push origin main
git tag v1.0.0
git push origin v1.0.0
```

Use an unused version tag, greater than existing versions. The tag supplies both
the app version and the Velopack package version; you do not edit the project version.

Open **Actions > Package release with Velopack** and wait for success. The workflow:

1. Builds and tests the solution using .NET 10.
2. Publishes a Windows x64 tray app without bundling .NET.
3. Installs `vpk` 1.2.161, matching the app's Velopack dependency.
4. Downloads the previous stable package when a Velopack feed exists.
5. Builds Setup.exe, the portable archive, full package, delta when possible, and feed.
6. Creates and publishes the GitHub release with the generated assets.

Do not manually create the release first. A rerun uses `--merge` for the same tag.
The first Velopack release needs no previous package. Keep the package ID
`LogitechCameraBridge` and the default `win` channel unchanged in future
releases. All packages are explicitly built for `win-x64`.

The publish command uses `--self-contained false`; packaging specifies
`--framework net10.0-x64-desktop`. Both settings must stay together so Setup.exe
knows which runtime the app requires.

The app's `ApplicationIcon` and Velopack's `--icon` both use
`src/LogitechCameraBridge.Wpf/Assets/LogitechCameraBridge.ico`: the existing green
camera artwork, exported at multiple sizes. This supplies the executable,
installer and Windows Installed Apps icon. See Assets/README.md for regeneration.
Publish a new release and install/update to that build to deploy icon changes.

## 4. Install once on each machine

The application requires **.NET 10 Desktop Runtime for Windows x64**.
Setup.exe checks for this dependency and can download/install it when missing.
An internet connection and approval of any runtime installation prompts are required
when installing the dependency. If it is already installed, no download is needed.

For manual installation or portable ZIP users, open the
[Microsoft .NET 10 downloads page](https://dotnet.microsoft.com/en-us/download/dotnet/10.0),
choose **.NET Desktop Runtime > Windows > x64**, install it, then launch the app.
The plain .NET Runtime alone does not include WPF/Windows Forms. The portable ZIP
does not include the runtime; install the Desktop Runtime before launching it.

Open the repository's **Releases** page and download
`LogitechCameraBridge-win-Setup.exe` from `v1.0.0`.
Exit any old manually copied version, run Setup.exe, and start the installed app.
Running the executable from `bin/Release` or a `dotnet publish` directory does not
provide the installed Velopack layout needed for updates.

The app installs under `%LOCALAPPDATA%\LogitechCameraBridge`; user settings are
stored separately under `%LOCALAPPDATA%\LogitechCameraBridgeData`.
If you already installed a package with the former ID `RomanPadura.LogitechCameraBridge`,
uninstall it and install the new Setup.exe: changing the package ID creates a different
Velopack application. Before switching, exit the app and copy any existing
`%LOCALAPPDATA%\LogitechCameraBridge\userSettings.json` to
`%LOCALAPPDATA%\LogitechCameraBridgeData\userSettings.json` to retain camera selection.

The installer is currently unsigned. Signing can be added to the packaging command
when you have a code-signing certificate; Windows may show a trust prompt for the
initial download/install.

## 5. Publish the next version

After making and committing your next changes:

```powershell
git push origin main
git tag v1.0.1
git push origin v1.0.1
```

Wait for the workflow to publish all assets. Keep `releases.win.json` and the
`.nupkg` assets attached; uploading only Setup.exe does not provide an update feed.
Concurrent releases are queued so packaging can use the preceding release as a
delta base. If the previous stable release has no Velopack feed, packaging starts
with a full package.

For an experimental release, push a tag such as `v1.1.0-beta.1`. The workflow marks
it as a GitHub prerelease; the tray updater ignores prereleases.

## 6. Test the update

1. Keep the installed `v1.0.0` running while `v1.0.1` finishes publishing.
2. Right-click the tray icon and choose **About > Check for updates...**.
3. The menu action becomes disabled while checking/downloading. The bridge keeps
   running during download, then exits and restarts after silent installation
   (a missing runtime can require a separate installation prompt).
4. Reopen **About** and verify the version is `1.0.1`.
5. Check that the selected camera is retained and the controller reconnects.
6. Check again: the app should report that it is already current.

Camera selection remains in `%LOCALAPPDATA%\LogitechCameraBridgeData\userSettings.json`,
outside versioned application files. The packaged `appSettings.json` is replaced
by each release; manual changes inside the install directory are not persistent.

## Optional: access while the repository is private

Each Windows user who runs the app needs a GitHub account with access to this
repository and a fine-grained personal access token restricted to this repository
with **Contents: Read-only** permission (plus required metadata access).
Configure the token under that Windows user's account, using **Edit environment
variables for your account > User variables > New**:

```text
Name:  LOGITECH_CAMERA_BRIDGE_GITHUB_TOKEN
Value: your read-only token
```

Restart the app. The updater checks process environment first, then the current
user's persisted environment. Never put this token in source, appSettings.json,
an installer, or an Actions packaging variable. Environment variables are readable
by processes running as that user; this is per-user provisioning, not a secret
embedded in a distributed client. Token expiry/revocation requires replacing it.

Once the repository is public, remove this variable if anonymous access is desired.
GitHub's anonymous API rate limit is 60 requests per hour per IP; repeated checks
from many machines sharing one public IP can hit that limit.

## References

- [Velopack WPF entry point and packaging](https://docs.velopack.io/getting-started/csharp)
- [GitHub update source and private authentication](https://docs.velopack.io/integrating/update-sources)
- [GitHub Actions packaging](https://docs.velopack.io/distributing/github-actions)
- [Silent apply and graceful exit API](https://docs.velopack.io/reference/cs/Velopack/UpdateManager)
