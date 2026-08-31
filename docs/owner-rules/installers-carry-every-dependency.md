---
name: installers-carry-every-dependency
description: "Global rule - every installer, PC or mobile, must carry 100% of what it needs to run on a machine that is not the author's."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 24ef8bf9-16b3-4393-a279-c618b6b265d1
  modified: 2026-08-29T16:08:27.259Z
---

Every package Capital-Iz ships — MSI, bundle, APK, AAB — must carry **100% of the dependencies
needed to run on a machine that is not Rea's**. Nothing may rely on something that happens to be
present on the development machine.

Rea's words: *"they must come carrying 100% of the dependencies needed to run on another machine
that is not mine, mine has the benefit of being the author system so things may work on mine but
not on others, i dont want this - this is global agent rule."*

**Why:** the author's machine accumulates runtimes, SDKs, codecs, drivers and registry state that
a customer's will not have. Software that only works where it was written is not shipped software.
Rea cites AudioRelay as the standard to match: it packaged its own virtual audio driver rather
than asking the user to find one. **Virtual drivers are explicitly authorised** where a feature
needs one — a phone-as-webcam that other applications can select needs a virtual camera driver,
and shipping it inside the installer is the expected answer, not a reason to decline.

**How to apply:** publish self-contained, never framework-dependent. Bundle native runtimes,
codecs and drivers in the installer rather than detecting and instructing. When something
genuinely cannot be bundled, say so explicitly and record it as a limit rather than letting it
pass silently — the known case is Store-gated Windows image extensions (HEIC, AVIF, JPEG XL,
camera raw), which Microsoft distributes only as Store packages with no redistributable form.
Test on a machine that is not the author's, or prove the claim some other way; "it works here" is
not evidence. Related: [[patent-standard-blueprints]], [[local-is-the-authority]].
