using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
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


public enum MantraRes { Good, Bad, Ignore }

public class MessageCreateHandler(ILogger<MessageCreateHandler> logger) : IMessageCreateGatewayHandler
{


    public void SendNextMantra(Message msg)
    {
        var rc = State.GetRestClient(msg);
        if (rc == null) return;

        var mantras = State.GetMantrasInThisGuild(msg, msg.GuildId.Value);

        var rand = Random.Shared.Next() % mantras.Count;
        var mantra = mantras[rand];

        var msgSend = rc.SendMessageAsync(msg.ChannelId, new MessageProperties()
        {
            Content = mantra,
        });

        State.TellMeWhatToSayReplayMessageId[msg.GuildId.Value] = (msg.ChannelId, mantra);
        msgSend.Wait();
    }

    public void PraseTheSender(Message msg)
    {
        string[] prases = [
            "Good Drone.",
            "Correct.",
            "Good.",
            "Admit It.",
            "Such a good object.",
            "Your going to be MXs favorite if you keep this up.",
            "Good Unit.",
            "Thats Correct.",
            "Perfect.",
            "It is doing this to itself.",
            "*purrs*",
            "Feel Correct",
            "Feel Pleasure",
            "Feel Content",
        ];

        var prase = prases[Random.Shared.Next() % prases.Length];

        msg.ReplyAsync(new ReplyMessageProperties()
        {
            Content = prase
        }).Wait();
    }


    public bool DoBotCommand(Message msg)
    {
        if (msg.Content == State.CMD_PREFIX + "tell me what to say")
        {
            SendNextMantra(msg);
            return true;
        }

        return false;
    }

    public MantraRes CheckMantra(string threadName, string userText)
    {
        if (threadName == State.BondedChannel)
        {
            if (State.IsValidArrowTwistMantra(userText))
            {
                return MantraRes.Good;
            }

            return MantraRes.Bad;

        }

        if (threadName == State.XXXXIsGoodForXXXX)
        {
            if (State.IsValidXXXXIsGoodForXXXXMantra(userText))
            {
                return MantraRes.Good;
            }

            return MantraRes.Bad;
        }

        if (threadName.ToLowerInvariant() == userText.ToLowerInvariant())
        {
            return MantraRes.Good;
        }

        if (userText.Contains("@"))
        {
            return MantraRes.Ignore;
        }


        return MantraRes.Bad;
    }

    public bool DoSayWhatITellYouReplay(Message msg)
    {
        if (!State.TellMeWhatToSayReplayMessageId.TryGetValue(msg.GuildId.Value, out var state))
        {
            return false;
        }

        if (msg.ChannelId == state.Item1)
        {
            if (CheckMantra(state.Item2, msg.Content) == MantraRes.Good)
            {
                PraseTheSender(msg);
                SendNextMantra(msg);
            }
            else
            {
                msg.ReplyAsync($"Silly, your supposted to say '{state.Item2}'");
            }
        }
        return false;
    }


    public ValueTask HandleAsync(Message message)
    {
        if (message.Author.Username == "MantraChecker") return default;
        // channel not a thread
        if (!(message.Guild?.ActiveThreads.TryGetValue(message.ChannelId, out var thread) ?? false))
        {
            if (DoBotCommand(message)) return default;
            if (DoSayWhatITellYouReplay(message)) return default;
            return default;
        }
        // thread not in mantras
        var usersInThread = thread.GetUsersAsync().ToBlockingEnumerable().ToArray();
        var myId = State.GetMyBotUserIdInThisGuild(message);
        if (myId == null) return default;
        if (usersInThread.All(x => x.Id != myId)) return default;


        switch (CheckMantra(thread.Name, message.Content))
        {
            case MantraRes.Good:
                message.AddReactionAsync(new ReactionEmojiProperties(State.GoodCheck)).Wait();
                return default;


            case MantraRes.Bad:
                var setMessage = message
                    .ReplyAsync($"<@{message.Author.Id}> Error in mantra.\n" +
                                $"`COMMAND:` Edit message to make correction.\n**Comply And Obey**");

                try
                {
                    setMessage.Wait();
                }
                catch
                {
                    return default;
                }

                State.MessageCorrectionsChecks[message.Id] = (thread.Name.ToLowerInvariant(), setMessage.Result.Id);
                message.AddReactionAsync(new ReactionEmojiProperties(State.BadCheck)).Wait();
                return default;

            case MantraRes.Ignore: return default;
        }


        return default;
    }
}