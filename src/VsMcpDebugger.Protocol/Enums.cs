namespace VsMcpDebugger.Protocol;

public enum DebuggerMode { Design, Run, Break }

public enum StepKind { Into, Over, Out }

public enum BreakReason { Breakpoint, Step, Exception, Pause, Other, RunTo }

public enum ConditionKind { WhenTrue, WhenChanged }

public enum HitCountKind { None, Equal, AtLeast, Multiple }

public enum VariableScope { Locals, Args }

/// <summary>Declaration order is the sort order of errors_list.</summary>
public enum ErrorSeverity { Error, Warning, Message }

public enum PermissionLevel { Allow, Ask, Deny }

public enum LogVerbosity { Error, Warning, Information, Debug }
