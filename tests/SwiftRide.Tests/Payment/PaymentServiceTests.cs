using Moq;
using Payment_Service.Application.Contracts;
using Payment_Service.Application.Services;
using Payment_Service.Application.Services.Impl;
using Payment_Service.Domain;
using Payment_Service.Infrastructure.Persistence.Impl;

namespace SwiftRide.Tests.Payment;

public sealed class PaymentServiceTests
{
    private readonly Mock<IPaymentRepository> _payments = new();
    private readonly Mock<IPaymentTransactionService> _transactions = new();

    [Fact]
    public async Task GetByUuid_PaymentFound_MapsPaymentAndTransactions()
    {
        var payment = CreatePayment();
        IReadOnlyList<PaymentTransactionResponseContract> transactions =
        [
            new()
            {
                Uuid = Guid.NewGuid(),
                PaymentUuid = payment.Uuid,
                ReferenceCode = "REF-1",
                Amount = payment.Amount,
                Status = PaymentTransactionStatus.Succeeded
            }
        ];
        _payments.Setup(repository => repository.GetPaymentByUuidAsync(
                payment.Uuid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);
        _transactions.Setup(service => service.GetByPaymentUuidAsync(
                payment.Uuid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions);
        var service = CreateService();

        var result = await service.GetByUuidAsync(payment.Uuid, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(payment.Uuid, result.Uuid);
        Assert.Equal(payment.TripUuid, result.TripUuid);
        Assert.Equal(payment.Amount, result.Amount);
        Assert.Same(transactions, result.Transactions);
    }

    [Fact]
    public async Task GetByUuid_PaymentMissing_ReturnsNullWithoutTransactionQuery()
    {
        _payments.Setup(repository => repository.GetPaymentByUuidAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Payment_Service.Domain.Payment?)null);
        var service = CreateService();

        var result = await service.GetByUuidAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
        _transactions.Verify(service => service.GetByPaymentUuidAsync(
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetByRiderUuid_MapsEveryPayment()
    {
        var riderUuid = Guid.NewGuid();
        var payment1 = CreatePayment(riderUuid: riderUuid);
        var payment2 = CreatePayment(riderUuid: riderUuid);
        _payments.Setup(repository => repository.GetPaymentsByRiderUuidAsync(
                riderUuid, It.IsAny<CancellationToken>()))
            .ReturnsAsync([payment1, payment2]);
        var service = CreateService();

        var result = await service.GetByRiderUuidAsync(riderUuid, CancellationToken.None);

        Assert.Collection(result,
            item => Assert.Equal(payment1.Uuid, item.Uuid),
            item => Assert.Equal(payment2.Uuid, item.Uuid));
    }

    [Fact]
    public async Task GetByDriverUuid_MapsEveryPayment()
    {
        var driverUuid = Guid.NewGuid();
        var payment = CreatePayment(driverUuid: driverUuid);
        _payments.Setup(repository => repository.GetPaymentsByDriverUuidAsync(
                driverUuid, It.IsAny<CancellationToken>()))
            .ReturnsAsync([payment]);
        var service = CreateService();

        var result = await service.GetByDriverUuidAsync(driverUuid, CancellationToken.None);

        var item = Assert.Single(result);
        Assert.Equal(payment.Uuid, item.Uuid);
        Assert.Equal(payment.QrUrl, item.QrUrl);
        Assert.Equal(payment.TransferCode, item.TransferCode);
        Assert.Equal(payment.Status, item.Status);
    }

    private PaymentService CreateService() => new(_payments.Object, _transactions.Object);

    private static Payment_Service.Domain.Payment CreatePayment(
        Guid? riderUuid = null,
        Guid? driverUuid = null) => new()
    {
        Uuid = Guid.NewGuid(),
        TripUuid = Guid.NewGuid(),
        RiderUuid = riderUuid ?? Guid.NewGuid(),
        DriverUuid = driverUuid ?? Guid.NewGuid(),
        Amount = 125_000m,
        QrUrl = "https://qr.example/payment",
        TransferCode = "ABC123",
        Status = PaymentStatus.Pending
    };
}
