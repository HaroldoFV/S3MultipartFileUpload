using Amazon.S3.Model;

namespace S3MultipartFileUpload.API.Dtos;

public class MultipartUploadStateDto
{
    public string BucketName { get; set; }
    public string Key { get; set; }
    public string UploadId { get; set; }
    public int NextPartNumber { get; set; }
    public long NextFilePosition { get; set; }
    public List<PartETag> UploadedParts { get; set; }

    public MultipartUploadStateDto()
    {
        UploadedParts = new List<PartETag>();
    }
}