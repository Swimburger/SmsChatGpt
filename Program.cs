using OpenAI.GPT3.Extensions;
using OpenAI.GPT3.Interfaces;
using OpenAI.GPT3.ObjectModels;
using OpenAI.GPT3.ObjectModels.RequestModels;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenAIService();

var app = builder.Build();

app.MapPost("/message", async (
    IOpenAIService openAiService,
    HttpRequest request,
    CancellationToken cancellationToken
) =>
{
    var form = await request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
    var body = form["Body"].ToString();
    var completionResult = await openAiService.Completions.CreateCompletion(
        new CompletionCreateRequest()
        {
            Prompt = body,
            Model = Models.TextDavinciV3,
            //A number between 0 and 1 that determines how many creative risks the engine takes when generating text.
            Temperature = 0.7f,
            // Maximum completion length
            MaxTokens = 3000,
            // # between -2.0 and 2.0. The higher this value, the bigger the effort the model will make in not repeating itself.
            FrequencyPenalty = 0.7f
        }
    ).ConfigureAwait(false);

    if (!completionResult.Successful)
    {
        if (completionResult.Error == null) throw new Exception("An unexpected error occurred.");
        var errorMessage = completionResult.Error.Code ?? "";
        if (errorMessage != "") errorMessage += ": ";
        errorMessage += completionResult.Error.Message;
        throw new Exception(errorMessage);
    }

    return Results.Text(completionResult.Choices[0].Text.Trim());
});

app.Run();