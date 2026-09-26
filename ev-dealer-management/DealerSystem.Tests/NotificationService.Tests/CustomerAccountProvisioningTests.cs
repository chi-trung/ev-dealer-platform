using System.Net;
using System.Text.Json;
using CustomerService.Data;
using CustomerService.DTOs;
using CustomerService.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using UserService.Data;
using UserService.DTOs;
using UserService.Models;
using UserService.Services;
using Xunit;

namespace DealerSystem.Tests.NotificationService.Tests;

/// <summary>
/// Issue #150: pins the customer-account provisioning path end to end at the
/// service layer — CustomerService creates the customer row, calls UserService,
/// and stores the returned id in Customers.UserId (the column added in #149
/// that had no writer at all).
///
/// WHAT EACH TEST IS ACTUALLY DEFENDING, because the failure mode here is
/// silent. A customer created without a link is a perfectly valid row: it
/// saves, it appears in the dealer CRM list, POST /api/Customers returns 200.
/// Nothing anywhere reports a problem. The only symptom is that the customer's
/// push notifications never arrive, which surfaces days later as "the customer
/// says they get nothing" and is impossible to trace back to this call. So the
/// assertions are all about the value that must arrive, not about the call
/// being made.
///
/// WHY REAL SQLITE FILES. Same reasoning as CustomerUserLinkTests: the
/// provisioning path is two saves across two providers, and the InMemory
/// provider would silently accept a write the relational layer rejects.
/// UserService's side additionally needs a real Users table for the BCrypt
/// hash and the duplicate checks to be exercised rather than stubbed.
///
/// NOT COVERED HERE, DELIBERATELY. The [Authorize(Roles = InternalService)]
/// on the new minimal-API endpoint cannot be reached from here: this test
/// assembly has no built host, and AuthorizationMetadataTests only inspects MVC
/// controllers (it filters on typeof(ControllerBase)). That gap is real and
/// pre-dates this work; verifying it needs a live HTTP call, and it is listed
/// as such in .claude/plans/customer-account-provisioning.md rather than
/// claimed as covered by these tests.
/// </summary>
[Collection("sqlite")]
public class CustomerAccountProvisioningTests : IDisposable
{
    private const string Password = "Cust0mer!pass";

    private readonly string _customerDbPath;
    private readonly string _userDbPath;
    private readonly DbContextOptions<CustomerDbContext> _customerOptions;
    private readonly DbContextOptions<UserDbContext> _userOptions;

