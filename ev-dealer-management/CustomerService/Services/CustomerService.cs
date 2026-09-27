using CustomerService.Data;
using CustomerService.DTOs;
using CustomerService.Events;
using CustomerService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration; // Needed for IConfiguration if we inject it
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CustomerService.Services;

public class CustomerService : ICustomerService
{
    private readonly CustomerDbContext _context;
    private readonly IMessageProducer _messageProducer;
    // Issue #150: nullable so the many tests that construct this service with
    // just a context and a producer keep working. A null provisioner means
    // "the account cannot be created here", which is exactly the state those
    // callers are already in — unlinked customers — so nothing about their
    // existing assertions changes.
    private readonly ICustomerAccountProvisioner? _accountProvisioner;

    public CustomerService(
        CustomerDbContext context,
        IMessageProducer messageProducer,
        ICustomerAccountProvisioner? accountProvisioner = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _messageProducer = messageProducer ?? throw new ArgumentNullException(nameof(messageProducer));
        _accountProvisioner = accountProvisioner;
    }

    public async Task<IEnumerable<CustomerDto>> GetAllCustomersAsync()
    {
        var customers = await _context.Customers
            .Include(c => c.Purchases) // Eagerly load purchases
            .AsNoTracking()
            .ToListAsync();

        // Map entities to DTOs
        return customers.Select(c => new CustomerDto
        {
            Id = c.Id,
            Name = c.Name,
            Email = c.Email,
            Phone = c.Phone,
            Address = c.Address,
            Status = c.Status,
            JoinDate = c.JoinDate,
            Purchases = c.Purchases.Select(p => new PurchaseDto
            {
                Id = p.Id,
                CustomerId = p.CustomerId,
                Vehicle = p.Vehicle,
                Amount = p.Amount,
                PurchaseDate = p.PurchaseDate
            }).ToList(),
            TestDrives = new List<TestDriveDto>() // No need to load test drives for the list view
        }).ToList();
    }

    public async Task<CustomerDto?> GetCustomerByIdAsync(int id)
    {
        var customer = await _context.Customers
            .Include(c => c.Purchases)
            .Include(c => c.TestDrives)
            .AsNoTracking() // Use AsNoTracking for read-only queries to improve performance
            .FirstOrDefaultAsync(c => c.Id == id);

        if (customer == null)
        {
            return null;
        }

        // Manual mapping from the entity to the DTO
        return new CustomerDto
        {
            Id = customer.Id,
            Name = customer.Name,
            Email = customer.Email,
            Phone = customer.Phone,
            Address = customer.Address,
            Status = customer.Status,
            JoinDate = customer.JoinDate,
            Purchases = customer.Purchases.Select(p => new PurchaseDto
            {
                Id = p.Id,
                CustomerId = p.CustomerId,
                Vehicle = p.Vehicle,
                Amount = p.Amount,
                PurchaseDate = p.PurchaseDate
            }).ToList(),
            TestDrives = customer.TestDrives.Select(td => new TestDriveDto
            {
                Id = td.Id,
                CustomerId = td.CustomerId,
                VehicleId = td.VehicleId,
                DealerId = td.DealerId,
                AppointmentDate = td.AppointmentDate,
                Status = td.Status,
                Notes = td.Notes,
                CreatedAt = td.CreatedAt
            }).ToList()
        };
    }

