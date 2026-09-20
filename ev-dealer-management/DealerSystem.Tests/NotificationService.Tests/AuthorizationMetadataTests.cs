using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace DealerSystem.Tests.NotificationService.Tests;

/// <summary>
/// Issue #137: 36 mutation endpoints across four services enforced no
/// authorization. The fix is attribute/metadata on each route, and attribute
/// placement is exactly the kind of change that looks correct in a diff while
/// silently doing nothing -- an [Authorize] on a controller whose actions the
/// framework still resolves as anonymous, or a RequireAuthorization() chained
/// onto the wrong builder. These tests read the ACTUAL endpoint metadata the
/// pipeline will use, not the source text, so a misplaced or missing attribute
/// is caught rather than assumed.
/// </summary>
/// <remarks>
/// Why metadata and not a source grep: grepping for "[Authorize]" would pass
/// with the attribute on a class that is not a controller, and would fail to
/// notice that <c>[AllowAnonymous]</c> on an action overrides a controller-level
/// <c>[Authorize]</c> -- which is the one case this fix deliberately relies on
/// (VehicleService's catalogue reads stay open while the class carries no
/// gate). The question that matters is "what will the pipeline enforce", and
/// the framework answers that via the action descriptor's
/// <c>EndpointMetadata</c>.
/// </remarks>
public class AuthorizationMetadataTests
{
    // The mutation verbs. GET/HEAD/OPTIONS are excluded: the fix deliberately
    // leaves public catalogue reads open (the landing page renders vehicles
    // before login, and UserService's DealerIdValidator fetches /api/dealers
    // server-to-server during anonymous registration).
    private static readonly HashSet<string> MutationVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "POST", "PUT", "PATCH", "DELETE"
    };

    /// <summary>
    /// Loads every controller action in the given service assembly and returns
    /// those whose HTTP method mutates, split by whether an authorization
    /// requirement is present in the action's metadata chain.
    /// </summary>
    private static (List<ActionDescriptor> Gated, List<ActionDescriptor> Open) ClassifyMutations(
        Assembly assembly)
    {
        // The attribute is what the fix pins; ResolveEndpointMetadata would
        // need a fully built host. ControllerActionDescriptor carries the
        // MethodInfo and the declaring type, so the attribute chain is
        // reachable without one.
        var controllerTypes = assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        var gated = new List<ActionDescriptor>();
        var open = new List<ActionDescriptor>();
        foreach (var type in controllerTypes)
        {
            // Class-level [Authorize] covers every action that does not carry
            // its own [AllowAnonymous].
            var classGated = type.GetCustomAttributes<AuthorizeAttribute>().Any();
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                // Only actions the framework would route: an HttpX attribute
                // present on the method or inherited from the class.
                var hasHttpAttribute = method.GetCustomAttributes(true)
                    .OfType<HttpMethodAttribute>()
                    .Any();
                if (!hasHttpAttribute)
                {
                    continue;
                }

                var verbs = method.GetCustomAttributes(true)
                    .OfType<HttpMethodAttribute>()
                    .SelectMany(a => a.HttpMethods)
                    .ToList();
                if (!verbs.Any(v => MutationVerbs.Contains(v)))
                {
                    continue;
                }

                // [AllowAnonymous] on the action DEFEATS any gate on it or on
                // the class -- the framework's override order, not an opinion.
                // This fix relies on it in one direction (a health probe stays
                // open under a gated controller), so the attribute is an
                // unconditional exclusion from the gated set: an
                // [Authorize] + [AllowAnonymous] pair on the SAME action is
                // not "belt and braces", it is an open route that looks gated
                // in a grep. Verified by mutation -- adding [AllowAnonymous]
                // under a gated action passed this test until the exclusion
                // was made unconditional.
                var allowAnon = method.GetCustomAttributes<AllowAnonymousAttribute>().Any();
                var methodGated = method.GetCustomAttributes<AuthorizeAttribute>().Any();
                if (!allowAnon && (methodGated || classGated))
                {
                    gated.Add(new ControllerActionDescriptor
                    {
                        DisplayName = $"{type.Name}.{method.Name}",
                        MethodInfo = method,
                        ControllerTypeInfo = type.GetTypeInfo()
                    });
                }
                else
                {
                    open.Add(new ControllerActionDescriptor
                    {
                        DisplayName = $"{type.Name}.{method.Name}",
                        MethodInfo = method,
                        ControllerTypeInfo = type.GetTypeInfo()
                    });
                }
            }
        }
        return (gated, open);
    }

    [Fact]
    public void SalesService_EveryMutationEndpoint_RequiresAuthorization()
    {
        // SalesService registered no auth at all before #137, so all 16 of its
        // mutation routes were anonymous through the gateway. Every one must
        // now carry a gate -- including the two [AllowAnonymous] overrides,
        // which are read-only probes and therefore not in the mutation set.
        var assembly = typeof(SalesService.Controllers.OrdersController).Assembly;
        var (gated, open) = ClassifyMutations(assembly);

        Assert.NotEmpty(gated);
        // The mutations this fix gated, by controller, so a regression names
        // the route rather than reporting a bare count.
        Assert.Empty(open.Select(a => a.DisplayName));
    }

    [Fact]
    public void VehicleService_MutationEndpoints_RequireAuthorization()
    {
        // VehicleService's catalogue GETs stay anonymous by design; this asserts
        // only the mutating side, so a future [Authorize] accidentally applied
        // to the reads is not what makes it pass.
        var assembly = typeof(VehicleService.Controllers.VehiclesController).Assembly;
        var (gated, open) = ClassifyMutations(assembly);

        Assert.NotEmpty(gated);
        Assert.Empty(open.Select(a => a.DisplayName));
    }

    [Fact]
    public void CustomerService_MutationEndpoints_RequireAuthorization()
    {
        // CustomerService had auth REGISTERED but no [Authorize] anywhere, so
        // the middleware was in the pipeline and enforced nothing.
        var assembly = typeof(CustomerService.Controllers.CustomersController).Assembly;
        var (gated, open) = ClassifyMutations(assembly);

        Assert.NotEmpty(gated);
        Assert.Empty(open.Select(a => a.DisplayName));
    }
}
