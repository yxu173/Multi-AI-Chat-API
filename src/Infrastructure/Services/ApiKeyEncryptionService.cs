using Microsoft.AspNetCore.DataProtection;

namespace Infrastructure.Services;

public class ApiKeyEncryptionService
{
    private readonly IDataProtector _protector;

    public ApiKeyEncryptionService(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("ApiKeyProtector");
    }

    public string Encrypt(string plainText) => _protector.Protect(plainText);
    public string Decrypt(string cipherText) => _protector.Unprotect(cipherText);
} 