    public async Task<CustomerDto> CreateCustomerAsync(CreateCustomerRequest request)
    {
        // Basic validation: Check if email already exists
        if (await _context.Customers.AnyAsync(c => c.Email == request.Email))
        {
            throw new InvalidOperationException($"Customer with email '{request.Email}' already exists.");
        }

        var customer = new Customer
        {
            Name = request.Name,
            Email = request.Email,
            Phone = request.Phone,
            Address = request.Address,
            // Issue #150: this was missing. The request has always carried a
            // [Required] DealerId and the column is NOT NULL, so the row was
            // being written with 0 — silently unassigned to any dealer. It
            // became visible only because the login account is provisioned
            // with the same dealer id, which would have created a customer
            // account scoped to dealer 0. Every other reader of DealerId
            // (test drives, complaints, the reservation path) has been reading
            // that 0.
            DealerId = request.DealerId,
            Status = request.Status ?? "Active", // Default to Active if not provided
            JoinDate = DateTime.UtcNow
            // Add other properties from request if they exist and are needed
        };

        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        // Issue #150: create the login account and link it. Deliberately AFTER
        // the customer row exists, so the event published below carries a
        // CustomerId that is already durable, and deliberately NOT fatal — see
        // CustomerAccountProvisioner for why failing open is the right default
        // when a sibling service is down.
        await LinkAccountAsync(customer, request);

        _messageProducer.PublishMessage(new CustomerCreatedEvent
        {
            CustomerId = customer.Id,
            Name = customer.Name,
            Email = customer.Email,
            Timestamp = DateTime.UtcNow
        }, EventNames.CustomerCreated);

        // Return the created customer as a DTO
        return new CustomerDto
        {
            Id = customer.Id,
            Name = customer.Name,
            Email = customer.Email,
            Phone = customer.Phone,
            Address = customer.Address,
            Status = customer.Status,
            JoinDate = customer.JoinDate
        };
    }

    /// <summary>
    /// Creates the customer's login account and stores its id on the row
    /// (issue #150). Leaves <c>UserId</c> null when that is not possible.
    /// </summary>
    /// <remarks>
    /// WHY USERNAME = EMAIL. The customer form has no username field, and
    /// Users requires one. Reusing the email also means UserService's
    /// "Email already exists" check fires on a staff/customer collision, so an
    /// email that already belongs to a staff account cannot silently become a
    /// customer login. The side effect is that a customer and a staff member
    /// can never share an email — which is correct, they are different people.
    /// </remarks>
    private async Task LinkAccountAsync(Customer customer, CreateCustomerRequest request)
    {
        if (_accountProvisioner is null)
        {
            // Only reachable from tests and any caller that constructs this
            // service directly. In production the provisioner is always
            // registered; log anyway so a wiring mistake is visible.
            return;
        }

        var userId = await _accountProvisioner.ProvisionAsync(
            username: request.Email,
            email: request.Email,
            fullName: request.Name,
            password: request.Password,
            dealerId: customer.DealerId);

        if (userId is null)
        {
            // Already logged in detail by the provisioner. Nothing to do here
            // beyond leaving the column null, which is the documented state.
            return;
        }

        customer.UserId = userId.Value;
        await _context.SaveChangesAsync();
    }

    public async Task<CustomerDto?> UpdateCustomerAsync(int id, UpdateCustomerRequest request)
    {
        var customer = await _context.Customers.FindAsync(id);
        if (customer == null)
        {
            return null; // Customer not found
        }

        // Update properties if they are provided in the request
        if (request.Name != null) customer.Name = request.Name;
        if (request.Email != null)
        {
            // Check if the new email is already used by another customer
            if (await _context.Customers.AnyAsync(c => c.Id != id && c.Email == request.Email))
            {
                throw new InvalidOperationException($"Email '{request.Email}' is already in use by another customer.");
            }
            customer.Email = request.Email;
        }
        if (request.Phone != null) customer.Phone = request.Phone;
        if (request.Address != null) customer.Address = request.Address;
        if (request.Status != null) customer.Status = request.Status;

        // Update last modified timestamp (assuming a property like UpdatedAt exists or log it)
        customer.UpdatedAt = DateTime.UtcNow; // Update the UpdatedAt timestamp

        await _context.SaveChangesAsync();

        _messageProducer.PublishMessage(new CustomerUpdatedEvent
        {
            CustomerId = customer.Id,
            Name = customer.Name,
            Email = customer.Email,
            Phone = customer.Phone,
            Address = customer.Address,
            Status = customer.Status,
            Timestamp = DateTime.UtcNow
        }, EventNames.CustomerUpdated);

        // Return the updated customer as a DTO
        return new CustomerDto
        {
            Id = customer.Id,
            Name = customer.Name,
            Email = customer.Email,
            Phone = customer.Phone,
            Address = customer.Address,
            Status = customer.Status,
            JoinDate = customer.JoinDate
        };
    }

