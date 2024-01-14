namespace S3MultipartFileUpload.API.Services.Interfaces;

public interface IUploadService
{
    Task UploadObjectAsync(string filePath);
}