namespace SecretManager.Core.Security;

public sealed record EncryptedEnvelope(int Version, int Iterations, string Salt, string Nonce, string Tag, string Ciphertext);
