using System.ComponentModel.DataAnnotations;

namespace Trip_Service.Application.Contracts.Auth;

public sealed class ChangePasswordRequestContract
{
    [Required, MinLength(6)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, MinLength(6)]
    public string NewPassword { get; set; } = string.Empty;
}