    public async Task<bool> DeleteCustomerAsync(int id)
    {
        var customer = await _context.Customers.FindAsync(id);
        if (customer == null)
        {
            return false; // Customer not found
        }

        // Implement logic to handle related data (e.g., purchases, test drives, complaints)
        // For now, we'll just delete the customer. Consider soft delete if needed.
        _context.Customers.Remove(customer);
        await _context.SaveChangesAsync();
        _messageProducer.PublishMessage(new CustomerDeletedEvent
        {
            CustomerId = id,
            Timestamp = DateTime.UtcNow
        }, EventNames.CustomerDeleted);
        return true;
    }

    // --- Test Drive Booking Methods ---
    public async Task<IEnumerable<TestDriveDto>> GetTestDrivesByCustomerIdAsync(int customerId)
    {
        // Ensure customer exists
        if (!await _context.Customers.AnyAsync(c => c.Id == customerId))
        {
            throw new KeyNotFoundException($"Customer with ID {customerId} not found.");
        }

        var testDrives = await _context.TestDrives
            .Where(td => td.CustomerId == customerId)
            .Select(td => new TestDriveDto
            {
                Id = td.Id,
                CustomerId = td.CustomerId,
                VehicleId = td.VehicleId,
                DealerId = td.DealerId,
                AppointmentDate = td.AppointmentDate,
                Status = td.Status,
                Notes = td.Notes,
                CreatedAt = td.CreatedAt
            })
            .ToListAsync();
        return testDrives;
    }

    public async Task<TestDriveDto> CreateTestDriveAsync(int customerId, CreateTestDriveRequest request)
    {
        if (!await _context.Customers.AnyAsync(c => c.Id == customerId))
        {
            throw new KeyNotFoundException($"Customer with ID {customerId} not found.");
        }

        var testDrive = new TestDrive
        {
            CustomerId = customerId,
            VehicleId = request.VehicleId,
            DealerId = request.DealerId,
            AppointmentDate = request.AppointmentDate,
            Status = "Scheduled",
            Notes = request.Notes,
            CreatedAt = DateTime.UtcNow
        };

        _context.TestDrives.Add(testDrive);
        await _context.SaveChangesAsync();

        return new TestDriveDto
        {
            Id = testDrive.Id,
            CustomerId = testDrive.CustomerId,
            VehicleId = testDrive.VehicleId,
            DealerId = testDrive.DealerId,
            AppointmentDate = testDrive.AppointmentDate,
            Status = testDrive.Status,
            Notes = testDrive.Notes,
            CreatedAt = testDrive.CreatedAt
        };
    }

    public async Task<TestDriveDto?> UpdateTestDriveAsync(int id, UpdateTestDriveRequest request)
    {
        var testDrive = await _context.TestDrives.FindAsync(id);
        if (testDrive == null)
        {
            return null; // Test drive not found
        }

        if (request.AppointmentDate.HasValue) testDrive.AppointmentDate = request.AppointmentDate.Value;
        if (request.Status != null) testDrive.Status = request.Status;
        if (request.Notes != null) testDrive.Notes = request.Notes;

        await _context.SaveChangesAsync();

        // Return the updated test drive as a DTO
        return new TestDriveDto
        {
            Id = testDrive.Id,
            CustomerId = testDrive.CustomerId,
            VehicleId = testDrive.VehicleId,
            DealerId = testDrive.DealerId,
            AppointmentDate = testDrive.AppointmentDate,
            Status = testDrive.Status,
            Notes = testDrive.Notes,
            CreatedAt = testDrive.CreatedAt // Note: CreatedAt might not change, depends on requirements
        };
    }

    public async Task<bool> CancelTestDriveAsync(int id)
    {
        var testDrive = await _context.TestDrives.FindAsync(id);
        if (testDrive == null)
        {
            return false; // Test drive not found
        }

        // Implement more robust cancellation logic if needed (e.g., check if already completed)
        testDrive.Status = "Cancelled";
        await _context.SaveChangesAsync();
        return true;
    }

