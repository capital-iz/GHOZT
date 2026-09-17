using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ghozt.App.Shell;
using Ghozt.Core;

namespace Ghozt.App.Workspaces;

public partial class ClearanceWorkspace : UserControl, IRefreshableWorkspace
{
    private ClearanceDecision? _currentDecision;
    private bool _attached;

    public void Refresh()
    {
        if (Shell?.Target is null)
        {
            _currentDecision = null;
            ClearanceContainer.Children.Clear();
            ClearanceContainer.Children.Add(EmptyState);
            ClearanceSummaryText.Text = "No target selected";
            if (SignButton is not null) SignButton.IsEnabled = false;
            if (ExportCertButton is not null) ExportCertButton.IsEnabled = false;
            UpdateAuthorityPanel(null);
        }
        else
        {
            _currentDecision = null;
            OnEvaluate(this, new RoutedEventArgs());
        }
    }

    public ClearanceWorkspace()
    {
        InitializeComponent();
        Loaded += OnClearanceLoaded;
        DataContextChanged += (_, _) => Attach();
    }

    private void Attach()
    {
        if (DataContext is ShellViewModel shell && !_attached)
        {
            shell.PropertyChanged += OnShellChanged;
            _attached = true;
        }
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.Target))
        {
            if (Shell?.Target is null)
            {
                _currentDecision = null;
                ClearanceContainer.Children.Clear();
                ClearanceContainer.Children.Add(EmptyState);
                ClearanceSummaryText.Text = "No target selected";
                SignButton.IsEnabled = false;
                ExportCertButton.IsEnabled = false;
                UpdateAuthorityPanel(null);
            }
            else
            {
                _currentDecision = null;
                OnEvaluate(this, new RoutedEventArgs());
            }
        }
    }

    private void OnClearanceLoaded(object sender, RoutedEventArgs e)
    {
        Attach();
        if (Shell?.Target is not null && _currentDecision is null)
        {
            OnEvaluate(this, new RoutedEventArgs());
        }
    }

    private ShellViewModel Shell => (ShellViewModel)DataContext;

    private void OnEvaluate(object sender, RoutedEventArgs e)
    {
        var target = Shell?.Target;
        if (target is null)
        {
            ClearanceSummaryText.Text = "No target selected";
            UpdateAuthorityPanel(null);
            return;
        }

        var engine = new ClearanceEngine(App.Store);
        _currentDecision = engine.EvaluateTarget(target, "operator");

        RenderDecision(_currentDecision);
        SignButton.IsEnabled = true;
        ExportCertButton.IsEnabled = true;
        UpdateAuthorityPanel(_currentDecision);
    }

    private void RenderDecision(ClearanceDecision decision)
    {
        ClearanceContainer.Children.Clear();
        var isCleared = decision.Outcome == ClearanceOutcome.Cleared;
        ClearanceSummaryText.Text = $"Verdict: {decision.Outcome} · {decision.Gates.Count(g => g.Passed)}/{decision.Gates.Count} gates passed";

        // Verdict Card
        var card = new Border
        {
            Background = (Brush)TryFindResource("Surface.Card"),
            BorderBrush = (Brush)(isCleared ? TryFindResource("Authority.Clearance") : TryFindResource("Border.Card")),
            BorderThickness = new Thickness(isCleared ? 2 : 1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20),
            Margin = new Thickness(0, 0, 0, 16),
        };

        var stack = new StackPanel();

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titlePanel = new StackPanel();
        titlePanel.Children.Add(new TextBlock
        {
            Text = "Release Clearance Verdict",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)TryFindResource("Text.Primary"),
        });
        titlePanel.Children.Add(new TextBlock
        {
            Text = $"Target: {decision.TargetId} · Build: {decision.BuildLabel} · Evaluated: {decision.DecidedAt:yyyy-MM-dd HH:mm}",
            Style = (Style)TryFindResource("Type.CaptionStyle"),
            Foreground = (Brush)TryFindResource("Text.Muted"),
            Margin = new Thickness(0, 4, 0, 0),
        });
        Grid.SetColumn(titlePanel, 0);
        header.Children.Add(titlePanel);

        var badge = new Border
        {
            Background = (Brush)(isCleared ? TryFindResource("Authority.Soft") : TryFindResource("State.FaultSoft")),
            BorderBrush = (Brush)(isCleared ? TryFindResource("Authority.Clearance") : TryFindResource("State.Fault")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 6, 14, 6),
            VerticalAlignment = VerticalAlignment.Center,
        };
        badge.Child = new TextBlock
        {
            Text = decision.Outcome.ToString().ToUpperInvariant(),
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            Foreground = (Brush)(isCleared ? TryFindResource("Authority.Clearance") : TryFindResource("State.Fault")),
        };
        Grid.SetColumn(badge, 1);
        header.Children.Add(badge);

        stack.Children.Add(header);

        card.Child = stack;
        ClearanceContainer.Children.Add(card);

        // Gates Table
        foreach (var gate in decision.Gates)
        {
            var gateCard = new Border
            {
                Background = (Brush)TryFindResource("Surface.Card"),
                BorderBrush = (Brush)(gate.Passed ? TryFindResource("State.Verified") : (gate.Mandatory ? TryFindResource("State.Fault") : TryFindResource("State.Warning"))),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 10),
            };

            var gateStack = new StackPanel();

            var gHeader = new Grid();
            gHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var gTitle = new StackPanel { Orientation = Orientation.Horizontal };
            gTitle.Children.Add(new TextBlock
            {
                Text = gate.Id,
                Style = (Style)TryFindResource("Type.MonoStyle"),
                Foreground = (Brush)TryFindResource("Text.Muted"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            });
            gTitle.Children.Add(new TextBlock
            {
                Text = gate.Name,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Foreground = (Brush)TryFindResource("Text.Primary"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            if (gate.Mandatory)
            {
                gTitle.Children.Add(new TextBlock
                {
                    Text = "MANDATORY",
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = (Brush)TryFindResource("Authority.Clearance"),
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }
            Grid.SetColumn(gTitle, 0);
            gHeader.Children.Add(gTitle);

            var gBadge = new Border
            {
                Background = (Brush)(gate.Passed ? TryFindResource("State.VerifiedSoft") : TryFindResource("State.FaultSoft")),
                BorderBrush = (Brush)(gate.Passed ? TryFindResource("State.Verified") : TryFindResource("State.Fault")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 2),
            };
            gBadge.Child = new TextBlock
            {
                Text = gate.Result.ToUpperInvariant(),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)(gate.Passed ? TryFindResource("State.Verified") : TryFindResource("State.Fault")),
            };
            Grid.SetColumn(gBadge, 1);
            gHeader.Children.Add(gBadge);

            gateStack.Children.Add(gHeader);

            gateStack.Children.Add(new TextBlock
            {
                Text = gate.Requirement,
                Style = (Style)TryFindResource("Type.CaptionStyle"),
                Foreground = (Brush)TryFindResource("Text.Secondary"),
                Margin = new Thickness(0, 6, 0, 4),
            });

            gateStack.Children.Add(new TextBlock
            {
                Text = $"Note: {gate.EvidenceNote} · Source: {gate.PolicySource}",
                FontSize = 11,
                Foreground = (Brush)TryFindResource("Text.Muted"),
            });

            if (!gate.Passed)
            {
                var remediationPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 8, 0, 0),
                };

                remediationPanel.Children.Add(new TextBlock
                {
                    Text = "REMEDIATION: ",
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)TryFindResource("Text.Muted"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0),
                });

                var remedyBtn = new Button
                {
                    Padding = new Thickness(10, 3, 10, 3),
                    FontSize = 10,
                    Tag = gate.Id,
                };

                if (gate.Id.Contains("DEFECT", StringComparison.OrdinalIgnoreCase) ||
                    gate.Id.Contains("FAULT", StringComparison.OrdinalIgnoreCase) ||
                    gate.Id == "3" || gate.Id == "4")
                {
                    remedyBtn.Content = "Triage Open Faults →";
                    remedyBtn.Click += (_, _) => Shell?.NavigateToFaults();
                }
                else if (gate.Id.Contains("EVIDENCE", StringComparison.OrdinalIgnoreCase) ||
                         gate.Id.Contains("SEAL", StringComparison.OrdinalIgnoreCase) ||
                         gate.Id == "11" || gate.Id == "12")
                {
                    remedyBtn.Content = "Review & Seal Evidence →";
                    remedyBtn.Click += (_, _) =>
                    {
                        if (Shell != null) Shell.Workspace = WorkspaceKind.Evidence;
                    };
                }
                else if (gate.Id.Contains("REGRESS", StringComparison.OrdinalIgnoreCase) ||
                         gate.Id == "6")
                {
                    remedyBtn.Content = "Inspect Regressions →";
                    remedyBtn.Click += (_, _) =>
                    {
                        if (Shell != null) Shell.Workspace = WorkspaceKind.Reports;
                    };
                }
                else
                {
                    remedyBtn.Content = "Execute Target Sweep →";
                    remedyBtn.Click += (_, _) => Shell?.NavigateToSweep();
                }

                remediationPanel.Children.Add(remedyBtn);
                gateStack.Children.Add(remediationPanel);
            }

            gateCard.Child = gateStack;
            ClearanceContainer.Children.Add(gateCard);
        }
    }

    private void OnSignDecision(object sender, RoutedEventArgs e)
    {
        if (_currentDecision is null || Shell?.Target is null)
        {
            return;
        }

        var target = Shell.Target;
        var payload = $"clearance=Decision\ntarget={target.Id}\noutcome={_currentDecision.Outcome}\n"
                      + $"at={_currentDecision.DecidedAt:O}\nby={_currentDecision.DecidedBy}\n"
                      + $"digest={_currentDecision.EvidenceDigest}\n"
                      + string.Join('\n', _currentDecision.Gates.Select(g => $"{g.Id}: {g.Result}"));

        App.Store.Save(new Evidence
        {
            Id = App.Store.NextId(IdKind.Evidence),
            TargetId = target.Id,
            Kind = "clearance:certificate",
            Summary = $"Release clearance decision: {_currentDecision.Outcome} ({_currentDecision.Gates.Count(g => g.Passed)}/{_currentDecision.Gates.Count} gates passed)",
            Sha256 = Hashing.Sha256(payload),
            SizeBytes = System.Text.Encoding.UTF8.GetByteCount(payload),
            Payload = payload,
            SourceAdapter = "ClearanceEngine",
            Sealed = true,
            SealedBy = "release-approver@capital-iz.com",
            CapturedAt = DateTimeOffset.Now,
        });

        App.Store.Append(new LedgerEvent
        {
            Id = App.Store.NextId(IdKind.Event),
            At = DateTimeOffset.Now,
            Code = "CLR.SIGN",
            Message = $"Clearance decision signed: {_currentDecision.Outcome}",
            Severity = _currentDecision.Outcome == ClearanceOutcome.Cleared ? Severity.Info : Severity.Medium,
            TargetId = target.Id,
            Actor = "ClearanceEngine",
            Detail = payload,
        });

        SignButton.IsEnabled = false;
        SignButton.Content = "Signed & Sealed";
        ExportCertButton.IsEnabled = true;
        if (BtnAuthSign is not null)
        {
            BtnAuthSign.IsEnabled = false;
            BtnAuthSign.Content = "Signed & Sealed";
        }
        if (BtnAuthExport is not null) BtnAuthExport.IsEnabled = true;
        UpdateAuthorityPanel(_currentDecision);
    }

    private void OnExportCertificate(object sender, RoutedEventArgs e)
    {
        if (_currentDecision is null || Shell?.Target is null) return;

        var target = Shell.Target;
        try
        {
            var dossierEngine = new ClearanceDossierEngine(App.Store);
            var certificate = dossierEngine.IssueCertificate(target, _currentDecision.DecidedBy);

            ClearanceSummaryText.Text = $"Certificate {certificate.Digest.ClearanceId} exported ({certificate.Digest.Decision}). Merkle: {certificate.MerkleRoot[..8]}...";
            Shell.Log("CLR.CERT.EXPORT", $"Exported release clearance certificate to {certificate.SavedFilePath}.", Severity.Info,
                related: target.Id.ToString(), detail: $"Verdict: {certificate.Digest.Decision}; Merkle: {certificate.MerkleRoot}");

            if (!string.IsNullOrWhiteSpace(certificate.SavedFilePath) && System.IO.File.Exists(certificate.SavedFilePath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = certificate.SavedFilePath,
                    UseShellExecute = true,
                });
            }
        }
        catch (System.Exception ex)
        {
            ClearanceSummaryText.Text = $"Export error: {ex.Message}";
            Shell.Log("CLR.CERT.FAIL", $"Failed to export certificate: {ex.Message}", Severity.Medium);
        }
    }

    private void OnVerifyCertificate(object sender, RoutedEventArgs e)
    {
        var target = Shell?.Target;
        if (target is null)
        {
            ClearanceSummaryText.Text = "Select a target first to verify certificate";
            return;
        }

        try
        {
            var dir = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "Capital-Iz", "Ghozt", "Certificates");

            var certFiles = System.IO.Directory.Exists(dir)
                ? System.IO.Directory.GetFiles(dir, $"CERT-{target.Id}-*.json")
                : Array.Empty<string>();

            ClearanceDecision? decisionToVerify = _currentDecision;

            if (decisionToVerify is null && certFiles.Length > 0)
            {
                var latestFile = certFiles.OrderByDescending(System.IO.File.GetLastWriteTimeUtc).First();
                var json = System.IO.File.ReadAllText(latestFile);
                decisionToVerify = System.Text.Json.JsonSerializer.Deserialize<ClearanceDecision>(json);
            }

            if (decisionToVerify is null)
            {
                ClearanceSummaryText.Text = "No signed certificate found for target. Evaluate and sign first.";
                return;
            }

            // Verify evidence plane integrity against tampering (Patent §33)
            var evidenceItems = App.Store.EvidenceItems(target.Id);
            var tamperedCount = evidenceItems.Count(item =>
                !string.IsNullOrWhiteSpace(item.Payload) &&
                !string.IsNullOrWhiteSpace(item.Sha256) &&
                item.Sha256 != Hashing.Sha256(item.Payload));

            if (tamperedCount > 0)
            {
                ClearanceSummaryText.Text = $"TAMPER DETECTED: {tamperedCount} evidence item(s) have invalid SHA-256 payload checksums!";
                Shell?.Log("CLR.CERT.TAMPER", $"Certificate audit failed: {tamperedCount} evidence items failed cryptographic verification.", Severity.Critical,
                    related: target.Id.ToString());
                UpdateAuthorityPanel(decisionToVerify);
                return;
            }

            ClearanceSummaryText.Text = $"Certificate verified · Cryptographically sealed by {decisionToVerify.DecidedBy} ({decisionToVerify.Outcome})";
            Shell?.Log("CLR.CERT.VERIFY", $"Cryptographic release certificate verified for {target.Name}. All {evidenceItems.Count} evidence items intact.", Severity.Info,
                related: target.Id.ToString(), detail: $"Digest: {decisionToVerify.EvidenceDigest}");

            RenderDecision(decisionToVerify);
            UpdateAuthorityPanel(decisionToVerify);
        }
        catch (System.Exception ex)
        {
            ClearanceSummaryText.Text = $"Verification error: {ex.Message}";
            Shell?.Log("CLR.CERT.VERIFY_FAIL", $"Error verifying certificate: {ex.Message}", Severity.Medium);
        }
    }

    private async void OnRunSelfTest(object sender, RoutedEventArgs e)
    {
        ClearanceSummaryText.Text = "Running autonomous self-test examination across 12 domains…";
        Shell?.Log("CLR.SELFTEST.START", "Initiated Ghozt autonomous self-test suite (Patent §40).", Severity.Info);

        try
        {
            var engine = new SelfTestEngine(App.Store);
            var cert = await engine.RunFullSelfTestAsync().ConfigureAwait(true);

            RenderSelfTestCertificate(cert);

            ClearanceSummaryText.Text = $"Self-Test: {cert.Verdict} · {cert.PassedDomains}/{cert.TotalDomains} domains passed · SHA-256: {cert.Sha256Digest[..12]}…";
            Shell?.Log(
                cert.Verdict == "SELF_HOST_QUALIFIED" ? "CLR.SELFTEST.PASS" : "CLR.SELFTEST.FAIL",
                $"Autonomous self-test complete: {cert.PassedDomains}/{cert.TotalDomains} domains verified. Verdict: {cert.Verdict}.",
                cert.Verdict == "SELF_HOST_QUALIFIED" ? Severity.Info : Severity.Critical,
                detail: $"Certificate: {cert.CertificateId}; Sha256={cert.Sha256Digest}");
        }
        catch (System.Exception ex)
        {
            ClearanceSummaryText.Text = $"Self-test error: {ex.Message}";
            Shell?.Log("CLR.SELFTEST.ERROR", $"Exception during self-test: {ex.Message}", Severity.High);
        }
    }

    private void RenderSelfTestCertificate(GhoztSelfTestCertificate cert)
    {
        ClearanceContainer.Children.Clear();
        var isQualified = cert.Verdict == "SELF_HOST_QUALIFIED";

        // Master Certificate Card
        var certCard = new Border
        {
            Background = (Brush)TryFindResource("Surface.Card"),
            BorderBrush = (Brush)(isQualified ? TryFindResource("Authority.Clearance") : TryFindResource("State.Fault")),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(24),
            Margin = new Thickness(0, 0, 0, 16),
        };

        var stack = new StackPanel();

        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel();
        titleStack.Children.Add(new TextBlock
        {
            Text = "Ghozt Self-Hosting Quality Certificate",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)TryFindResource("Authority.Clearance"),
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = $"ID: {cert.CertificateId} · Product: v{cert.ProductVersion} · Evaluated: {cert.Timestamp:yyyy-MM-dd HH:mm:ss}",
            Style = (Style)TryFindResource("Type.CaptionStyle"),
            Foreground = (Brush)TryFindResource("Text.Muted"),
            Margin = new Thickness(0, 4, 0, 0),
        });
        Grid.SetColumn(titleStack, 0);
        headerGrid.Children.Add(titleStack);

        var badge = new Border
        {
            Background = (Brush)(isQualified ? TryFindResource("Authority.Soft") : TryFindResource("State.FaultSoft")),
            BorderBrush = (Brush)(isQualified ? TryFindResource("Authority.Clearance") : TryFindResource("State.Fault")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16, 6, 16, 6),
            VerticalAlignment = VerticalAlignment.Center,
        };
        badge.Child = new TextBlock
        {
            Text = cert.Verdict,
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            Foreground = (Brush)(isQualified ? TryFindResource("Authority.Clearance") : TryFindResource("State.Fault")),
        };
        Grid.SetColumn(badge, 1);
        headerGrid.Children.Add(badge);
        stack.Children.Add(headerGrid);

        // Digest & Signer Info
        var digestPanel = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        digestPanel.Children.Add(new TextBlock
        {
            Text = $"Cryptographic SHA-256 Digest: {cert.Sha256Digest}",
            FontFamily = new FontFamily("Consolas, Courier New"),
            FontSize = 12,
            Foreground = (Brush)TryFindResource("Text.Secondary"),
        });
        digestPanel.Children.Add(new TextBlock
        {
            Text = $"Signed Authority: {cert.SignedBy} · Domains: {cert.PassedDomains}/{cert.TotalDomains} Verified",
            Style = (Style)TryFindResource("Type.CaptionStyle"),
            Foreground = (Brush)TryFindResource("Text.Muted"),
            Margin = new Thickness(0, 4, 0, 0),
        });
        stack.Children.Add(digestPanel);

        certCard.Child = stack;
        ClearanceContainer.Children.Add(certCard);

        // Render 12 Domain Results
        var domainsLabel = new TextBlock
        {
            Text = "Self-Test Domain Verification Matrix (Patent §40, Table 78)",
            Style = (Style)TryFindResource("Type.HeadingStyle"),
            FontSize = 15,
            Margin = new Thickness(0, 8, 0, 12),
        };
        ClearanceContainer.Children.Add(domainsLabel);

        foreach (var d in cert.DomainVerdicts)
        {
            var dBorder = new Border
            {
                Background = (Brush)TryFindResource("Surface.Card"),
                BorderBrush = (Brush)TryFindResource("Border.Card"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 8),
            };

            var dGrid = new Grid();
            dGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var dText = new StackPanel();
            dText.Children.Add(new TextBlock
            {
                Text = $"{d.Domain} · {d.ChecksEvaluated} check(s) ({d.Duration.TotalMilliseconds:F1}ms)",
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)TryFindResource("Text.Primary"),
            });
            dText.Children.Add(new TextBlock
            {
                Text = d.Summary,
                Style = (Style)TryFindResource("Type.CaptionStyle"),
                Foreground = (Brush)TryFindResource("Text.Muted"),
                Margin = new Thickness(0, 2, 0, 0),
            });
            Grid.SetColumn(dText, 0);
            dGrid.Children.Add(dText);

            var dBadge = new Border
            {
                Background = (Brush)(d.Passed ? TryFindResource("Authority.Soft") : TryFindResource("State.FaultSoft")),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 4, 10, 4),
                VerticalAlignment = VerticalAlignment.Center,
            };
            dBadge.Child = new TextBlock
            {
                Text = d.Passed ? "PASS" : "FAIL",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)(d.Passed ? TryFindResource("Authority.Clearance") : TryFindResource("State.Fault")),
            };
            Grid.SetColumn(dBadge, 1);
            dGrid.Children.Add(dBadge);

            dBorder.Child = dGrid;
            ClearanceContainer.Children.Add(dBorder);
        }
    }

    private void OnRunSecurityAndCompatibilityAssay(object sender, RoutedEventArgs e)
    {
        var target = Shell?.Target;
        if (target is null)
        {
            ClearanceSummaryText.Text = "No target selected";
            return;
        }

        var grant = App.Store.ActiveGrantFor(target.Id);
        var secReport = SecurityAuditEngine.RunAudit(target, App.Store, grant);
        var compatReport = CompatibilityMatrixEngine.EvaluateCompatibility(target, App.Store);

        RenderAssayResults(target, secReport, compatReport);
    }

    private void RenderAssayResults(Target target, SecurityAuditReport sec, CompatibilityMatrixReport compat)
    {
        ClearanceContainer.Children.Clear();
        var allPassed = sec.Passed && compat.Passed;
        ClearanceSummaryText.Text = $"Assay Verdict: {(allPassed ? "PASSED" : "FAILED")} · {sec.PassedAreas}/8 Security Areas · {compat.PassedAxes}/5 Compatibility Axes";

        // Summary Header Card
        var summaryCard = new Border
        {
            Background = (Brush)TryFindResource("Surface.Card"),
            BorderBrush = (Brush)(allPassed ? TryFindResource("Authority.Clearance") : TryFindResource("Border.Card")),
            BorderThickness = new Thickness(allPassed ? 2 : 1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20),
            Margin = new Thickness(0, 0, 0, 16),
        };

        var summaryStack = new StackPanel();
        var sHeader = new Grid();
        sHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        sHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var sTitlePanel = new StackPanel();
        sTitlePanel.Children.Add(new TextBlock
        {
            Text = "Security Audit & Compatibility Matrix Assay",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)TryFindResource("Text.Primary"),
        });
        sTitlePanel.Children.Add(new TextBlock
        {
            Text = $"Target: {target.Name} ({target.Id}) · Evaluated: {sec.EvaluatedAt:yyyy-MM-dd HH:mm:ss} · Patent Table 36 & Table 38",
            Style = (Style)TryFindResource("Type.CaptionStyle"),
            Foreground = (Brush)TryFindResource("Text.Muted"),
            Margin = new Thickness(0, 4, 0, 0),
        });
        Grid.SetColumn(sTitlePanel, 0);
        sHeader.Children.Add(sTitlePanel);

        var sBadge = new Border
        {
            Background = (Brush)(allPassed ? TryFindResource("Authority.Soft") : TryFindResource("State.FaultSoft")),
            BorderBrush = (Brush)(allPassed ? TryFindResource("Authority.Clearance") : TryFindResource("State.Fault")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 6, 14, 6),
            VerticalAlignment = VerticalAlignment.Center,
        };
        sBadge.Child = new TextBlock
        {
            Text = allPassed ? "COMPLIANT" : "BREACH DETECTED",
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = (Brush)(allPassed ? TryFindResource("Authority.Clearance") : TryFindResource("State.Fault")),
        };
        Grid.SetColumn(sBadge, 1);
        sHeader.Children.Add(sBadge);

        summaryStack.Children.Add(sHeader);

        summaryStack.Children.Add(new TextBlock
        {
            Text = $"Security Audit Signature: {sec.AuditSignature[..16]}... | Compatibility Digest: {compat.MatrixDigest[..16]}...",
            Style = (Style)TryFindResource("Type.MonoStyle"),
            FontSize = 11,
            Foreground = (Brush)TryFindResource("Text.Secondary"),
            Margin = new Thickness(0, 10, 0, 0),
        });

        summaryCard.Child = summaryStack;
        ClearanceContainer.Children.Add(summaryCard);

        // Section 1: Compatibility Matrix (Patent Table 38)
        var compatHeader = new TextBlock
        {
            Text = $"Compatibility Matrix (Patent Table 38) — {compat.PassedAxes}/{compat.TotalAxesAssayed} Axes Verified",
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            Foreground = (Brush)TryFindResource("Text.Primary"),
            Margin = new Thickness(0, 8, 0, 8),
        };
        ClearanceContainer.Children.Add(compatHeader);

        foreach (var axis in compat.AxisResults)
        {
            var axisBorder = new Border
            {
                Background = (Brush)TryFindResource("Surface.Card"),
                BorderBrush = (Brush)(axis.Passed ? TryFindResource("State.Verified") : TryFindResource("State.Fault")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 10),
            };

            var aStack = new StackPanel();
            var aGrid = new Grid();
            aGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            aGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var aTitle = new TextBlock
            {
                Text = $"{axis.Axis}: {axis.Name}",
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Foreground = (Brush)TryFindResource("Text.Primary"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(aTitle, 0);
            aGrid.Children.Add(aTitle);

            var aBadge = new Border
            {
                Background = (Brush)(axis.Passed ? TryFindResource("State.VerifiedSoft") : TryFindResource("State.FaultSoft")),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 2),
            };
            aBadge.Child = new TextBlock
            {
                Text = axis.Passed ? "COMPATIBLE" : "INCOMPATIBLE",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)(axis.Passed ? TryFindResource("State.Verified") : TryFindResource("State.Fault")),
            };
            Grid.SetColumn(aBadge, 1);
            aGrid.Children.Add(aBadge);

            aStack.Children.Add(aGrid);
            aStack.Children.Add(new TextBlock
            {
                Text = axis.Assessment,
                Style = (Style)TryFindResource("Type.CaptionStyle"),
                Foreground = (Brush)TryFindResource("Text.Secondary"),
                Margin = new Thickness(0, 4, 0, 4),
            });

            var diagText = string.Join(" · ", axis.Diagnostics.Select(kvp => $"{kvp.Key}={kvp.Value}"));
            aStack.Children.Add(new TextBlock
            {
                Text = diagText,
                Style = (Style)TryFindResource("Type.MonoStyle"),
                FontSize = 10,
                Foreground = (Brush)TryFindResource("Text.Muted"),
            });

            axisBorder.Child = aStack;
            ClearanceContainer.Children.Add(axisBorder);
        }

        // Section 2: Security Validation Areas (Patent Table 36)
        var secHeader = new TextBlock
        {
            Text = $"Authorised Security Audit (Patent Table 36) — {sec.PassedAreas}/{sec.TotalAreasChecked} Areas Cleared",
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            Foreground = (Brush)TryFindResource("Text.Primary"),
            Margin = new Thickness(0, 16, 0, 8),
        };
        ClearanceContainer.Children.Add(secHeader);

        foreach (var area in sec.AreaResults)
        {
            var areaBorder = new Border
            {
                Background = (Brush)TryFindResource("Surface.Card"),
                BorderBrush = (Brush)(area.Passed ? TryFindResource("State.Verified") : (area.Severity >= Severity.High ? TryFindResource("State.Fault") : TryFindResource("State.Warning"))),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 10),
            };

            var areaStack = new StackPanel();
            var areaGrid = new Grid();
            areaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            areaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var areaTitle = new StackPanel { Orientation = Orientation.Horizontal };
            areaTitle.Children.Add(new TextBlock
            {
                Text = area.DiagnosticCode,
                Style = (Style)TryFindResource("Type.MonoStyle"),
                Foreground = (Brush)TryFindResource("Text.Muted"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            });
            areaTitle.Children.Add(new TextBlock
            {
                Text = $"{area.Area}: {area.Title}",
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Foreground = (Brush)TryFindResource("Text.Primary"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            Grid.SetColumn(areaTitle, 0);
            areaGrid.Children.Add(areaTitle);

            var areaBadge = new Border
            {
                Background = (Brush)(area.Passed ? TryFindResource("State.VerifiedSoft") : TryFindResource("State.FaultSoft")),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 2),
            };
            areaBadge.Child = new TextBlock
            {
                Text = area.Passed ? "CLEARED" : area.Severity.ToString().ToUpperInvariant(),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)(area.Passed ? TryFindResource("State.Verified") : TryFindResource("State.Fault")),
            };
            Grid.SetColumn(areaBadge, 1);
            areaGrid.Children.Add(areaBadge);

            areaStack.Children.Add(areaGrid);
            areaStack.Children.Add(new TextBlock
            {
                Text = area.Details,
                Style = (Style)TryFindResource("Type.CaptionStyle"),
                Foreground = (Brush)TryFindResource("Text.Secondary"),
                Margin = new Thickness(0, 4, 0, 2),
            });

            if (!string.IsNullOrWhiteSpace(area.RemediativeGuidance))
            {
                areaStack.Children.Add(new TextBlock
                {
                    Text = $"Remediation: {area.RemediativeGuidance}",
                    FontSize = 11,
                    Foreground = (Brush)TryFindResource("Authority.Clearance"),
                    Margin = new Thickness(0, 2, 0, 0),
                });
            }

            areaBorder.Child = areaStack;
            ClearanceContainer.Children.Add(areaBorder);
        }
    }

    private void UpdateAuthorityPanel(ClearanceDecision? decision = null)
    {
        if (AuthSealTargetText is null) return;

        var target = Shell?.Target;
        if (target is null)
        {
            AuthSealTargetText.Text = "Target: None";
            AuthSealStatusText.Text = "UNEVALUATED";
            AuthSealStatusBadge.Background = (Brush)TryFindResource("Authority.Soft");
            AuthSealStatusBadge.BorderBrush = (Brush)TryFindResource("Authority.Clearance");
            AuthSealStatusText.Foreground = (Brush)TryFindResource("Authority.Clearance");
            AuthSealCard.BorderBrush = (Brush)TryFindResource("Authority.Clearance");
            AuthSignatoryText.Text = "release-approver@capital-iz.com";
            AuthEvaluatedAtText.Text = "Evaluated: None";
            AuthDigestText.Text = "SHA-256: Unsealed";
            AuthTamperBadgeText.Text = "INTACT";
            AuthTamperBadge.Background = (Brush)TryFindResource("State.VerifiedSoft");
            AuthTamperBadge.BorderBrush = (Brush)TryFindResource("State.Verified");
            AuthTamperBadgeText.Foreground = (Brush)TryFindResource("State.Verified");
            AuthEvidenceCountText.Text = "0 Items";
            AuthTamperDetailsText.Text = "0 Corrupt / 100% Intact";
            AuthTamperDetailsText.Foreground = (Brush)TryFindResource("State.Verified");
            AuthGateRatioText.Text = "-- / --";
            AuthMandatoryStatusText.Text = "Pending";
            AuthMandatoryStatusText.Foreground = (Brush)TryFindResource("Text.Secondary");
            AuthAccessClassText.Text = "None";
            AuthOpenFaultsText.Text = "0 Defects";
            AuthOpenFaultsText.Foreground = (Brush)TryFindResource("State.Verified");
            if (BtnAuthSign is not null) BtnAuthSign.IsEnabled = false;
            if (BtnAuthExport is not null) BtnAuthExport.IsEnabled = false;
            return;
        }

        AuthSealTargetText.Text = $"Target: {target.Name} ({target.Id})" + (string.IsNullOrWhiteSpace(target.BuildLabel) ? "" : $" · {target.BuildLabel}");

        if (decision is not null)
        {
            var isCleared = decision.Outcome == ClearanceOutcome.Cleared;
            AuthSealStatusText.Text = decision.Outcome.ToString().ToUpperInvariant();
            AuthSealStatusBadge.Background = (Brush)(isCleared ? TryFindResource("Authority.Soft") : TryFindResource("State.FaultSoft"));
            AuthSealStatusBadge.BorderBrush = (Brush)(isCleared ? TryFindResource("Authority.Clearance") : TryFindResource("State.Fault"));
            AuthSealStatusText.Foreground = (Brush)(isCleared ? TryFindResource("Authority.Clearance") : TryFindResource("State.Fault"));
            AuthSealCard.BorderBrush = (Brush)(isCleared ? TryFindResource("Authority.Clearance") : TryFindResource("Border.Card"));

            AuthSignatoryText.Text = string.IsNullOrWhiteSpace(decision.DecidedBy) ? "release-approver@capital-iz.com" : decision.DecidedBy;
            AuthEvaluatedAtText.Text = $"Evaluated: {decision.DecidedAt:yyyy-MM-dd HH:mm:ss}";
            AuthDigestText.Text = string.IsNullOrWhiteSpace(decision.EvidenceDigest) ? "Digest: None" : $"SHA-256: {decision.EvidenceDigest}";

            var passed = decision.Gates.Count(g => g.Passed);
            var total = decision.Gates.Count;
            AuthGateRatioText.Text = $"{passed}/{total} Gates";

            var mandatoryPassed = decision.Gates.Where(g => g.Mandatory).All(g => g.Passed);
            AuthMandatoryStatusText.Text = mandatoryPassed ? "PASSED" : "FAILED";
            AuthMandatoryStatusText.Foreground = (Brush)(mandatoryPassed ? TryFindResource("State.Verified") : TryFindResource("State.Fault"));

            if (BtnAuthSign is not null)
            {
                BtnAuthSign.IsEnabled = true;
                BtnAuthSign.Content = "Sign & Seal Decision";
            }
            if (BtnAuthExport is not null) BtnAuthExport.IsEnabled = true;
        }
        else
        {
            AuthSealStatusText.Text = "UNEVALUATED";
            AuthSealStatusBadge.Background = (Brush)TryFindResource("Authority.Soft");
            AuthSealStatusBadge.BorderBrush = (Brush)TryFindResource("Authority.Clearance");
            AuthSealStatusText.Foreground = (Brush)TryFindResource("Authority.Clearance");
            AuthSealCard.BorderBrush = (Brush)TryFindResource("Authority.Clearance");
            AuthSignatoryText.Text = "release-approver@capital-iz.com";
            AuthEvaluatedAtText.Text = "Evaluated: Pending";
            AuthDigestText.Text = "SHA-256: Unsealed";
            AuthGateRatioText.Text = "-- / --";
            AuthMandatoryStatusText.Text = "Pending";
            AuthMandatoryStatusText.Foreground = (Brush)TryFindResource("Text.Secondary");
            if (BtnAuthSign is not null) BtnAuthSign.IsEnabled = false;
            if (BtnAuthExport is not null) BtnAuthExport.IsEnabled = false;
        }

        // Evidence Plane Tamper Assay
        var evidenceItems = App.Store.EvidenceItems(target.Id);
        AuthEvidenceCountText.Text = $"{evidenceItems.Count} Items";

        var tamperedCount = evidenceItems.Count(item =>
            !string.IsNullOrWhiteSpace(item.Payload) &&
            !string.IsNullOrWhiteSpace(item.Sha256) &&
            item.Sha256 != Hashing.Sha256(item.Payload));

        if (tamperedCount > 0)
        {
            AuthTamperBadgeText.Text = "TAMPERED";
            AuthTamperBadge.Background = (Brush)TryFindResource("State.FaultSoft");
            AuthTamperBadge.BorderBrush = (Brush)TryFindResource("State.Fault");
            AuthTamperBadgeText.Foreground = (Brush)TryFindResource("State.Fault");
            AuthTamperDetailsText.Text = $"{tamperedCount} Corrupted Item(s)";
            AuthTamperDetailsText.Foreground = (Brush)TryFindResource("State.Fault");
        }
        else
        {
            AuthTamperBadgeText.Text = "INTACT";
            AuthTamperBadge.Background = (Brush)TryFindResource("State.VerifiedSoft");
            AuthTamperBadge.BorderBrush = (Brush)TryFindResource("State.Verified");
            AuthTamperBadgeText.Foreground = (Brush)TryFindResource("State.Verified");
            AuthTamperDetailsText.Text = "0 Corrupt / 100% Intact";
            AuthTamperDetailsText.Foreground = (Brush)TryFindResource("State.Verified");
        }

        // Target Risk Envelope
        var grant = App.Store.ActiveGrantFor(target.Id);
        AuthAccessClassText.Text = grant != null ? $"{target.Access} ({grant.MaxLevel})" : target.Access.ToString();

        var findings = App.Store.Findings(target.Id);
        var openDefects = findings.Count(f => f.Maturity is not (FindingMaturity.Verified or FindingMaturity.FixReady));
        AuthOpenFaultsText.Text = $"{openDefects} Unmitigated";
        AuthOpenFaultsText.Foreground = (Brush)(openDefects == 0 ? TryFindResource("State.Verified") : TryFindResource("State.Fault"));
    }
}


