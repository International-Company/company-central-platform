using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CCP.Architecture.Tests;

/// <summary>
/// Every service an endpoint takes is bound with <c>[FromServices]</c>, never by
/// inference.
/// <para>
/// <b>This was a real failure in Phase 1 and has been a written rule with
/// nothing enforcing it ever since.</b> Minimal APIs will work out that a
/// parameter is a service by asking the container what happens to be registered
/// at the moment the route is mapped. That answer depends on the composition —
/// so an endpoint that binds correctly in the host can fail in a test harness,
/// or in a deployment with one module switched off, and it fails at runtime
/// rather than at build.
/// </para>
/// <para>
/// <b>Most of the rule is already enforced, by accident, and unreadably.</b>
/// Mapping a route whose service parameter is not registered in this harness
/// throws <c>Failure to infer one or more parameters</c> while the host starts,
/// so the architecture suite goes red — with a framework error that says
/// nothing about the rule being broken. What is caught here is stated instead:
/// the exception is translated below, and the parameters that <i>do</i> infer
/// successfully, because their type happens to be registered, are caught by the
/// assertion.
/// </para>
/// <para>
/// The rule was listed among the non-negotiables as "enforced by the build". It
/// was, in the way a tripwire enforces a locked door. This says so out loud.
/// </para>
/// </summary>
public sealed class ServiceBindingTests
{
    [Fact]
    public async Task EveryEndpointServiceParameter_IsBoundExplicitly()
    {
        IReadOnlyList<RouteEndpoint> endpoints = await ApiEndpointsAsync();

        Assert.NotEmpty(endpoints);

        var inferred = new List<string>();

        foreach (RouteEndpoint endpoint in endpoints)
        {
            MethodInfo? handler = endpoint.Metadata.GetMetadata<MethodInfo>();

            if (handler is null)
            {
                continue;
            }

            foreach (ParameterInfo parameter in handler.GetParameters())
            {
                if (!LooksLikeAService(parameter.ParameterType) || IsBoundExplicitly(parameter))
                {
                    continue;
                }

                inferred.Add(
                    $"{endpoint.RoutePattern.RawText}: {parameter.ParameterType.Name} {parameter.Name}");
            }
        }

        Assert.True(
            inferred.Count == 0,
            "These endpoint parameters are services bound by inference. Mark them "
            + "[FromServices]: inference depends on what happens to be registered when the "
            + "route is mapped, which is a runtime failure under a different composition."
            + Environment.NewLine
            + string.Join(Environment.NewLine, inferred));
    }

    /// <summary>
    /// Proves the inspection can still see anything at all.
    /// <para>
    /// A rule expressed as "nothing matches" passes for ever the moment the
    /// matching stops working — which is how a guard becomes decoration. This
    /// asserts that endpoint methods and their parameters are actually being
    /// read, and that services are actually being recognised as services.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TheInspection_ActuallySeesServiceParameters()
    {
        IReadOnlyList<RouteEndpoint> endpoints = await ApiEndpointsAsync();

        int services = endpoints
            .Select(endpoint => endpoint.Metadata.GetMetadata<MethodInfo>())
            .Where(handler => handler is not null)
            .SelectMany(handler => handler!.GetParameters())
            .Count(parameter => LooksLikeAService(parameter.ParameterType));

        Assert.True(
            services > 50,
            $"Only {services} service parameters were recognised across "
            + $"{endpoints.Count} endpoints. The inspection has stopped seeing them, so the "
            + "test above is passing on an empty set.");
    }

    /// <summary>
    /// What counts as a service rather than something read off the request.
    /// <para>
    /// An interface, or a type the Platform's own application, infrastructure or
    /// kernel layers own. Request bodies are records declared in a module's
    /// <c>.Api</c> or <c>.Contracts</c> assembly, which is what keeps them out of
    /// this — and it is a structural difference rather than a naming convention,
    /// so it does not rot when somebody names a record badly.
    /// </para>
    /// </summary>
    private static bool LooksLikeAService(Type type)
    {
        string? assembly = type.Assembly.GetName().Name;

        if (assembly is null || !assembly.StartsWith("CCP.", StringComparison.Ordinal))
        {
            // Framework types — HttpContext, CancellationToken, ClaimsPrincipal —
            // are bound by the framework itself and need no attribute.
            return false;
        }

        if (type.IsInterface)
        {
            return true;
        }

        return assembly.Contains(".Application", StringComparison.Ordinal)
            || assembly.Contains(".Infrastructure", StringComparison.Ordinal)
            || assembly.StartsWith("CCP.Kernel", StringComparison.Ordinal);
    }

    private static bool IsBoundExplicitly(ParameterInfo parameter)
        => parameter.GetCustomAttributes(inherit: true).Any(attribute =>
            attribute is IFromServiceMetadata
                or IFromBodyMetadata
                or IFromRouteMetadata
                or IFromQueryMetadata
                or IFromHeaderMetadata
                or IFromFormMetadata);

    /// <summary>
    /// The same host the security tests boot, for the same reason: the endpoints
    /// under test are the ones that ship rather than a reimplementation of them.
    /// </summary>
    private static async Task<IReadOnlyList<RouteEndpoint>> ApiEndpointsAsync()
    {
        using IHost host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseStartup<ArchitectureTestStartup>();
            })
            .StartAsync();

        var dataSource = host.Services.GetRequiredService<EndpointDataSource>();

        try
        {
            // Enumerated here rather than returned lazily, so the failure below
            // is caught where it can be explained. The route table is built on
            // first read, not when the host starts.
            return
            [
                .. dataSource.Endpoints
                    .OfType<RouteEndpoint>()
                    .Where(e => e.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) == true)
            ];
        }
        catch (InvalidOperationException exception)
            when (exception.Message.Contains("infer", StringComparison.Ordinal))
        {
            // The rule, breaking in the way it actually breaks. Translated,
            // because the framework's message is a table of parameter sources
            // and gives no hint that a written rule has been broken -- and
            // because this is precisely the failure that shipped in Phase 1.
            Assert.Fail(
                "An endpoint parameter is a service bound by inference. Mark it "
                + "[FromServices]: inference asks the container what happens to be "
                + "registered when the route is mapped, so an endpoint that binds in "
                + "the host fails under a different composition -- at runtime, not at "
                + "build."
                + Environment.NewLine
                + Environment.NewLine
                + exception.Message);

            throw;
        }
    }
}
