# Release Checklist

- [ ] CI is green for the exact release revision.
- [ ] Version and release notes are approved.
- [ ] Source manifest is current.
- [ ] Release publish completes from locked dependencies.
- [ ] Installer version matches `Directory.Build.props` and compiles from the tracked Inno script.
- [ ] `VAL.exe` and installer are timestamp-signed and signatures validate.
- [ ] SHA-256 checksums match the packaged executable and installer.
- [ ] Complete payload fingerprints agree across installer metadata, lifecycle reports and the machine kit; no files changed after validation.
- [ ] Automated smoke and supported-machine manual checks pass.
- [ ] Published locked-cleanup/retry and invalid-cleanup startup gates pass using isolated synthetic data.
- [ ] Isolated installer path rejection, payload hashes, startup, same-version repair, and uninstall/data-retention checks pass.
- [ ] Exact tagged v5.0.0 archive compatibility and isolated v5.0.0/candidate payload upgrade/rollback checks pass for the candidate hashes.
- [ ] Authenticated component checks are recorded against an isolated synthetic conversation/profile, without uploading authentication data.
- [ ] Original production-installer machine kit is run on pristine disposable supported Windows machines; staging alone does not satisfy this gate.
- [ ] Clean install, prior-version upgrade, rollback, and uninstall have been tested without user-data loss.
- [ ] Release artifacts are retained and the rollback owner is identified.
