using CCP.Kernel.Results;
using CCP.Modules.Organization.Domain.Employees;

namespace CCP.Modules.Organization.UnitTests.Domain;

/// <summary>
/// Reporting-line cycle prevention.
/// <para>
/// The workflow engine walks this chain to find an approver
/// (ARCHITECTURE.md §16.3). A loop would either hang that walk or route an
/// approval to the wrong person — neither acceptable in an approval path — so
/// loops are prevented where they would be created, not defended against on
/// every read.
/// </para>
/// </summary>
public sealed class ManagementChainTests
{
    /// <summary>An in-memory reporting graph: employee → their manager.</summary>
    private sealed class Graph
    {
        private readonly Dictionary<Guid, Guid?> _managers = [];

        public Guid Add(Guid? managerId = null)
        {
            Guid id = Guid.CreateVersion7();
            _managers[id] = managerId;

            return id;
        }

        public void SetManager(Guid employeeId, Guid? managerId) => _managers[employeeId] = managerId;

        public Task<Guid?> ManagerOfAsync(Guid employeeId, CancellationToken cancellationToken)
            => Task.FromResult(_managers.GetValueOrDefault(employeeId));
    }

    [Fact]
    public async Task ClearingTheManager_IsAlwaysAllowed()
    {
        var graph = new Graph();
        Guid employee = graph.Add();

        Result result = await ManagementChain.ValidateAssignmentAsync(
            employee, null, graph.ManagerOfAsync);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task AnUnrelatedManager_IsAllowed()
    {
        var graph = new Graph();
        Guid director = graph.Add();
        Guid employee = graph.Add();

        Result result = await ManagementChain.ValidateAssignmentAsync(
            employee, director, graph.ManagerOfAsync);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task SelfManagement_IsRejected()
    {
        var graph = new Graph();
        Guid employee = graph.Add();

        Result result = await ManagementChain.ValidateAssignmentAsync(
            employee, employee, graph.ManagerOfAsync);

        Assert.True(result.IsFailure);
        Assert.Equal("ORGANIZATION.EMPLOYEE_CANNOT_MANAGE_SELF", result.Error.Code);
    }

    [Fact]
    public async Task ADirectTwoPersonLoop_IsRejected()
    {
        // A manages B. Making B manage A closes the loop.
        var graph = new Graph();
        Guid a = graph.Add();
        Guid b = graph.Add(a);

        Result result = await ManagementChain.ValidateAssignmentAsync(a, b, graph.ManagerOfAsync);

        Assert.True(result.IsFailure);
        Assert.Equal("ORGANIZATION.MANAGEMENT_CYCLE", result.Error.Code);
    }

    [Fact]
    public async Task AnIndirectLoop_IsRejected()
    {
        // A → B → C → D. Making A report to D closes a four-person loop, which
        // no single-link check would catch.
        var graph = new Graph();
        Guid a = graph.Add();
        Guid b = graph.Add(a);
        Guid c = graph.Add(b);
        Guid d = graph.Add(c);

        Result result = await ManagementChain.ValidateAssignmentAsync(a, d, graph.ManagerOfAsync);

        Assert.True(result.IsFailure);
        Assert.Equal("ORGANIZATION.MANAGEMENT_CYCLE", result.Error.Code);
    }

    [Fact]
    public async Task ADeepButValidChain_IsAllowed()
    {
        // Fifty levels is absurd for a real company but must not be refused as
        // if it were a cycle.
        var graph = new Graph();
        Guid top = graph.Add();
        Guid current = top;

        for (int i = 0; i < 50; i++)
        {
            current = graph.Add(current);
        }

        Guid newcomer = graph.Add();

        Result result = await ManagementChain.ValidateAssignmentAsync(
            newcomer, current, graph.ManagerOfAsync);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task PreExistingCorruptData_TerminatesRatherThanHanging()
    {
        // If a loop already exists — written directly to the database, or by an
        // older version — the walk must stop rather than run forever.
        var graph = new Graph();
        Guid a = graph.Add();
        Guid b = graph.Add(a);
        graph.SetManager(a, b);

        Guid outsider = graph.Add();

        Result result = await ManagementChain.ValidateAssignmentAsync(
            outsider, a, graph.ManagerOfAsync);

        // It detects the existing loop rather than looping.
        Assert.True(result.IsFailure);
        Assert.Equal("ORGANIZATION.MANAGEMENT_CYCLE", result.Error.Code);
    }

    [Fact]
    public async Task WalkUp_ReturnsTheChainNearestFirst()
    {
        var graph = new Graph();
        Guid chief = graph.Add();
        Guid director = graph.Add(chief);
        Guid manager = graph.Add(director);
        Guid employee = graph.Add(manager);

        IReadOnlyList<Guid> chain = await ManagementChain.WalkUpAsync(employee, graph.ManagerOfAsync);

        Assert.Equal([manager, director, chief], chain);
    }

    [Fact]
    public async Task WalkUp_IsEmptyAtTheTop()
    {
        var graph = new Graph();
        Guid chief = graph.Add();

        Assert.Empty(await ManagementChain.WalkUpAsync(chief, graph.ManagerOfAsync));
    }

    [Fact]
    public async Task WalkUp_TerminatesOnCorruptData()
    {
        // A truncated answer is bad; a hung approval request is worse.
        var graph = new Graph();
        Guid a = graph.Add();
        Guid b = graph.Add(a);
        graph.SetManager(a, b);

        IReadOnlyList<Guid> chain = await ManagementChain.WalkUpAsync(b, graph.ManagerOfAsync);

        Assert.True(chain.Count <= ManagementChain.MaxDepth);
    }
}
