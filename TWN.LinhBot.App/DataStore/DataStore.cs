﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace TWN.LinhBot.App.DataStore;
internal class DataStore(DataStoreSettings dataStoreSettings)
{
  private readonly DataStoreSettings _dataStoreSettings = dataStoreSettings;

  public async Task<List<Data>> GetDataAsync()
  {
    if (!File.Exists(_dataStoreSettings.FilePath))
      return [];

    var json = await File.ReadAllTextAsync(_dataStoreSettings.FilePath, Encoding.UTF8);
    return JsonSerializer.Deserialize<List<Data>>(json) ?? [];
  }

  public void StoreDataAsync(IEnumerable<Data> data)
  {
    var json = JsonSerializer.Serialize(data, new JsonSerializerOptions()
    {

    });
    File.WriteAllText(_dataStoreSettings.FilePath, json, Encoding.UTF8);
  }

  public async Task<Data> AddDataAsync(string twitchUser, ulong guildID, ulong channelID)
  {
    var existingData = await GetDataAsync() ?? [];

    var mayExistingData = existingData.FirstOrDefault(d => d.TwitchUser == twitchUser && d.GuildID == guildID && d.ChannelID == channelID);
    if (mayExistingData is null)
    {
      var newData = new Data(twitchUser, guildID, channelID);
      existingData.Add(newData);
      StoreDataAsync(existingData);
      return newData;
    }
    return mayExistingData;
  }
}

public sealed record Data(string TwitchUser, ulong GuildID, ulong ChannelID) { }
