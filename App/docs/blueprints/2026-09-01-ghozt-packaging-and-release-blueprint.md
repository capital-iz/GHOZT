# Ghozt — Packaging, Branding and Release: Build Blueprint

| Field | Value |
|---|---|
| Document | Build blueprint (mechanism, alternatives, failure paths) |
| Product | Ghozt - Dev Tester · publisher **Capital-Iz** |
| Scope | `Apps\Desktop\GHOZT\Windows` — brand assets, WiX MSI, iteration workflow, git |
| Date | 2026-09-01 |
| Status | Implemented; MSI built and its payload verified by execution |
| Repository | https://github.com/capital-iz/GHOZT |

---

## 1. What was built

```
GHOZT/
  .gitignore                              artifacts and .claude excluded, with reasons
  Windows/
    iterate.ps1                           one iteration: bump → test → MSI → commit → push
    Installer/
      brand/build-brand-assets.py         generates every branded image from the master mark
      msi/Package.wxs                     WiX authoring
      msi/build-msi.ps1                   brand → test → publish → package
      msi/License.rtf                     Ghozt's own licence text
      msi/Assets/BannerTop.bmp            493x58,  mark RIGHT
      msi/Assets/DialogBackground.bmp     493x312, mark left-of-centre
    artifacts/Ghozt-<version>.msi         not committed (see §6)
  App/src/Ghozt.App/Assets/GhoztLogo.ico  7 frames, 16px to 256px
```

## 2. The branded signature

Read off the existing suite installers rather than invented. `CapitalIz.CommandDeck-3.1.x.msi` and
`CapitalIz-Suite-3.1.x.exe` establish the pattern, and Ghozt matches it:

| Element | Suite convention | Ghozt |
|---|---|---|
| Toolchain | WiX 5, `WixUI_InstallDir` | same |
| Banner | 493x58, white, Capital-Iz mark at the **right** | same, plus a spectral hairline at the foot |
| Dialog | 493x312, white, mark left-of-centre | same, plus the spectral rail down the left edge |
| Publisher | `Capital-Iz` | same |
| Install root | `Program Files\Capital-Iz\<Product>` | `…\Capital-Iz\Ghozt` |
| Start menu | shared `Capital-Iz` folder | same |
| HKLM registration | `SOFTWARE\Capital-Iz\<Product>` with InstallPath + Version | same, plus `Executable` |
| Upgrade | `MajorUpgrade`, immutable `UpgradeCode` | same, `8F3A6C21-…` |
| Files | `<Files Include="$(var.PublishDir)\**"/>` | same |
| Runtime | self-contained win-x64 | same |

**The mark sits right on the banner because WiX overprints the dialog title on the left.** That is
not a stylistic choice; artwork placed there is covered by "End-User License Agreement".
`BrandingTests.TheBannersTitleZoneIsClearOfArtwork` measures the left 60% is empty and was
mutation-tested: moving the mark to x=200 reported 800 offending pixels.

### Where Ghozt deliberately differs

| Difference | Reason |
|---|---|
| **No autostart** | CommandDeck registers a Run key because a clipboard manager that is not running has missed the copy. Ghozt examines systems under explicit authority; a forensics tool that launches itself at every logon can act before anyone decided it should. |
| Icon is a chamfered void plate in spectral teal | Ghozt's own geometry and colour (GZT-UX-001 §04), carrying the family monogram unchanged. The product is Ghozt; the house is Capital-Iz. |
| `CloseApplication` timeout 15s, not 10 | Ghozt holds a SQLite write-ahead log. The extra time lets it close cleanly rather than be killed mid-transaction. |

## 3. Branding is generated, not drawn

`build-brand-assets.py` derives every image from the single master mark
(`capital-iz-logo-01.png`). Rejected alternative: a folder of hand-made bitmaps. WiX's sizes are
fixed expectations rather than validated constraints — a wrong size is stretched or letterboxed, not
refused — so the geometry is stated once in code and the assets are regenerated rather than
retouched. `build-msi.ps1` regenerates them on every run, so an edited source mark cannot leave a
stale banner in the package.

### Failure path found and fixed

The icon's chamfer was first built by painting a rounded rectangle, cutting the corner, and drawing
the diagonal back. The three operations disagreed by a pixel and left a visible seam, and the
rounded rectangle added to patch it reintroduced the corner the chamfer had removed. Rebuilt as a
single mask, cut once: no seam to get wrong.

## 4. Full dependency inclusion

