// The module rules for the check. Core and HTTP are what the updater names; anything else
// appearing here would mean the updater grew a dependency the generated Build.cs does not
// declare, which is itself worth failing over.

using UnrealBuildTool;

public class TabbitUpdaterCheck : ModuleRules
{
	public TabbitUpdaterCheck(ReadOnlyTargetRules Target) : base(Target)
	{
		PublicIncludePaths.Add("Runtime/Launch/Public");
		PrivateIncludePaths.Add("Runtime/Launch/Private");

		PrivateDependencyModuleNames.Add("Core");
		PrivateDependencyModuleNames.Add("Projects");

		// The dependency the generated Build.cs adds when the updater is written. If the
		// updater compiles here and not in a project, this line is where the difference is.
		PrivateDependencyModuleNames.Add("HTTP");
	}
}
