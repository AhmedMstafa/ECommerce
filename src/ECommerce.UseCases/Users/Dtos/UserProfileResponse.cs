namespace ECommerce.UseCases.Users.Dtos;

public sealed record UserProfileResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string? Description,
    string? PictureUrl,
    IReadOnlyList<string> Roles);
