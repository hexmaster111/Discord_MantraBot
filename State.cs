using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NetCord.Gateway;
using NetCord.Rest;

public class UserState
{
    public int FirstTryMantras = 0;
}

public static class State
{

    public static List<string> GetMantrasInThisGuild(Message m, ulong guildId)
    {
        var c = State.GetRestClient(m);

        var guildWork = c.GetGuildAsync(guildId);
        guildWork.Wait();
        var threadsWork = guildWork.Result.GetActiveThreadsAsync();
        threadsWork.Wait();
        var threads = threadsWork.Result;
        var botId = State.GetMyBotUserIdInThisGuild(m);

        List<string> mantras = new();

        foreach (var th in threads)
        {
            foreach (var usr in th.GetUsersAsync().ToBlockingEnumerable())
            {
                if (usr.Id == botId)
                {
                    mantras.Add(th.Name);
                }
            }
        }

        return mantras;
    }



    // guildid by message id -> what msg should be
    public static readonly ConcurrentDictionary<ulong, (string shouldBe, ulong fixItMsg)> MessageCorrectionsChecks =
        new();

// guild id -> channel we are expecting our reply, and words
    public static readonly ConcurrentDictionary<ulong, (ulong, string)>
        TellMeWhatToSayReplayMessageId = new();


    public const string BondedChannel = "5XXX 🔀 5XXX.";
    public const string GoodCheck = "✅";
    public const string BadCheck = "❌";
    public const string XXXXIsGoodForXXXX = "XXXX is good for XXXX";
    public const string CMD_PREFIX = "mt!";

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

        var client = GetRestClient(msg);
        if (client == null) throw new Exception("No client for this message?");
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
