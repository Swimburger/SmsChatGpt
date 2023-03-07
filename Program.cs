using Microsoft.AspNetCore.HttpOverrides;
using OpenAI.GPT3.Extensions;
using Twilio.AspNet.Core;
using SmsChatGpt;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDistributedMemoryCache();

builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

builder.Services.Configure<ForwardedHeadersOptions>(
    options => options.ForwardedHeaders = ForwardedHeaders.All
);

builder.Services
    .AddTwilioClient()
    .AddTwilioRequestValidation();

builder.Services.AddOpenAIService();

var app = builder.Build();

app.UseSession();

app.UseForwardedHeaders();

app.UseTwilioRequestValidation();

app.MapMessageEndpoint();

app.Run();
