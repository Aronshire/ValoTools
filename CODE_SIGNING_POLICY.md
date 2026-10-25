# Code signing policy

## Current status

ValoTools 1.2.2 and earlier Windows installers are **unsigned**. The project is seeking a trusted code signing route; a release must not be described as signed until its Authenticode signature has been verified. Adding the MIT license does not sign existing files.

## Intended release procedure

The maintained source and build scripts are in [Aronshire/ValoTools](https://github.com/Aronshire/ValoTools). Aronshire is the current author, committer, reviewer, and release approver. Contributions from other authors are reviewed before merging. A release is built on a GitHub-hosted Windows runner from a version tag matching the project and installer versions.

If SignPath Foundation accepts the project, release signing will use SignPath.io with a certificate issued to SignPath Foundation. Each signing request will require a manual approval. The signed installer will be checked with Windows Authenticode verification before it replaces the unsigned release artifact. The release page will identify the signing provider and link to this policy. Until that integration is active, the GitHub release asset remains unsigned.

The [privacy policy](PRIVACY.md) describes local data and network connections. Windows installation and uninstallation are described in the [README](README.md).
