using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.BusinessLogic.Models;
using HanyOptics.DataAccess.Persistence;
using HanyOptics.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HanyOptics.BusinessLogic.Services;

public class DoctorAdminService : IDoctorAdminService
{
    private readonly HanyOpticsDbContext _dbContext;
    private readonly ILogger<DoctorAdminService> _logger;

    public DoctorAdminService(HanyOpticsDbContext dbContext, ILogger<DoctorAdminService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Doctor>> ListAsync() =>
        await _dbContext.Doctors.AsNoTracking().OrderBy(d => d.Name).ToListAsync();

    // No stored procedure behind this - doctors carry no triggers or business rules the way
    // orders/frames do, so a plain EF insert is the whole of what's needed, the same as
    // NewOrderService.ResolveOrCreateCustomerAsync does for a new customer row.
    public async Task<OperationResult> CreateAsync(CreateDoctorRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return OperationResult.Failure("أدخل اسم الطبيب");

        var doctor = new Doctor
        {
            Name = request.Name.Trim(),
            Clinic = string.IsNullOrWhiteSpace(request.Clinic) ? null : request.Clinic.Trim(),
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim()
        };

        _dbContext.Doctors.Add(doctor);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Added doctor {DoctorId} ({Name}).", doctor.DoctorId, doctor.Name);

        return OperationResult.Success();
    }
}
