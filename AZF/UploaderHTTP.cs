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
    public class UploaderHTTP
    {
        private readonly ILogger<UploaderHTTP> _logger;

        public UploaderHTTP(ILogger<UploaderHTTP> logger)
        {
            _logger = logger;
        }

        [Function("UploaderHTTP")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req)
        {
            _logger.LogInformation("C# HTTP trigger function processed a request.");

            // connexion au blob storage
            string connectionString = Environment.GetEnvironmentVariable("ConnectionString");
            string containerName = "uploaded-files";
            BlobServiceClient blobServiceClient = new BlobServiceClient(connectionString);
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);

            // Lire le corps de la requête en tant que flux binaire
            using (var stream = new MemoryStream())
            {
                await req.Body.CopyToAsync(stream);
                stream.Position = 0;

                // Utiliser un nom de fichier fourni dans les en-têtes ou générer un nom unique
                string fileName = req.Headers["file-name"].ToString();
                if (string.IsNullOrEmpty(fileName))
                {
                    fileName = $"uploaded-file-{Guid.NewGuid()}";
                }

                var blobClient = containerClient.GetBlobClient(fileName);
                await blobClient.UploadAsync(stream, true);

                _logger.LogInformation($"File {fileName} uploaded successfully.");
                return new OkObjectResult($"File {fileName} uploaded successfully.");
            }
        }
    }
}