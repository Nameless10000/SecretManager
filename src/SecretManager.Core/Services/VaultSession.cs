using System.Security.Cryptography;
using System.Text.Json;
using SecretManager.Core.Models;
using SecretManager.Core.Search;
using SecretManager.Core.Security;
using SecretManager.Core.Storage;

namespace SecretManager.Core.Services;

public sealed class VaultSession(VaultRepository repository) : IDisposable
{
    private readonly object sync = new();
    private byte[]? key;
    private byte[]? salt;
    private VaultDocument? document;
    private TitleSearchIndex? index;
    public bool IsUnlocked { get { lock (sync) return key is not null; } }
    public bool Exists => repository.Read() is not null;
    public VaultDocument Document { get { lock (sync) return document ?? throw new InvalidOperationException("Хранилище заблокировано."); } }

    public void Create(string password)
    {
        lock (sync)
        {
            if (repository.Read() is not null) throw new InvalidOperationException("Хранилище уже существует.");
            var protectedValue = VaultCrypto.Protect(new VaultDocument(), password, VaultCrypto.LocalContext);
            try
            {
                repository.Write(protectedValue.Envelope);
                Open(new VaultDocument(), protectedValue.Key, Convert.FromBase64String(protectedValue.Envelope.Salt));
            }
            catch { CryptographicOperations.ZeroMemory(protectedValue.Key); throw; }
        }
    }

    public void Unlock(string password)
    {
        lock (sync)
        {
            var envelope = repository.Read() ?? throw new InvalidOperationException("Сначала создайте хранилище.");
            var value = VaultCrypto.Unprotect(envelope, password, VaultCrypto.LocalContext);
            Open(value.Document, value.Key, Convert.FromBase64String(envelope.Salt));
        }
    }

    private void Open(VaultDocument value, byte[] newKey, byte[] newSalt)
    {
        TitleSearchIndex newIndex;
        try { newIndex = new TitleSearchIndex(value.Associations); }
        catch { CryptographicOperations.ZeroMemory(newKey); throw; }
        Lock();
        document = value;
        key = newKey;
        salt = newSalt;
        index = newIndex;
    }

    public IReadOnlyList<SecretAssociation> Search(string query)
    {
        lock (sync)
        {
            var ids = (index ?? throw new InvalidOperationException("Хранилище заблокировано.")).Search(query);
            return Document.Associations.Where(a => ids.Contains(a.Id)).OrderBy(a => a.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
    }

    public void Save(VaultDocument next)
    {
        lock (sync)
        {
            _ = Document;
            VaultValidation.Validate(next);
            using var candidate = new DisposableIndexHolder(new TitleSearchIndex(next.Associations));
            var envelope = VaultCrypto.Encrypt(next, key!, salt!, VaultCrypto.LocalContext);
            repository.Write(envelope);
            index?.Dispose();
            index = candidate.Detach();
            document = next;
        }
    }

    public void ChangePassword(string currentPassword, string newPassword)
    {
        lock (sync)
        {
            _ = Document;
            var current = repository.Read()!;
            var verified = VaultCrypto.Unprotect(current, currentPassword, VaultCrypto.LocalContext);
            CryptographicOperations.ZeroMemory(verified.Key);
            var updated = VaultCrypto.Protect(Document, newPassword, VaultCrypto.LocalContext);
            try
            {
                repository.Write(updated.Envelope);
                CryptographicOperations.ZeroMemory(key!);
                key = updated.Key;
                salt = Convert.FromBase64String(updated.Envelope.Salt);
            }
            catch { CryptographicOperations.ZeroMemory(updated.Key); throw; }
        }
    }

    public string Export(string password)
    {
        lock (sync)
        {
            var value = VaultCrypto.Protect(Document, password, VaultCrypto.ExportContext);
            try { return ExportPackage.Create(value.Envelope); }
            finally { CryptographicOperations.ZeroMemory(value.Key); }
        }
    }

    public static VaultDocument ReadExport(string json, string password)
    {
        var value = VaultCrypto.Unprotect(ExportPackage.Read(json), password, VaultCrypto.ExportContext);
        try { return value.Document; }
        finally { CryptographicOperations.ZeroMemory(value.Key); }
    }

    public void Lock()
    {
        lock (sync)
        {
            index?.Dispose(); index = null;
            document = null;
            if (key is not null) CryptographicOperations.ZeroMemory(key);
            key = null; salt = null;
        }
    }

    public void Dispose() => Lock();

    private sealed class DisposableIndexHolder(TitleSearchIndex value) : IDisposable
    {
        private TitleSearchIndex? held = value;
        public TitleSearchIndex Detach() { var result = held!; held = null; return result; }
        public void Dispose() => held?.Dispose();
    }
}
