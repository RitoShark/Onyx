# Security

## Supported versions

Security fixes target the latest Onyx release. Older builds do not receive backports.
Onyx checks for app updates in the bottom bar and links to the release page. Close
Onyx and replace the executable to install a new version.

## Reporting

Do not open a public issue for a security bug.

Use GitHub's private vulnerability reporting on this repository if available
(Security tab, "Report a vulnerability"). Otherwise, contact the maintainers privately.
Include the Onyx version, affected plugin and host, reproduction steps, and expected
versus actual behavior. Remove credentials and personal paths from any logs you share.

Worth reporting:

- A downloaded plugin archive writing outside its extraction or intended install folder.
- A catalog entry or release asset bypassing install-plan validation or a declared checksum.
- An elevated install or uninstall job performing unintended operations with administrator access.
- Uninstall removing files outside the recorded installation, or registration affecting
  an unintended DLL.
- A download or app-update link being redirected to an unintended source through untrusted data.

Ordinary support issues can be reported publicly, unless they also expose a security risk:

- A host installation not being detected, or a manually chosen folder not being remembered.
- GitHub rate limits, unavailable releases, or failed downloads.
- A plugin failing to load in its host after installation.
