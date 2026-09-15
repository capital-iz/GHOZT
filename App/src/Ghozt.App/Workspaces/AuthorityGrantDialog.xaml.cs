using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Ghozt.Core;

namespace Ghozt.App.Workspaces;

public partial class AuthorityGrantDialog : Window
{
    private readonly Target _target;
    private readonly GhoztStore _store;
    private AuthorityGrant? _activeGrant;

    public AuthorityGrant? ResultGrant { get; private set; }

    public AuthorityGrantDialog(Target target, GhoztStore store)
    {
        _target = target;
        _store = store;

        InitializeComponent();

        TargetNameText.Text = target.Name;
        TargetIdText.Text = target.Id.ToString();

        _activeGrant = _store.ActiveGrantFor(target.Id);
        if (_activeGrant != null && !_activeGrant.IsExpired())
        {
            CurrentLevelText.Text = $"{_activeGrant.MaxLevel}";
            CurrentGrantIdText.Text = $"{_activeGrant.GrantId} (Expires: {_activeGrant.ExpiresAt:yyyy-MM-dd HH:mm})";
        }
        else
        {
            CurrentLevelText.Text = "L0_Observe (Default)";
            CurrentGrantIdText.Text = "No active valid grant recorded";
        }

        ActorBox.Text = !string.IsNullOrWhiteSpace(_activeGrant?.Actor) ? _activeGrant.Actor : Environment.UserName;
        ApproverBox.Text = !string.IsNullOrWhiteSpace(_activeGrant?.Approver) ? _activeGrant.Approver : "Rea";
        JustificationBox.Text = !string.IsNullOrWhiteSpace(_activeGrant?.ScopeJustification)
            ? _activeGrant.ScopeJustification
            : $"Automated inspection and testing of target {_target.Name}.";

        UpdatePreview();
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnFormChanged(object sender, RoutedEventArgs e)
    {
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (PreviewTokenText is null || PreviewSigText is null) return;

        var level = GetSelectedLevel();
        var actor = ActorBox?.Text?.Trim() ?? Environment.UserName;
        var approver = ApproverBox?.Text?.Trim() ?? "Rea";
        var justification = JustificationBox?.Text?.Trim() ?? "";

        var previewId = GhoztId.Create(IdKind.Authority, 9999);
        var dummyGrant = AuthorityKernel.IssueGrant(
            grantId: previewId,
            targetId: _target.Id,
            actor: actor,
            maxLevel: level,
            allowedActionClasses: ["sweep", "probe", "action:*"],
            expiresAt: DateTimeOffset.Now.AddHours(GetSelectedDurationHours()),
            approver: approver,
            scopeJustification: justification,
            sensitivity: GetSelectedSensitivity());

        PreviewTokenText.Text = dummyGrant.GrantId.ToString();
        PreviewSigText.Text = dummyGrant.AuditSignature;
    }

    private ExecutionIsolationLevel GetSelectedLevel()
    {
        if (IsolationLevelBox?.SelectedItem is ComboBoxItem item && item.Tag is string tagStr &&
            Enum.TryParse<ExecutionIsolationLevel>(tagStr, out var level))
        {
            return level;
        }
        return ExecutionIsolationLevel.L0_Observe;
    }

    private DataSensitivityTier GetSelectedSensitivity()
    {
        if (SensitivityBox?.SelectedItem is ComboBoxItem item && item.Tag is string tagStr &&
            Enum.TryParse<DataSensitivityTier>(tagStr, out var tier))
        {
            return tier;
        }
        return DataSensitivityTier.Public;
    }

    private int GetSelectedDurationHours()
    {
        if (DurationBox?.SelectedItem is ComboBoxItem item && item.Tag is string tagStr &&
            int.TryParse(tagStr, out var hours))
        {
            return hours;
        }
        return 8;
    }

    private void OnIssue(object sender, RoutedEventArgs e)
    {
        var level = GetSelectedLevel();
        var actor = ActorBox.Text.Trim();
        var approver = ApproverBox.Text.Trim();
        var justification = JustificationBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(actor)) actor = Environment.UserName;
        if (string.IsNullOrWhiteSpace(approver)) approver = "Rea";
        if (string.IsNullOrWhiteSpace(justification)) justification = "Operator inspection.";

        var grantId = _store.NextId(IdKind.Authority);
        var expiresAt = DateTimeOffset.Now.AddHours(GetSelectedDurationHours());

        var grant = AuthorityKernel.IssueGrant(
            grantId: grantId,
            targetId: _target.Id,
            actor: actor,
            maxLevel: level,
            allowedActionClasses: ["sweep", "probe", "ui:*", "api:*"],
            expiresAt: expiresAt,
            approver: approver,
            scopeJustification: justification,
            sensitivity: GetSelectedSensitivity());

        _store.Save(grant);

        // Update target authority note to seal grant in target metadata
        var updatedNote = $"[{grant.GrantId} | {grant.MaxLevel}] Approver: {grant.Approver} — {grant.ScopeJustification}";
        var updatedTarget = _target with { AuthorityNote = updatedNote };
        _store.Save(updatedTarget);

        _store.Append(new LedgerEvent
        {
            Id = _store.NextId(IdKind.Event),
            At = DateTimeOffset.Now,
            Code = "GZT.AUT.ISSUE",
            Message = $"Issued cryptographic authority grant {grant.GrantId} for {_target.Name} at level {grant.MaxLevel}.",
            Severity = Severity.Info,
            TargetId = _target.Id,
            Actor = grant.Actor,
            Detail = $"Approver={grant.Approver}; Tier={grant.AllowedDataSensitivity}; Expires={grant.ExpiresAt:O}; Sig={grant.AuditSignature}",
        });

        ResultGrant = grant;
        DialogResult = true;
        Close();
    }

    private void OnRevoke(object sender, RoutedEventArgs e)
    {
        var grantId = _store.NextId(IdKind.Authority);
        var revocation = AuthorityKernel.IssueGrant(
            grantId: grantId,
            targetId: _target.Id,
            actor: Environment.UserName,
            maxLevel: ExecutionIsolationLevel.L0_Observe,
            allowedActionClasses: ["observe"],
            expiresAt: DateTimeOffset.Now.AddSeconds(-1), // immediately expired
            approver: Environment.UserName,
            scopeJustification: "Authority revoked by operator.",
            sensitivity: DataSensitivityTier.Public);

        _store.Save(revocation);

        var updatedTarget = _target with { AuthorityNote = "No authority grant recorded — Ghozt will observe only." };
        _store.Save(updatedTarget);

        _store.Append(new LedgerEvent
        {
            Id = _store.NextId(IdKind.Event),
            At = DateTimeOffset.Now,
            Code = "GZT.AUT.REVOKE",
            Message = $"Revoked authority on {_target.Name}. Target locked to L0_Observe.",
            Severity = Severity.Medium,
            TargetId = _target.Id,
            Actor = Environment.UserName,
            Detail = $"RevokedAt={DateTimeOffset.Now:O}",
        });

        ResultGrant = revocation;
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
