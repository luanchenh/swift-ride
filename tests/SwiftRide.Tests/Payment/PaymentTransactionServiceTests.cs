using MassTransit;
using Moq;
using Payment_Service.Application.Contracts;
using Payment_Service.Application.Services;
using Payment_Service.Application.Services.Impl;
using Payment_Service.Domain;
using Payment_Service.Infrastructure.Persistence.Impl;
using SwiftRide.Contracts.Events;

namespace SwiftRide.Tests.Payment;

public sealed class PaymentTransactionServiceTests
{
    private readonly Mock<IPaymentTransactionRepository> _transactions = new();
    private readonly Mock<IPaymentRepository> _payments = new();
    private readonly Mock<IPublishEndpoint> _publisher = new();

    [Fact]
    public async Task GetByPaymentUuid_MapsTransactions()
    {
        var paymentUuid = Guid.NewGuid();
        var transaction = new PaymentTransaction
        {
            Uuid = Guid.NewGuid(),
            PaymentUuid = paymentUuid,
            ReferenceCode = "REF-1",
            Amount = 100_000,
            Reason = "test",
            Status = PaymentTransactionStatus.Refunded
        };
        _transactions.Setup(repository => repository.GetByPaymentUuidAsync(
                paymentUuid, It.IsAny<CancellationToken>()))
            .ReturnsAsync([transaction]);
        var service = CreateService();

        var result = await service.GetByPaymentUuidAsync(paymentUuid, CancellationToken.None);

        var item = Assert.Single(result);
        Assert.Equal(transaction.Uuid, item.Uuid);
        Assert.Equal(transaction.ReferenceCode, item.ReferenceCode);
        Assert.Equal(transaction.Reason, item.Reason);
        Assert.Equal(transaction.Status, item.Status);
    }

