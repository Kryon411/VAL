# Disposable-Machine Validation

This kit contains the original published VAL v5.0.0 installer and an unreleased v5.1.0 candidate. It is not a customer download or evidence that machine testing has passed. The candidate is unsigned unless the build was explicitly signed.

Use a disposable Windows x64 virtual machine or a dedicated throwaway machine/user account with no VAL installation, data, browser profile, or uninstall registration. Never run this kit against a personal VAL installation. Install PowerShell 7 for the validation script. Preserve the VM snapshot before testing.

From PowerShell 7 in the copied kit directory:

```powershell
./Verify_DisposableMachine.ps1 -KitRoot $PWD -ConfirmDisposableMachine
```

The guard refuses existing VAL installations or data even with the acknowledgement. The script verifies both installer hashes, installs the original v5.0.0 customer package, creates synthetic preferences and archives, upgrades to the candidate, rolls back to v5.0.0, checks executable hashes and the stable production uninstall identity, then uninstalls only the owned installation. Rollback first uninstalls the candidate while retaining data, then reinstalls the original baseline; an in-place downgrade can leave newer assets behind. The restored baseline must have exactly its original application files and hashes. Synthetic archives and disabled-capture preferences must remain unchanged. Logs and a scoped JSON result are written to a new Results directory. It does not delete existing data or infer that a machine is disposable from its hostname.

This script does not launch the legacy desktop executable. Before discarding the VM, separately run clean startup, WebView2 acquisition/missing-runtime recovery, actual runtime upgrade and rollback, installer repair, and an authenticated component pass with a dedicated test account and synthetic conversations. Verify data readability after each runtime transition. Test the intended supported Windows versions, DPI/display configurations, standard-user account, and offline recovery. Do not reuse a personal browser profile.

Return the Results directory and a record of the manual checks to the review. Do not mark the supported-machine gate complete merely because the kit was staged or the local isolated-payload tests passed. VM snapshots, application/data backups, and a retained previous installer form the recovery route; VAL does not yet implement an automatic application updater or rollback manager.
