using AutoMapper;
using CustomerService.Data;
using CustomerService.DTOs;
using CustomerService.Events;
using CustomerService.Models;
using Microsoft.EntityFrameworkCore;

namespace CustomerService.Services
{
    public class TestDriveService : ITestDriveService
    {
        private readonly CustomerDbContext _context;
        private readonly IMapper _mapper;
        private readonly IMessageProducer _messageProducer;

        public TestDriveService(CustomerDbContext context, IMapper mapper, IMessageProducer messageProducer)
        {
            _context = context;
            _mapper = mapper;
            _messageProducer = messageProducer;
        }

        public async Task<IEnumerable<TestDriveDto>> GetAllTestDrivesAsync()
        {
            var testDrives = await _context.TestDrives
                                           .Include(td => td.Customer) // Include customer info
                                           .OrderByDescending(td => td.AppointmentDate)
                                           .ToListAsync();
            return _mapper.Map<IEnumerable<TestDriveDto>>(testDrives);
        }

        public async Task<TestDriveDto> CreateTestDriveAsync(CreateTestDriveRequest request)
        {
            var testDrive = _mapper.Map<TestDrive>(request);
            testDrive.CreatedAt = DateTime.UtcNow;

            _context.TestDrives.Add(testDrive);
            await _context.SaveChangesAsync();

            var customer = await _context.Customers.FindAsync(testDrive.CustomerId);
            var testDriveDto = _mapper.Map<TestDriveDto>(testDrive);
            testDriveDto.CustomerName = customer?.Name;

            // Notify (NotificationService consumes testdrive.scheduled on the
            // vehicle_events exchange). Vehicle model is not resolvable locally;
            // see docs/EVENTS.md for the cross-service lookup follow-up.
            _messageProducer.PublishMessage(new TestDriveScheduledEvent
            {
                TestDriveId = testDrive.Id,
                CustomerId = testDrive.CustomerId,
                VehicleId = testDrive.VehicleId,
                DealerId = testDrive.DealerId,
                CustomerEmail = customer?.Email ?? string.Empty,
                CustomerName = customer?.Name ?? string.Empty,
                VehicleModel = string.Empty,
                ScheduledDate = testDrive.AppointmentDate,
                DeviceToken = null
            }, EventNames.TestDriveScheduled);

            return testDriveDto;
        }

        public async Task<TestDriveDto?> GetTestDriveByIdAsync(int id)
        {
            var testDrive = await _context.TestDrives
                                          .Include(td => td.Customer)
                                          .FirstOrDefaultAsync(td => td.Id == id);
            return _mapper.Map<TestDriveDto>(testDrive);
        }

        public async Task<IEnumerable<TestDriveDto>> GetTestDrivesByCustomerIdAsync(int customerId)
        {
            var testDrives = await _context.TestDrives
                                           .Where(td => td.CustomerId == customerId)
                                           .Include(td => td.Customer)
                                           .ToListAsync();
            return _mapper.Map<IEnumerable<TestDriveDto>>(testDrives);
        }

        public async Task<TestDriveDto?> UpdateTestDriveAsync(int id, UpdateTestDriveRequest request)
        {
            var testDrive = await _context.TestDrives.FindAsync(id);
            if (testDrive == null)
            {
                return null;
            }

            _mapper.Map(request, testDrive);
            await _context.SaveChangesAsync();

            var customer = await _context.Customers.FindAsync(testDrive.CustomerId);
            var testDriveDto = _mapper.Map<TestDriveDto>(testDrive);
            testDriveDto.CustomerName = customer?.Name;

            return testDriveDto;
        }

        public async Task<bool> DeleteTestDriveAsync(int id)
        {
            var testDrive = await _context.TestDrives.FindAsync(id);
            if (testDrive == null)
            {
                return false;
            }

            _context.TestDrives.Remove(testDrive);
            await _context.SaveChangesAsync();
            return true;
        }
    }
}
