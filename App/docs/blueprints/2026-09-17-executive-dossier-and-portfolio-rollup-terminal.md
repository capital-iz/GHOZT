# Ghozt Blueprint: Executive Dossier & Estate Portfolio Rollup Terminal
**Blueprint Code:** GZT-UX-005  
**Classification:** Patent-Standard Architecture Specification  
**Status:** IMPLEMENTED  
**Date:** 2026-09-17  
**Authority:** Capital-Iz Systems Architecture & Universal Design System v2.0  

---

## 1. Executive Summary & Purpose

The **Reports Workspace** in Ghozt serves as the definitive presentation and governance interface for forensic software examination. Historically, report presentation was constrained to a single-column container (`MaxWidth="900"`), resulting in substantial horizontal whitespace on high-resolution displays and requiring multiple manual steps to inspect cryptographic seals, export multi-format technical documents, and review estate-wide readiness.

**GZT-UX-005** elevates `ReportsWorkspace` into a high-density, two-column **Executive Dossier & Estate Portfolio Rollup Terminal** (`Column 0: Width="*"`, `Column 1: Width="480"`):
1. **Primary Dossier & Rollup Viewport (`Column 0`)**: An unconstrained, responsive canvas rendering complete executive summaries, clearance gate verification matrices, deviation catalogs with 95% Wilson score confidence intervals, and state lattice traversal metrics.
2. **Executive Report Operations & Portfolio Analytics Panel (`Column 1`, `ReportOperationsPanel`)**: A dedicated forensic terminal providing cryptographic Merkle report sealing, live estate portfolio governance (Patent §41), a 5-format multi-export dispatch hub, and an interactive in-app document reader.

---

## 2. System Architecture & Component Model

```
+-------------------------------------------------------------------------------------------------------+
| Reports Workspace (ReportsWorkspace.xaml)                                                             |
| +---------------------------------------------------------------------------------------------------+ |
| | Top Command Header: [Generate Dossier] [Export MD/HTML/JSON/SARIF/JUnit] [Portfolio Rollup]      | |
| +---------------------------------------------------------------------------------------------------+ |
| | Row 1: Two-Column High-Density Grid Layout                                                        | |
| | +--------------------------------------------+ +------------------------------------------------+ | |
| | | Column 0 (Width="*")                       | | Column 1 (Width="480px")                       | | |
| | | Primary Dossier & Portfolio Viewport       | | Report Operations & Dispatch Terminal          | | |
| | |                                            | | (ReportOperationsPanel)                        | | |
| | | - Executive Examination Dossier Card       | |                                                | | |
| | |   * Target identity, build label, verdict  | | - Report Authentication & Merkle Seal Card     | | |
| | |   * Clearance badge (CLEARED/BLOCKED)      | |   * Report ID, Target Build Label              | | |
| | | - 4-Quadrant Forensic Metric Cards         | |   * Signatory: release-approver@capital-iz.com | | |
| | |   * Sweeps, States, Deviations, Artefacts  | |   * SHA-256 Merkle Digest Block                | | |
| | | - Release Policy Clearance Gates Matrix    | |   * Seal status badge: SEALED & AUDITED        | | |
| | | - Deviations & Wilson Confidence Catalog   | |                                                | | |
| | | - Markdown / Formatted Dossier View        | | - Estate Portfolio Governance Card (Patent §41)| | |
| | |                                            | |   * Estate Readiness Index (ERI %) Hero        | | |
| | | OR                                         | |   * Monitored Targets / Clearance Ratio        | | |
| | |                                            | |   * Estate Critical Defect Counter             | | |
| | | - Multi-Target Portfolio Rollup Dashboard  | |   * [Generate Estate Portfolio Rollup] Trigger | | |
| | |   * Estate Readiness Index Hero Card       | |                                                | | |
| | |   * Cross-Product Target Breakdown Grid    | | - Multi-Format Export Dispatch Hub             | | |
| | |   * Strategic Estate Risk Warnings         | |   * One-click MD, HTML, JSON, SARIF, JUnit     | | |
| | |                                            | |   * Last Exported Path & Status Telemetry      | | |
| | |                                            | |                                                | | |
| | |                                            | | - In-App Quick Reader & System Viewer Card     | | |
| | |                                            | |   * [Preview In-App] Document Drawer           | | |
| | |                                            | |   * [Open in System Viewer] External App       | | |
| | +--------------------------------------------+ +------------------------------------------------+ | |
| +---------------------------------------------------------------------------------------------------+ |
+-------------------------------------------------------------------------------------------------------+
```

