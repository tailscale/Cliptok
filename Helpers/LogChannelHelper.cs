﻿using Cliptok.Constants;
using System.Globalization;

namespace Cliptok.Helpers
{
    public class LogChannelNotFoundException : Exception
    {
        public LogChannelNotFoundException(string message) : base(message)
        {
        }
    }

    public class LogChannelHelper
    {
        internal static Dictionary<string, DiscordChannel> ChannelCache = new();
        internal static Dictionary<string, (DiscordWebhookClient webhookClient, ulong id)> WebhookCache = new();
        public static bool ready = false;

        public static async Task UnpackLogConfigAsync(ConfigJson config)
        {
            if (ready) return;

            Dictionary<string, ulong> MigrationMapping = new()
            {
                { "mod", config.LogChannel},
                { "users", config.UserLogChannel },
                { "home", config.HomeChannel },
                { "investigations" , config.InvestigationsChannelId },
                { "support", config.SupportLogChannel },
                { "dms", config.DmLogChannelId },
                { "errors", config.ErrorLogChannelId },
                { "secret", config.MysteryLogChannelId },
            };

            if (config.LogChannels is not null)
            {
                foreach (KeyValuePair<string, LogChannelConfig> logChannel in config.LogChannels)
                {
                    if (logChannel.Value.ChannelId != 0)
                    {
                        DiscordChannel channel = default;
                        try
                        {
                            channel = await Program.discord.GetChannelAsync(logChannel.Value.ChannelId);
                        }
                        catch (Exception e)
                        {
                            Program.discord.Logger.LogError(Program.CliptokEventID, e, "Error getting channel {id} for log channel {key}", logChannel.Value.ChannelId, logChannel.Key);
                            Environment.Exit(1);
                        }
                        ChannelCache.Add(logChannel.Key, channel);
                    }

                    DiscordWebhookClient webhookClient = new DiscordWebhookClient();
                    string webhookUrl = "";
                    if (logChannel.Value.WebhookEnvVar != "")
                    {
                        webhookUrl = Environment.GetEnvironmentVariable(logChannel.Value.WebhookEnvVar);
                    }
                    else if (logChannel.Value.WebhookUrl != "")
                    {
                        webhookUrl = logChannel.Value.WebhookUrl;
                    }

                    if (webhookUrl != "")
                    {
                        Match m = RegexConstants.webhook_rx.Match(webhookUrl);
                        if (!m.Success)
                        {
                            throw new ArgumentException("Invalid webhook URL supplied.", nameof(webhookUrl));
                        }

                        Group idraw = m.Groups["id"];
                        if (!ulong.TryParse(idraw.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong id))
                        {
                            throw new ArgumentException("Invalid webhook URL supplied.", nameof(webhookUrl));
                        }

                        await webhookClient.AddWebhookAsync(new Uri(webhookUrl));
                        WebhookCache.Add(logChannel.Key, (webhookClient, id));
                    }
                }
            }

            foreach (KeyValuePair<string, ulong> migration in MigrationMapping)
            {
                if (migration.Value != 0 && !ChannelCache.ContainsKey(migration.Key))
                {
                    var channel = await Program.discord.GetChannelAsync(migration.Value);
                    ChannelCache.Add(migration.Key, channel);
                }
                else if (migration.Value == 0 && !ChannelCache.ContainsKey(migration.Key) && !WebhookCache.ContainsKey(migration.Key))
                {
                    // all channels that dont exist fallback to the home channel,
                    // which is the only channel that will always exist in config
                    var channel = await Program.discord.GetChannelAsync(config.HomeChannel);
                    ChannelCache.Add(migration.Key, channel);
                }
            }

            ready = true;
        }

        public static async Task<DiscordMessage> LogMessageAsync(string key, string content)
        {
            return await LogMessageAsync(key, new DiscordMessageBuilder().WithContent(content));
        }

        public static async Task<DiscordMessage> LogMessageAsync(string key, string content, DiscordEmbed embed)
        {
            return await LogMessageAsync(key, new DiscordMessageBuilder().WithContent(content).AddEmbed(embed));
        }

