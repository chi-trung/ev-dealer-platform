using AutoMapper;
using CustomerService.DTOs;
using CustomerService.Models;

namespace CustomerService.Profiles
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            // TestDrive Mappings
            CreateMap<TestDrive, TestDriveDto>()
                .ForMember(dest => dest.CustomerName, opt => opt.MapFrom(src => src.Customer != null ? src.Customer.Name : null));

            // TestDrives.Status is NOT NULL in the database, but the entity
            // carries a C# initializer ("Đã lên lịch"). Convention mapping
            // copies the nullable DTO member straight over that initializer, so
            // a request that omits `status` wrote null into a NOT NULL column
            // and the save threw:
            //   POST without status -> 500, TestDriveService.cs:38
            //   PUT  without status -> 500, TestDriveService.cs:89 — worse,
            //   because the update map runs ONTO the loaded entity and blanked
            //   the status of a booking that already existed.
            // Skipping the member when the source has nothing to say leaves the
            // destination's own value alone: the initializer on create, the
            // current status on update.
            CreateMap<CreateTestDriveRequest, TestDrive>()
                .ForMember(dest => dest.Status, opt => opt.Condition(src => !string.IsNullOrEmpty(src.Status)));
            CreateMap<UpdateTestDriveRequest, TestDrive>()
                .ForMember(dest => dest.Status, opt => opt.Condition(src => !string.IsNullOrEmpty(src.Status)));

            // Customer Mappings
            CreateMap<Customer, CustomerDto>();
            CreateMap<CustomerCreateDto, Customer>();
            CreateMap<CreateCustomerRequest, Customer>();
            CreateMap<UpdateCustomerRequest, Customer>();

            // Purchase Mappings
            CreateMap<Purchase, PurchaseDto>();

            // Complaint Mappings
            CreateMap<Complaint, ComplaintDto>();
            CreateMap<CreateComplaintRequest, Complaint>();
            CreateMap<UpdateComplaintRequest, Complaint>();
        }
    }
}
