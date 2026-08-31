---
name: publish-every-release-to-vps-registry
description: "Standing rule - every desktop/mobile release build must be published to the admin.capital-iz.com registry on the VPS, because the in-app updater serves from it."
metadata: 
  node_type: memory
  type: project
  originSessionId: 24ef8bf9-16b3-4393-a279-c618b6b265d1
  modified: 2026-08-29T21:27:04.351Z
---

Every release build (CommanDeck / Mouse n Key MSI, the suite bundle, and mobile APK/AAB once
signed) must be **published to the app-store registry on admin.capital-iz.com** as a standing part
of the release process — not only when Rea asks. The in-app update feature fetches from that
registry, so a build that is not there is a build users cannot receive as an update.

Rea's words: *"you have not maintained memory when i said always push to admin.capital-iz on vps
for in app updates, i dont want to keep repeating this ... its part of the system."*

**How to publish** (established working procedure): the site is live at admin.capital-iz.com on the
gtower VPS (`ssh gtower-vps`, real host 169.58.98.213, app at
`/home/capital-iz/apps/capital-iz/capital-iz-admin`). Artefacts go to the **private** channel
(unsigned is allowed there; public channels still require signing). Command:
`php artisan app-store:publish-artefact <slug> <version> <file> --channel=private
--signing-status=unsigned --approved-by=release-approver@capital-iz.com --source-commit=<sha>
--publish`. Slugs: `commandeck`, `mouse-n-key`, `capital-iz-desktop-suite`. The registry refuses a
build with no `--source-commit`, so always pass the real commit hash. Verify the sha256 on the VPS
matches the local build after upload.

**Do this as part of finishing a build**: build → verify tests → hand the local file path to Rea →
publish to the registry. Back up the admin DB before any migration. Never relax the public
channels' signing requirement. Related: [[installers-carry-every-dependency]],
[[patent-standard-blueprints]].
