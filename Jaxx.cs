using System.Collections.Concurrent;
using System.Threading.Tasks;
using NetCord;
using NetCord.Gateway.Voice;
using NetCord.Logging;
using NetCord.Services.ApplicationCommands;


public static class JaxxState
{
    public static readonly ConcurrentDictionary<long, VoiceClient> VoiceClients = new();
}

public class Jaxx : ApplicationCommandModule<ApplicationCommandContext>
{

    [SlashCommand("echo", "Creates echo", Contexts = [InteractionContextType.Guild])]
    public async Task<string> EchoAsync()
    {
        var guild = Context.Guild!;
        var userId = Context.User.Id;

        // Get the user voice state
        if (!guild.VoiceStates.TryGetValue(userId, out var voiceState))
            return "You are not connected to any voice channel!";

        var client = Context.Client;

        // You should check if the bot is already connected to the voice channel.
        // If so, you should use an existing 'VoiceClient' instance instead of creating a new one.
        // You also need to add a synchronization here. 'JoinVoiceChannelAsync' should not be used concurrently for the same guild
        var voiceClient = await client.JoinVoiceChannelAsync(
            guild.Id,
            voiceState.ChannelId.GetValueOrDefault(),
            new VoiceClientConfiguration
            {
                ReceiveHandler = new VoiceReceiveHandler(), // Required to receive voice
                Logger = new ConsoleLogger(),
            });

        // Connect
        await voiceClient.StartAsync();

        // Enter speaking state, to be able to send voice
        await voiceClient.EnterSpeakingStateAsync(new SpeakingProperties(SpeakingFlags.Microphone));

        // Create a stream that sends voice to Discord
        var outStream = voiceClient.CreateOutputStream(normalizeSpeed: false);

        voiceClient.VoiceReceive += args =>
        {
            // Pass current user voice directly to the output to create echo
            if (voiceClient.Cache.Users.TryGetValue(args.Ssrc, out var voiceUserId) && voiceUserId == userId)
                outStream.Write(args.Frame);
            return default;
        };

        // Return the response
        return "Echo!";
    }


}