    // --- Complaint Methods ---
    public async Task<IEnumerable<ComplaintDto>> GetComplaintsByCustomerIdAsync(int customerId)
    {
        // Ensure customer exists
        if (!await _context.Customers.AnyAsync(c => c.Id == customerId))
        {
            throw new KeyNotFoundException($"Customer with ID {customerId} not found.");
        }

        var complaints = await _context.Complaints
            .Where(c => c.CustomerId == customerId)
            .Select(c => new ComplaintDto // Assuming ComplaintDto exists
            {
                Id = c.Id,
                CustomerId = c.CustomerId,
                Title = c.Title,
                Description = c.Description,
                Status = c.Status,
                CreatedAt = c.CreatedAt,
                ResolvedAt = c.ResolvedAt,
                Resolution = c.Resolution
            })
            .ToListAsync();
        return complaints;
    }

    public async Task<ComplaintDto> CreateComplaintAsync(int customerId, CreateComplaintRequest request)
    {
        // Ensure customer exists
        if (!await _context.Customers.AnyAsync(c => c.Id == customerId))
        {
            throw new KeyNotFoundException($"Customer with ID {customerId} not found.");
        }

        var complaint = new Complaint
        {
            CustomerId = customerId,
            Title = request.Title,
            Description = request.Description,
            Status = "Open", // Default status
            CreatedAt = DateTime.UtcNow
        };

        _context.Complaints.Add(complaint);
        await _context.SaveChangesAsync();

        return new ComplaintDto
        {
            Id = complaint.Id,
            CustomerId = complaint.CustomerId,
            Title = complaint.Title,
            Description = complaint.Description,
            Status = complaint.Status,
            CreatedAt = complaint.CreatedAt,
            ResolvedAt = complaint.ResolvedAt,
            Resolution = complaint.Resolution
        };
    }

    public async Task<ComplaintDto?> UpdateComplaintAsync(int id, UpdateComplaintRequest request)
    {
        var complaint = await _context.Complaints.FindAsync(id);
        if (complaint == null)
        {
            return null;
        }

        if (request.Title != null) complaint.Title = request.Title;
        if (request.Description != null) complaint.Description = request.Description;
        if (request.Status != null) complaint.Status = request.Status;
        if (request.Resolution != null) complaint.Resolution = request.Resolution;



        await _context.SaveChangesAsync();

        return new ComplaintDto
        {
            Id = complaint.Id,
            CustomerId = complaint.CustomerId,
            Title = complaint.Title,
            Description = complaint.Description,
            Status = complaint.Status,
            CreatedAt = complaint.CreatedAt,
            ResolvedAt = complaint.ResolvedAt,
            Resolution = complaint.Resolution
        };
    }

    public async Task<bool> ResolveComplaintAsync(int id)
    {
        var complaint = await _context.Complaints.FindAsync(id);
        if (complaint == null)
        {
            return false;
        }

        if (complaint.Status == "Resolved" || complaint.Status == "Closed")
        {
            return false;
        }

        complaint.Status = "Resolved";
        complaint.ResolvedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<CustomerDto?> GetCustomerByEmailAsync(string email)
    {
        var customer = await _context.Customers
            .Include(c => c.Purchases)
            .Include(c => c.TestDrives)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Email == email);

        if (customer == null)
        {
            return null;
        }

        return new CustomerDto
        {
            Id = customer.Id,
            Name = customer.Name,
            Email = customer.Email,
            Phone = customer.Phone,
            Address = customer.Address,
            Status = customer.Status,
            JoinDate = customer.JoinDate,
            Purchases = customer.Purchases.Select(p => new PurchaseDto
            {
                Id = p.Id,
                CustomerId = p.CustomerId,
                Vehicle = p.Vehicle,
                Amount = p.Amount,
                PurchaseDate = p.PurchaseDate
            }).ToList(),
            TestDrives = customer.TestDrives.Select(t => new TestDriveDto
            {
                Id = t.Id,
                CustomerId = t.CustomerId,
                VehicleId = t.VehicleId,
                DealerId = t.DealerId,
                AppointmentDate = t.AppointmentDate,
                Status = t.Status,
                Notes = t.Notes,
                CreatedAt = t.CreatedAt
            }).ToList()
        };
    }

