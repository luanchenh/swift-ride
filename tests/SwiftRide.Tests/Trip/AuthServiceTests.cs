using Microsoft.EntityFrameworkCore;
using Moq;
using Trip_Service.Application.Contracts.Auth;
using Trip_Service.Application.Services;
using Trip_Service.Application.Services.Impl;
using Trip_Service.Domain;
using Trip_Service.Infrastructure.Persistence.Impl;

namespace SwiftRide.Tests.Trip;

public sealed class AuthServiceTests
{
    private readonly Mock<IAccountRepository> _accounts = new();
    private readonly Mock<IDriverRepository> _drivers = new();
    private readonly Mock<IPasswordService> _passwords = new();
    private readonly Mock<IJwtService> _jwt = new();

    [Fact]
    public async Task Login_ValidCredentials_NormalizesPhoneAndReturnsSession()
    {
        var account = Rider();
        var profile = DriverProfile(account.Uuid);
        _accounts.Setup(repository => repository.FindByPhoneAsync("0901234567", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        _passwords.Setup(service => service.Verify(account, account.Password, "secret123")).Returns(true);
        _drivers.Setup(repository => repository.FindByAccountUuidAsync(account.Uuid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _jwt.Setup(service => service.Create(account, profile)).Returns(("jwt-token", 100L, 200L));
        var service = CreateService();

        var result = await service.LoginAsync(new LoginRequestContract
        {
            Phone = " 0901234567 ",
            Password = "secret123"
        });

        Assert.Equal("jwt-token", result.Token);
        Assert.Same(account, result.Account);
        Assert.Equal(100, result.IssuedAt);
        Assert.Equal(200, result.ExpiresAt);
    }

    [Fact]
    public async Task Login_AccountMissing_ThrowsWithoutVerifyingPassword()
    {
        _accounts.Setup(repository => repository.FindByPhoneAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Account?)null);
        var service = CreateService();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.LoginAsync(
            new LoginRequestContract { Phone = "0900000000", Password = "secret" }));

        _passwords.Verify(service => service.Verify(
            It.IsAny<Account>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Login_WrongPassword_ThrowsWithoutCreatingToken()
    {
        var account = Rider();
        _accounts.Setup(repository => repository.FindByPhoneAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        _passwords.Setup(service => service.Verify(account, account.Password, It.IsAny<string>())).Returns(false);
        var service = CreateService();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.LoginAsync(
            new LoginRequestContract { Phone = account.Phone, Password = "wrong-password" }));

        _jwt.Verify(service => service.Create(It.IsAny<Account>(), It.IsAny<DriverProfile?>()), Times.Never);
    }

    [Fact]
    public async Task Register_NewPhone_CreatesNormalizedRiderWithHashedPassword()
    {
        _accounts.Setup(repository => repository.PhoneExistsAsync("0901234567", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _passwords.Setup(service => service.Hash(It.IsAny<Account>(), "secret123")).Returns("hashed");
        var service = CreateService();

        var result = await service.RegisterAsync(new RegisterRequestContract
        {
            Fullname = "  Nguyễn Văn An  ",
            Phone = " 0901234567 ",
            Password = "secret123"
        });

        Assert.NotEqual(Guid.Empty, result.Uuid);
        Assert.Equal("Nguyễn Văn An", result.Fullname);
        Assert.Equal("0901234567", result.Phone);
        Assert.Equal("hashed", result.Password);
        Assert.Equal(Role.Rider, result.Role);
        _accounts.Verify(repository => repository.AddAsync(result, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Register_DuplicatePhone_Throws()
    {
        _accounts.Setup(repository => repository.PhoneExistsAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterAsync(
            new RegisterRequestContract { Fullname = "An", Phone = "0901234567", Password = "secret" }));

        _accounts.Verify(repository => repository.AddAsync(
            It.IsAny<Account>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Register_DatabaseConflict_WrapsException()
    {
        _accounts.Setup(repository => repository.PhoneExistsAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _passwords.Setup(service => service.Hash(It.IsAny<Account>(), It.IsAny<string>())).Returns("hash");
        _accounts.Setup(repository => repository.AddAsync(
                It.IsAny<Account>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("duplicate"));
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterAsync(
            new RegisterRequestContract { Fullname = "An", Phone = "0901234567", Password = "secret" }));

        Assert.IsType<DbUpdateException>(exception.InnerException);
    }

    [Fact]
    public async Task RegisterDriver_ValidRequest_NormalizesAndPersistsProfile()
    {
        var account = Rider();
        _accounts.Setup(repository => repository.FindByUuidAsync(account.Uuid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        _drivers.Setup(repository => repository.FindByAccountUuidAsync(account.Uuid, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DriverProfile?)null);
        _drivers.Setup(repository => repository.HasDuplicateAsync(
                "GPLX-123", "51A-12345", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var service = CreateService();

        var result = await service.RegisterDriverAsync(account.Uuid, new RegisterDriverRequestContract
        {
            LicenseNumber = " gplx-123 ",
            VehicleName = " Honda Vision ",
            LicensePlate = " 51a-12345 "
        });

        Assert.Same(account, result.Account);
        Assert.Equal(account.Uuid, result.Profile.AccountUuid);
        Assert.Equal("GPLX-123", result.Profile.LicenseNumber);
        Assert.Equal("Honda Vision", result.Profile.VehicleName);
        Assert.Equal("51A-12345", result.Profile.LicensePlate);
        Assert.False(result.Profile.IsVerified);
        _drivers.Verify(repository => repository.AddAndPromoteAsync(
            account, result.Profile, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterDriver_AccountMissing_Throws()
    {
        _accounts.Setup(repository => repository.FindByUuidAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Account?)null);
        var service = CreateService();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.RegisterDriverAsync(
            Guid.NewGuid(), DriverRequest()));
    }

    [Fact]
    public async Task RegisterDriver_AlreadyDriverRole_ThrowsWithoutProfileLookup()
    {
        var account = Rider();
        account.Role = Role.Driver;
        _accounts.Setup(repository => repository.FindByUuidAsync(account.Uuid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RegisterDriverAsync(account.Uuid, DriverRequest()));

        _drivers.Verify(repository => repository.FindByAccountUuidAsync(
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegisterDriver_ProfileAlreadyExists_Throws()
    {
        var account = Rider();
        _accounts.Setup(repository => repository.FindByUuidAsync(account.Uuid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        _drivers.Setup(repository => repository.FindByAccountUuidAsync(account.Uuid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DriverProfile(account.Uuid));
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RegisterDriverAsync(account.Uuid, DriverRequest()));
    }

    [Fact]
    public async Task RegisterDriver_DuplicateLicenseOrPlate_Throws()
    {
        var account = Rider();
        _accounts.Setup(repository => repository.FindByUuidAsync(account.Uuid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        _drivers.Setup(repository => repository.FindByAccountUuidAsync(account.Uuid, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DriverProfile?)null);
        _drivers.Setup(repository => repository.HasDuplicateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RegisterDriverAsync(account.Uuid, DriverRequest()));
    }

    [Fact]
    public async Task AboutMe_ReturnsAccountAndOptionalProfile()
    {
        var account = Rider();
        var profile = DriverProfile(account.Uuid);
        _accounts.Setup(repository => repository.FindByUuidAsync(account.Uuid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        _drivers.Setup(repository => repository.FindByAccountUuidAsync(account.Uuid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        var service = CreateService();

        var result = await service.AboutMeAsync(account.Uuid);

        Assert.Same(account, result.Account);
        Assert.Same(profile, result.Profile);
    }

    [Fact]
    public async Task AboutMe_MissingAccount_ThrowsWithoutProfileLookup()
    {
        _accounts.Setup(repository => repository.FindByUuidAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Account?)null);
        var service = CreateService();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.AboutMeAsync(Guid.NewGuid()));

        _drivers.Verify(repository => repository.FindByAccountUuidAsync(
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChangePassword_ValidCurrentPassword_HashesAndUpdates()
    {
        var account = Rider();
        _accounts.Setup(repository => repository.FindByUuidAsync(account.Uuid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        _passwords.Setup(service => service.Verify(account, account.Password, "old-secret")).Returns(true);
        _passwords.Setup(service => service.Hash(account, "new-secret")).Returns("new-hash");
        var service = CreateService();

        await service.ChangePasswordAsync(account.Uuid, new ChangePasswordRequestContract
        {
            CurrentPassword = "old-secret",
            NewPassword = "new-secret"
        });

        _accounts.Verify(repository => repository.UpdatePasswordAsync(
            account.Uuid, "new-hash", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChangePassword_MissingAccount_Throws()
    {
        _accounts.Setup(repository => repository.FindByUuidAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Account?)null);
        var service = CreateService();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ChangePasswordAsync(
            Guid.NewGuid(), ChangePasswordRequest()));
    }

    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_ThrowsWithoutUpdate()
    {
        var account = Rider();
        _accounts.Setup(repository => repository.FindByUuidAsync(account.Uuid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        _passwords.Setup(service => service.Verify(
            account, account.Password, It.IsAny<string>())).Returns(false);
        var service = CreateService();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ChangePasswordAsync(
            account.Uuid, ChangePasswordRequest()));

        _accounts.Verify(repository => repository.UpdatePasswordAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChangePassword_SamePassword_ThrowsWithoutHashing()
    {
        var account = Rider();
        _accounts.Setup(repository => repository.FindByUuidAsync(account.Uuid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        _passwords.Setup(service => service.Verify(account, account.Password, "same-secret")).Returns(true);
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() => service.ChangePasswordAsync(
            account.Uuid,
            new ChangePasswordRequestContract
            {
                CurrentPassword = "same-secret",
                NewPassword = "same-secret"
            }));

        _passwords.Verify(service => service.Hash(It.IsAny<Account>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Logout_ExistingAccount_Completes()
    {
        var account = Rider();
        _accounts.Setup(repository => repository.FindByUuidAsync(account.Uuid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        var service = CreateService();

        await service.LogoutAsync(account.Uuid);
    }

    [Fact]
    public async Task Logout_MissingAccount_Throws()
    {
        _accounts.Setup(repository => repository.FindByUuidAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Account?)null);
        var service = CreateService();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.LogoutAsync(Guid.NewGuid()));
    }

    private AuthService CreateService() => new(
        _accounts.Object, _drivers.Object, _passwords.Object, _jwt.Object);

    private static Account Rider() => new()
    {
        Uuid = Guid.NewGuid(),
        Fullname = "Nguyễn Văn An",
        Phone = "0901234567",
        Password = "old-hash",
        Role = Role.Rider
    };

    private static DriverProfile DriverProfile(Guid accountUuid) => new()
    {
        DriverUuid = Guid.NewGuid(),
        AccountUuid = accountUuid,
        LicenseNumber = "GPLX-123",
        VehicleName = "Honda Vision",
        LicensePlate = "51A-12345"
    };

    private static RegisterDriverRequestContract DriverRequest() => new()
    {
        LicenseNumber = "GPLX-123",
        VehicleName = "Honda Vision",
        LicensePlate = "51A-12345"
    };

    private static ChangePasswordRequestContract ChangePasswordRequest() => new()
    {
        CurrentPassword = "old-secret",
        NewPassword = "new-secret"
    };
}
