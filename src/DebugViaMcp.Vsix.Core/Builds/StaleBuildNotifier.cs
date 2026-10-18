using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DebugViaMcp.Protocol;

namespace DebugViaMcp.Vsix.Core.Builds;

/// <summary>The optional "Rebuild?" bar of one project. Implementations marshal to the UI thread themselves.</summary>
public interface IRebuildPrompt
{
    void Show(string project, string text, Action rebuild);
    void Hide(string project);
}

/// <summary>
/// Decides when the Rebuild bars are up: one per stale project, none while a debug session runs, none again after the
/// owner dismissed it until the project has been fresh in between. The bar is only an offer; nothing rebuilds by itself.
/// </summary>
public sealed class StaleBuildNotifier(IRebuildPrompt prompt, Func<DebuggerMode> mode, Action<string> rebuild)
{
    private readonly object _gate = new();
    private readonly HashSet<string> _shown = [];
    private readonly HashSet<string> _dismissed = [];
    private readonly HashSet<string> _stale = [];

    public IReadOnlyCollection<string> ShownProjects
    {
        get { lock (_gate) return _shown.ToArray(); }
    }

    /// <summary>Records what a freshness check found for <paramref name="project"/> (unique name).</summary>
    public void Observe(string project, bool stale)
    {
        var inDesign = mode() == DebuggerMode.Design;
        var show = false;
        var hide = false;
        lock (_gate)
        {
            if (!stale)
            {
                _stale.Remove(project);
                _dismissed.Remove(project);
                hide = _shown.Remove(project);
            }
            else
            {
                _stale.Add(project);
                show = inDesign && !_dismissed.Contains(project) && _shown.Add(project);
            }
        }

        if (hide) prompt.Hide(project);
        if (show) Show(project);
    }

    /// <summary>A debug session started: bars that are up go away and come back when it ends, if still stale.</summary>
    public void OnSessionStarted()
    {
        string[] hide;
        lock (_gate)
        {
            hide = _shown.ToArray();
            _shown.Clear();
        }

        foreach (var project in hide) prompt.Hide(project);
    }

    public void OnSessionEnded()
    {
        if (mode() != DebuggerMode.Design) return;
        string[] show;
        lock (_gate)
        {
            show = _stale.Where(p => !_dismissed.Contains(p) && _shown.Add(p)).ToArray();
        }

        foreach (var project in show) Show(project);
    }

    /// <summary>The owner closed the bar: it stays away until the project has been fresh and gone stale again.</summary>
    public void Dismissed(string project)
    {
        lock (_gate)
        {
            _shown.Remove(project);
            _dismissed.Add(project);
        }
    }

    private void Show(string project)
    {
        var name = Path.GetFileNameWithoutExtension(project);
        prompt.Show(project, $"{name} is out of date. Rebuild?", () =>
        {
            lock (_gate)
            {
                _shown.Remove(project);
                _stale.Remove(project);
            }

            prompt.Hide(project);
            rebuild(project);
        });
    }
}
