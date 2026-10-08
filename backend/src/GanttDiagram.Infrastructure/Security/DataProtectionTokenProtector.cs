using System.Security.Cryptography;
using GanttDiagram.Application.Abstractions;
using GanttDiagram.Application.Jira;
using Microsoft.AspNetCore.DataProtection;

namespace GanttDiagram.Infrastructure.Security;

public class DataProtectionTokenProtector(IDataProtectionProvider provider) : ITokenProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("GanttDiagram.JiraToken.v1");

    public string Protect(string token) => _protector.Protect(token);

    public string Unprotect(string encryptedToken)
    {
        try
        {
            return _protector.Unprotect(encryptedToken);
        }
        catch (CryptographicException ex)
        {
            // Ключи шифрования потеряны или сменились — токен нужно ввести заново.
            throw new JiraException("Не удалось расшифровать токен Jira. Укажите токен в настройках подключения заново.", inner: ex);
        }
    }
}
