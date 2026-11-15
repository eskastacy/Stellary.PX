# 06 · Release

The release pipeline produces a framework-dependent Windows x64 build, a
SHA-256 manifest, and a versioned ZIP. Signing is optional but strictly real: the
scripts require `signtool.exe` and a CA-issued certificate, and verification
fails unless `Get-AuthenticodeSignature` reports `Valid`.

## Versioning

The version lives in `src/Stellary.PX/Stellary.PX.csproj`:

```xml
<Version>0.1.0</Version>
<AssemblyVersion>0.1.0.0</AssemblyVersion>
<FileVersion>0.1.0.0</FileVersion>
```

`Build-Release.ps1` accepts `-Version` and overrides all three. A three-part
version is expanded to four parts for the file/assembly attributes. Use semantic
versioning and tag releases `v<version>`.

## One-command build

```powershell
.\scripts\Build-Release.ps1 -Version 0.1.0
```

Output under `dist\`:

```text
dist\
├─ Stellary.PX-0.1.0-win-x64\
│  ├─ Stellary.PX.exe
│  ├─ Stellary.PX.dll
│  ├─ Stellary.PX.deps.json
│  ├─ Stellary.PX.runtimeconfig.json
│  ├─ Stellary.PX.pdb
│  ├─ branding.json
│  ├─ README.md, LICENSE, CHANGELOG.md, SECURITY.md, CONTRIBUTING.md
│  ├─ docs\*.md
│  ├─ screenshots\*.png
│  ├─ scripts\Verify-Release.ps1, New-Checksums.ps1
│  └─ SHA256SUMS.txt
├─ Stellary.PX-0.1.0-win-x64.zip
└─ Stellary.PX-0.1.0-win-x64.zip.sha256
```

The command:

1. Resolves a dotnet host that actually has an SDK.
2. Builds the solution in Release and runs the read-only harness.
3. Publishes framework-dependent by default (`-SelfContained` to change).
4. Copies docs, screenshots, license, and verification scripts into the package.
5. Signs the executable if credentials are supplied.
6. Writes `SHA256SUMS.txt` over the final, post-signing files.
7. Archives the folder and writes a separate SHA-256 for the ZIP.

`-SkipTests` skips step 2 when CI already ran the harness. Do not use it for a
local release candidate.

## Signing

### Prerequisites

- A code-signing certificate issued by a public CA (DigiCert, Sectigo, SSL.com,
  GlobalSign, etc.). OV or EV both work; EV usually requires a hardware token.
- `signtool.exe` from the Windows SDK.
- A timestamp service. The script defaults to
  `http://timestamp.digicert.com`.

This repository does **not** include a certificate. A self-signed certificate is
not a release signature and is not supported by the scripts.

### PFX file

```powershell
.\scripts\Build-Release.ps1 -Version 0.1.0 `
  -Sign -Pfx .\secrets\codesign.pfx `
  -PfxPassword (Read-Host -AsSecureString)
```

The PFX path and password are never written to the repository. Keep `secrets/`
ignored and backed up securely. The password is passed to `signtool` on the
command line; run the build on a trusted machine and avoid shared shells.

### Certificate store / hardware token

```powershell
# Find the thumbprint
Get-ChildItem Cert:\CurrentUser\My |
  Where-Object { $_.EnhancedKeyUsageList.ObjectId -contains '1.3.6.1.5.5.7.3.3' } |
  Format-List Subject, Thumbprint, NotAfter

.\scripts\Build-Release.ps1 -Version 0.1.0 `
  -Sign -Thumbprint <SHA1-THUMBPRINT>
```

For a token, unlock it when `signtool` prompts. EV tokens may require a
manufacturer middleware driver.

### Sign only (existing build)

```powershell
.\scripts\Sign-Release.ps1 -Path .\dist\Stellary.PX-0.1.0-win-x64\Stellary.PX.exe `
  -Pfx .\secrets\codesign.pfx -PfxPassword (Read-Host -AsSecureString)

# Re-hash after signing; signatures change the file bytes.
.\scripts\New-Checksums.ps1 -Path .\dist\Stellary.PX-0.1.0-win-x64
```

