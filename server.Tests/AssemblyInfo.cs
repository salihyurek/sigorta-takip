using Xunit;

// Several tests mutate process-wide environment variables (DATA_DIR, SUPERADMIN_*),
// so run test classes sequentially to avoid cross-test interference.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
