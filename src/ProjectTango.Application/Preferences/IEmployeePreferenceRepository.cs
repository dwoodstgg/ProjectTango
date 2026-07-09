namespace ProjectTango.Application.Preferences;

/// <summary>A small per-employee key/value store for UI preferences (synced across devices).</summary>
public interface IEmployeePreferenceRepository
{
    Task<string?> GetAsync(Guid employeeId, string key, CancellationToken cancellationToken = default);

    Task SetAsync(Guid employeeId, string key, string value, CancellationToken cancellationToken = default);
}
