using Microsoft.AspNetCore.Identity;
using Trip_Service.Application.Services;
using Trip_Service.Domain;

namespace SwiftRide.Tests.Trip;

public sealed class PasswordServiceTests
{
    [Fact]
    public void HashAndVerify_CorrectPassword_ReturnsTrue()
    {
        var account = new Account { Uuid = Guid.NewGuid() };
        var service = new PasswordService(new PasswordHasher<Account>());

        var hash = service.Hash(account, "strong-password");

        Assert.NotEqual("strong-password", hash);
        Assert.True(service.Verify(account, hash, "strong-password"));
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        var account = new Account { Uuid = Guid.NewGuid() };
        var service = new PasswordService(new PasswordHasher<Account>());
        var hash = service.Hash(account, "correct-password");

        var result = service.Verify(account, hash, "wrong-password");

        Assert.False(result);
    }
}
