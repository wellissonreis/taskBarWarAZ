using Xunit;

// All tests share the process-wide "SideWar.Scene" window class (one SceneWindow at a time).
[assembly: CollectionBehavior(DisableTestParallelization = true)]
