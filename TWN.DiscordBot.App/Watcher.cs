﻿using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using TWN.DiscordBot.Interfaces;
using TWN.DiscordBot.Interfaces.Types;
using TWN.DiscordBot.Settings;

namespace TWN.DiscordBot.App;
internal class Watcher(WatcherSettings settings, IDiscordClient discordClient, ITwitchClient twitchClient, IDataStore dataStore, ILogger<Watcher> logger) : BackgroundService
{
  private readonly Dictionary<string, DateTime> onlineCache = [];

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    await discordClient.StartAsync();

    PeriodicTimer timer = new(TimeSpan.FromMilliseconds(settings.Delay));
    logger.LogDebug("{obj} created ({Delay})", nameof(timer), settings.Delay);

    try
    {
      while (await timer.WaitForNextTickAsync(stoppingToken))
      {
        try
        {
          var lookUpData = await dataStore.GetDataAsync();
          logger.Log(LogLevel.Debug, new EventId(), lookUpData, null, (s, ex) => "lookUpData:" + string.Join(", ", s.Announcements.Select(_s => $"{(_s.TwitchUser, _s.GuildID, _s.ChannelID)}")));
          if (lookUpData.Announcements.Count != 0)
          {
            var twitchUsers = lookUpData.Announcements.Select(lud => lud.TwitchUser).Distinct();
            logger.Log(LogLevel.Debug, new EventId(), twitchUsers, null, (s, ex) => "twitchUsers:" + string.Join(", ", s));
            var twitchStreamData = await twitchClient.GetStreams(twitchUsers, stoppingToken);

            await twitchStreamData.Match
            (
              async streamData =>
              {
                logger.Log(LogLevel.Debug, new EventId(), streamData, null, (s, ex) => "twitchStreamData:" + string.Join(", ", s.Data.Select(_s => $"{_s.User_Login}")));

                var onlineUser = streamData.Data.Select(td => td.User_Login);
                logger.Log(LogLevel.Debug, new EventId(), onlineUser, null, (s, ex) => "onlineUser:" + string.Join(", ", s));
                var offlineUser = onlineCache.Keys.Select(oc => oc).Except(onlineUser).ToList();
                logger.Log(LogLevel.Debug, new EventId(), offlineUser, null, (s, ex) => "offlineUser:" + string.Join(", ", s));

                onlineCache.RemoveAll(oc => offlineUser.Contains(oc.Key) || oc.Value < DateTime.Now.AddMilliseconds(-settings.Horizon));
                logger.Log(LogLevel.Debug, new EventId(), onlineCache, null, (s, ex) => "onlineCache:" + string.Join(", ", s));

                if (onlineUser.Any())
                {
                  var twitchUserData = await twitchClient.GetUsers(onlineUser, stoppingToken);

                  await twitchUserData.Match
                  (
                    async userResponse =>
                    {
                      logger.Log(LogLevel.Debug, new EventId(), userResponse, null, (s, ex) => "twitchUserData:" + string.Join(", ", s.Data.Select(_s => $"{_s.Login}")));

                      var dataGroups = lookUpData.Announcements
                        .Join(streamData.Data, o => o.TwitchUser, i => i.User_Login, (o, i) => (lookUpData: o, twitchStreamData: i))
                        .Join(userResponse.Data, o => o.twitchStreamData.User_Login, i => i.Login, (o, i) => (lookUpData: o.lookUpData, twitchStreamData: o.twitchStreamData, twitchUserData: i))
                        .GroupBy(d => (twitchUser: d.lookUpData.TwitchUser, d.twitchStreamData.Game_Name));
                      logger.Log(LogLevel.Debug, new EventId(), dataGroups, null, (s, ex) => "dataGroups:" + string.Join(", ", s.Select(_s => $"{(_s.Key, string.Join(", ", _s.Select(_s1 => (_s1.lookUpData.GuildID, _s1.lookUpData.ChannelID, _s1.twitchStreamData.Game_Name))))}")));

                      foreach (var dataGroup in dataGroups)
                      {
                        logger.Log(LogLevel.Debug, new EventId(), dataGroup, null, (s, ex) => "dataGroup:" + string.Join(", ", s.Select(_s => (_s.lookUpData.GuildID, _s.lookUpData.ChannelID, _s.twitchStreamData.Game_Name))));
                        logger.Log(LogLevel.Debug, new EventId(), onlineCache, null, (s, ex) => "onlineCache-pre:" + dataGroup.Key + ":" + string.Join(", ", s));
                        if (onlineCache.ContainsKey(dataGroup.Key.twitchUser))
                        {
                          onlineCache[dataGroup.Key.twitchUser] = DateTime.Now;
                          continue;
                        }

                        onlineCache.Add(dataGroup.Key.twitchUser, DateTime.Now);
                        logger.Log(LogLevel.Debug, new EventId(), onlineCache, null, (s, ex) => "onlineCache-post:" + string.Join(", ", s));

                        foreach (var data in dataGroup)
                        {
                          logger.Log(LogLevel.Debug, new EventId(), data, null, (s, ex) => "data:" + dataGroup.Key + ":" + (s.lookUpData.GuildID, s.lookUpData.ChannelID, s.twitchStreamData.Game_Name));
                          await discordClient.SendTwitchMessage(data.lookUpData.GuildID, data.lookUpData.ChannelID, new DiscordTwitchEmbedData()
                          {
                            Title = data.twitchStreamData.Title,
                            UserLogin = data.twitchStreamData.User_Login,
                            UserName = data.twitchStreamData.User_Name,
                            GameName = data.twitchStreamData.Game_Name,
                            UserImage = data.twitchUserData.Profile_Image_Url,
                            ThumbnailURL = data.twitchStreamData.Thumbnail_Url,
                            StartedAt = data.twitchStreamData.Started_At,
                          });
                        }
                      }
                    },
                    error => Task.FromResult(error)
                  );
                }
              },
              error => Task.FromResult(error)
            );
          }
        }
        catch (Exception ex)
        {
          logger.LogError(ex, "An Exception was thrown while watching");
        }
      }
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "An Exception was thrown trying to watch");
    }
  }
}
