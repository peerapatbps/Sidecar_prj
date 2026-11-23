using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;

namespace Uroboros.MiniUploader;

internal static class DriveSyncCli
{
    public readonly record struct UploadResult(string RemoteFileName, bool UpdatedExisting);

    public static async Task<UploadResult> UploadFileToSpecificFolderAsNameAsync(
        DriveService service,
        string filePath,
        string folderId,
        string remoteFileName,
        CancellationToken ct = default)
    {
        if (service is null) throw new ArgumentNullException(nameof(service));
        if (!File.Exists(filePath)) throw new FileNotFoundException("Local file not found", filePath);
        if (string.IsNullOrWhiteSpace(remoteFileName)) throw new ArgumentException("remoteFileName is required", nameof(remoteFileName));

        var searchRequest = service.Files.List();
        searchRequest.Q = $"name = '{EscapeDriveQueryLiteral(remoteFileName)}' and '{folderId}' in parents and trashed = false";
        searchRequest.Fields = "files(id, name)";
        searchRequest.PageSize = 1;
        searchRequest.SupportsAllDrives = true;
        searchRequest.IncludeItemsFromAllDrives = true;

        var searchResult = await searchRequest.ExecuteAsync(ct).ConfigureAwait(false);
        string? existingFileId = (searchResult.Files != null && searchResult.Files.Count > 0)
            ? searchResult.Files[0].Id
            : null;

        await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        if (!string.IsNullOrWhiteSpace(existingFileId))
        {
            var updateMetadata = new Google.Apis.Drive.v3.Data.File { Name = remoteFileName };
            var updateRequest = service.Files.Update(updateMetadata, existingFileId, fileStream, "application/octet-stream");
            updateRequest.Fields = "id";
            updateRequest.SupportsAllDrives = true;
            var updateProgress = await updateRequest.UploadAsync(ct).ConfigureAwait(false);
            if (updateProgress.Status != Google.Apis.Upload.UploadStatus.Completed)
                throw new InvalidOperationException($"Drive update failed: {updateProgress.Exception?.Message ?? updateProgress.Status.ToString()}");
            return new UploadResult(remoteFileName, UpdatedExisting: true);
        }

        var createMetadata = new Google.Apis.Drive.v3.Data.File
        {
            Name = remoteFileName,
            Parents = [folderId]
        };

        var createRequest = service.Files.Create(createMetadata, fileStream, "application/octet-stream");
        createRequest.Fields = "id";
        createRequest.SupportsAllDrives = true;
        var createProgress = await createRequest.UploadAsync(ct).ConfigureAwait(false);
        if (createProgress.Status != Google.Apis.Upload.UploadStatus.Completed)
            throw new InvalidOperationException($"Drive create failed: {createProgress.Exception?.Message ?? createProgress.Status.ToString()}");
        return new UploadResult(remoteFileName, UpdatedExisting: false);
    }

    private static string EscapeDriveQueryLiteral(string s) => s.Replace("'", "''", StringComparison.Ordinal);
}

internal static class GoogleDriveHelper
{
    private static readonly string[] Scopes = [DriveService.Scope.Drive];
    private const string ApplicationName = "UroborosMiniUploader";
    private static DriveService? _service;

    public static DriveService GetDriveServiceOrThrow(string startupPath)
    {
        if (_service != null) return _service;

        var credentialPath = Path.Combine(startupPath, "credentials.json");
        if (!File.Exists(credentialPath))
            throw new FileNotFoundException("Missing credentials.json", credentialPath);

        UserCredential credential;
        using (var stream = new FileStream(credentialPath, FileMode.Open, FileAccess.Read))
        {
            var tokenDir = Path.Combine(startupPath, "token_store");
            credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                GoogleClientSecrets.FromStream(stream).Secrets,
                Scopes,
                "user",
                CancellationToken.None,
                new FileDataStore(tokenDir, true)).GetAwaiter().GetResult();
        }

        _service = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = ApplicationName
        });

        return _service;
    }
}