        public static async Task<DiscordMessage> LogMessageAsync(string key, DiscordEmbed embed)
        {
            return await LogMessageAsync(key, new DiscordMessageBuilder().AddEmbed(embed));
        }
        public static async Task<DiscordMessage> LogMessageAsync(string key, DiscordMessageBuilder message)
        {
            if (!ready)
                return null;

            try
            {
                if (WebhookCache.ContainsKey(key))
                {
                    var builder = new DiscordWebhookBuilder(message)
                        .WithAvatarUrl(Program.discord.CurrentUser.GetAvatarUrl(MediaFormat.Png, 1024))
                        .WithUsername(Program.discord.CurrentUser.Username);

                    if (ChannelCache.ContainsKey(key) && ChannelCache[key].IsThread)
                        builder.WithThreadId(ChannelCache[key].Id);

                    var webhookResults = await WebhookCache[key].webhookClient.BroadcastMessageAsync(builder);
                    return webhookResults.FirstOrDefault().Value;
                }
                else if (ChannelCache.ContainsKey(key))
                {
                    return await ChannelCache[key].SendMessageAsync((DiscordMessageBuilder)message);
                }
                else
                {
                    throw new LogChannelNotFoundException($"A valid log channel for key '{key}' was not found!");
                }
            }
            catch (Exception ex)
            {
                EventId eventId = Program.CliptokEventID;
                if (key == "errors")
                    eventId = Program.LogChannelErrorID;

                Program.discord.Logger.LogError(eventId, ex, "Error ocurred trying to send message to key {key}", key);
                return null;
            }
        }

        public static async Task<(DiscordMessageBuilder messageBuilder, string pasteUrl)> CreateDumpMessageAsync(string content, List<DiscordMessage> messages, DiscordChannel channel)
        {
            string messageLog = await DiscordHelpers.CompileMessagesAsync(messages.AsEnumerable().OrderBy(x => x.Id).ToList(), channel);
            return await DumpMessageFromStringAsync(messageLog, content);
        }

        public static async Task<(DiscordMessageBuilder messageBuilder, string pasteUrl)> CreateDumpMessageAsync(string content, List<Models.CachedDiscordMessage> messages, DiscordChannel channel)
        {
            string messageLog = await DiscordHelpers.CompileMessagesAsync(messages.AsEnumerable().OrderBy(x => x.Id).ToList(), channel);
            return await DumpMessageFromStringAsync(messageLog, content);
        }

        public static async Task<(DiscordMessageBuilder messageBuilder, string pasteUrl)> DumpMessageFromStringAsync(string messageLog, string content)
        {
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(messageLog));
            var msg = new DiscordMessageBuilder().WithContent(content).AddFile("messages.txt", stream);

            var hasteResult = await Program.hasteUploader.PostAsync(messageLog);

            if (hasteResult.IsSuccess)
            {
                msg.AddEmbed(new DiscordEmbedBuilder().WithDescription($"[`📄 View online`]({hasteResult.RawUrl})"));
            }

            return (msg, hasteResult.RawUrl);
        }

        public static async Task<DiscordMessage> LogDeletedMessagesAsync(string key, string content, List<DiscordMessage> messages, DiscordChannel channel)
        {
            return await LogMessageAsync(key, (await CreateDumpMessageAsync(content, messages, channel)).messageBuilder);
        }

        public static ulong GetLogChannelId(string key)
        {
            if (WebhookCache.ContainsKey(key))
            {
                if (ChannelCache.ContainsKey(key) && ChannelCache[key].IsThread)
                    return ChannelCache[key].Id;
                else
                    return WebhookCache[key].webhookClient.GetRegisteredWebhook(WebhookCache[key].id).ChannelId;
            }
            else if (ChannelCache.ContainsKey(key))
                return ChannelCache[key].Id;
            else
                throw new LogChannelNotFoundException($"A valid log channel for key '{key}' was not found!");
        }
    }
}
