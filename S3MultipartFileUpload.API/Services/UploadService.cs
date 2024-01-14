using System.Text.Json;
using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.S3;
using Amazon.S3.Model;
using S3MultipartFileUpload.API.Dtos;
using S3MultipartFileUpload.API.Services.Interfaces;

namespace S3MultipartFileUpload.API.Services;

public class UploadService : IUploadService
{
    // Variáveis privadas que contém informações sobre o bucket, o nome da chave e a região do bucket
    private readonly string bucketName;
    private string keyName;
    private readonly RegionEndpoint bucketRegion;

    // Criação dos clientes para o S3 e para o DynamoDB
    private static IAmazonS3 s3Client;
    private static AmazonDynamoDBClient dynamoDBClient;

    public UploadService(IConfiguration configuration)
    {
        bucketName = configuration.GetValue<string>("S3Settings:BucketName") ?? throw new InvalidOperationException();

        var bucketRegionName = configuration.GetValue<string>("S3Settings:BucketRegion") ??
                               throw new InvalidOperationException();
        bucketRegion = RegionEndpoint.GetBySystemName(bucketRegionName);
        s3Client = new AmazonS3Client(bucketRegion);
        dynamoDBClient = new AmazonDynamoDBClient(bucketRegion);
    }

    // Função de Upload do objeto para o S3
    public async Task UploadObjectAsync(string filePath)
    {
        // Aqui o keyName é definido com o nome do arquivo
        keyName = Path.GetFileName(filePath);

        // Inicializando a lista que contém as partes do arquivo que estão sendo carregadas
        List<PartETag> uploadParts = new List<PartETag>();

        // Carregando o status do upload
        MultipartUploadStateDto uploadState = await LoadUploadState();

        // Se o status do upload não for encontrado, começamos um novo upload
        if (uploadState == null)
        {
            InitiateMultipartUploadRequest initRequest = new InitiateMultipartUploadRequest
            {
                BucketName = bucketName, Key = keyName
            };

            // Iniciando o novo upload
            InitiateMultipartUploadResponse initResponse = await s3Client.InitiateMultipartUploadAsync(initRequest);

            // Definindo o estado inicial do upload
            uploadState = new MultipartUploadStateDto
            {
                BucketName = bucketName,
                Key = keyName,
                UploadId = initResponse.UploadId,
                NextPartNumber = 1,
                NextFilePosition = 0
            };
        }
        else
        {
            // Se o status do upload for encontrado, resume o upload
            uploadParts = uploadState.UploadedParts;
        }

        // Definindo o tamanho das partes do arquivo (5MB)
        long partSize = 5 * (long) Math.Pow(2, 20); // 5 MB.

        // Acompanhando o progresso do upload
        long currentPosition = uploadState.NextFilePosition;
        long fileLength = new FileInfo(filePath).Length;

        // Enviando as partes para o S3
        while (currentPosition < fileLength)
        {
            int partNum = uploadState.NextPartNumber++;

            UploadPartRequest uploadRequest = new UploadPartRequest
            {
                BucketName = bucketName,
                Key = keyName,
                UploadId = uploadState.UploadId,
                PartNumber = partNum,
                PartSize = partSize,
                FilePosition = currentPosition,
                FilePath = filePath
            };

            uploadRequest.StreamTransferProgress += (sender, args) =>
            {
                Console.WriteLine($"Transferred: {args.TransferredBytes}/{args.TotalBytes}");
            };

            // Atualizando o status do upload
            UploadPartResponse uploadResponse = await s3Client.UploadPartAsync(uploadRequest);

            // Avançando para a próxima parte do arquivo
            currentPosition += partSize;
            uploadParts.Add(new PartETag(partNum, uploadResponse.ETag));

            // Atualizando o status e salvando no DynamoDB  
            uploadState.NextFilePosition = currentPosition;
            uploadState.UploadedParts = uploadParts;
            await SaveUploadState(uploadState);
        }

        // Uma vez que todas as partes foram enviadas, completando o upload
        CompleteMultipartUploadRequest compRequest = new CompleteMultipartUploadRequest
        {
            BucketName = bucketName, Key = keyName, UploadId = uploadState.UploadId, PartETags = uploadParts
        };

        // Chamando o método para completar o upload no S3
        await s3Client.CompleteMultipartUploadAsync(compRequest);

        // Imprime a mensagem de que o upload foi concluído
        Console.WriteLine("Upload completed");

        // Chamando o método para deletar o estado de upload do banco DynamoDB
        await DeleteUploadState();
    }

    // Método para carregar o estado do upload do DynamoDB
    private async Task<MultipartUploadStateDto> LoadUploadState()
    {
        // especifica a tabela e chave para recuperar o estado do upload
        GetItemResponse response = await dynamoDBClient.GetItemAsync("UploadState",
            new Dictionary<string, AttributeValue> {{"KeyName", new AttributeValue {S = keyName}}});

        // Verifica se recebeu alguma coisa
        if (response.Item == null || response.Item.Count == 0)
            return null;

        // Se o estado do upload for encontrado, ele é retornado
        return new MultipartUploadStateDto
        {
            BucketName = response.Item["BucketName"].S,
            Key = response.Item["Key"].S,
            UploadId = response.Item["UploadId"].S,
            NextPartNumber = int.Parse(response.Item["NextPartNumber"].N),
            NextFilePosition = long.Parse(response.Item["NextFilePosition"].N),
            UploadedParts = JsonSerializer.Deserialize<List<PartETag>>(response.Item["UploadedParts"].S)
        };
    }

    // Esse método salva o estado atual do upload no DynamoDB
    private async Task SaveUploadState(MultipartUploadStateDto uploadState)
    {
        // Envia um PutItemRequest para o DynamoDB para salvar o atual estado do upload
        await dynamoDBClient.PutItemAsync("UploadState",
            new Dictionary<string, AttributeValue>
            {
                {"BucketName", new AttributeValue {S = uploadState.BucketName}},
                {"Key", new AttributeValue {S = uploadState.Key}},
                {"KeyName", new AttributeValue {S = keyName}},
                {"UploadId", new AttributeValue {S = uploadState.UploadId}},
                {"NextPartNumber", new AttributeValue {N = uploadState.NextPartNumber.ToString()}},
                {"NextFilePosition", new AttributeValue {N = uploadState.NextFilePosition.ToString()}},
                {"UploadedParts", new AttributeValue {S = JsonSerializer.Serialize(uploadState.UploadedParts)}}
            });
    }

    // Este método exclui o estado do upload da tabela DynamoDB
    private async Task DeleteUploadState()
    {
        // Chamada para o DynamoDB para deletar a entrada do estado de upload
        await dynamoDBClient.DeleteItemAsync("UploadState",
            new Dictionary<string, AttributeValue> {{"KeyName", new AttributeValue {S = keyName}}});
    }
}