using Microsoft.AspNetCore.HttpOverrides;
using OpenAI.GPT3.Extensions;
using OpenAI.GPT3.Interfaces;
using OpenAI.GPT3.ObjectModels;
using OpenAI.GPT3.ObjectModels.RequestModels;
using System.Text;
using System.Text.Json;
using Twilio.AspNet.Core;
using Twilio.Clients;
using Twilio.Rest.Api.V2010.Account;

namespace SmsChatGpt;

public class Program
{
    private const string PreviousMessagesKey = "PreviousMessages";
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddDistributedMemoryCache();

        builder.Services.AddSession(options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        });

        builder.Services.Configure<ForwardedHeadersOptions>(options => options.ForwardedHeaders = ForwardedHeaders.All);

        builder.Services
            .AddTwilioClient()
            .AddTwilioRequestValidation();

        builder.Services.AddOpenAIService();

        var app = builder.Build();

        app.UseSession();

        app.UseForwardedHeaders();

        app.UseTwilioRequestValidation();

        app.MapPost("/message", async (
            HttpContext context,
            CancellationToken cancellationToken,
            IOpenAIService openAiService,
            ITwilioRestClient twilioClient
        ) =>
        {
            var request = context.Request;
            var response = context.Response;
            var session = context.Session;
            await session.LoadAsync(cancellationToken).ConfigureAwait(false);

            var form = await request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
            var receivedFrom = form["From"].ToString();
            var sentTo = form["To"].ToString();
            var body = form["Body"].ToString().Trim();

            if (body.Equals("reset", StringComparison.OrdinalIgnoreCase))
            {
                RemovePreviousMessages(session);
                var __ = MessageResource.CreateAsync(
                    to: receivedFrom,
                    from: sentTo,
                    body: "Your conversation is now reset.",
                    client: twilioClient
                );
                return Results.Ok();
            }

            var messages = GetPreviousMessages(session);

            messages.Add(ChatMessage.FromUser(body));
            string chatResponse = await GetChatResponse(openAiService, receivedFrom, messages);

            messages.Add(ChatMessage.FromAssistance(chatResponse));
            SetPreviousMessages(session, messages);

            var responseMessages = SplitResponseInChunks(chatResponse);

            var _ = SendResponse(twilioClient, to: receivedFrom, from: sentTo, responseMessages);

            return Results.Ok();
        });

        app.Run();
    }

    private static List<string> SplitResponseInChunks(string chatResponse)
    {
        List<string> messages = new();
        var paragraphs = chatResponse.Split("\n\n");

        StringBuilder messageBuilder = new();
        for (int paragraphIndex = 0; paragraphIndex < paragraphs.Length - 1; paragraphIndex++)
        {
            string currentParagraph = paragraphs[paragraphIndex];
            string nextParagraph = paragraphs[paragraphIndex + 1];
            messageBuilder.Append(currentParagraph);

            // 320 is the recommended message length for maximum deliverability
            // + 2 for "\n\n"
            if (messageBuilder.Length + nextParagraph.Length > 320 + 2)
            {
                messages.Add(messageBuilder.ToString());
                messageBuilder.Clear();
            }
            else
            {
                messageBuilder.Append("\n\n");
            }
        }

        messageBuilder.Append(paragraphs.Last());
        messages.Add(messageBuilder.ToString());

        return messages;
    }

    private static async Task<string> GetChatResponse(
        IOpenAIService openAiService,
        string from,
        List<ChatMessage> messages
    )
    {
        var completionResult = await openAiService.ChatCompletion.CreateCompletion(
            new ChatCompletionCreateRequest
            {
                Messages = messages,
                Model = Models.ChatGpt3_5Turbo,
                User = from
            }
        );

        if (!completionResult.Successful)
        {
            if (completionResult.Error == null) throw new Exception("An unexpected error occurred.");
            var errorMessage = completionResult.Error.Code ?? "";
            if (errorMessage != "") errorMessage += ": ";
            errorMessage += completionResult.Error.Message;
            throw new Exception(errorMessage);
        }

        var chatResponse = completionResult.Choices[0].Message.Content.Trim();
        return chatResponse;
    }


    private static async Task SendResponse(
        ITwilioRestClient twilioClient,
        string to,
        string from,
        List<string> responseMessages
    )
    {
        foreach (var responseMessage in responseMessages)
        {
            await MessageResource.CreateAsync(
                to: to,
                from: from,
                body: responseMessage,
                client: twilioClient
            )
            .ConfigureAwait(false);
            await Task.Delay(1000);
        }
    }

    private static List<ChatMessage> GetPreviousMessages(ISession session)
    {
        var json = session.GetString(PreviousMessagesKey);

        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<ChatMessage>();
        }

        return JsonSerializer.Deserialize<List<ChatMessage>>(json)
                ?? new List<ChatMessage>();
    }

    private static void SetPreviousMessages(ISession session, List<ChatMessage> messages)
    {
        var serializedJson = JsonSerializer.Serialize(messages);
        session.SetString(PreviousMessagesKey, serializedJson);
    }

    private static void RemovePreviousMessages(ISession session)
        => session.Remove(PreviousMessagesKey);
}