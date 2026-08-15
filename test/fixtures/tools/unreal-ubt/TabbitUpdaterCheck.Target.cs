// A UnrealBuildTool target that exists to compile one file: the updater the Unreal target
// emits. Copied into an engine's Source/Programs by the test that runs it, built, and
// removed again.
//
// A Program rather than a game or an editor target, because what needs proving is that the
// updater's calls into Core and HTTP are real - and a Program links those two and nothing
// else. An editor target would link the whole engine to answer the same question, which is
// twenty minutes instead of two.
//
// Modelled on the engine's own BlankProgram, which is what Epic ships this shape for.

using UnrealBuildTool;

[SupportedPlatforms(UnrealPlatformClass.Desktop)]
public class TabbitUpdaterCheckTarget : TargetRules
{
	public TabbitUpdaterCheckTarget(TargetInfo Target) : base(Target)
	{
		Type = TargetType.Program;
		LinkType = TargetLinkType.Monolithic;
		LaunchModuleName = "TabbitUpdaterCheck";

		// Lean: no editor, no UObject, no engine. The updater uses none of them, and every
		// one of them would be minutes of link time to prove nothing.
		bBuildDeveloperTools = false;
		bUseMallocProfiler = false;
		bBuildWithEditorOnlyData = true;
		bCompileAgainstEngine = false;
		bCompileAgainstCoreUObject = false;
		bCompileAgainstApplicationCore = false;

		bIsBuildingConsoleApplication = true;
	}
}
