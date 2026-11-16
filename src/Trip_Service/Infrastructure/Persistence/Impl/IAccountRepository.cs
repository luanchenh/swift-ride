using Trip_Service.Domain;

namespace Trip_Service.Infrastructure.Persistence.Impl;

public interface IAccountRepository
{
    Task<Account?> FindByPhoneAsync(string phone, CancellationToken cancellationToken = default);
    Task<Account?> FindByUuidAsync(Guid uuid, CancellationToken cancellationToken = default);
    Task<bool> PhoneExistsAsync(string phone, CancellationToken cancellationToken = default);
    Task AddAsync(Account account, CancellationToken cancellationToken = default);
    Task UpdatePasswordAsync(Guid uuid, string hashedPassword, CancellationToken cancellationToken = default);
}
