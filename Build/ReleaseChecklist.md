# Release Checklist

- [ ] CI is green for the exact release revision.
- [ ] Version and release notes are approved.
- [ ] Source manifest is current.
- [ ] Release publish completes from locked dependencies.
- [ ] `VAL.exe` and installer are timestamp-signed and signatures validate.
- [ ] SHA-256 checksum matches the packaged executable.
- [ ] Automated smoke and supported-machine manual checks pass.
- [ ] Upgrade, rollback, and uninstall have been tested.
- [ ] Release artifacts are retained and the rollback owner is identified.
