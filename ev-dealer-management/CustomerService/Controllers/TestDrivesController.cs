using Microsoft.AspNetCore.Mvc;
using CustomerService.DTOs;
using CustomerService.Services;
using Microsoft.AspNetCore.Authorization;

namespace CustomerService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // Issue #137: test-drive slots are dealer inventory (which car is booked
    // when, and by whom). Anonymous read/write here leaks the booking sheet.
    [Authorize]
    public class TestDrivesController : ControllerBase
    {
        private readonly ITestDriveService _testDriveService;

        public TestDrivesController(ITestDriveService testDriveService)
        {
            _testDriveService = testDriveService;
        }

        // GET: api/TestDrives
        [HttpGet]
        public async Task<ActionResult<IEnumerable<TestDriveDto>>> GetTestDrives()
        {
            var testDrives = await _testDriveService.GetAllTestDrivesAsync();
            return Ok(testDrives);
        }

        // GET: api/TestDrives/customer/{customerId}
        [HttpGet("customer/{customerId}")]
        public async Task<ActionResult<IEnumerable<TestDriveDto>>> GetTestDrivesByCustomerId(int customerId)
        {
            var testDrives = await _testDriveService.GetTestDrivesByCustomerIdAsync(customerId);
            return Ok(testDrives);
        }


        // GET: api/TestDrives/5
        [HttpGet("{id}")]
        public async Task<ActionResult<TestDriveDto>> GetTestDrive(int id)
        {
            var testDrive = await _testDriveService.GetTestDriveByIdAsync(id);

            if (testDrive == null)
            {
                return NotFound();
            }

            return Ok(testDrive);
        }

        // POST: api/TestDrives
        [HttpPost]
        public async Task<ActionResult<TestDriveDto>> PostTestDrive(CreateTestDriveRequest createTestDriveRequest)
        {
            var testDriveDto = await _testDriveService.CreateTestDriveAsync(createTestDriveRequest);
            if (testDriveDto == null)
            {
                return BadRequest("Could not create test drive. Customer might not exist or other issue.");
            }

            return CreatedAtAction(nameof(GetTestDrive), new { id = testDriveDto.Id }, testDriveDto);
        }

        // PUT: api/TestDrives/5
        [HttpPut("{id}")]
        public async Task<IActionResult> PutTestDrive(int id, UpdateTestDriveRequest updateTestDriveRequest)
        {
            var updatedTestDrive = await _testDriveService.UpdateTestDriveAsync(id, updateTestDriveRequest);

            if (updatedTestDrive == null)
            {
                return NotFound();
            }

            return NoContent();
        }

        // DELETE: api/TestDrives/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTestDrive(int id)
        {
            var result = await _testDriveService.DeleteTestDriveAsync(id);

            if (!result)
            {
                return NotFound();
            }

            return NoContent();
        }
    }
}
