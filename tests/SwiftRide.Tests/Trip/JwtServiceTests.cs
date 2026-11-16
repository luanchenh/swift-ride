using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Trip_Service.Application.Services;
using Trip_Service.Domain;
using Trip_Service.Infrastructure.Authentication;

namespace SwiftRide.Tests.Trip;

public sealed class JwtServiceTests
{
    private static readonly JwtOptions Options = new()
    {
        Issuer = "swift-ride-tests",
        Audience = "swift-ride-client-tests",
        Secret = "this-is-a-test-secret-with-at-least-32-bytes",
        ExpirationMinutes = 90
    };

    [Fact]
    public void Create_RiderAccount_ProducesExpectedClaimsWithoutProfile()
    {
        var account = Account(Role.Rider);
        var service = new JwtService(Microsoft.Extensions.Options.Options.Create(Options));

        var result = service.Create(account, null);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);

        Assert.Equal(account.Uuid.ToString(), token.Payload["uuid"]?.ToString());
        Assert.Equal(Options.Issuer, token.Issuer);
        Assert.Contains(Options.Audience, token.Audiences);
        Assert.InRange(result.ExpiresAt - result.IssuedAt, 5_399, 5_401);
        Assert.Equal(result.IssuedAt, Convert.ToInt64(token.Payload[JwtRegisteredClaimNames.Iat]));
        Assert.Equal(result.ExpiresAt, Convert.ToInt64(token.Payload[JwtRegisteredClaimNames.Exp]));

        var accountPayload = Assert.IsType<JsonElement>(token.Payload["account"]);
        Assert.Equal(account.Fullname, accountPayload.GetProperty("fullname").GetString());
        Assert.Equal("Rider", accountPayload.GetProperty("role").GetString());

        Assert.Null(token.Payload["profile"]);
    }

    [Fact]
    public void Create_DriverAccount_IncludesCompleteProfile()
    {
        var account = Account(Role.Driver);
        var profile = new DriverProfile
        {
            DriverUuid = Guid.NewGuid(),
            AccountUuid = account.Uuid,
            LicenseNumber = "GPLX-123",
            VehicleName = "Honda Vision",
            LicensePlate = "51A-12345",
            IsVerified = true
        };
        var service = new JwtService(Microsoft.Extensions.Options.Options.Create(Options));

        var result = service.Create(account, profile);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        var payload = Assert.IsType<JsonElement>(token.Payload["profile"]);

        Assert.Equal(profile.DriverUuid.ToString(), payload.GetProperty("profileUuid").GetString());
        Assert.Equal(profile.LicenseNumber, payload.GetProperty("licenseNumber").GetString());
        Assert.Equal(profile.VehicleName, payload.GetProperty("vehicleName").GetString());
        Assert.Equal(profile.LicensePlate, payload.GetProperty("licensePlate").GetString());
        Assert.True(payload.GetProperty("isVerified").GetBoolean());
    }

    private static Account Account(Role role) => new()
    {
        Uuid = Guid.NewGuid(),
        Fullname = "Nguyễn Văn An",
        Phone = "0901234567",
        Role = role
    };
}
