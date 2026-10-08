namespace GanttDiagram.Application.Abstractions;

/// <summary>Шифрование токена Jira для хранения в БД.</summary>
public interface ITokenProtector
{
    string Protect(string token);
    string Unprotect(string encryptedToken);
}