    [Theory]
    [InlineData("out")]
    [InlineData("OUT")]
    [InlineData("withdraw")]
    public async Task ProcessIpn_OutgoingTransfer_IsIgnored(string transferType)
    {
        var service = CreateService();

        var result = await service.ProcessIpnAsync(
            WithTransferType(ValidIpn(), transferType),
            CancellationToken.None);

        Assert.Equal(PaymentIpnOutcome.Ignored, result.Outcome);
        _transactions.Verify(repository => repository.GetByReferenceCodeAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessIpn_NonPositiveAmount_IsInvalid()
    {
        var contract = ValidIpn();
        contract.TransferAmount = 0;
        var service = CreateService();

        var result = await service.ProcessIpnAsync(contract, CancellationToken.None);

        Assert.Equal(PaymentIpnOutcome.Invalid, result.Outcome);
    }

    [Fact]
    public async Task ProcessIpn_BlankReferenceCode_IsInvalid()
    {
        var contract = ValidIpn();
        contract.ReferenceCode = " ";
        var service = CreateService();

        var result = await service.ProcessIpnAsync(contract, CancellationToken.None);

        Assert.Equal(PaymentIpnOutcome.Invalid, result.Outcome);
    }

    [Fact]
    public async Task ProcessIpn_ExistingSucceededTransaction_IsAlreadyProcessed()
    {
        var existing = new PaymentTransaction
        {
            ReferenceCode = "REF-1",
            Status = PaymentTransactionStatus.Succeeded
        };
        _transactions.Setup(repository => repository.GetByReferenceCodeAsync(
                "REF-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        var service = CreateService();

        var result = await service.ProcessIpnAsync(ValidIpn(), CancellationToken.None);

        Assert.Equal(PaymentIpnOutcome.AlreadyProcessed, result.Outcome);
        _payments.Verify(repository => repository.GetPaymentByCodeAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessIpn_ExistingRefundedTransaction_RequiresRefund()
    {
        _transactions.Setup(repository => repository.GetByReferenceCodeAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentTransaction
            {
                ReferenceCode = "REF-1",
                Status = PaymentTransactionStatus.Refunded
            });
        var service = CreateService();

        var result = await service.ProcessIpnAsync(ValidIpn(), CancellationToken.None);

        Assert.Equal(PaymentIpnOutcome.RefundRequired, result.Outcome);
    }

    [Fact]
    public async Task ProcessIpn_NoTransferCode_IsInvalid()
    {
        SetupNoExistingTransaction();
        var contract = ValidIpn();
        contract.Code = null;
        contract.Content = "payment without expected marker";
        contract.Description = "nothing useful";
        var service = CreateService();

        var result = await service.ProcessIpnAsync(contract, CancellationToken.None);

        Assert.Equal(PaymentIpnOutcome.Invalid, result.Outcome);
        _payments.Verify(repository => repository.GetPaymentByCodeAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessIpn_PaymentNotFound_ReturnsSpecificOutcome()
    {
        SetupNoExistingTransaction();
        _payments.Setup(repository => repository.GetPaymentByCodeAsync(
                "ABC123", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Payment_Service.Domain.Payment?)null);
        var service = CreateService();

        var result = await service.ProcessIpnAsync(ValidIpn(), CancellationToken.None);

        Assert.Equal(PaymentIpnOutcome.PaymentNotFound, result.Outcome);
    }

    [Fact]
    public async Task ProcessIpn_AlreadySettledPayment_RecordsRefund()
    {
        SetupNoExistingTransaction();
        var payment = Payment(PaymentStatus.Settled);
        SetupPayment(payment);
        PaymentTransaction? created = null;
        _transactions.Setup(repository => repository.CreateAsync(
                It.IsAny<PaymentTransaction>(), It.IsAny<CancellationToken>()))
            .Callback<PaymentTransaction, CancellationToken>((transaction, _) => created = transaction)
            .Returns(Task.CompletedTask);
        var service = CreateService();

        var result = await service.ProcessIpnAsync(ValidIpn(), CancellationToken.None);

        Assert.Equal(PaymentIpnOutcome.RefundRequired, result.Outcome);
        Assert.NotNull(created);
        Assert.Equal(PaymentTransactionStatus.Refunded, created.Status);
        Assert.Contains("thanh toán trước", created.Reason);
        _publisher.Verify(endpoint => endpoint.Publish(
            It.IsAny<PaymentSettled>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessIpn_WrongAmount_RecordsRefund()
    {
        SetupNoExistingTransaction();
        var payment = Payment();
        payment.Amount = 200_000;
        SetupPayment(payment);
        PaymentTransaction? created = null;
        _transactions.Setup(repository => repository.CreateAsync(
                It.IsAny<PaymentTransaction>(), It.IsAny<CancellationToken>()))
            .Callback<PaymentTransaction, CancellationToken>((transaction, _) => created = transaction)
            .Returns(Task.CompletedTask);
        var service = CreateService();

        var result = await service.ProcessIpnAsync(ValidIpn(), CancellationToken.None);

        Assert.Equal(PaymentIpnOutcome.RefundRequired, result.Outcome);
        Assert.NotNull(created);
        Assert.Equal(PaymentTransactionStatus.Refunded, created.Status);
        Assert.Contains("Sai số tiền", created.Reason);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
    }

    [Fact]
    public async Task ProcessIpn_ExactAmount_SettlesPaymentAndPublishesEvent()
    {
        SetupNoExistingTransaction();
        var payment = Payment();
        SetupPayment(payment);
        PaymentTransaction? created = null;
        _transactions.Setup(repository => repository.CreateAsync(
                It.IsAny<PaymentTransaction>(), It.IsAny<CancellationToken>()))
            .Callback<PaymentTransaction, CancellationToken>((transaction, _) => created = transaction)
            .Returns(Task.CompletedTask);
        var service = CreateService();

        var result = await service.ProcessIpnAsync(ValidIpn(), CancellationToken.None);

        Assert.Equal(PaymentIpnOutcome.Processed, result.Outcome);
        Assert.Equal(PaymentStatus.Settled, payment.Status);
        Assert.NotNull(created);
        Assert.Equal(PaymentTransactionStatus.Succeeded, created.Status);
        Assert.Null(created.Reason);
        _publisher.Verify(endpoint => endpoint.Publish(
            It.Is<PaymentSettled>(message => message.TripUuid == payment.TripUuid),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("SEVQR ABC123", null, null)]
    [InlineData(null, "chuyen khoan SEVQR ABC123 cam on", null)]
    [InlineData(null, null, "SEVQR+ABC123")]
    public async Task ProcessIpn_ExtractsTransferCodeFromSupportedFields(
        string? code,
        string? content,
        string? description)
    {
        SetupNoExistingTransaction();
        var payment = Payment();
        _payments.Setup(repository => repository.GetPaymentByCodeAsync(
                "ABC123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);
        _transactions.Setup(repository => repository.CreateAsync(
                It.IsAny<PaymentTransaction>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var contract = ValidIpn();
        contract.Code = code;
        contract.Content = content ?? string.Empty;
        contract.Description = description ?? string.Empty;
        var service = CreateService();

        var result = await service.ProcessIpnAsync(contract, CancellationToken.None);

        Assert.Equal(PaymentIpnOutcome.Processed, result.Outcome);
        _payments.Verify(repository => repository.GetPaymentByCodeAsync(
            "ABC123", It.IsAny<CancellationToken>()), Times.Once);
    }

    private PaymentTransactionService CreateService() => new(
        _transactions.Object, _payments.Object, _publisher.Object);

    private void SetupNoExistingTransaction() =>
        _transactions.Setup(repository => repository.GetByReferenceCodeAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentTransaction?)null);

    private void SetupPayment(Payment_Service.Domain.Payment payment) =>
        _payments.Setup(repository => repository.GetPaymentByCodeAsync(
                "ABC123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

    private static SepayIpnContract ValidIpn() => new()
    {
        TransferType = "in",
        TransferAmount = 100_000,
        ReferenceCode = "REF-1",
        Code = "ABC123"
    };

    private static SepayIpnContract WithTransferType(SepayIpnContract contract, string value)
    {
        contract.TransferType = value;
        return contract;
    }

    private static Payment_Service.Domain.Payment Payment(PaymentStatus status = PaymentStatus.Pending) => new()
    {
        Uuid = Guid.NewGuid(),
        TripUuid = Guid.NewGuid(),
        RiderUuid = Guid.NewGuid(),
        DriverUuid = Guid.NewGuid(),
        Amount = 100_000,
        TransferCode = "ABC123",
        Status = status
    };
}
