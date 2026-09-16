namespace Ghozt.App.Workspaces;

/// <summary>
/// Implemented by workspaces that support active operator refresh (F5) to re-query the store,
/// reload state, and re-evaluate active verification gates.
/// </summary>
public interface IRefreshableWorkspace
{
    void Refresh();
}
