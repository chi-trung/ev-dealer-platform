using Microsoft.AspNetCore.Mvc;
using CustomerService.Services;
using CustomerService.DTOs;
using CustomerService.Models; // Required for the event model if it's not in DTOs

using Microsoft.AspNetCore.Authorization;
namespace CustomerService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // Issue #137: every customer route is staff-only data. Register goes
    // through UserService's /api/auth/register -- this controller's reads and
    // writes are the dealer CRM surface, not the public signup path.
    [Authorize]
    public class CustomersController : ControllerBase
    {
        private readonly ICustomerService _customerService;
        private readonly ILogger<CustomersController> _logger;

        public CustomersController(ICustomerService customerService, ILogger<CustomersController> logger)
        {
            _customerService = customerService;
            _logger = logger;
        }

        // GET: api/Customers
        [HttpGet]
        public async Task<ActionResult<IEnumerable<CustomerDto>>> GetCustomers()
        {
            var customers = await _customerService.GetAllCustomersAsync();
            return Ok(customers);
        }

        // GET: api/Customers/5
        [HttpGet("{id}")]
        public async Task<ActionResult<CustomerDto>> GetCustomer(int id)
        {
            var customer = await _customerService.GetCustomerByIdAsync(id);

            if (customer == null)
            {
                return NotFound();
            }

            return Ok(customer);
        }

        // POST: api/Customers
        [HttpPost]
        public async Task<ActionResult<CustomerDto>> PostCustomer(CreateCustomerRequest createCustomerRequest)
        {
            try
            {
                // CustomerCreatedEvent is published inside CreateCustomerAsync
                // (CustomerService) on the customer_events exchange.
                var createdCustomer = await _customerService.CreateCustomerAsync(createCustomerRequest);

                return Ok(createdCustomer);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Customer creation failed: {Message}", ex.Message);
                return Conflict(new { message = ex.Message }); // e.g., email already exists
            }
            catch (Exception ex)
            {
                // This will now only catch errors from the customer creation itself
                _logger.LogError(ex, "Error creating customer: {CustomerName}", createCustomerRequest.Name);
                return StatusCode(500, "Internal server error");
            }
        }

        // PUT: api/Customers/5
        [HttpPut("{id}")]
        public async Task<IActionResult> PutCustomer(int id, UpdateCustomerRequest updateCustomerRequest)
        {
            try
            {
                var updatedCustomer = await _customerService.UpdateCustomerAsync(id, updateCustomerRequest);
                if (updatedCustomer == null)
                {
                    return NotFound();
                }
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating customer with ID: {CustomerId}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        // DELETE: api/Customers/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCustomer(int id)
        {
            var result = await _customerService.DeleteCustomerAsync(id);
            if (!result)
            {
                return NotFound();
            }

            return NoContent();
        }
    }
}
