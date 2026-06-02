using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AIWriterPublisher.Api.Agents.ArtDirector
{
    public class ArtDirectorAgent
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        public ArtDirectorAgent(IConfiguration configuration)
        {
            _httpClient = new HttpClient();
            _apiKey = configuration["Gemini:ApiKey"] 
                ?? throw new InvalidOperationException("API-ключ Gemini не найден!");
        }

        public async Task<string> BrainstormConceptsAsync(string genre, string description)
        {
            // Стучимся напрямую в стабильный gemini-2.5-flash для генерации текста
            // Было:
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={_apiKey}";

            // Стало (переходим на 1.5 Pro, она еще и умнее идеи накидает):
            // string url = $"https://generativelanguage.googleapis.com/v1/models/gemini-1.5-flash:generateContent?key={_apiKey}";

            // string url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-pro:generateContent?key={_apiKey}";

            var systemPrompt = @"Вы — профессиональный арт-директор в книжном издательстве. 
            Ваша задача — придумать ровно 3 альтернативные концепции для обложки книги на основе её жанра и синопсиса.
            Вы ДОЛЖНЫ вернуть ответ СТРОГО в формате JSON-массива объектов. Никакого лишнего текста, никаких markdown-разметок ```json ... ```, только чистый массив.

            Формат JSON, который от вас ожидается:
            [
            {
                ""title"": ""Название концепции 1"",
                ""description"": ""Краткое художественное описание идеи для автора"",
                ""visualElements"": ""Детальное описание визуальных элементов на русском языке (композиция, свет, детали) для художника-иллюстратора""
            },
            ...
            ]";

            var userContent = $"Жанр: {genre}\nСинопсис: {description}\nСгенерируй 3 концепции в формате JSON:";

            var requestBody = new
            {
                contents = new[]
                {
                    new { parts = new[] { new { text = $"{systemPrompt}\n\n{userContent}" } } }
                },
                // Принудительно просим Gemini вернуть именно JSON-структуру
                generationConfig = new
                {
                    responseMimeType = "application/json"
                }
            };

            var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, jsonContent);

            if (!response.IsSuccessStatusCode)
            {
                string errorLog = await response.Content.ReadAsStringAsync();
                throw new Exception($"ArtDirector API error {response.StatusCode}: {errorLog}");
            }

            string responseJson = await response.Content.ReadAsStringAsync();
            var node = JsonNode.Parse(responseJson);
            string cleanJson = node?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString() ?? "[]";

            return cleanJson.Trim();
        }
    }
}