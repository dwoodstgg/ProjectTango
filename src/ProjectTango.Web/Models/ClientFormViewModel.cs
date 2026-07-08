using System.ComponentModel.DataAnnotations;
using ProjectTango.Domain.Entities;

namespace ProjectTango.Web.Models;

public class ClientFormViewModel
{
    [Required]
    [Display(Name = "Client name")]
    public string? Name { get; set; }

    [Display(Name = "Billing contact name")]
    public string? BillingContactName { get; set; }

    [EmailAddress]
    [Display(Name = "Billing contact email")]
    public string? BillingContactEmail { get; set; }

    [Range(0, 365)]
    [Display(Name = "Payment terms (days)")]
    public int PaymentTermsDays { get; set; } = 30;

    [Display(Name = "Address line 1")]
    public string? AddressLine1 { get; set; }

    [Display(Name = "Address line 2")]
    public string? AddressLine2 { get; set; }

    [Display(Name = "City")]
    public string? City { get; set; }

    [Display(Name = "State")]
    public string? State { get; set; }

    [Display(Name = "Postal code")]
    public string? PostalCode { get; set; }

    public BillingAddress? ToBillingAddress()
    {
        if (string.IsNullOrWhiteSpace(AddressLine1) && string.IsNullOrWhiteSpace(City)
            && string.IsNullOrWhiteSpace(State) && string.IsNullOrWhiteSpace(PostalCode))
        {
            return null;
        }

        return new BillingAddress
        {
            Line1 = AddressLine1?.Trim(),
            Line2 = string.IsNullOrWhiteSpace(AddressLine2) ? null : AddressLine2.Trim(),
            City = City?.Trim(),
            State = State?.Trim(),
            PostalCode = PostalCode?.Trim(),
        };
    }

    public static ClientFormViewModel From(Client client) => new()
    {
        Name = client.Name,
        BillingContactName = client.BillingContactName,
        BillingContactEmail = client.BillingContactEmail,
        PaymentTermsDays = client.PaymentTermsDays,
        AddressLine1 = client.BillingAddress?.Line1,
        AddressLine2 = client.BillingAddress?.Line2,
        City = client.BillingAddress?.City,
        State = client.BillingAddress?.State,
        PostalCode = client.BillingAddress?.PostalCode,
    };
}
