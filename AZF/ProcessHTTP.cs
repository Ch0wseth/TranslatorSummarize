using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Azure.Storage.Blobs;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Chowseth.TranslateSummarize
{
    public class ProcessHTTP
    {
        private readonly ILogger<ProcessHTTP> _logger;

        public ProcessHTTP(ILogger<ProcessHTTP> logger)
        {
            _logger = logger;
        }

        [Function("ProcessHTTP")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req)
        {
            _logger.LogInformation("C# HTTP trigger function processed a request.");

            // Lire le nom du blob à partir des paramètres de la requête
            string blobName = req.Query["fileName"];
            if (string.IsNullOrEmpty(blobName))
            {
                return new BadRequestObjectResult("Please pass a blobName on the query string.");
            }

            // Connexion au blob storage
            string connectionString = Environment.GetEnvironmentVariable("ConnectionString");
            string containerName = "uploaded-files";
            BlobServiceClient blobServiceClient = new BlobServiceClient(connectionString);
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);

            // Récupérer le blob
            BlobClient blobClient = containerClient.GetBlobClient(blobName);
            if (await blobClient.ExistsAsync())
            {
                var downloadInfo = await blobClient.DownloadAsync();
                using (var reader = new StreamReader(downloadInfo.Value.Content))
                {
                    string content = await reader.ReadToEndAsync();
                    return new OkObjectResult(content);
                }
            }
            else
            {
                return new NotFoundObjectResult("Blob not found.");
            }
        }
    }
}