    public async Task<CustomerDto> CreateOrUpdateCustomerFromReservationAsync(VehicleReservedEvent reservationEvent)
    {
        // Tìm customer có sẵn theo email
        var existingCustomer = await _context.Customers
            .FirstOrDefaultAsync(c => c.Email == reservationEvent.CustomerEmail);

        if (existingCustomer != null)
        {
            // Cập nhật thông tin nếu cần
            if (string.IsNullOrEmpty(existingCustomer.Phone) && !string.IsNullOrEmpty(reservationEvent.CustomerPhone))
            {
                existingCustomer.Phone = reservationEvent.CustomerPhone;
            }
            
            // Cập nhật tên nếu khác biệt đáng kể (có thể customer đã cập nhật tên)
            if (!existingCustomer.Name.Equals(reservationEvent.CustomerName, StringComparison.OrdinalIgnoreCase))
            {
                existingCustomer.Name = reservationEvent.CustomerName;
            }

            existingCustomer.UpdatedAt = DateTime.UtcNow;
            
            // Thêm purchase record mới
            var purchase = new Purchase
            {
                CustomerId = existingCustomer.Id,
                Vehicle = $"{reservationEvent.VehicleName} (x{reservationEvent.Quantity})",
                Amount = reservationEvent.VehiclePrice * reservationEvent.Quantity,
                PurchaseDate = reservationEvent.ReservedAt
            };
            
            existingCustomer.Purchases.Add(purchase);
            await _context.SaveChangesAsync();

            return new CustomerDto
            {
                Id = existingCustomer.Id,
                Name = existingCustomer.Name,
                Email = existingCustomer.Email,
                Phone = existingCustomer.Phone,
                Address = existingCustomer.Address,
                Status = existingCustomer.Status,
                JoinDate = existingCustomer.JoinDate,
                Purchases = existingCustomer.Purchases.Select(p => new PurchaseDto
                {
                    Id = p.Id,
                    CustomerId = p.CustomerId,
                    Vehicle = p.Vehicle,
                    Amount = p.Amount,
                    PurchaseDate = p.PurchaseDate
                }).ToList(),
                TestDrives = new List<TestDriveDto>()
            };
        }
        else
        {
            // Tạo customer mới
            var newCustomer = new Customer
            {
                Name = reservationEvent.CustomerName,
                Email = reservationEvent.CustomerEmail,
                Phone = reservationEvent.CustomerPhone,
                DealerId = reservationEvent.DealerId,
                Status = "active",
                JoinDate = reservationEvent.ReservedAt,
                UpdatedAt = reservationEvent.ReservedAt
            };

            // Thêm purchase record đầu tiên
            var purchase = new Purchase
            {
                Vehicle = $"{reservationEvent.VehicleName} (x{reservationEvent.Quantity})",
                Amount = reservationEvent.VehiclePrice * reservationEvent.Quantity,
                PurchaseDate = reservationEvent.ReservedAt
            };

            newCustomer.Purchases.Add(purchase);

            _context.Customers.Add(newCustomer);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Email has a unique index: a concurrent reservation for the same
                // new customer can win the insert race. Retry once through the
                // update path instead of failing (which would nack-requeue the event).
                _context.ChangeTracker.Clear();
                var racedCustomer = await _context.Customers
                    .FirstOrDefaultAsync(c => c.Email == reservationEvent.CustomerEmail);
                if (racedCustomer == null)
                {
                    throw; // not a conflict we can recover from
                }

                return await CreateOrUpdateCustomerFromReservationAsync(reservationEvent);
            }

            return new CustomerDto
            {
                Id = newCustomer.Id,
                Name = newCustomer.Name,
                Email = newCustomer.Email,
                Phone = newCustomer.Phone,
                Address = newCustomer.Address,
                Status = newCustomer.Status,
                JoinDate = newCustomer.JoinDate,
                Purchases = new List<PurchaseDto>
                {
                    new PurchaseDto
                    {
                        Id = purchase.Id,
                        CustomerId = newCustomer.Id,
                        Vehicle = purchase.Vehicle,
                        Amount = purchase.Amount,
                        PurchaseDate = purchase.PurchaseDate
                    }
                },
                TestDrives = new List<TestDriveDto>()
            };
        }
    }
}
