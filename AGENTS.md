# SecretManager

- C# / .NET 10, WinUI 3, MVVM (CommunityToolkit.Mvvm), SQLite.
- UI and Windows integration live in `src/SecretManager.App`; domain, encryption,
  persistence, search, import and merge live in `src/SecretManager.Core`.
- Keep all vault names, descriptions, values and attachment metadata encrypted on disk.
  Never add real secrets, databases, exports, credentials or personal screenshots to Git.
- Search indexes exist only in memory and must be destroyed on lock.
- Passwords are never stored; AES-GCM keys are derived with PBKDF2-SHA256 and random salts.
  Changes to crypto, validation, transactions, merge or lock behavior require meaningful tests.
- Maintain WORK_LOG.md, commit completed stages separately, and report actual verification.
- UI code-behind handles view and Windows platform interactions; business rules stay in Core.
- Do not introduce a server or email authentication without a new explicit requirement.
