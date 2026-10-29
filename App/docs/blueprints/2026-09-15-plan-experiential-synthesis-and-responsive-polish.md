# GZT-PLN-018: Implementation Plan — Experiential Synthesis, Responsive Polish & Deep Forensic Hardening

- **Document ID:** GZT-PLN-018
- **Date:** 2026-09-15
- **Author:** Antigravity (CAPITAL-IZ Systems Architect & Execution Engineer)
- **Status:** In Progress
- **Direct Parity Blueprint:** GZT-BLD-018

---

## 1. Objectives & Phased Execution

To achieve complete experiential fluidity and responsive robustness across all workspaces in Ghozt:

1. **Phase 5.1 — Clearance Lifecycle & Responsive Toolbar**:
   - Wire `Loaded` event in `ClearanceWorkspace.xaml.cs` to auto-evaluate active target.
   - Restructure command toolbar to prevent horizontal clipping.
2. **Phase 5.2 — Faults Action Bar & Cross-Navigation Un-clipping**:
   - Replace rigid toolbar `StackPanel` with `WrapPanel`.
   - Remove fixed column constraints on cross-navigation action strip.
   - Widen severity and state dropdowns to 130px.
3. **Phase 5.3 — Targets Header Fluidity & Algorithms Scroll Containment**:
   - Re-architect target card header grid to prevent title clipping.
   - Wrap Tab 3 ("Delta-Debugging Lab") in `ScrollViewer`.
4. **Phase 5.4 — Evidence Ingestion Spacing & Tactile Feedback**:
   - Widen `KindFilter` to 140px.
   - Add in-button verification feedback to `OnVerifyHash` and `OnVerifyAll`.
5. **Phase 5.5 — Reports Auto-Summary on Load**:
   - Wire `Loaded` in `ReportsWorkspace.xaml.cs` to auto-render Executive Dossier Summary.
6. **Phase 5.6 — Test Suite, Mutation Test, Package & UI Verification**:
   - Add `WorkspacesExperientialPhase5SynthesisAndPolishExist` in `RealExecutionTests.cs`.
   - Perform mutation testing (Red $\rightarrow$ Green).
   - Package MSI 1.0.9, install to `C:\Program Files\CAPITAL-IZ\Ghozt\`, commit, and push.
   - Re-run `deep_forensic_drive.py` and verify all 19 screenshots.
