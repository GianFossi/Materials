// Author:        Claude
// Last revision: 2026-11-03
// Purpose:       Assembly-wide xUnit settings for the CrudApp tests.

using Xunit;

// The tests share process-wide state (the static AppLog and the WPF dispatcher) and wait on detached background work with
// time-based polling; running test classes in parallel made them fail at random. They run sequentially instead.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
