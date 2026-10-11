namespace VsDebug.Protocol;

public enum DebuggerMode { Design, Run, Break }

public enum StepKind { Into, Over, Out }

public enum BreakReason { Breakpoint, Step, Exception, Pause, Other }

public enum ConditionKind { WhenTrue, WhenChanged }

public enum HitCountKind { None, Equal, AtLeast, Multiple }

public enum VariableScope { Locals, Args }
