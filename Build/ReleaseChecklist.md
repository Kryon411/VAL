# Release Checklist

- [ ] CI is green for the exact release revision.
- [ ] Version and release notes are approved.
- [ ] Source manifest is current.
- [ ] Release publish completes from locked dependencies.
- [ ] Installer version matches `Directory.Build.props` and compiles from the tracked Inno script.
- [ ] `VAL.exe` and installer are timestamp-signed and signatures validate.
- [ ] SHA-256 checksums match the packaged executable and installer.
- [ ] Automated smoke and supported-machine manual checks pass.
- [ ] Clean install, prior-version upgrade, rollback, and uninstall have been tested without user-data loss.
- [ ] Release artifacts are retained and the rollback owner is identified.
