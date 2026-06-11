using Microsoft.SemanticKernel;
using AIWriterPublisher.Api.Services;
using AIWriterPublisher.Api.Agents.ArtDirector;
using AIWriterPublisher.Api.Agents.LoraAgent;
using AIWriterPublisher.Api.Agents.LoraAgent.Interface;
using AIWriterPublisher.Api.Agents.PromptEngineer;
using AIWriterPublisher.Api.Agents.ArtArchitector;
using System.Net;
using System.Net.Http;

var builder = WebApplication.CreateBuilder(args);

// 1. Стандартные сервисы Web API
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// 2. Извлекаем API-ключ Gemini из User Secrets
string geminiApiKey = builder.Configuration["Gemini:ApiKey"] 
    ?? throw new InvalidOperationException("API-ключ Gemini не найден в конфигурации!");

// 3. Универсальное подключение к Gemini через OpenAI-протокол
builder.Services.AddTransient<Kernel>(sp =>
{
    // ВАЖНО: Создаем обработчик, который знает про SotaConnect
    // Замени 7890 на тот порт, который указан в настройках твоего VPN
    // Настраиваем HttpClientHandler с явным указанием локального прокси
    var proxyHandler = new HttpClientHandler
    {
        Proxy = new WebProxy("http://127.0.0.1:7890") // Твой порт туннеля
        {
            BypassProxyOnLocal = true
        },
        UseProxy = true
    };
    // Регистрируем HttpClient для работы с Hugging Face
    builder.Services.AddHttpClient("HuggingFaceClient", client =>
    {
        client.BaseAddress = new Uri("https://api-inference.huggingface.co/");
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {builder.Configuration["HF_API_KEY"]}");
        client.Timeout = TimeSpan.FromSeconds(60); // Генерация картинок/тяжелых ответов требует времени
    })
    .ConfigurePrimaryHttpMessageHandler(() => proxyHandler);
    // Создаем клиент с этим обработчиком
    var httpClient = new HttpClient(proxyHandler);

    var kernelBuilder = Kernel.CreateBuilder();
    
    // Эндпоинт v1beta/openai требует авторизации либо через заголовок Bearer, либо через api-key в URL.
    // Самый надежный способ для шлюза Google — передавать ключ в заголовке Authorization
    httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {geminiApiKey}");

    kernelBuilder.AddOpenAIChatCompletion(
        modelId: "gemini-2.5-flash",
        apiKey: geminiApiKey, // Оставляем для валидации коннектора
        endpoint: new Uri("https://generativelanguage.googleapis.com/v1beta/openai/"),
        httpClient: httpClient
    );

    return kernelBuilder.Build();
});

//Регистрируем наших ИИ-Агентов
builder.Services.AddTransient<ArtDirectorAgent>();
builder.Services.AddScoped<LoraPredictorAgent>();
builder.Services.AddTransient<PromptEngineerAgent>(); 
builder.Services.AddTransient<ArtArchitectorAgent>();
// Регистрируем сервис оркестрации Лор, который будет работать с ИИ-агентом
builder.Services.AddScoped<LoraOrchestrationService>();
builder.Services.AddScoped<ILoraOrchestrationService, LoraOrchestrationService>();
// Регистрируем генераторы картинок
builder.Services.AddTransient<RealImageGenerator>();
builder.Services.AddTransient<ComfyUiImageGenerator>();
builder.Services.AddHttpContextAccessor(); // Для доступа к строке запроса внутри генератора картинок

builder.Services.AddTransient<IImageGenerator>(sp =>
{
    var httpContext = sp.GetRequiredService<IHttpContextAccessor>().HttpContext;

    // Безопасно достаем значение из строки запроса
    var provider = httpContext?.Request.Query["provider"].ToString();
    bool useComfyUI = provider == "local";

    if (useComfyUI)
    {
        return sp.GetRequiredService<ComfyUiImageGenerator>();
    }
    
    return sp.GetRequiredService<RealImageGenerator>();
});

builder.Services.AddHttpClient();

// 1. Регистрируем политику CORS, которая пустит наш фронтенд
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.WithOrigins("http://localhost:5174", "http://127.0.0.1:5174")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseDefaultFiles(); // <-- Добавляем: ищет index.html по умолчанию
app.UseStaticFiles();  // <-- Добавляем: разрешает раздачу HTML, CSS, JS

app.UseAuthorization();
app.UseCors("AllowAll");

app.MapControllers(); 

app.Run();