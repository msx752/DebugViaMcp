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
using DebugViaMcp.Vsix.Core;

namespace DebugViaMcp.Vsix;

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

    // Rebuild = clean + build of that one project through the solution build manager, like Build > Rebuild.
    public void RebuildProject(string uniqueName)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var solution = (IVsSolution)Package.GetGlobalService(typeof(SVsSolution));
        var buildManager = (IVsSolutionBuildManager)Package.GetGlobalService(typeof(SVsSolutionBuildManager));
        ErrorHandler.ThrowOnFailure(solution.GetProjectOfUniqueName(uniqueName, out var hierarchy));
        const uint rebuild = (uint)(VSSOLNBUILDUPDATEFLAGS.SBF_OPERATION_CLEAN | VSSOLNBUILDUPDATEFLAGS.SBF_OPERATION_BUILD);
        ErrorHandler.ThrowOnFailure(buildManager.StartSimpleUpdateProjectConfiguration(hierarchy, null, null, rebuild, 0, 0));
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
