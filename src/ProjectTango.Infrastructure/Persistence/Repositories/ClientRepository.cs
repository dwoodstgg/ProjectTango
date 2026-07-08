using System.Text.Json;
using Dapper;
using Npgsql;
using ProjectTango.Application.Clients;
using ProjectTango.Domain.Entities;

namespace ProjectTango.Infrastructure.Persistence.Repositories;

public class ClientRepository(NpgsqlDataSource dataSource) : IClientRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string SelectColumns =
        """
        SELECT id, name, billing_contact_name, billing_contact_email,
               billing_address::text AS billing_address_json,
               payment_terms_days, is_internal, is_active
        FROM clients
        """;

    public async Task<IReadOnlyList<Client>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<ClientRow>(new CommandDefinition(
            $"{SelectColumns} ORDER BY name",
            cancellationToken: cancellationToken));
        return rows.Select(ToClient).ToList();
    }

    public async Task<Client?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<ClientRow>(new CommandDefinition(
            $"{SelectColumns} WHERE id = @id",
            new { id },
            cancellationToken: cancellationToken));
        return row is null ? null : ToClient(row);
    }

    public async Task AddAsync(Client client, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO clients (id, name, billing_contact_name, billing_contact_email,
                                 billing_address, payment_terms_days, is_internal, is_active)
            VALUES (@Id, @Name, @BillingContactName, @BillingContactEmail,
                    @billingAddressJson::jsonb, @PaymentTermsDays, @IsInternal, @IsActive)
            """,
            ToParams(client),
            cancellationToken: cancellationToken));
    }

    public async Task UpdateAsync(Client client, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE clients SET
                name = @Name,
                billing_contact_name = @BillingContactName,
                billing_contact_email = @BillingContactEmail,
                billing_address = @billingAddressJson::jsonb,
                payment_terms_days = @PaymentTermsDays
            WHERE id = @Id
            """,
            ToParams(client),
            cancellationToken: cancellationToken));
    }

    public async Task SetActiveAsync(Guid clientId, bool isActive, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE clients SET is_active = @isActive WHERE id = @clientId",
            new { clientId, isActive },
            cancellationToken: cancellationToken));
    }

    private static object ToParams(Client client) => new
    {
        client.Id,
        client.Name,
        client.BillingContactName,
        client.BillingContactEmail,
        billingAddressJson = client.BillingAddress is null
            ? null
            : JsonSerializer.Serialize(client.BillingAddress, JsonOptions),
        client.PaymentTermsDays,
        client.IsInternal,
        client.IsActive,
    };

    private static Client ToClient(ClientRow row) => new()
    {
        Id = row.Id,
        Name = row.Name,
        BillingContactName = row.BillingContactName,
        BillingContactEmail = row.BillingContactEmail,
        BillingAddress = row.BillingAddressJson is null
            ? null
            : JsonSerializer.Deserialize<BillingAddress>(row.BillingAddressJson, JsonOptions),
        PaymentTermsDays = row.PaymentTermsDays,
        IsInternal = row.IsInternal,
        IsActive = row.IsActive,
    };

    private sealed record ClientRow(
        Guid Id,
        string Name,
        string? BillingContactName,
        string? BillingContactEmail,
        string? BillingAddressJson,
        int PaymentTermsDays,
        bool IsInternal,
        bool IsActive);
}