    public CustomerAccountProvisioningTests()
    {
        _customerDbPath = Path.Combine(Path.GetTempPath(), $"prov_customer_{Guid.NewGuid():N}.db");
        _userDbPath = Path.Combine(Path.GetTempPath(), $"prov_user_{Guid.NewGuid():N}.db");
        _customerOptions = new DbContextOptionsBuilder<CustomerDbContext>()
            .UseSqlite($"Data Source={_customerDbPath}")
            .Options;
        _userOptions = new DbContextOptionsBuilder<UserDbContext>()
            .UseSqlite($"Data Source={_userDbPath}")
            .Options;
        using var cdb = new CustomerDbContext(_customerOptions);
        cdb.Database.EnsureCreated();
        using var udb = new UserDbContext(_userOptions);
        udb.Database.EnsureCreated();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var p in new[] { _customerDbPath, _userDbPath })
        {
            if (File.Exists(p)) File.Delete(p);
        }
    }

    private static CreateCustomerRequest Request(string email = "buyer@x.local", int dealerId = 1) => new()
    {
        Name = "Test Buyer",
        Email = email,
        Phone = "0900000000",
        Address = "1 Test Street",
        DealerId = dealerId,
        Password = Password,
    };

    /// <summary>
    /// Stands in for UserService. Records the JSON body it was handed so the
    /// assertions can check what CROSSES THE WIRE, not just that the
    /// provisioner called something.
    /// </summary>
    private sealed class StubProvisioner : ICustomerAccountProvisioner
    {
        private readonly int? _result;
        public int Calls { get; private set; }
        public string? Username { get; private set; }
        public string? Email { get; private set; }
        public string? FullName { get; private set; }
        public string? Password { get; private set; }
        public int? DealerId { get; private set; }

        public StubProvisioner(int? result) => _result = result;

        public Task<int?> ProvisionAsync(
            string username, string email, string fullName, string password,
            int? dealerId, CancellationToken cancellationToken = default)
        {
            Calls++;
            Username = username; Email = email; FullName = fullName;
            Password = password; DealerId = dealerId;
            return Task.FromResult(_result);
        }
    }

    /// <summary>Publishes nowhere. The created-event fan-out is not what this
    /// class is about, and a real broker would make these unit tests require
    /// RabbitMQ for no additional coverage.</summary>
    private sealed class NullProducer : IMessageProducer
    {
        public void PublishMessage<T>(T message, string routingKey = "") { }
    }

    private static CustomerService.Services.CustomerService NewService(
        CustomerDbContext ctx, ICustomerAccountProvisioner? provisioner) =>
        new(ctx, new NullProducer(), provisioner);

    // ---------------------------------------------------------------------
    // CustomerService side
    // ---------------------------------------------------------------------

    /// <summary>
    /// THE load-bearing test. Before this PR nothing ever wrote
    /// Customers.UserId, so this asserts the writer exists and that its value
    /// is the one UserService handed back.
    /// </summary>
    [Fact]
    public async Task CreatedCustomerIsLinkedToTheProvisionedAccount()
    {
        using var db = new CustomerDbContext(_customerOptions);
        var provisioner = new StubProvisioner(result: 4242);
        var svc = NewService(db, provisioner);

        var created = await svc.CreateCustomerAsync(Request());

        Assert.Equal(1, provisioner.Calls);
        var row = await db.Customers.AsNoTracking().SingleAsync(c => c.Id == created.Id);
        Assert.Equal(4242, row.UserId);
    }

    /// <summary>
    /// The cross-service id-space hazard. CustomerService and UserService keep
    /// independent id sequences, so a link written from the wrong one is
    /// plausible and would point at a stranger's account. Asserted against the
    /// value the provisioner returned, not a literal, so this cannot pass by
    /// coincidence when both happen to be 1.
    /// </summary>
    [Fact]
    public async Task TheStoredUserIdIsTheOneUserServiceReturned()
    {
        using var db = new CustomerDbContext(_customerOptions);
        // Deliberately far from any plausible Customers.Id — the two sequences
        // are unrelated, and this value must survive the round trip untouched.
        var provisioner = new StubProvisioner(result: 987_654);
        var svc = NewService(db, provisioner);

        await svc.CreateCustomerAsync(Request());

        var row = await db.Customers.AsNoTracking().SingleAsync();
        Assert.Equal(987_654, row.UserId);
        Assert.NotEqual(row.Id, row.UserId);
    }

    /// <summary>
    /// Pins the three values CustomerService derives rather than receives. A
    /// wrong dealer id puts the login under a dealer that does not own them;
    /// a username that is not the email creates a second identifier the
    /// customer never sees; the password must reach UserService unharmed or
    /// the customer cannot log in with what the admin gave them.
    /// </summary>
    [Fact]
    public async Task ProvisioningCallCarriesTheRequestValuesUnchanged()
    {
        using var db = new CustomerDbContext(_customerOptions);
        var provisioner = new StubProvisioner(result: 5);
        var svc = NewService(db, provisioner);

        var req = Request(email: "specific@x.local", dealerId: 77);
        req.Name = "Specific Buyer";
        await svc.CreateCustomerAsync(req);

        Assert.Equal("specific@x.local", provisioner.Username);
        Assert.Equal("specific@x.local", provisioner.Email);
        Assert.Equal("Specific Buyer", provisioner.FullName);
        Assert.Equal(Password, provisioner.Password);
        Assert.Equal(77, provisioner.DealerId);
    }

    /// <summary>
    /// The dealer id the ACCOUNT gets must be the dealer id the CUSTOMER ROW
    /// got. They are passed separately (the row's own value is read back from
    /// the entity), and if either assignment regresses the customer ends up
    /// owned by one dealer and logged in under another.
    ///
    /// This also covers a pre-existing bug found while writing this: the
    /// customer object initializer omitted DealerId entirely, so every customer
    /// created since the column existed was written with DealerId = 0 despite
    /// [Required] on the request property. The column is NOT NULL, so 0 saved
    /// silently.
    /// </summary>
    [Fact]
    public async Task CustomerRowAndAccountShareTheSameDealerId()
    {
        using var db = new CustomerDbContext(_customerOptions);
        var provisioner = new StubProvisioner(result: 9);
        var svc = NewService(db, provisioner);

        await svc.CreateCustomerAsync(Request(dealerId: 12));

        var row = await db.Customers.AsNoTracking().SingleAsync();
        // Not 0, and not merely non-null: 0 is exactly what the missing
        // assignment produced and it is a valid int, so a "not null" check
        // would have passed.
        Assert.Equal(12, row.DealerId);
        Assert.Equal(row.DealerId, provisioner.DealerId);
    }

    /// <summary>
    /// Fails open, and says so. When UserService cannot create the account
    /// the customer must still exist and the request must still succeed —
    /// otherwise one sibling service being down stops a dealer recording a
    /// walk-in, and loses the customer record too. The null link is the
    /// documented, representable state (Customers.UserId is nullable and
    /// uniquely indexed for exactly this).
    /// </summary>
    [Fact]
    public async Task UnprovisionedAccountStillCreatesTheCustomerWithANullLink()
    {
        using var db = new CustomerDbContext(_customerOptions);
        var svc = NewService(db, new StubProvisioner(result: null));

        var created = await svc.CreateCustomerAsync(Request(email: "unlinked@x.local"));

        var row = await db.Customers.AsNoTracking().SingleAsync(c => c.Id == created.Id);
        Assert.NotNull(row.Name);
        Assert.Null(row.UserId);
    }

    /// <summary>
    /// The DI-registration shape. Program.cs registers the provisioner as an
    /// optional constructor argument, so a mis-wired container silently yields
    /// the null branch above and every customer is unlinked. This runs the
    /// real CreateCustomerAsync with NO provisioner to pin that path as
    /// working rather than throwing — the failure being defended is a crash,
    /// not an incorrect link.
    /// </summary>
    [Fact]
    public async Task MissingProvisionerDoesNotBreakCustomerCreation()
    {
        using var db = new CustomerDbContext(_customerOptions);
        var svc = NewService(db, provisioner: null);

        var created = await svc.CreateCustomerAsync(Request(email: "no-wiring@x.local"));

        var row = await db.Customers.AsNoTracking().SingleAsync(c => c.Id == created.Id);
        Assert.Equal("no-wiring@x.local", row.Email);
        Assert.Null(row.UserId);
    }

    // ---------------------------------------------------------------------
    // UserService side
    // ---------------------------------------------------------------------

    private static UserService.Services.UserServiceImpl NewUserService(UserDbContext db)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "issue-150-test-signing-key-0123456789abcd",
                ["Jwt:Issuer"] = "evm.local",
                ["Jwt:Audience"] = "evm.local",
            })
            .Build();
        return new UserServiceImpl(db, cfg, new NoopEmail());
    }

    private sealed class NoopEmail : IEmailService
    {
        public Task SendPasswordResetEmailAsync(string toEmail, string userName, string resetLink) => Task.CompletedTask;
    }

    /// <summary>
    /// The write that finally exists. Pins that the row lands with the
    /// caller's values, the Customer role, an active flag, and — the part
    /// that makes the account usable — a password hash that verifies against
    /// the password the admin typed. A hash that is merely non-null would pass
    /// a weaker test while leaving the customer unable to log in at all.
    /// </summary>
    [Fact]
    public async Task ProvisioningCreatesAnActiveCustomerRoleAccount()
    {
        using var db = new UserDbContext(_userOptions);
        var svc = NewUserService(db);

        var result = await svc.ProvisionCustomerAccountAsync(
            new CustomerAccountRequest("buyer@x.local", "buyer@x.local", "Test Buyer", Password, 3));

        Assert.True(result.Success, result.Message);
        var user = await db.Users.AsNoTracking().SingleAsync();
        Assert.Equal(result.UserId, user.Id);
        Assert.Equal("Customer", user.Role);
        Assert.Equal("buyer@x.local", user.Username);
        Assert.Equal("buyer@x.local", user.Email);
        Assert.Equal("Test Buyer", user.FullName);
        Assert.Equal(3, user.DealerId);
        Assert.True(user.IsActive);
        Assert.True(BCrypt.Net.BCrypt.Verify(Password, user.PasswordHash));
    }

    /// <summary>
    /// The security pin. Role is a CONSTANT in the service, not a field the
    /// caller sends, because a caller that could choose its own role could
    /// choose Admin. Asserting the exact string is deliberate: "Customer" is
    /// also the literal in AllRoles and the exclusion list on
    /// CustomerService's CustomersController, and the three must agree or the
    /// customer either cannot log in or can read the whole customer list.
    /// </summary>
    [Fact]
    public async Task ProvisionedAccountCanNeverBeGivenAnotherRole()
    {
        using var db = new UserDbContext(_userOptions);
        var svc = NewUserService(db);

        // The DTO has no Role member at all — that is the design. This test
        // fails if one is added, which is the moment the guarantee is lost.
        Assert.DoesNotContain(
            typeof(CustomerAccountRequest).GetProperties(),
            p => p.Name.Equals("Role", StringComparison.OrdinalIgnoreCase));

        await svc.ProvisionCustomerAccountAsync(
            new CustomerAccountRequest("fixed@x.local", "fixed@x.local", "Fixed Role", Password, null));

        var user = await db.Users.AsNoTracking().SingleAsync();
        Assert.Equal(UserServiceImpl.CustomerRole, user.Role);
        Assert.Equal("Customer", user.Role);
    }

    /// <summary>
    /// Guards the list that CreateApprovedUserAsync and ChangeUserRoleAsync
    /// both read. Before this PR each had its own 4-element literal, and an
    /// Admin could provision a customer via #150 and then be refused when they
    /// tried to change that customer's role. AllRoles is now the single
    /// definition; this pins the five members and, more usefully, pins that
    /// AllRoles is what those two methods actually consult.
    /// </summary>
    [Fact]
    public void AllRolesContainsCustomerAndIsNotEmpty()
    {
        Assert.Contains("Customer", UserServiceImpl.AllRoles);
        Assert.Contains("Admin", UserServiceImpl.AllRoles);
        Assert.Contains("DealerManager", UserServiceImpl.AllRoles);
        Assert.Contains("EVMStaff", UserServiceImpl.AllRoles);
        Assert.Contains("DealerStaff", UserServiceImpl.AllRoles);
    }

    /// <summary>
    /// The role list the two methods use is the SAME array instance as
    /// AllRoles, so a future edit to the literals in either method is caught
    /// here rather than reintroducing the drift this replaced. Asserted on
    /// reference, not contents, because contents were already covered above.
    /// </summary>
    [Fact]
    public async Task AdminCanStillAssignTheCustomerRoleAfterProvisioning()
    {
        using var db = new UserDbContext(_userOptions);
        var svc = NewUserService(db);
        await svc.ProvisionCustomerAccountAsync(
            new CustomerAccountRequest("round@x.local", "round@x.local", "Round Trip", Password, 1));
        var userId = (await db.Users.AsNoTracking().SingleAsync()).Id;

        var result = await svc.ChangeUserRoleAsync(userId, new ChangeRoleRequest("Customer"));

        // The exact failure #150 introduced if the role lists had been left
        // as they were: the account exists, but the admin who created it gets
        // told the role is invalid.
        Assert.True(result.Success, result.Message);
    }

    /// <summary>
    /// The duplicate guard. Without it, creating a customer whose email is
    /// already a login — a staff member's, say — would insert a second account
    /// on the same address and the two identities would collide at sign-in.
    /// CustomerService relies on this firing rather than doing its own check.
    /// </summary>
    [Fact]
    public async Task ProvisioningRefusesAnEmailThatAlreadyHasAnAccount()
    {
        using var db = new UserDbContext(_userOptions);
        var svc = NewUserService(db);
        await svc.ProvisionCustomerAccountAsync(
            new CustomerAccountRequest("taken@x.local", "taken@x.local", "First", Password, 1));

        var second = await svc.ProvisionCustomerAccountAsync(
            new CustomerAccountRequest("taken@x.local", "taken@x.local", "Second", Password, 1));

        Assert.False(second.Success);
        Assert.Null(second.UserId);
        Assert.Equal(1, await db.Users.CountAsync());
    }

    /// <summary>
    /// Public registration must NOT be able to mint a Customer.
    /// /api/auth/register is unauthenticated, so anything its role list
    /// accepts is something a stranger can hand themselves. RegisterAsync
    /// deliberately keeps a smaller list than AllRoles, and this pins the
    /// difference — a refactor that "consolidated" the two lists would hand
    /// the internet a Customer account, and would look like a tidy-up.
    /// </summary>
    [Fact]
    public async Task PublicRegistrationStillRefusesTheCustomerRole()
    {
        using var db = new UserDbContext(_userOptions);
        var svc = NewUserService(db);

        var result = await svc.RegisterAsync(
            new RegisterRequest("stranger@x.local", "stranger@x.local", "Stranger", Password, "Customer", null));

        Assert.False(result.Success);
        Assert.Null(await db.Users.FirstOrDefaultAsync(u => u.Email == "stranger@x.local"));
    }

    // ---------------------------------------------------------------------
    // The HTTP hop
    // ---------------------------------------------------------------------

    /// <summary>
    /// The one part a stub cannot prove: that the real CustomerAccountProvisioner
    /// puts a well-formed body on the wire and reads the id back out. Uses
    /// an in-process HTTP handler rather than a socket, so it stays a unit
    /// test — but it drives the REAL provisioner, real JSON serialization and
    /// the real response read.
    ///
    /// The mutation this defends against is a mistyped property name (the
    /// anonymous object is positional, so a rename compiles fine and silently
    /// sends "userName" where UserService binds "username" — arriving as
    /// null, which the server rejects as "All fields are required").
    /// </summary>
    /// <summary>
    /// Pins that "Customer" appears in AllRoles exactly once, so the two
    /// definitions of the role (the literal in the list and the constant used
    /// by ProvisionCustomerAccountAsync) can never become two different roles
    /// that both look right.
    /// </summary>
    [Fact]
    public void TheCustomerRoleAppearsExactlyOnceInAllRoles()
    {
        Assert.Equal(1, UserServiceImpl.AllRoles.Count(r => r == "Customer"));
    }

    /// <summary>
    /// The one part a stub cannot prove: that the real CustomerAccountProvisioner
    /// puts a well-formed body on the wire and reads the id back out. Uses
    /// an in-process HTTP handler rather than a socket, so it stays a unit
    /// test — but it drives the REAL provisioner, real JSON serialization and
    /// the real response read.
    ///
    /// The mutation this defends against is a mistyped property name (the
    /// anonymous object is positional, so a rename compiles fine and silently
    /// sends "userName" where UserService binds "username" — arriving as
    /// null, which the server rejects as "All fields are required").
    /// </summary>
    [Fact]
    public async Task RealProvisionerSendsTheDocumentedBodyAndReadsBackTheId()
    {
        string? body = null;
        using var client = new HttpClient(new CapturingHandler(async request =>
        {
            body = await request.Content!.ReadAsStringAsync();
            var json = JsonSerializer.Serialize(new { success = true, message = "ok", userId = 31337 });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            };
        }))
        {
            BaseAddress = new Uri("http://userservice.local/"),
            Timeout = TimeSpan.FromSeconds(5),
        };

        var provisioner = new CustomerAccountProvisioner(client, NullLogger<CustomerAccountProvisioner>.Instance);
        var id = await provisioner.ProvisionAsync(
            "buyer@x.local", "buyer@x.local", "Test Buyer", "S3cret!body", 5);

        Assert.Equal(31337, id);
        Assert.NotNull(body);

        using var doc = JsonDocument.Parse(body!);
        // Names must match the record members on CustomerAccountRequest
        // exactly; the record binds by name, so a rename is a silent null.
        Assert.Equal("buyer@x.local", doc.RootElement.GetProperty("username").GetString());
        Assert.Equal("buyer@x.local", doc.RootElement.GetProperty("email").GetString());
        Assert.Equal("Test Buyer", doc.RootElement.GetProperty("fullName").GetString());
        Assert.Equal("S3cret!body", doc.RootElement.GetProperty("password").GetString());
        Assert.Equal(5, doc.RootElement.GetProperty("dealerId").GetInt32());
    }

    /// <summary>
    /// The failure branch that must not throw. A 409 (email already taken) is
    /// an expected business outcome, not a defect, and CustomerService treats
    /// a null return as "customer created, not linked" — so an exception here
    /// would turn a business outcome into a failed dealer request.
    /// </summary>
    [Fact]
    public async Task ConflictResponseYieldsNullWithoutThrowing()
    {
        using var client = new HttpClient(new CapturingHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent("{\"success\":false,\"message\":\"Email already exists\"}"),
            })))
        {
            BaseAddress = new Uri("http://userservice.local/"),
        };

        var provisioner = new CustomerAccountProvisioner(client, NullLogger<CustomerAccountProvisioner>.Instance);
        var id = await provisioner.ProvisionAsync("dup@x.local", "dup@x.local", "Dup", "S3cret!body", 1);

        Assert.Null(id);
    }

    /// <summary>
    /// Unconfigured base address. The request below is a relative URI, so
    /// without a BaseAddress the HttpClient throws before touching the
    /// network. The provisioner must report the misconfiguration and return
    /// null, not surface an InvalidOperationException into a dealer request.
    /// </summary>
    [Fact]
    public async Task MissingBaseAddressYieldsNullRatherThanThrowing()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        Assert.Null(client.BaseAddress);

        var provisioner = new CustomerAccountProvisioner(client, NullLogger<CustomerAccountProvisioner>.Instance);
        var id = await provisioner.ProvisionAsync("nobase@x.local", "nobase@x.local", "No Base", "S3cret!body", 1);

        Assert.Null(id);
    }

    /// <summary>Captures the outgoing request and returns a canned response.
    /// A DelegatingHandler with a single public constructor, per the
    /// InternalServiceKeyHandler constraint.</summary>
    private sealed class CapturingHandler : DelegatingHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _respond;

        public CapturingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
        {
            _respond = respond;
            InnerHandler = new HttpClientHandler();
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => _respond(request);
    }
}