---

## 3. Cryptographic Report Sealing & Merkle Root Lineage (Patent §33)

Every forensic report generated by `ReportGenerator` produces an immutable SHA-256 digest:
$$\text{Seal}_{\text{Report}} = \mathcal{H}_{\text{SHA-256}}\left(\text{ReportId} \parallel \text{TargetId} \parallel \text{BuildLabel} \parallel \text{ClearanceVerdict} \parallel \text{MarkdownPayload}\right)$$

In the **Report Authentication & Merkle Seal Card** (`ReportSealingCard`), this digest is prominently presented alongside the verified signatory authority (`release-approver@capital-iz.com`). Users can verify cryptographic provenance at a glance without navigating to external verification consoles.

---

## 4. Multi-Target Estate Governance & Estate Readiness Index (Patent §41)

The **Estate Portfolio Rollup Governance Card** (`EstateRollupCard`) queries `GhoztStore.Targets()`, evaluating each target's state lattice coverage, clearance outcome, and active deviations:
$$\text{ERI} = \frac{1}{N} \sum_{i=1}^{N} \left[ w_{\text{clearance}} \cdot \mathbb{I}(\text{Cleared}_i) + w_{\text{coverage}} \cdot \text{Cov}_i - w_{\text{risk}} \cdot \min(1.0, \text{CriticalFindings}_i) \right]$$

The panel surfaces:
- **Estate Readiness Index (`ERI`)**: A percentage expressing overall software estate maturity.
- **Estate Distribution Breakdown**: Total Targets, Targets Cleared, Targets Blocked/Denied.
- **Estate Critical Finding Count**: Total unresolved High/Critical faults across all portfolio targets.
- **One-Click Rollup Action**: Direct button to run `PortfolioRollupEngine.GenerateRollup()`.

---

## 5. Multi-Format Export & Dispatch Hub

The **Export Hub Card** (`ExportHubCard`) provides dedicated operational cards for all five supported report export formats:
1. **Markdown (`.md`)**: Full technical brief formatted for GitHub/GitLab pull request descriptions.
2. **HTML (`.html`)**: Self-contained styled report with embedded SVG charts and Capital-Iz design tokens.
3. **JSON (`.json`)**: Machine-readable JSON schema containing the entire evidence dataset.
4. **OASIS SARIF v2.1.0 (`.sarif`)**: Static Analysis Results Interchange Format for automated ingestion by GitHub Code Scanning, SonarQube, and CI security gates.
5. **JUnit XML (`.xml`)**: Automated test execution XML for Jenkins, Azure Pipelines, and GitLab CI.

---

## 6. Token Design System & Ergonomic Invariants

1. **Strict Token Adherence**:
   - Zero hardcoded hex colors or arbitrary brush declarations.
   - Authority status uses `Authority.Clearance`, `Authority.Soft`, and `Authority.Border`.
   - Verified status uses `State.Verified` and `State.VerifiedSoft`.
   - Surface backgrounds resolve via `Surface.Card`, `Surface.CardFaint`, and `Surface.Inset`.
2. **Micro-Typography**:
   - Headers and identifiers bind to `Type.MicroLabelStyle` and `Type.MonoStyle`.
3. **Button Semantics**:
   - Primary action uses `Button.Primary`.
   - Secondary actions use default `<Button ... />` resolving to `Button.Base` without invalid `Button.Secondary` tags.
4. **Three-State Honesty**:
   - When no report is loaded, the panel displays `PENDING GENERATION` and disables export controls. Absence of report data is never rendered as a successful audit.