If you sign after archiving, delete and rebuild the ZIP and its `.sha256`.
Always sign **before** the final checksum and archive.

### Timestamping

Every signature uses RFC 3161 SHA-256 timestamping:

```text
/tr http://timestamp.digicert.com /td SHA256
```

A timestamp keeps the signature valid after the certificate expires. If the
timestamp service is unavailable, the script fails rather than producing an
untimestamped release.

## Verification

```powershell
# Folder and manifest
.\scripts\Verify-Release.ps1 `
  -Path .\dist\Stellary.PX-0.1.0-win-x64 `
  -Sums .\dist\Stellary.PX-0.1.0-win-x64\SHA256SUMS.txt

# Single executable, requiring a valid signature
.\scripts\Verify-Release.ps1 `
  -Path .\dist\Stellary.PX-0.1.0-win-x64\Stellary.PX.exe `
  -Sums .\dist\Stellary.PX-0.1.0-win-x64\SHA256SUMS.txt `
  -RequireSignature

# Native check
Get-AuthenticodeSignature .\dist\Stellary.PX-0.1.0-win-x64\Stellary.PX.exe |
  Format-List Status, StatusMessage, SignerCertificate

# Archive
Get-FileHash .\dist\Stellary.PX-0.1.0-win-x64.zip -Algorithm SHA256
Get-Content .\dist\Stellary.PX-0.1.0-win-x64.zip.sha256
```

Unsigned builds report `NotSigned`. Release notes must say **unsigned** in that
case. Never publish a SHA-256 that was generated before signing, and never paste
a hash into prose as the only source of truth — the manifest is authoritative.

## GitHub release

1. Update `CHANGELOG.md` and write `docs/RELEASE-NOTES-<version>.md`.
2. Commit, tag, and push:

   ```powershell
   git tag v0.1.0
   git push origin v0.1.0
   ```

3. The `release` workflow builds the tagged commit. If
   `CERT_PFX_BASE64` and `CERT_PASSWORD` repository secrets exist, it decodes the
   PFX into the runner temp directory, signs, verifies, hashes, and archives. If
   they do not exist, it publishes an explicitly unsigned build.
4. Attach:

   - `Stellary.PX-<version>-win-x64.zip`
   - `Stellary.PX-<version>-win-x64.zip.sha256`
   - `SHA256SUMS.txt` (inside the ZIP and as a standalone asset)
   - `docs/RELEASE-NOTES-<version>.md`

5. State the signature status and signer subject in the release body.

### Repository secrets for signed CI

| Secret | Contents |
|---|---|
| `CERT_PFX_BASE64` | Base64 of the PFX file |
| `CERT_PASSWORD` | PFX password |

Generate the base64 locally:

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes('.\secrets\codesign.pfx')) |
  Set-Clipboard
```

Remove the PFX from the runner after the build. The workflow does this in a
`finally`-style step and never uploads the certificate.

## Release checklist

- [ ] Version bumped in csproj, changelog, and release notes.
- [ ] `dotnet build -c Release` clean.
- [ ] Safe harness: 16/16 pass.
- [ ] `--system` harness: 19/19 pass on a controlled machine.
- [ ] All six captures reviewed; log shows the matching `selected='<key>'`.
- [ ] No new NuGet dependencies; `dotnet list package` shows none.
- [ ] No secrets, PFX files, certificates, logs, or machine paths in the diff.
- [ ] `Build-Release.ps1` produces the folder, manifest, ZIP, and ZIP hash.
- [ ] `Verify-Release.ps1` passes for the folder and the exe.
- [ ] Signature status and signer recorded in the release notes.
- [ ] SHA-256 values match the published manifest.
- [ ] Tag pushed; GitHub release assets uploaded.

## Rollback

If a release is bad:

1. Mark the GitHub release as a pre-release or delete it.
2. Publish a fixed patch version; do not reuse the version or tag.
3. Keep the old manifest for audit, but make the new release's notes explicit
   about the superseded build.
4. If a signed binary was distributed and the key is compromised, revoke the
   certificate through the CA and publish an advisory. A new signature cannot
   make an old compromised binary trustworthy.
