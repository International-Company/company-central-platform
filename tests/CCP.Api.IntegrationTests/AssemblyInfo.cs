using Xunit;

// Integration tests run one class at a time.
//
// Each test class gets its own database, but the setting that points the host at
// that database is an environment variable, which is process-global. Two classes
// running at once would overwrite each other's connection string and send one
// class's requests to the other's database -- producing failures that look like
// data bugs and are not.
//
// It is an environment variable because the composition root resolves the
// connection string from builder.Configuration before builder.Build(), so that a
// deployment pointed at no database fails at startup rather than at its first
// request. Nothing a WebApplicationFactory contributes is visible that early.
// The fail-fast is worth more than the wall-clock.
//
// This is the whole of the reason, and it is deliberately the whole of it: every
// other per-class setting has been moved onto the host itself (UseSetting), so
// if the fail-fast read ever changes, this attribute can come off in one edit
// rather than after an investigation into what else was relying on it.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
