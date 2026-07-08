using ProjectTango.Application.Common;
using ProjectTango.Domain;
using ProjectTango.Domain.Entities;

namespace ProjectTango.Application.Clients;

/// <summary>Client management — Operations Manager (Admin bypass is audit-flagged).</summary>
public class ClientAdminService(ICurrentUser currentUser, IClientRepository clients, IAuditLog audit)
{
    public async Task<IReadOnlyList<Client>> ListAsync(CancellationToken cancellationToken = default)
    {
        currentUser.RequireAny(RoleNames.OperationsManager, RoleNames.ProjectManager);
        return await clients.GetAllAsync(cancellationToken);
    }

    public async Task<Client?> GetAsync(Guid clientId, CancellationToken cancellationToken = default)
    {
        currentUser.RequireAny(RoleNames.OperationsManager);
        return await clients.GetByIdAsync(clientId, cancellationToken);
    }

    public async Task<Client> CreateAsync(
        string name,
        string? billingContactName,
        string? billingContactEmail,
        BillingAddress? billingAddress,
        int paymentTermsDays,
        CancellationToken cancellationToken = default)
    {
        var adminOverride = currentUser.RequireAny(RoleNames.OperationsManager);

        var client = new Client
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            BillingContactName = Normalize(billingContactName),
            BillingContactEmail = Normalize(billingContactEmail),
            BillingAddress = billingAddress,
            PaymentTermsDays = paymentTermsDays,
        };
        await clients.AddAsync(client, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            currentUser.EmployeeId, "client.created", "client", client.Id,
            new { client.Name, adminOverride }), cancellationToken);

        return client;
    }

    public async Task UpdateAsync(
        Guid clientId,
        string name,
        string? billingContactName,
        string? billingContactEmail,
        BillingAddress? billingAddress,
        int paymentTermsDays,
        CancellationToken cancellationToken = default)
    {
        var adminOverride = currentUser.RequireAny(RoleNames.OperationsManager);

        var client = await clients.GetByIdAsync(clientId, cancellationToken)
            ?? throw new DomainException("Unknown client.");

        client.Name = name.Trim();
        client.BillingContactName = Normalize(billingContactName);
        client.BillingContactEmail = Normalize(billingContactEmail);
        client.BillingAddress = billingAddress;
        client.PaymentTermsDays = paymentTermsDays;
        await clients.UpdateAsync(client, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            currentUser.EmployeeId, "client.updated", "client", client.Id,
            new { client.Name, adminOverride }), cancellationToken);
    }

    public async Task SetActiveAsync(Guid clientId, bool isActive, CancellationToken cancellationToken = default)
    {
        var adminOverride = currentUser.RequireAny(RoleNames.OperationsManager);

        var client = await clients.GetByIdAsync(clientId, cancellationToken)
            ?? throw new DomainException("Unknown client.");
        if (client.IsActive == isActive)
        {
            return;
        }

        await clients.SetActiveAsync(clientId, isActive, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            currentUser.EmployeeId, isActive ? "client.reactivated" : "client.deactivated", "client", clientId,
            new { adminOverride }), cancellationToken);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
