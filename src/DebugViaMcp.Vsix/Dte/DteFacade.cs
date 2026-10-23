using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using EnvDTE;
using EnvDTE80;
using EnvDTE90a;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.TableControl;
using Microsoft.VisualStudio.Shell.TableManager;
using DebugViaMcp.Protocol;
using DebugViaMcp.Vsix.Core.Abstractions;
using DebugViaMcp.Vsix.Core.Bridge;
using DebugViaMcp.Vsix.Core.Builds;
using DebugViaMcp.Vsix.Core.Support;
using DebugViaMcp.Vsix.Infrastructure;

namespace DebugViaMcp.Vsix.Dte;

/// <summary>IDteFacade over EnvDTE and the Error List table. Every member runs on the UI thread (enforced by IUiThread callers).</summary>
internal sealed class DteFacade(DTE2 dte, IErrorList? errorList) : IDteFacade
{
    public bool IsBuilding
    {
        get { ThreadHelper.ThrowIfNotOnUIThread(); return dte.Solution.SolutionBuild.BuildState == vsBuildState.vsBuildStateInProgress; }
    }

    public IReadOnlyList<string> ProjectNames()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var names = new List<string>();
        foreach (Project p in dte.Solution.Projects) AddProject(p, names);
        return names;
    }

    private static void AddProject(Project project, List<string> names)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (project.Kind == ProjectKinds.vsProjectKindSolutionFolder)
        {
            foreach (ProjectItem item in project.ProjectItems)
            {
                if (item.SubProject is { } sub) AddProject(sub, names);
            }

            return;
        }

        if (!string.IsNullOrEmpty(project.FullName)) names.Add(project.UniqueName);
    }

    // Source times come from the files under the project's folder (known source extensions, bin/obj and similar skipped):
    // one directory walk instead of a COM call per project item, which is slow on large projects. Output times come from
    // the active configuration's Built and Symbols output groups, so the target-framework folder is respected.
    public ProjectBuildInfo GetBuildInfo(string uniqueName)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var info = new ProjectBuildInfo { UniqueName = uniqueName };
        var project = FindProject(uniqueName);
        if (project is null) return info;

        if (ComSafe.TryRead(() => { ThreadHelper.ThrowIfNotOnUIThread(); return project.FullName; }, out var projectFile) && !string.IsNullOrEmpty(projectFile))
            info.NewestSourceUtc = NewestSource(projectFile);

        var built = new List<string>();
        var symbols = new List<string>();
        var haveGroups = false;
        if (ComSafe.TryRead(() => { ThreadHelper.ThrowIfNotOnUIThread(); return project.ConfigurationManager?.ActiveConfiguration?.OutputGroups; }, out var groups) && groups is not null)
        {
            haveGroups = true;
            foreach (EnvDTE.OutputGroup group in groups)
            {
                if (!ComSafe.TryRead(() => { ThreadHelper.ThrowIfNotOnUIThread(); return (group.CanonicalName, group.FileURLs); }, out var read)) continue;
                if (read.FileURLs is not object[] urls) continue;
                var paths = urls.OfType<string>().Select(url => new Uri(url).LocalPath);
                if (read.CanonicalName == "Built") built.AddRange(paths);
                else if (read.CanonicalName == "Symbols") symbols.AddRange(paths);
            }
        }

        // Only the assembly and the symbols say whether the build is current (see StaleBuildChecker.ProductFiles).
        // A missing product (or none at all) means the project was never built: leave OldestOutputUtc null.
        var products = haveGroups ? StaleBuildChecker.ProductFiles(built, symbols) : [];
        info.OldestOutputUtc = products.Count > 0 && products.All(System.IO.File.Exists)
            ? products.Min(System.IO.File.GetLastWriteTimeUtc)
            : (DateTime?)null;
        // Visual Studio's own up-to-date query is not reachable through DTE; file times are the signal (UpToDateByVisualStudio stays null).
        return info;
    }

    private static readonly string[] SourceExtensions = [".cs", ".vb", ".fs", ".xaml", ".razor", ".cshtml", ".resx", ".csproj", ".vbproj", ".fsproj", ".props", ".targets"];
    private static readonly string[] SkippedFolders = ["bin", "obj", ".vs", ".git", "node_modules", "packages", "artifacts", "TestResults", ".idea"];

    private static DateTime? NewestSource(string projectFile)
    {
        var root = System.IO.Path.GetDirectoryName(projectFile);
        if (root is null || !System.IO.Directory.Exists(root)) return null;
        DateTime? newest = null;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            try
            {
                foreach (var sub in System.IO.Directory.EnumerateDirectories(directory))
                {
                    // Junctions and symbolic links can lead anywhere (or in circles); they are not part of the project's own sources.
                    if ((System.IO.File.GetAttributes(sub) & System.IO.FileAttributes.ReparsePoint) != 0) continue;
                    if (!SkippedFolders.Contains(System.IO.Path.GetFileName(sub), StringComparer.OrdinalIgnoreCase)) pending.Push(sub);
                }

                foreach (var file in System.IO.Directory.EnumerateFiles(directory))
                {
                    if (!SourceExtensions.Contains(System.IO.Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) continue;
                    var time = System.IO.File.GetLastWriteTimeUtc(file);
                    if (newest is null || time > newest) newest = time;
                }
            }
            catch (System.IO.IOException)
            {
                // A folder that vanishes or cannot be read while walking is skipped.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return newest;
    }

    // The project that contains a file is the one whose folder is the deepest ancestor of the file (SDK-style projects
    // include everything below their folder), so nested projects win over a project at the repository root.
    public IReadOnlyList<string> ProjectsContaining(string file)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        string? bestFolder = null;
        var best = new List<string>();
        foreach (var unique in ProjectNames())
        {
            var project = FindProject(unique);
            if (project is null || !ComSafe.TryRead(() => { ThreadHelper.ThrowIfNotOnUIThread(); return project.FullName; }, out var projectFile) || string.IsNullOrEmpty(projectFile)) continue;
            var folder = System.IO.Path.GetDirectoryName(projectFile);
            if (folder is null || !FolderMatch.IsInsideOrEqual(file, folder)) continue;
            if (bestFolder is null || folder.Length > bestFolder.Length)
            {
                bestFolder = folder;
                best = [unique];
            }
            else if (string.Equals(folder, bestFolder, StringComparison.OrdinalIgnoreCase))
            {
                best.Add(unique);
            }
        }

        return best;
    }

    public IReadOnlyList<string> StartupProjects
    {
        get
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return dte.Solution.SolutionBuild.StartupProjects is object[] names ? names.OfType<string>().ToArray() : [];
        }
        set
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            dte.Solution.SolutionBuild.StartupProjects = value.ToArray();
        }
    }

    // Startable means the project's output is an executable: its OutputType property is WinExe (1) or Exe (0). Class
    // libraries (2) and projects without that property are not offered.
    public bool IsLaunchable(string uniqueName)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var project = FindProject(uniqueName);
        if (project is null) return false;
        return ComSafe.TryRead(() => { ThreadHelper.ThrowIfNotOnUIThread(); return project.Properties.Item("OutputType").Value; }, out var value) && value is int type && type is 0 or 1;
    }

    private Project? FindProject(string uniqueName)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        foreach (Project p in dte.Solution.Projects)
        {
            if (FindIn(p, uniqueName) is { } found) return found;
        }

        return null;
    }

    private static Project? FindIn(Project project, string uniqueName)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (project.Kind == ProjectKinds.vsProjectKindSolutionFolder)
        {
            foreach (ProjectItem item in project.ProjectItems)
            {
                if (item.SubProject is { } sub && FindIn(sub, uniqueName) is { } found) return found;
            }

            return null;
        }

        return string.Equals(project.UniqueName, uniqueName, StringComparison.OrdinalIgnoreCase) ? project : null;
    }

    public void StartBuild(string? projectUniqueName)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var build = dte.Solution.SolutionBuild;
        if (projectUniqueName is null) build.Build(WaitForBuildToFinish: false);
        else build.BuildProject(build.ActiveConfiguration.Name, projectUniqueName, WaitForBuildToFinish: false);
    }

    public IReadOnlyList<RawErrorItem> ErrorItems()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var result = new List<RawErrorItem>();
        if (errorList?.TableControl is not { } table) return result;
        foreach (var entry in table.Entries)
        {
            result.Add(new RawErrorItem
            {
                Severity = entry.TryGetValue(StandardTableKeyNames.ErrorSeverity, out var s) && s is __VSERRORCATEGORY c
                    ? c switch { __VSERRORCATEGORY.EC_ERROR => ErrorSeverity.Error, __VSERRORCATEGORY.EC_WARNING => ErrorSeverity.Warning, _ => ErrorSeverity.Message }
                    : ErrorSeverity.Message,
                Code = Text(entry, StandardTableKeyNames.ErrorCode),
                Message = Text(entry, StandardTableKeyNames.Text) ?? "",
                Project = Text(entry, StandardTableKeyNames.ProjectName),
                File = Text(entry, StandardTableKeyNames.DocumentName),
                Line = entry.TryGetValue(StandardTableKeyNames.Line, out var l) && l is int line ? line + 1 : null,
                Column = entry.TryGetValue(StandardTableKeyNames.Column, out var col) && col is int column ? column + 1 : null,
                FromBuild = entry.TryGetValue(StandardTableKeyNames.ErrorSource, out var src) && src is ErrorSource source && source == ErrorSource.Build,
            });
        }

        return result;
    }

    private static string? Text(ITableEntryHandle entry, string key) =>
        entry.TryGetValue(key, out var value) && value is string s && s.Length > 0 ? s : null;

    private const int EvalTimeoutMs = 5000;

    public string VsVersion
    {
        get { ThreadHelper.ThrowIfNotOnUIThread(); return dte.Version; }
    }

    public string? SolutionPath
    {
        get
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var path = dte.Solution?.FullName;
            return string.IsNullOrEmpty(path) ? null : path;
        }
    }

    public DebuggerMode Mode
    {
        get
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return dte.Debugger.CurrentMode switch
            {
                dbgDebugMode.dbgBreakMode => DebuggerMode.Break,
                dbgDebugMode.dbgRunMode => DebuggerMode.Run,
                _ => DebuggerMode.Design,
            };
        }
    }

    public bool FileExists(string path) => System.IO.File.Exists(path);

    public RawBreakpoint? LastHitBreakpoint
    {
        get
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var bp = dte.Debugger.BreakpointLastHit;
            return bp is null ? null : ToRaw(bp.Parent ?? bp);
        }
    }

    public IReadOnlyList<RawBreakpoint> Breakpoints()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return dte.Debugger.Breakpoints.Cast<Breakpoint>().Select(ToRaw).ToList();
    }

    public void AddBreakpoint(BreakpointSpec spec)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var created = dte.Debugger.Breakpoints.Add(
            Function: spec.Function ?? "",
            File: spec.File ?? "",
            Line: spec.Line ?? 1,
            Column: 1,
            Condition: spec.Condition ?? "",
            ConditionType: spec.ConditionKind == ConditionKind.WhenChanged
                ? dbgBreakpointConditionType.dbgBreakpointConditionTypeWhenChanged
                : dbgBreakpointConditionType.dbgBreakpointConditionTypeWhenTrue,
            Language: "",
            Data: "",
            DataCount: 1,
            Address: "",
            HitCount: spec.HitCount,
            HitCountType: spec.HitCountKind switch
            {
                HitCountKind.Equal => dbgHitCountType.dbgHitCountTypeEqual,
                HitCountKind.AtLeast => dbgHitCountType.dbgHitCountTypeGreaterOrEqual,
                HitCountKind.Multiple => dbgHitCountType.dbgHitCountTypeMultiple,
                _ => dbgHitCountType.dbgHitCountTypeNone,
            });

        if (spec.LogMessage is not null)
        {
            foreach (Breakpoint2 bp in created)
            {
                bp.Message = spec.LogMessage;
                bp.BreakWhenHit = false;
            }
        }
    }

    public int RemoveBreakpoints(Func<RawBreakpoint, bool> match)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var doomed = dte.Debugger.Breakpoints.Cast<Breakpoint>().Where(b => match(ToRaw(b))).ToList();
        foreach (var b in doomed) b.Delete();
        return doomed.Count;
    }

    public RawBreakpoint? SetEnabled(Func<RawBreakpoint, bool> match, bool enabled)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        foreach (Breakpoint b in dte.Debugger.Breakpoints)
        {
            if (!match(ToRaw(b))) continue;
            b.Enabled = enabled;
            return ToRaw(b);
        }

        return null;
    }

    /// <summary>
    /// A modal dialog disables Visual Studio's main window and is its enabled popup (GW_ENABLEDPOPUP). Read off a live Visual Studio with the
    /// Hot Reload "changes not supported" dialog open: main window disabled, popup titled with the dialog's name.
    /// </summary>
    public string? ModalDialogTitle()
    {
        var main = MainWindowHandle();
        if (main == IntPtr.Zero || NativeDialog.IsWindowEnabled(main)) return null;
        var popup = NativeDialog.GetWindow(main, NativeDialog.GW_ENABLEDPOPUP);
        if (popup == IntPtr.Zero || popup == main) return "";
        var title = new char[256];
        var length = NativeDialog.GetWindowText(popup, title, title.Length);
        return length > 0 ? new string(title, 0, length) : "";
    }

    // Read once on the UI thread (this class is built there), so any thread can use it without touching COM.
    private IntPtr _mainWindow = DteMainWindow(dte);

    /// <summary>
    /// Visual Studio's main window as DTE reports it. The process's "main window" is only the first visible top-level window, which early on can be
    /// a splash or start window that is gone later: a destroyed handle reads as disabled and would report a dialog that is not there (seen on 17.14).
    /// </summary>
    private static IntPtr DteMainWindow(DTE2 dte)
    {
#pragma warning disable CA1031 // Without the DTE handle the process's main window is used.
        try { ThreadHelper.ThrowIfNotOnUIThread(); return dte.MainWindow.HWnd; }
        catch (Exception) { return IntPtr.Zero; }
#pragma warning restore CA1031
    }

    private IntPtr MainWindowHandle()
    {
        if (_mainWindow != IntPtr.Zero && NativeDialog.IsWindow(_mainWindow)) return _mainWindow;
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        return _mainWindow = self.MainWindowHandle;   // not cached when it is not a live window: the next call looks again
    }

    private static class NativeDialog
    {
        public const uint GW_ENABLEDPOPUP = 6;

        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, [Out] char[] text, int maxCount);
    }

    public RawDebuggerOptions DebuggerOptions()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return new RawDebuggerOptions
        {
            RequireSourceToMatch = DebuggingFlag("RequireSourceToMatch"),
            JustMyCode = DebuggingFlag("EnableJustMyCode"),
            WarnIfNoSymbols = DebuggingFlag("WarnIfNoSymbols"),
            SuppressJitOptimization = DebuggingFlag("SuppressJitOptimization"),
            SourceServer = DebuggingFlag("EnableSourceServer"),
            SourceServerDiagnostics = DebuggingFlag("PrintSourceServerDiagnostics"),
        };
    }

    private EnvDTE90.ExceptionGroups ExceptionGroupsRoot()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return ((EnvDTE90.Debugger3)dte.Debugger).ExceptionGroups;
    }

    public IReadOnlyList<string> ExceptionGroupNames()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var names = new List<string>();
        foreach (EnvDTE90.ExceptionSettings group in ExceptionGroupsRoot()) names.Add(group.Name);
        return names;
    }

    public IReadOnlyList<ExceptionEntry> ExceptionEntries(string group)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        EnvDTE90.ExceptionSettings settings;
        try { settings = ExceptionGroupsRoot().Item(group); }
        catch (COMException e) when (e.HResult == DispEBadIndex) { throw new BridgeException(BridgeErrorCode.NotFound, $"No exception group '{group}'."); }

        var entries = new List<ExceptionEntry>(settings.Count);
        foreach (EnvDTE90.ExceptionSetting setting in settings) entries.Add(new ExceptionEntry(setting.Name, setting.BreakWhenThrown));   // not Code: it throws in groups without codes
        return entries;
    }

    public bool? BreakWhenThrown(string group, string name)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try { return ExceptionGroupsRoot().Item(group).Item(name).BreakWhenThrown; }
        catch (COMException e) when (e.HResult == DispEBadIndex) { return null; }
    }

    public void SetBreakWhenThrown(string group, string name, bool value)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        EnvDTE90.ExceptionSettings settings;
        EnvDTE90.ExceptionSetting setting;
        try
        {
            settings = ExceptionGroupsRoot().Item(group);
            setting = settings.Item(name);
        }
        catch (COMException e) when (e.HResult == DispEBadIndex)
        {
            throw new BridgeException(BridgeErrorCode.NotFound, $"No exception '{name}' in '{group}'.", "Call exception_list with group and filter to see the names.");
        }

        settings.SetBreakWhenThrown(value, setting);
    }

    /// <summary>HRESULT of <c>DISP_E_BADINDEX</c>, which Exception Settings answers for a group or name it does not have.</summary>
    private const int DispEBadIndex = unchecked((int)0x8002000B);

    /// <summary>One boolean of Tools → Options → Debugging → General; null when this Visual Studio does not have it or it is not a boolean.</summary>
    private bool? DebuggingFlag(string name)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            return dte.get_Properties("Debugging", "General").Item(name).Value as bool?;
        }
        catch (COMException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public IReadOnlyList<RawProcess> LocalProcesses()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        int self;
        using (var me = System.Diagnostics.Process.GetCurrentProcess()) self = me.Id;
        var result = new List<RawProcess>();
        foreach (EnvDTE.Process p in dte.Debugger.LocalProcesses)
        {
            if (p.ProcessID == self) continue;
            result.Add(new RawProcess { Pid = p.ProcessID, Name = System.IO.Path.GetFileNameWithoutExtension(p.Name), OwnedByCurrentUser = ProcessOwner.IsCurrentUser(p.ProcessID) });
        }

        return result;
    }

    public void Attach(int pid)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        foreach (EnvDTE.Process p in dte.Debugger.LocalProcesses)
        {
            if (p.ProcessID != pid) continue;
            p.Attach();
            return;
        }

        throw new BridgeException(BridgeErrorCode.NotFound, $"Process {pid} is no longer running.");
    }

    public void DetachAll() { ThreadHelper.ThrowIfNotOnUIThread(); dte.Debugger.DetachAll(); }

    public void Go() { ThreadHelper.ThrowIfNotOnUIThread(); dte.Debugger.Go(WaitForBreakOrEnd: false); }

    public void Break() { ThreadHelper.ThrowIfNotOnUIThread(); dte.Debugger.Break(WaitForBreakMode: false); }

    public void Stop() { ThreadHelper.ThrowIfNotOnUIThread(); dte.Debugger.Stop(WaitForDesignMode: false); }

    public void Step(StepKind kind)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        switch (kind)
        {
            case StepKind.Into: dte.Debugger.StepInto(WaitForBreakOrEnd: false); break;
            case StepKind.Out: dte.Debugger.StepOut(WaitForBreakOrEnd: false); break;
            default: dte.Debugger.StepOver(WaitForBreakOrEnd: false); break;
        }
    }

    public IReadOnlyList<RawFrame> Stack(int max)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var thread = dte.Debugger.CurrentThread;
        if (thread is null) return [];
        return thread.StackFrames.Cast<EnvDTE.StackFrame>().Take(max).Select(ToFrame).ToList();
    }

    public int? CurrentThreadId
    {
        get { ThreadHelper.ThrowIfNotOnUIThread(); return dte.Debugger.CurrentThread?.ID; }
    }

    public IReadOnlyList<RawThread> Threads()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var result = new List<RawThread>();
        var program = dte.Debugger.CurrentProgram;
        if (program is null) return result;
        foreach (EnvDTE.Thread t in program.Threads)
        {
            // Location fails for threads without managed frames; the thread is still listed.
            var hasLocation = ComSafe.TryRead(() => { ThreadHelper.ThrowIfNotOnUIThread(); return t.Location; }, out var location);
            result.Add(new RawThread { Id = t.ID, Name = t.Name ?? "", Location = hasLocation && !string.IsNullOrEmpty(location) ? location : null });
        }
        return result;
    }

    public IReadOnlyList<RawFrame>? ThreadStack(int threadId, int max)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var thread = FindThread(threadId);
        if (thread is null) return null;
        var frames = new List<RawFrame>();
        foreach (EnvDTE.StackFrame f in thread.StackFrames)
        {
            if (frames.Count >= max) break;
            frames.Add(ToFrame(f));
        }

        return frames;
    }

    public int CurrentFrameIndex
    {
        get
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var current = dte.Debugger.CurrentStackFrame;
            var thread = dte.Debugger.CurrentThread;
            if (current is null || thread is null) return 0;
            var index = 0;
            foreach (EnvDTE.StackFrame f in thread.StackFrames)
            {
                // Same COM object → same RCW; the field comparison covers a re-created wrapper.
                if (ReferenceEquals(f, current) || SameFrame(f, current)) return index;
                if (++index >= DebugBridgeService.MaxStackFrames) break;
            }

            return 0;
        }
    }

    public void SelectFrame(int? threadId, int index)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (threadId is { } id)
            dte.Debugger.CurrentThread = FindThread(id) ?? throw new BridgeException(BridgeErrorCode.NotFound, $"No thread with id {id}.");
        var i = 0;
        foreach (EnvDTE.StackFrame f in dte.Debugger.CurrentThread.StackFrames)
        {
            if (i++ != index) continue;
            dte.Debugger.CurrentStackFrame = f;
            return;
        }

        throw new BridgeException(BridgeErrorCode.NotFound, $"Frame {index} does not exist.");
    }

    public RawExceptionInfo? CurrentException(int maxInner)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var e = dte.Debugger.GetExpression("$exception", false, EvalTimeoutMs);
        if (e is null || !e.IsValidValue) return null;
        var result = new RawExceptionInfo { Type = RuntimeType(e.Type), Message = MemberValue(e, "Message") ?? "", StackTrace = MemberValue(e, "StackTrace") };
        var inner = Member(e, "InnerException");
        while (inner is not null && inner.IsValidValue && inner.Value != "null" && result.Inner.Count < maxInner)
        {
            result.Inner.Add((RuntimeType(inner.Type), MemberValue(inner, "Message") ?? ""));
            inner = Member(inner, "InnerException");
        }

        return result;
    }

    private static Expression? Member(Expression parent, string name)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        foreach (Expression m in parent.DataMembers)
        {
            if (m.Name == name) return m;
        }

        return null;
    }

    private static string? MemberValue(Expression parent, string name)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var m = Member(parent, name);
        if (m is null || !m.IsValidValue || m.Value == "null") return null;
        var v = m.Value;
        return v.Length >= 2 && v[0] == '"' && v[v.Length - 1] == '"' ? v.Substring(1, v.Length - 2) : v;
    }

    /// <summary>"System.Exception {System.ArgumentException}" (declared {runtime}) → the runtime type.</summary>
    private static string RuntimeType(string type)
    {
        var open = type.IndexOf('{');
        var close = type.LastIndexOf('}');
        return open >= 0 && close > open ? type.Substring(open + 1, close - open - 1) : type;
    }

    public IReadOnlyList<RawOutputPane> OutputPanes()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var result = new List<RawOutputPane>();
        foreach (OutputWindowPane pane in dte.ToolWindows.OutputWindow.OutputWindowPanes)
        {
            if (!Guid.TryParse(pane.Guid, out var id)) continue;
            // TextDocument fails for a pane Visual Studio has not created text for yet: list it, size unknown.
            var known = ComSafe.TryRead(() => { ThreadHelper.ThrowIfNotOnUIThread(); return LineCount(pane.TextDocument); }, out var lines);
            result.Add(new RawOutputPane { Id = id, Name = pane.Name, Lines = known ? lines : null });
        }

        return result;
    }

    public IReadOnlyList<string> OutputLines(Guid paneId, int firstLine, int count)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        foreach (OutputWindowPane pane in dte.ToolWindows.OutputWindow.OutputWindowPanes)
        {
            if (!Guid.TryParse(pane.Guid, out var id) || id != paneId) continue;
            var text = pane.TextDocument.StartPoint.CreateEditPoint().GetLines(firstLine, firstLine + count);
            return text.Split(["\r\n", "\n"], StringSplitOptions.None).Take(count).ToList();
        }

        return [];
    }

    private static int LineCount(TextDocument doc)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var end = doc.EndPoint;
        return end.AtStartOfLine ? end.Line - 1 : end.Line;
    }

    private static bool SameFrame(EnvDTE.StackFrame a, EnvDTE.StackFrame b)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var a2 = a as StackFrame2;
        var b2 = b as StackFrame2;
        return a.FunctionName == b.FunctionName && a2?.FileName == b2?.FileName && a2?.LineNumber == b2?.LineNumber;
    }

    private EnvDTE.Thread? FindThread(int id)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var program = dte.Debugger.CurrentProgram;
        if (program is null) return null;
        foreach (EnvDTE.Thread t in program.Threads)
        {
            if (t.ID == id) return t;
        }

        return null;
    }

    public IReadOnlyList<VariableInfo> Variables(VariableScope scope, int maxItems)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var frame = dte.Debugger.CurrentStackFrame;
        if (frame is null) return [];
        var expressions = scope == VariableScope.Args ? frame.Arguments : frame.Locals;
        return expressions.Cast<Expression>().Take(maxItems).Select(ToInfo).ToList();
    }

    public IReadOnlyList<VariableInfo> Members(string expression, int maxItems)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var e = dte.Debugger.GetExpression(expression, true, EvalTimeoutMs);
        if (!e.IsValidValue)
            throw new BridgeException(BridgeErrorCode.EvalFailed, e.Value, "Check the path against vars output.");
        return e.DataMembers.Cast<Expression>().Take(maxItems).Select(ToInfo).ToList();
    }

    public EvaluateResult Evaluate(string expression)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var e = dte.Debugger.GetExpression(expression, true, EvalTimeoutMs);
        return new EvaluateResult { IsValid = e.IsValidValue, Value = e.Value, Type = e.Type };
    }

    public EvaluateResult SetValue(string expression, string value)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var target = dte.Debugger.GetExpression(expression, true, EvalTimeoutMs);
        if (!target.IsValidValue)
            return new EvaluateResult { IsValid = false, Value = target.Value, Type = target.Type };
        try
        {
            target.Value = value;
        }
        catch (COMException e)
        {
            return new EvaluateResult { IsValid = false, Value = $"Visual Studio could not assign the value: {e.Message}", Type = target.Type };
        }

        var after = dte.Debugger.GetExpression(expression + ", nse", true, EvalTimeoutMs);
        return new EvaluateResult { IsValid = after.IsValidValue, Value = after.Value, Type = after.Type };
    }

    private static RawFrame ToFrame(EnvDTE.StackFrame f)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var f2 = f as StackFrame2;
        return new RawFrame
        {
            Function = f.FunctionName,
            Language = f.Language,
            File = string.IsNullOrEmpty(f2?.FileName) ? null : f2!.FileName,
            Line = f2 is null || f2.LineNumber == 0 ? null : (int)f2.LineNumber,
        };
    }

    private static VariableInfo ToInfo(Expression e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return new VariableInfo { Name = e.Name, Type = e.Type, Value = e.Value, HasChildren = e.DataMembers.Count > 0 };
    }

    private static RawBreakpoint ToRaw(Breakpoint b)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var b2 = b as Breakpoint2;
        var isFileLine = !string.IsNullOrEmpty(b.File);
        return new RawBreakpoint
        {
            File = isFileLine ? b.File : null,
            Line = isFileLine ? b.FileLine : null,
            Function = isFileLine ? null : b.FunctionName,
            Condition = string.IsNullOrEmpty(b.Condition) ? null : b.Condition,
            ConditionKind = b.ConditionType == dbgBreakpointConditionType.dbgBreakpointConditionTypeWhenChanged ? ConditionKind.WhenChanged : ConditionKind.WhenTrue,
            HitCount = b.HitCountTarget,
            HitCountKind = b.HitCountType switch
            {
                dbgHitCountType.dbgHitCountTypeEqual => HitCountKind.Equal,
                dbgHitCountType.dbgHitCountTypeGreaterOrEqual => HitCountKind.AtLeast,
                dbgHitCountType.dbgHitCountTypeMultiple => HitCountKind.Multiple,
                _ => HitCountKind.None,
            },
            LogMessage = b2 is not null && !b2.BreakWhenHit && !string.IsNullOrEmpty(b2.Message) ? b2.Message : null,
            Enabled = b.Enabled,
            BoundCount = b.Children?.Count ?? 0,
        };
    }
}
