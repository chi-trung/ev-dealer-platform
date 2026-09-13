using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NotificationService.Controllers;
using NotificationService.Services;
using Xunit;

namespace DealerSystem.Tests.NotificationService.Tests;

/// <summary>
/// Issue #36: the registry API is authenticated and write-scoped to the
/// caller's own subjects — "user:&lt;id claim&gt;" always, "dealer:&lt;n&gt;"
/// only when the JWT carries a matching "dealer" claim. The controller is
/// unit-tested directly with a synthetic ClaimsPrincipal (a real
/// ClaimsIdentity with an authenticated type so User.FindFirst works),
/// against an in-memory registry fake; the [Authorize] 401 layer itself is
/// framework behavior, verified by the live anonymous-PUT probe rather than
/// pulled in via WebApplicationFactory.
/// The tests here pin the authorization DECISION table — 204 vs 403 per
/// (claims, key) — because a wrong cell there is exactly the privilege bug
/// the issue set out to close (plant tokens on someone else's subject).
/// </summary>
public class DeviceTokensAuthTests
{
    private sealed class FakeRegistry : IDeviceTokenRegistry
    {
        public string? LastRegisterKey { get; private set; }
        public string? LastRegisterToken { get; private set; }
        public string? LastRevokeKey { get; private set; }
        public bool RevokeResult { get; set; } = true;

