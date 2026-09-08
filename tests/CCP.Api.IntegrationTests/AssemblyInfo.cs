using Xunit;

// Integration tests run one class at a time.
//
// Each test class gets its own database, but the settings that point the host at
// that database travel through environment variables, which are process-global.
// Two classes running at once would overwrite each other's connection string and
// send one class's requests to the other's database — producing failures that
// look like data bugs and are not.
//
// The cost is wall-clock time. The alternative is a suite whose failures cannot
// be trusted, which is worse than a slow one.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
