using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Azure.Storage.Blobs;
using Azure;
using Azure.AI.TextAnalytics;
using System.Text;
using PdfSharpCore.Pdf.IO;

namespace Chowseth.TranslateSummarize
{
    public class ProcessHTTP
    {
        private readonly ILogger<ProcessHTTP> _logger;
        private static readonly AzureKeyCredential credentials = new AzureKeyCredential(
            Environment.GetEnvironmentVariable("TEXT_ANALYTICS_KEY")
                ?? throw new ArgumentNullException("TEXT_ANALYTICS_KEY is not set in environment variables.")
        );
        private static readonly Uri endpoint = new Uri(
            Environment.GetEnvironmentVariable("TEXT_ANALYTICS_ENDPOINT")
                ?? throw new ArgumentNullException("TEXT_ANALYTICS_ENDPOINT is not set in environment variables.")
        );

        public ProcessHTTP(ILogger<ProcessHTTP> logger)
        {
            _logger = logger;
        }

        [Function("ProcessHTTP")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req)
        {
            _logger.LogInformation("Azure Function ProcessHTTP triggered.");

            // Lire le nom du blob à partir des paramètres de la requête
            string blobName = req.Query["fileName"];
            if (string.IsNullOrWhiteSpace(blobName))
            {
                return new BadRequestObjectResult("Please provide a valid fileName parameter.");
            }

            // Lire la chaîne de connexion et vérifier la nullabilité
            string connectionString = Environment.GetEnvironmentVariable("ConnectionString")
                ?? throw new ArgumentNullException("ConnectionString is not set in environment variables.");
            string containerName = "uploaded-files";

            // Connexion au Blob Storage
            BlobServiceClient blobServiceClient = new BlobServiceClient(connectionString);
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);
            BlobClient blobClient = containerClient.GetBlobClient(blobName);

            if (!await blobClient.ExistsAsync())
            {
                return new NotFoundObjectResult($"Blob '{blobName}' not found in container '{containerName}'.");
            }

            // Télécharger et extraire le texte du PDF
            string pdfText;
            using (var stream = new MemoryStream())
            {
                await blobClient.DownloadToAsync(stream);
                stream.Position = 0; // Réinitialiser le flux pour la lecture
                pdfText = ExtractTextFromPdf(stream);
            }

            if (string.IsNullOrWhiteSpace(pdfText))
            {
                return new BadRequestObjectResult("Failed to extract text from the PDF.");
            }

            // Appeler Azure Text Analytics pour résumer le texte
            string summary = await SummarizeText(pdfText);

            // Retourner le résumé
            return new OkObjectResult(new { FileName = blobName, Summary = summary });
        }

        private string ExtractTextFromPdf(Stream pdfStream)
        {
            try
            {
                // Extraire le texte avec PdfSharpCore
                StringBuilder text = new StringBuilder();
                using (var pdfDocument = PdfReader.Open(pdfStream, PdfDocumentOpenMode.ReadOnly))
                {
                    foreach (var page in pdfDocument.Pages)
                    {
                        text.Append(page.Contents.ToString());
                    }
                }
                return text.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error extracting text from PDF: {ex.Message}");
                throw;
            }
        }

        private async Task<string> SummarizeText(string document)
        {
            try
            {
                var client = new TextAnalyticsClient(endpoint, credentials);

                // Préparer les actions pour l'analyse
                var batchInput = new List<string> { document };
                var actions = new TextAnalyticsActions
                {
                    ExtractSummaryActions = new List<ExtractSummaryAction> { new ExtractSummaryAction() }
                };

                // Lancer l'analyse
                AnalyzeActionsOperation operation = await client.StartAnalyzeActionsAsync(batchInput, actions);
                await operation.WaitForCompletionAsync();

                // Construire le résumé
                StringBuilder summaryBuilder = new StringBuilder();
                await foreach (var documentsInPage in operation.Value)
                {
                    foreach (var summaryActionResults in documentsInPage.ExtractSummaryResults)
                    {
                        foreach (var documentResults in summaryActionResults.DocumentsResults)
                        {
                            foreach (var sentence in documentResults.Sentences)
                            {
                                summaryBuilder.AppendLine(sentence.Text);
                            }
                        }
                    }
                }

                return summaryBuilder.ToString();
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError($"Azure API error: {ex.Message}");
                return $"Azure API error: {ex.ErrorCode}";
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during text summarization: {ex.Message}");
                return "Error summarizing text.";
            }
        }
    }
}