        public Task RegisterAsync(string key, string token, CancellationToken ct = default)
        {
            LastRegisterKey = key; LastRegisterToken = token;
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<string>> GetTokensAsync(string key, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<bool> RevokeAsync(string key, string token, CancellationToken ct = default)
        {
            LastRevokeKey = key;
            return Task.FromResult(RevokeResult);
        }
    }

    private static DeviceTokensController ControllerWith(
        FakeRegistry registry, params (string Type, string Value)[] claims)
    {
        var identity = new ClaimsIdentity(
            claims.Select(c => new Claim(c.Type, c.Value)), authenticationType: "Test");
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        return new DeviceTokensController(registry)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    // ---- own subjects are writable -------------------------------------------

    [Fact]
    public async Task Put_OwnUserSubject_Succeeds()
    {
        var reg = new FakeRegistry();
        var res = await ControllerWith(reg, ("id", "7"))
            .Register("user:7", new RegisterDeviceTokenRequest("tok-a"), default);

        Assert.IsType<NoContentResult>(res);
        Assert.Equal(("user:7", "tok-a"), (reg.LastRegisterKey, reg.LastRegisterToken));
    }

    [Fact]
    public async Task Put_DealerSubject_WithMatchingClaim_Succeeds()
    {
        var reg = new FakeRegistry();
        var res = await ControllerWith(reg, ("id", "7"), ("dealer", "3"))
            .Register("dealer:3", new RegisterDeviceTokenRequest("tok-a"), default);

        Assert.IsType<NoContentResult>(res);
    }

    // ---- everything else is 403 ----------------------------------------------

    [Fact]
    public async Task Put_OtherUsersSubject_Forbidden()
    {
        // The Issue #33 hole, closed: user 7 must not plant tokens on user 8.
        var reg = new FakeRegistry();
        var res = await ControllerWith(reg, ("id", "7"))
            .Register("user:8", new RegisterDeviceTokenRequest("tok-x"), default);

        Assert.Equal(403, Assert.IsType<ObjectResult>(res).StatusCode);
        Assert.Null(reg.LastRegisterKey); // nothing reached the registry
    }

    [Fact]
    public async Task Put_AnyCustomerSubject_Forbidden()
    {
        // Customer subjects have no owner in the staff JWT — nobody may write
        // them from the portal (the consumer read path is unaffected).
        var reg = new FakeRegistry();
        var res = await ControllerWith(reg, ("id", "7"), ("dealer", "3"))
            .Register("customer:42", new RegisterDeviceTokenRequest("tok-x"), default);

        Assert.Equal(403, Assert.IsType<ObjectResult>(res).StatusCode);
    }

    [Fact]
    public async Task Put_DealerSubject_WithoutDealerClaim_Forbidden()
    {
        var reg = new FakeRegistry();
        var res = await ControllerWith(reg, ("id", "7"))
            .Register("dealer:3", new RegisterDeviceTokenRequest("tok-x"), default);

        Assert.Equal(403, Assert.IsType<ObjectResult>(res).StatusCode);
    }

    [Fact]
    public async Task Put_DealerSubject_WrongNumber_Forbidden()
    {
        // dealer claim = 3 does not open dealer:4 (or dealer:03-style drift:
        // the comparison is against NotificationSubjects.Dealer(parsed), so
        // only the canonical spelling of THIS dealer id passes).
        var reg = new FakeRegistry();
        var ctl = ControllerWith(reg, ("id", "7"), ("dealer", "3"));

        foreach (var key in new[] { "dealer:4", "dealer:03", "Dealer:3", "dealer: 3" })
        {
            var res = await ctl.Register(key, new RegisterDeviceTokenRequest("tok-x"), default);
            Assert.Equal(403, Assert.IsType<ObjectResult>(res).StatusCode);
        }
    }

    [Fact]
    public async Task Put_MissingIdClaim_Forbidden()
    {
        // A JWT without the "id" claim (hand-forged with the dev key, or a
        // future token shape change) gets no user subject at all — fail closed.
        var reg = new FakeRegistry();
        var res = await ControllerWith(reg, ("unique_name", "root"))
            .Register("user:1", new RegisterDeviceTokenRequest("tok-x"), default);

        Assert.Equal(403, Assert.IsType<ObjectResult>(res).StatusCode);
    }

    [Fact]
    public async Task Get_OtherSubject_Forbidden_AndGet_OwnSubject_Ok()
    {
        // GET is scoped too: masked previews are still someone else's device
        // inventory (enumeration leak #33 guarded with masking; ownership now).
        var reg = new FakeRegistry();
        var ctl = ControllerWith(reg, ("id", "7"));

        Assert.Equal(403, Assert.IsType<ObjectResult>(
            await ctl.List("user:8", default)).StatusCode);

        Assert.IsType<OkObjectResult>(await ctl.List("user:7", default));
    }

    [Fact]
    public async Task Delete_OtherSubject_Forbidden_AndOwnSubject_PassesThrough()
    {
        var reg = new FakeRegistry();
        var ctl = ControllerWith(reg, ("id", "7"));

        Assert.Equal(403, Assert.IsType<ObjectResult>(
            await ctl.Revoke("user:8", "tok-x", default)).StatusCode);
        Assert.Null(reg.LastRevokeKey);

        Assert.IsType<NoContentResult>(await ctl.Revoke("user:7", "tok-x", default));
        Assert.Equal("user:7", reg.LastRevokeKey);
    }

    [Fact]
    public async Task Put_OwnSubject_WhitespaceVariant_NormalizedToSameScope()
    {
        // The registry trims keys; the scope check trims too, so " user:7 "
        // and "user:7" are the same subject for BOTH auth and storage — no
        // way to smuggle "user:7 " past auth into a distinct registry row.
        var reg = new FakeRegistry();
        var res = await ControllerWith(reg, ("id", "7"))
            .Register("  user:7  ", new RegisterDeviceTokenRequest("tok-a"), default);

        Assert.IsType<NoContentResult>(res);
        Assert.Equal("  user:7  ", reg.LastRegisterKey); // registry trims downstream
    }

    [Fact]
    public async Task Put_NegativeAndHugeIdClaims_DoNotMatchAnything()
    {
        // int.TryParse accepts "-5" and huge values; NotificationSubjects.User
        // would build "user:-5" — a principal claiming id=-5 may "own" that
        // string, but no consumer and no legitimate account ever uses it, and
        // it cannot open user:5. Pin that the claim value is used verbatim.
        var reg = new FakeRegistry();
        var ctl = ControllerWith(reg, ("id", "-5"));

        Assert.Equal(403, Assert.IsType<ObjectResult>(
            await ctl.Register("user:5", new RegisterDeviceTokenRequest("tok-x"), default)).StatusCode);
        Assert.IsType<NoContentResult>(
            await ctl.Register("user:-5", new RegisterDeviceTokenRequest("tok-x"), default));
    }
}
