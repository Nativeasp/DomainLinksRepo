using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace DomainLinksDesktop;

internal sealed class ChatBackupService(HttpClient httpClient)
{
    private const string EncryptionType = "aes-gcm-identity-v1";
    private const string CompressionType = "gzip";
    private const int KeyVersion = 1;

    private readonly HttpClient _httpClient = httpClient;

    public ChatBackupUserIdentity ResolveCurrentUser()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var machineName = Environment.MachineName;
        var userDomain = Environment.UserDomainName;
        var windowsUserName = identity.Name ?? Environment.UserName;
        var windowsSid = identity.User?.Value;
        var displayName = Environment.UserName;
        var useSid = !string.Equals(userDomain, machineName, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(windowsSid);

        return new ChatBackupUserIdentity
        {
            WindowsUserName = windowsUserName,
            WindowsSid = windowsSid,
            DisplayName = displayName,
            IdentityKeyKind = useSid ? "sid" : "username",
            IdentityKeyValue = useSid ? windowsSid! : windowsUserName,
        };
    }

    public async Task<ChatBackupAvailabilityResponse> CheckAvailabilityAsync(ChatBackupUserIdentity user)
    {
        using var response = await _httpClient.PostAsJsonAsync("/chat-backups/check", user);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ChatBackupAvailabilityResponse>()
            ?? new ChatBackupAvailabilityResponse();
    }

    public async Task<IReadOnlyList<LocalChatFileSnapshot>> RestoreAsync(ChatBackupUserIdentity user)
    {
        using var response = await _httpClient.PostAsJsonAsync("/chat-backups/restore", user);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ChatBackupRestoreResponse>();
        if (payload?.Files is null || payload.Files.Count == 0)
        {
            return [];
        }

        var files = new List<LocalChatFileSnapshot>();
        foreach (var file in payload.Files)
        {
            var encryptedBytes = Convert.FromBase64String(file.PayloadBase64);
            var jsonBytes = DecryptAndDecompress(user, encryptedBytes);
            var contentHash = SHA256.HashData(jsonBytes);
            var expectedHash = Convert.FromBase64String(file.ContentHashBase64);
            if (!CryptographicOperations.FixedTimeEquals(contentHash, expectedHash))
            {
                throw new InvalidOperationException($"Backup integrity check failed for '{file.FileName}'.");
            }

            files.Add(
                new LocalChatFileSnapshot
                {
                    RootCollectionCode = file.RootCollectionCode,
                    RootDisplayName = file.RootDisplayName,
                    FileName = file.FileName,
                    JsonContent = Encoding.UTF8.GetString(jsonBytes),
                    ClientModifiedUtc = file.ClientModifiedUtc,
                }
            );
        }

        return files;
    }

    public async Task BackupAsync(ChatBackupUserIdentity user, LocalChatFileSnapshot snapshot)
    {
        await SendBackupAsync(user, snapshot, isDeleted: false);
    }

    public async Task DeleteBackupAsync(ChatBackupUserIdentity user, string rootCollectionCode, string rootDisplayName)
    {
        var tombstone = new LocalChatFileSnapshot
        {
            RootCollectionCode = rootCollectionCode,
            RootDisplayName = rootDisplayName,
            FileName = $"{rootDisplayName}--{rootCollectionCode}.json",
            JsonContent = "{}",
            ClientModifiedUtc = DateTimeOffset.UtcNow,
        };
        await SendBackupAsync(user, tombstone, isDeleted: true);
    }

    private async Task SendBackupAsync(ChatBackupUserIdentity user, LocalChatFileSnapshot snapshot, bool isDeleted)
    {
        var jsonBytes = Encoding.UTF8.GetBytes(snapshot.JsonContent);
        var encryptedBytes = CompressAndEncrypt(user, jsonBytes);
        var request = new
        {
            user.WindowsUserName,
            user.WindowsSid,
            user.DisplayName,
            user.IdentityKeyKind,
            user.IdentityKeyValue,
            snapshot.RootCollectionCode,
            snapshot.RootDisplayName,
            snapshot.FileName,
            PayloadBase64 = Convert.ToBase64String(encryptedBytes),
            ContentHashBase64 = Convert.ToBase64String(SHA256.HashData(jsonBytes)),
            CompressionType,
            EncryptionType,
            KeyVersion,
            snapshot.ClientModifiedUtc,
            ClientMachineName = Environment.MachineName,
            AppVersion = typeof(ChatBackupService).Assembly.GetName().Version?.ToString(),
            IsDeleted = isDeleted,
        };

        using var response = await _httpClient.PutAsJsonAsync("/chat-backups/file", request);
        response.EnsureSuccessStatusCode();
    }

    private static byte[] CompressAndEncrypt(ChatBackupUserIdentity user, byte[] content)
    {
        var compressed = Compress(content);
        var key = DeriveKey(user);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipherText = new byte[compressed.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, compressed, cipherText, tag);

        var payload = new byte[nonce.Length + tag.Length + cipherText.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipherText, 0, payload, nonce.Length + tag.Length, cipherText.Length);
        return payload;
    }

    private static byte[] DecryptAndDecompress(ChatBackupUserIdentity user, byte[] payload)
    {
        if (payload.Length < 28)
        {
            throw new InvalidOperationException("Backup payload is too short.");
        }

        var nonce = payload[..12];
        var tag = payload[12..28];
        var cipherText = payload[28..];
        var plainBytes = new byte[cipherText.Length];

        using var aes = new AesGcm(DeriveKey(user), 16);
        aes.Decrypt(nonce, cipherText, tag, plainBytes);
        return Decompress(plainBytes);
    }

    private static byte[] DeriveKey(ChatBackupUserIdentity user)
    {
        var material = $"{user.IdentityKeyKind}|{user.IdentityKeyValue}|DomainLinksChatBackup|v1";
        return SHA256.HashData(Encoding.UTF8.GetBytes(material));
    }

    private static byte[] Compress(byte[] input)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(input, 0, input.Length);
        }

        return output.ToArray();
    }

    private static byte[] Decompress(byte[] input)
    {
        using var source = new MemoryStream(input);
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }
}
