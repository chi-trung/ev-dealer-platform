using Microsoft.EntityFrameworkCore;
using VehicleService.Data;
using VehicleService.DTOs;
using VehicleService.Models;
using System.IO;
using System.Net.Http.Headers;

namespace VehicleService.Services;

public class VehicleService : IVehicleService
{
    private readonly ApplicationDbContext _context;
    private readonly IMessageProducer _messageProducer;

    public VehicleService(ApplicationDbContext context, IMessageProducer messageProducer)
    {
        _context = context;
        _messageProducer = messageProducer;
    }

    public async Task<PaginatedResult<VehicleDto>> GetVehiclesAsync(VehicleQueryDto query)
    {
        var queryable = _context.Vehicles
            .Include(v => v.Dealer)
            .Include(v => v.Images.OrderBy(i => i.Order))
            .Include(v => v.ColorVariants)
            .Include(v => v.Specifications)
            .AsQueryable();

        // Apply filters
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var searchTerm = query.Search.ToLower();
            queryable = queryable.Where(v =>
                EF.Functions.Like(v.Model.ToLower(), $"%{searchTerm}%") ||
                (v.Description != null && EF.Functions.Like(v.Description.ToLower(), $"%{searchTerm}%")));
        }

        if (!string.IsNullOrWhiteSpace(query.Type))
        {
            queryable = queryable.Where(v => v.Type == query.Type);
        }

        if (query.DealerId.HasValue)
        {
            queryable = queryable.Where(v => v.DealerId == query.DealerId.Value);
        }

        if (query.MinPrice.HasValue)
        {
            queryable = queryable.Where(v => v.Price >= query.MinPrice.Value);
        }

        if (query.MaxPrice.HasValue)
        {
            queryable = queryable.Where(v => v.Price <= query.MaxPrice.Value);
        }

        var totalCount = await queryable.CountAsync();

        var vehicles = await queryable
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(v => new
            {
                Vehicle = v,
                DealerName = v.Dealer!.Name,
                Images = v.Images.Select(i => new VehicleImageDto
                {
                    Id = i.Id,
                    Url = i.Url,
                    AltText = i.AltText,
                    Order = i.Order
                }).ToList(),
                ColorVariants = v.ColorVariants.Select(cv => new ColorVariantDto
                {
                    Id = cv.Id,
                    Name = cv.Name,
                    Hex = cv.Hex,
                    Stock = cv.Stock
                }).ToList(),
                Specifications = v.Specifications == null ? null : new VehicleSpecificationsDto
                {
                    Id = v.Specifications.Id,
                    Acceleration = v.Specifications.Acceleration,
                    TopSpeed = v.Specifications.TopSpeed,
                    Charging = v.Specifications.Charging,
                    Warranty = v.Specifications.Warranty,
                    Seats = v.Specifications.Seats,
                    Cargo = v.Specifications.Cargo
                }
            })
            .ToListAsync();

        // Apply sorting in memory
        vehicles = query.SortBy?.ToLower() switch
        {
            "model" => query.SortOrder == "asc"
                ? vehicles.OrderBy(v => v.Vehicle.Model).ToList()
                : vehicles.OrderByDescending(v => v.Vehicle.Model).ToList(),
            "price" => query.SortOrder == "asc"
                ? vehicles.OrderBy(v => v.Vehicle.Price).ToList()
                : vehicles.OrderByDescending(v => v.Vehicle.Price).ToList(),
            "createdat" => query.SortOrder == "asc"
                ? vehicles.OrderBy(v => v.Vehicle.CreatedAt).ToList()
                : vehicles.OrderByDescending(v => v.Vehicle.CreatedAt).ToList(),
            _ => vehicles.OrderByDescending(v => v.Vehicle.CreatedAt).ToList()
        };

