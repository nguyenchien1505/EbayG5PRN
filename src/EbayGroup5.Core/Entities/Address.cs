namespace EbayGroup5.Core.Entities;

public sealed class Address
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public string? FullName { get; set; }
    public string? Phone { get; set; }
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public bool? IsDefault { get; set; }
}
