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
using NetCord.Services;


var token = File.ReadAllText("DISCORD_TOKEN.txt");

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddDiscordGateway(opt =>
    {
        opt.Token = token;
        opt.Intents = GatewayIntents.AllNonPrivileged | GatewayIntents.MessageContent | GatewayIntents.GuildUsers;
    })
    .AddGatewayHandlers(typeof(Program).Assembly)
    .AddApplicationCommands();


var host = builder.Build();


// // Add commands using minimal APIs
// host.AddSlashCommand("ping", "Ping!", (User usr) => $"Pong! {usr.Username}");
// host.AddUserCommand("Username", (User user) => user.Username);
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

    public const string BondedChannel = "5XXX 🔀 5XXX.";
    public const string GoodCheck = "✅";
    public const string BadCheck = "❌";
    public const string XXXXIsGoodForXXXX = "XXXX is good for XXXX";

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


    public static readonly ConcurrentDictionary<ulong, ulong> GuildToMyId = new();

    public static ulong? GetMyBotUserIdInThisGuild(Message msg)
    {
        if (msg.GuildId == null) return null;
        if (GuildToMyId.TryGetValue(msg.GuildId.Value, out ulong value)) return value;

        var cli = msg.GetType();
        var mi = cli.GetMember("_client",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (mi.Length == 0) throw new Exception("Could not get _client member info from Message");
        var client = (mi[0] as System.Reflection.FieldInfo)?.GetValue(msg) as RestClient;
        if (client == null) throw new Exception("Could not get RestClient from Message");
        var currentUserAsync = client.GetCurrentUserAsync();
        currentUserAsync.Wait();
        var myId = currentUserAsync.Result.Id;
        value = myId;
        GuildToMyId[msg.GuildId.Value] = value;

        return value;
    }

    public static RestClient? GetRestClient(Message msg)
    {
        var cli = msg.GetType();
        var mi = cli.GetMember("_client",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (mi.Length == 0) throw new Exception("Could not get _client member info from Message");
        return (mi[0] as System.Reflection.FieldInfo)?.GetValue(msg) as RestClient;
    }

    public static bool IsValidXXXXIsGoodForXXXXMantra(string messageContent)
    {
        var split = messageContent.Split(" is good for ");
        if (split.Length != 2) return false;
        if (string.IsNullOrWhiteSpace(split[0]) || string.IsNullOrWhiteSpace(split[1])) return false;
        if (!int.TryParse(split[0], out _) || !int.TryParse(split[1], out _)) return false;
        return true;
    }
}
// IGuildThreadCreateGatewayHandler

public class MessageUpdateHandler(ILogger<MessageCreateHandler> logger) : IMessageUpdateGatewayHandler
{
    public ValueTask HandleAsync(Message editMsg)
    {
        if (editMsg.Guild == null) return default;

        logger.LogInformation($"{editMsg.Id}");
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
        var usersInThread = thread.GetUsersAsync().ToBlockingEnumerable().ToArray();
        var myId = State.GetMyBotUserIdInThisGuild(message);
        if (myId == null) return default;
        if (usersInThread.All(x => x.Id != myId)) return default;

        if (thread.Name == State.BondedChannel)
        {
            if (State.IsValidArrowTwistMantra(message.Content))
            {
                goto GOOD_MESSAGE;
            }

            goto BAD_MESSAGE;
        }

        if (thread.Name == State.XXXXIsGoodForXXXX)
        {
            if (State.IsValidXXXXIsGoodForXXXXMantra(message.Content))
            {
                goto GOOD_MESSAGE;
            }
        }

        if (thread.Name.ToLowerInvariant() == message.Content.ToLowerInvariant())
        {
            goto GOOD_MESSAGE;
        }

        if (!message.Content.Contains("@"))
        {
            goto BAD_MESSAGE;
        }

        goto IGNORE_MESSAGE;


        GOOD_MESSAGE:
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