        var items = vehicles.Select(v => new VehicleDto
        {
            Id = v.Vehicle.Id,
            Model = v.Vehicle.Model,
            Type = v.Vehicle.Type,
            Price = v.Vehicle.Price,
            BatteryCapacity = v.Vehicle.BatteryCapacity,
            Range = v.Vehicle.Range,
            StockQuantity = v.Vehicle.StockQuantity,
            Description = v.Vehicle.Description,
            DealerId = v.Vehicle.DealerId,
            DealerName = v.DealerName,
            Images = v.Images,
            ColorVariants = v.ColorVariants,
            Specifications = v.Specifications,
            CreatedAt = v.Vehicle.CreatedAt,
            UpdatedAt = v.Vehicle.UpdatedAt
        }).ToList();

        return new PaginatedResult<VehicleDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = query.Page,
            PageSize = query.PageSize
        };
    }

    public async Task<VehicleDto?> GetVehicleByIdAsync(int id)
    {
        var vehicle = await _context.Vehicles
            .Include(v => v.Dealer)
            .Include(v => v.Images.OrderBy(i => i.Order))
            .Include(v => v.ColorVariants)
            .Include(v => v.Specifications)
            .FirstOrDefaultAsync(v => v.Id == id);

        if (vehicle == null) return null;

        return new VehicleDto
        {
            Id = vehicle.Id,
            Model = vehicle.Model,
            Type = vehicle.Type,
            Price = vehicle.Price,
            BatteryCapacity = vehicle.BatteryCapacity,
            Range = vehicle.Range,
            StockQuantity = vehicle.StockQuantity,
            Description = vehicle.Description,
            DealerId = vehicle.DealerId,
            DealerName = vehicle.Dealer!.Name,
            Images = vehicle.Images.Select(i => new VehicleImageDto
            {
                Id = i.Id,
                Url = i.Url,
                AltText = i.AltText,
                Order = i.Order
            }).ToList(),
            ColorVariants = vehicle.ColorVariants.Select(cv => new ColorVariantDto
            {
                Id = cv.Id,
                Name = cv.Name,
                Hex = cv.Hex,
                Stock = cv.Stock
            }).ToList(),
            Specifications = vehicle.Specifications == null ? null : new VehicleSpecificationsDto
            {
                Id = vehicle.Specifications.Id,
                Acceleration = vehicle.Specifications.Acceleration,
                TopSpeed = vehicle.Specifications.TopSpeed,
                Charging = vehicle.Specifications.Charging,
                Warranty = vehicle.Specifications.Warranty,
                Seats = vehicle.Specifications.Seats,
                Cargo = vehicle.Specifications.Cargo
            },
            CreatedAt = vehicle.CreatedAt,
            UpdatedAt = vehicle.UpdatedAt
        };
    }

    public async Task<VehicleDto> CreateVehicleAsync(CreateVehicleDto createDto, List<IFormFile>? imageFiles = null)
    {
        var vehicle = new Vehicle
        {
            Model = createDto.Model,
            Type = createDto.Type,
            Price = createDto.Price,
            BatteryCapacity = createDto.BatteryCapacity,
            Range = createDto.Range,
            StockQuantity = createDto.StockQuantity,
            Description = createDto.Description,
            DealerId = createDto.DealerId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Add images
        if (imageFiles != null && imageFiles.Count > 0)
        {
            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images");
            Directory.CreateDirectory(uploadsFolder);

            for (int i = 0; i < imageFiles.Count; i++)
            {
                var file = imageFiles[i];
                var imageDto = (createDto.Images != null && i < createDto.Images.Count) ? createDto.Images[i] : null;

                if (file.Length > 0)
                {
                    var fileName = $"{Guid.NewGuid()}_{ContentDispositionHeaderValue.Parse(file.ContentDisposition)?.FileName?.Trim('"') ?? "unknown"}";
                    var filePath = Path.Combine(uploadsFolder, fileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }

                    vehicle.Images.Add(new VehicleImage
                    {
                        Url = $"/images/{fileName}",
                        AltText = imageDto?.AltText ?? "Vehicle Image", // Use provided AltText or default
                        Order = imageDto?.Order ?? 0 // Consistent default for nullable int
                    });
                }
            }
        }
        else
        {
            // Fallback to URL-based images if no files provided or if file processing fails
            if (createDto.Images != null)
            {
                foreach (var imageDto in createDto.Images)
                {
                    vehicle.Images.Add(new VehicleImage
                    {
                        Url = imageDto.Url,
                        AltText = imageDto.AltText ?? "Vehicle Image", // Handle nullable AltText
                        Order = imageDto.Order ?? 0 // Explicitly cast and provide default for nullable int
                    });
                }
            }
        }

        // Add color variants
        foreach (var colorDto in createDto.ColorVariants)
        {
            vehicle.ColorVariants.Add(new ColorVariant
            {
                Name = colorDto.Name,
                Hex = colorDto.Hex,
                Stock = colorDto.Stock
            });
        }

        // Add specifications
        if (createDto.Specifications != null)
        {
            vehicle.Specifications = new VehicleSpecifications
            {
                Acceleration = createDto.Specifications.Acceleration,
                TopSpeed = createDto.Specifications.TopSpeed,
                Charging = createDto.Specifications.Charging,
                Warranty = createDto.Specifications.Warranty,
                Seats = createDto.Specifications.Seats,
                Cargo = createDto.Specifications.Cargo
            };
        }

        _context.Vehicles.Add(vehicle);
        await _context.SaveChangesAsync();

        // Publish vehicle created event
        var vehicleCreatedEvent = new VehicleCreatedEvent
        {
            VehicleId = vehicle.Id,
            Model = vehicle.Model,
            Type = vehicle.Type,
            Price = vehicle.Price,
            DealerId = vehicle.DealerId,
            CreatedAt = vehicle.CreatedAt
        };
        _messageProducer.PublishMessage(vehicleCreatedEvent);

        // Return the created vehicle with all related data
        return await GetVehicleByIdAsync(vehicle.Id) ?? throw new InvalidOperationException("Failed to retrieve created vehicle");
    }

    public async Task<VehicleDto?> UpdateVehicleAsync(int id, UpdateVehicleDto updateDto, List<IFormFile>? imageFiles = null)
    {
        var vehicle = await _context.Vehicles
            .Include(v => v.Images)
            .Include(v => v.ColorVariants)
            .Include(v => v.Specifications)
            .FirstOrDefaultAsync(v => v.Id == id);

        if (vehicle == null) return null;

        // Update basic properties
        vehicle.Model = updateDto.Model;
        vehicle.Type = updateDto.Type;
        vehicle.Price = updateDto.Price;
        vehicle.BatteryCapacity = updateDto.BatteryCapacity;
        vehicle.Range = updateDto.Range;
        vehicle.StockQuantity = updateDto.StockQuantity;
        vehicle.Description = updateDto.Description;
        vehicle.DealerId = updateDto.DealerId;
        vehicle.UpdatedAt = DateTime.UtcNow;

        // Update images - remove existing and add new ones
        if (imageFiles != null && imageFiles.Count > 0)
        {
            // Delete old image files from wwwroot
            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images");
            foreach (var existingImage in vehicle.Images)
            {
                if (!string.IsNullOrWhiteSpace(existingImage.Url) && existingImage.Url.StartsWith("/images/"))
                {
                    var fileName = Path.GetFileName(existingImage.Url);
                    var filePath = Path.Combine(uploadsFolder, fileName);
                    if (System.IO.File.Exists(filePath))
                    {
                        System.IO.File.Delete(filePath);
                    }
                }
            }
            _context.VehicleImages.RemoveRange(vehicle.Images);
            vehicle.Images.Clear();

            // Add new image files
            Directory.CreateDirectory(uploadsFolder);
            for (int i = 0; i < imageFiles.Count; i++)
            {
                var file = imageFiles[i];
                var imageDto = (updateDto.Images != null && i < updateDto.Images.Count) ? updateDto.Images[i] : null;

                if (file.Length > 0)
                {
                    var fileName = $"{Guid.NewGuid()}_{ContentDispositionHeaderValue.Parse(file.ContentDisposition)?.FileName?.Trim('"') ?? "unknown"}";
                    var filePath = Path.Combine(uploadsFolder, fileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }

                    vehicle.Images.Add(new VehicleImage
                    {
                        Url = $"/images/{fileName}",
                        AltText = imageDto?.AltText ?? "Vehicle Image", // Handle nullable AltText
                        Order = imageDto?.Order ?? 0 // Consistent default for nullable int
                    });
                }
            }
        }
        else
        {
            // Fallback to URL-based images if no files provided
            _context.VehicleImages.RemoveRange(vehicle.Images);
            vehicle.Images.Clear();
            if (updateDto.Images != null)
            {
                foreach (var imageDto in updateDto.Images)
                {
                    vehicle.Images.Add(new VehicleImage
                    {
                        Url = imageDto.Url,
                        AltText = imageDto.AltText ?? "Vehicle Image", // Handle nullable AltText
                        Order = imageDto.Order ?? 0 // Explicitly cast and provide default for nullable int
                    });
                }
            }
        }

        // Update color variants - remove existing and add new ones
        _context.ColorVariants.RemoveRange(vehicle.ColorVariants);
        foreach (var colorDto in updateDto.ColorVariants)
        {
            vehicle.ColorVariants.Add(new ColorVariant
            {
                Name = colorDto.Name,
                Hex = colorDto.Hex,
                Stock = colorDto.Stock
            });
        }

        // Update specifications
        if (vehicle.Specifications != null && updateDto.Specifications != null)
        {
            vehicle.Specifications.Acceleration = updateDto.Specifications.Acceleration;
            vehicle.Specifications.TopSpeed = updateDto.Specifications.TopSpeed;
            vehicle.Specifications.Charging = updateDto.Specifications.Charging;
            vehicle.Specifications.Warranty = updateDto.Specifications.Warranty;
            vehicle.Specifications.Seats = updateDto.Specifications.Seats;
            vehicle.Specifications.Cargo = updateDto.Specifications.Cargo;
        }
        else if (vehicle.Specifications == null && updateDto.Specifications != null)
        {
            vehicle.Specifications = new VehicleSpecifications
            {
                Acceleration = updateDto.Specifications.Acceleration,
                TopSpeed = updateDto.Specifications.TopSpeed,
                Charging = updateDto.Specifications.Charging,
                Warranty = updateDto.Specifications.Warranty,
                Seats = updateDto.Specifications.Seats,
                Cargo = updateDto.Specifications.Cargo
            };
        }
        else if (vehicle.Specifications != null && updateDto.Specifications == null)
        {
            _context.VehicleSpecifications.Remove(vehicle.Specifications);
            vehicle.Specifications = null;
        }

        await _context.SaveChangesAsync();

        // Publish vehicle updated event
        var vehicleUpdatedEvent = new VehicleUpdatedEvent
        {
            VehicleId = vehicle.Id,
            Model = vehicle.Model,
            Type = vehicle.Type,
            Price = vehicle.Price,
            DealerId = vehicle.DealerId,
            UpdatedAt = vehicle.UpdatedAt
        };
        _messageProducer.PublishMessage(vehicleUpdatedEvent);

        return await GetVehicleByIdAsync(id);
    }

    public async Task<bool> DeleteVehicleAsync(int id)
    {
        var vehicle = await _context.Vehicles.FindAsync(id);
        if (vehicle == null) return false;

        _context.Vehicles.Remove(vehicle);
        await _context.SaveChangesAsync();

        // Publish vehicle deleted event
        var vehicleDeletedEvent = new VehicleDeletedEvent
        {
            VehicleId = id,
            DeletedAt = DateTime.UtcNow
        };
        _messageProducer.PublishMessage(vehicleDeletedEvent);

        return true;
    }

    public async Task<VehicleReservedEvent?> ReserveVehicleAsync(int vehicleId, ReservationRequestDto request)
    {
        var vehicle = await _context.Vehicles
            .Include(v => v.ColorVariants)
            .FirstOrDefaultAsync(v => v.Id == vehicleId);

        if (vehicle == null || vehicle.StockQuantity < request.Quantity)
        {
            return null;
        }

        // Get color variant name if provided
        string? colorVariantName = null;
        if (request.ColorVariantId.HasValue)
        {
            var colorVariant = vehicle.ColorVariants
                .FirstOrDefault(cv => cv.Id == request.ColorVariantId.Value);
            colorVariantName = colorVariant?.Name;
        }

        // Create and publish vehicle reserved event
        var vehicleReservedEvent = new VehicleReservedEvent
        {
            VehicleId = vehicleId,
            VehicleName = vehicle.Model,
            VehiclePrice = vehicle.Price,
            DealerId = vehicle.DealerId,
            CustomerName = request.CustomerName,
            CustomerEmail = request.CustomerEmail,
            CustomerPhone = request.CustomerPhone,
            ColorVariantId = request.ColorVariantId,
            ColorVariantName = colorVariantName,
            Quantity = request.Quantity,
            Notes = request.Notes,
            ReservedAt = DateTime.UtcNow,
            DeviceToken = request.DeviceToken
        };

        _messageProducer.PublishMessage(vehicleReservedEvent);

        return vehicleReservedEvent;
    }

    public async Task<List<DealerDto>> GetDealersAsync()
    {
        return await _context.Dealers
            .Select(d => new DealerDto
            {
                Id = d.Id,
                Name = d.Name,
                Region = d.Region,
                Contact = d.Contact,
                Email = d.Email,
                Address = d.Address,
                VehicleCount = d.Vehicles.Count,
                CreatedAt = d.CreatedAt,
                UpdatedAt = d.UpdatedAt
            })
            .ToListAsync();
    }

    public async Task<DealerDto?> GetDealerByIdAsync(int id)
    {
        var dealer = await _context.Dealers
            .Include(d => d.Vehicles)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (dealer == null) return null;

        return new DealerDto
        {
            Id = dealer.Id,
            Name = dealer.Name,
            Region = dealer.Region,
            Contact = dealer.Contact,
            Email = dealer.Email,
            Address = dealer.Address,
            VehicleCount = dealer.Vehicles.Count,
            CreatedAt = dealer.CreatedAt,
            UpdatedAt = dealer.UpdatedAt
        };
    }

    public async Task<DealerDto> CreateDealerAsync(CreateDealerDto createDto)
    {
        var dealer = new Dealer
        {
            Name = createDto.Name,
            Region = createDto.Region,
            Contact = createDto.Contact,
            Email = createDto.Email,
            Address = createDto.Address,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Dealers.Add(dealer);
        await _context.SaveChangesAsync();

        return new DealerDto
        {
            Id = dealer.Id,
            Name = dealer.Name,
            Region = dealer.Region,
            Contact = dealer.Contact,
            Email = dealer.Email,
            Address = dealer.Address,
            VehicleCount = 0,
            CreatedAt = dealer.CreatedAt,
            UpdatedAt = dealer.UpdatedAt
        };
    }

    public async Task<DealerDto?> UpdateDealerAsync(int id, UpdateDealerDto updateDto)
    {
        var dealer = await _context.Dealers.FindAsync(id);
        if (dealer == null) return null;

        dealer.Name = updateDto.Name;
        dealer.Region = updateDto.Region;
        dealer.Contact = updateDto.Contact;
        dealer.Email = updateDto.Email;
        dealer.Address = updateDto.Address;
        dealer.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return await GetDealerByIdAsync(id);
    }

    public async Task<bool> DeleteDealerAsync(int id)
    {
        var dealer = await _context.Dealers.FindAsync(id);
        if (dealer == null) return false;

        _context.Dealers.Remove(dealer);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<List<VehicleType>> GetVehicleTypesAsync()
    {
        return await _context.VehicleTypes.ToListAsync();
    }
}