Self-contained `win-x64` publish: **256 files, 170.9 MB**, compressed to a **57.4 MB** MSI.

`build-msi.ps1` refuses to package a publish of fewer than 100 files — far fewer means the runtime
was not carried, which produces an installer that works only on the author's machine. It also fails
if `Ghozt.exe` or `GhoztLogo.ico` is missing from the publish folder rather than building an
unbranded or unrunnable package.

**Verified by execution, not by inspection.** `msiexec /a` extracted the payload to a clean folder
and `Ghozt.exe` was run from there with `--capture`: it started, rendered the full shell, and wrote a
PNG. Confirmed present in the package: `coreclr.dll`, `hostfxr.dll`, `PresentationFramework.dll`,
`WindowsBase.dll`, `SQLitePCLRaw.batteries_v2.dll`, `GhoztLogo.ico`.

Both branded bitmaps were extracted from the MSI's OLE streams and confirmed to be Ghozt's own at
493x58 and 493x312 — not inherited from another product.

## 5. The iteration workflow

`Windows\iterate.ps1 -Message "…" [-Bump patch|minor|major|none] [-NoPush]`

1. Bump `App\Directory.Build.props` (the single source of version truth).
2. Regenerate brand assets → run tests → publish self-contained → package the MSI.
3. `git add` **by explicit path** → commit → push.

**The MSI is built before the commit deliberately.** A commit claiming to be an iteration whose code
does not package is worse than no commit: the history then contains versions that were never
buildable and nobody can tell which. A failing test stops the run and produces no installer.

### Failure paths found and fixed

| Failure | Cause | Fix |
|---|---|---|
| Script would not parse | Em-dashes in a `.ps1`; PowerShell 5.1 reads the file as ANSI without a BOM | Scripts are ASCII, as the sibling build scripts are |
| A successful `git add` aborted the run | PowerShell raises `NativeCommandError` for **any** native stderr under `$ErrorActionPreference = 'Stop'`, and git reports line-ending conversion there | `Invoke-Git` relaxes the preference for the call only and checks the exit code — the real signal. Redirecting the stream and assigning it to a variable were both tried first and neither suppresses it. |
| WiX CAB step fails | Long `TEMP` | The script sets `C:\cizt` itself rather than relying on the caller |

## 6. Artifacts are not committed

`Windows/artifacts/` and `Windows/build/` are ignored. A self-contained MSI is ~57 MB and GitHub
refuses any blob over 100 MB outright, so a repository carrying every iteration's installer would
stop accepting pushes within a few builds. Releases go to the private registry at
admin.capital-iz.com, which is where the in-app updater looks; git carries the source that produces
them.

`.claude/` is ignored too: it holds worktrees of a *different* repository that happen to sit inside
this folder, and sweeping them into a commit is how another agent's in-flight work gets committed by
mistake.

## 7. Verification performed

- 143 tests pass, including 6 branding tests.
- Mutation-tested the banner clearance check: moved the mark left, watched it report 800 offending
  pixels, restored.
- MSI properties read back from the package: `ProductName = Ghozt - Dev Tester`,
  `Manufacturer = Capital-Iz`, `ProductVersion = 0.1.1`, `UpgradeCode = 8F3A6C21-…`,
  `ARPPRODUCTICON = GhoztIcon`.
- 258 file rows, 261 components, both shortcuts named `Ghozt`.
- Payload extracted and executed successfully.
- `Ghozt.exe` metadata: ProductName `Ghozt - Dev Tester`, Company `Capital-Iz`, FileVersion
  `0.1.1.0`.
- Two full iterations run through `iterate.ps1`, both pushed
  (`4f5501b..150ef39`, `150ef39..b485766`).

## 8. Not done

- **The MSI has not been installed.** `msiexec /i` returned 1603 with **Error 1925 — insufficient
  privileges**: a perMachine package needs an elevated shell and this session is not elevated. The
  administrative install and the payload execution above cover everything except the registry and
  shortcut writes. **Owner action: run the MSI from an elevated prompt, or double-click it.**
- **Not published to the registry.** The standing rule is that every build goes to
  admin.capital-iz.com. `iterate.ps1` prints the command but does not run it, because publishing is
  outward-facing and needs the owner's approval and the `ghozt` slug registering first.
- **Unsigned.** Public channels require signing; this is `--signing-status=unsigned`, private
  channel only.
- No `.exe` suite bundle. CommandDeck ships one that chains its MSI with Mouse n Key. Whether Ghozt
  joins that bundle is a product decision.
