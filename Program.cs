using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Rest;


var token = File.ReadAllText("DISCORD_TOKEN.txt");

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddDiscordGateway(opt =>
    {
        opt.Token = token;
        opt.Intents = GatewayIntents.AllNonPrivileged | GatewayIntents.MessageContent;
    })
    .AddGatewayHandlers(typeof(Program).Assembly)
    .AddApplicationCommands();


var host = builder.Build();


// // Add commands using minimal APIs
// host.AddSlashCommand("ping", "Ping!", (User usr) => $"Pong! {usr.Username}");
host.AddSlashCommand("info", "Info!",() =>
    $"{State.MessageCorrectionsChecks.Count} uncorrected messages this session\n" +
    $"{State.GoodMantras} good mantras this session");
// host.AddMessageCommand("Length", (RestMessage message) => message.Content.Length.ToString());


// Add commands from modules
// host.AddModules(typeof(Program).Assembly);

await host.RunAsync();


return;

public static class State
{
    // guildid by message id -> what msg should be
    public static readonly ConcurrentDictionary<ulong, (string shouldBe, ulong fixItMsg)> MessageCorrectionsChecks =
        new();

    public static int GoodMantras = 0;

    public const string BondedChannel = "5XXX 🔀 5XXX.";
    public const string GoodCheck = "✅";
    public const string BadCheck = "❌";

    public static bool IsValidArrowTwistMantra(string message)
    {
        var split = message.Split("🔀");

        if (!split[0].EndsWith(' ')) return false;
        if (!split[1].StartsWith(' ')) return false;

        if (!(int.TryParse(split[0], out int a) || int.TryParse(split[1], out int b)))
        {
            return false;
        }

        return true;
    }
}
// IGuildThreadCreateGatewayHandler

public class MessageUpdateHandler(ILogger<MessageCreateHandler> logger) : IMessageUpdateGatewayHandler
{
    public ValueTask HandleAsync(Message editMsg)
    {
        if (editMsg.Guild == null) return default;
        if (!State.MessageCorrectionsChecks.TryGetValue(editMsg.Id, out var shouldbe)) return default;
        if (!(editMsg.Guild?.ActiveThreads.TryGetValue(editMsg.ChannelId, out var thread) ?? false)) return default;


        if (thread.Name == State.BondedChannel)
        {
            if (State.IsValidArrowTwistMantra(editMsg.Content))
            {
                goto GOOD_MESSAGE;
            }

            goto BAD_MESSAGE;
        }


        if (editMsg.Content.ToLowerInvariant() == shouldbe.shouldBe.ToLowerInvariant())
        {
            goto GOOD_MESSAGE;
        }


    BAD_MESSAGE:
        return default;

    GOOD_MESSAGE:
        editMsg.AddReactionAsync(State.GoodCheck).Wait();
        editMsg.DeleteAllReactionsForEmojiAsync(State.BadCheck).Wait();
        if (editMsg.Channel != null) editMsg.Channel.DeleteMessageAsync(shouldbe.fixItMsg).Wait();
        State.MessageCorrectionsChecks.Remove(editMsg.Id, out _);
        return default;
    }
}


public class MessageCreateHandler(ILogger<MessageCreateHandler> logger) : IMessageCreateGatewayHandler
{
    public ValueTask HandleAsync(Message message)
    {
        if (message.Author.Username == "MantraChecker") return default;
        // channel not a thread
        if (!(message.Guild?.ActiveThreads.TryGetValue(message.ChannelId, out var thread) ?? false)) return default;
        // thread not in mantras

        if (thread.Name == State.BondedChannel)
        {
            if (State.IsValidArrowTwistMantra(message.Content))
            {
                goto GOOD_MESSAGE;
            }

            goto BAD_MESSAGE;
        }

        if (thread.Name.ToLowerInvariant() == message.Content.ToLowerInvariant())
        {
            goto GOOD_MESSAGE;
        }

        if (message.Content.Contains("@"))
        {
            goto IGNORE_MESSAGE;
        }

        goto BAD_MESSAGE;


    GOOD_MESSAGE:
        State.GoodMantras += 1;
        message.AddReactionAsync(new ReactionEmojiProperties(State.GoodCheck)).Wait();
        return default;

    BAD_MESSAGE:
        var setMessage = message
            .ReplyAsync($"<@{message.Author.Id}> Error in mantra.\n" +
                        $"`COMMAND:` Edit message to make correction.\n**Comply And Obey**");

        try
        {
            setMessage.Wait();
        }
        catch
        {
            goto IGNORE_MESSAGE;
        }

        message.AddReactionAsync(new ReactionEmojiProperties(State.BadCheck)).Wait();
        State.MessageCorrectionsChecks[message.Id] = (thread.Name.ToLowerInvariant(), setMessage.Result.Id);
        return default;


    IGNORE_MESSAGE:

        return default;
    }

}
