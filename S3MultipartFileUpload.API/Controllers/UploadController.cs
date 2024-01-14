using Microsoft.AspNetCore.Mvc;
using S3MultipartFileUpload.API.Services.Interfaces;

namespace S3MultipartFileUpload.API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class UploadController : Controller
{
    private readonly IUploadService _uploadService;

    public UploadController(IUploadService uploadService)
    {
        _uploadService = uploadService;
    }

    [HttpPost]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        // Cria o caminho do arquivo temporário com o nome do arquivo original
        var tempFileName = Path.GetTempFileName();
        var filePath = Path.Combine(Path.GetDirectoryName(tempFileName), file.FileName);

        using (var stream = System.IO.File.Create(filePath))
        {
            await file.CopyToAsync(stream);
        }

        await _uploadService.UploadObjectAsync(filePath);

        // Apaga o arquivo temporário após o upload.
        System.IO.File.Delete(filePath);

        return Ok("Upload completed");
    }
}