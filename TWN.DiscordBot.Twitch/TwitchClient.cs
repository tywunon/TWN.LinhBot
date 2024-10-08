﻿using Microsoft.Extensions.Logging;

using OneOf.Types;

using System.Net.Http.Json;

using TWN.DiscordBot.Interfaces;
using TWN.DiscordBot.Interfaces.Types;
using TWN.DiscordBot.Settings;
using TWN.DiscordBot.Utils;

namespace TWN.DiscordBot.Twitch;
public class TwitchClient(IHttpClientFactory httpClientFactory,
                          TwitchSettings twitchAPISettings,
                          ILogger<TwitchClient> logger)
: ITwitchClientAsync
{
  public async Task<TwitchOAuthResult> GetOAuthTokenAsync(CancellationToken cancellationToken)
  {
    try
    {
      var client = httpClientFactory.CreateTwitchOAuthClient();
      var response = await PostOAuthAsync(cancellationToken);
      var result = await response.Content.ReadFromJsonAsync<OAuthResponse>(cancellationToken);
      if (result is null)
      {
        var parseException = new Exception($"Response:\n{response?.Content}\ncouldn't be parsed");
        logger.LogException(parseException, "GetOAuthTokenAsync");
        return new Error<Exception>();
      }
      return new Success<string>(result.Access_Token ?? string.Empty);
    }
    catch (Exception ex)
    {
      logger.LogException(ex, "GetOAuthTokenAsync");
      return new Error<Exception>(ex);
    }
  }

  public async Task<TwitchStreamsResult> GetStreamsAsync(IEnumerable<string> userLogins, CancellationToken cancellationToken)
  {
    try
    {
      var oAuthTokenResult = await GetOAuthTokenAsync(cancellationToken);
      return await oAuthTokenResult.Match(
        async oAuthToken =>
        {
          var client = httpClientFactory.CreateTwitchAPIClient();
          var queryParameter = userLogins.Any() ? $"?{string.Join("&", userLogins.Select(ul => $"user_login={ul}"))}" : string.Empty;
          var request = new HttpRequestMessage(HttpMethod.Get, $"streams{queryParameter}");
          request.Headers.Authorization = new("Bearer", oAuthToken.Value);
          request.Headers.Add("client-id", twitchAPISettings.ClientID);
          var response = await client.SendAsync(request, cancellationToken);
          var result = await response.Content.ReadFromJsonAsync<StreamsResponse>(cancellationToken);
          if (result is null)
          {
            var parseException = new Exception($"Response:\n{response?.Content}\ncouldn't be parsed");
            logger.LogException(parseException, "GetStreamsAsync");
            return new Error<Exception>();
          }
          return new Success<StreamsResponse>(result);
        },
        error => Task.FromResult<TwitchStreamsResult>(error)
      ); ;
    }
    catch (Exception ex)
    {
      logger.LogException(ex, "GetStreamsAsync");
      return new Error<Exception>(ex);
    }
  }

  public async Task<TwitchUsersResult> GetUsersAsync(IEnumerable<string> userLogins, CancellationToken cancellationToken)
  {
    try
    {
      var oAuthTokenResult = await GetOAuthTokenAsync(cancellationToken);
      return await oAuthTokenResult.Match(
        async oAuthToken =>
        {
          var client = httpClientFactory.CreateTwitchAPIClient();
          var queryParameter = userLogins.Any() ? $"?{string.Join("&", userLogins.Select(ul => $"login={ul}"))}" : string.Empty;
          var request = new HttpRequestMessage(HttpMethod.Get, $"users{queryParameter}");
          request.Headers.Authorization = new("Bearer", oAuthToken.Value);
          request.Headers.Add("client-id", twitchAPISettings.ClientID);
          var response = await client.SendAsync(request, cancellationToken);
          var result = await response.Content.ReadFromJsonAsync<UsersResponse>(cancellationToken) ?? new([]);
          if (result is null)
          {
            var parseException = new Exception($"Response:\n{response?.Content}\ncouldn't be parsed");
            logger.LogException(parseException, "GetUsersAsync");
            return new Error<Exception>();
          }
          return new Success<UsersResponse>(result);
        },
        error => Task.FromResult<TwitchUsersResult>(error)
      );
    }
    catch (Exception ex)
    {
      logger.LogException(ex, "GetUsersAsync");
      return new Error<Exception>(ex);
    }
  }

  private async Task<HttpResponseMessage> PostOAuthAsync(CancellationToken cancellationToken) 
    => await httpClientFactory.CreateTwitchOAuthClient()
    .PostAsync(string.Empty, new OAuthContent(twitchAPISettings.ClientID, twitchAPISettings.ClientSecret), cancellationToken);

  public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken)
  {
    var response = await PostOAuthAsync(cancellationToken);
    return response.IsSuccessStatusCode;
  }
}

internal class OAuthContent(string clientID, string clientSecret)
: FormUrlEncodedContent
  ([
    KeyValuePair.Create("client_id", clientID),
    KeyValuePair.Create("client_secret", clientSecret),
    KeyValuePair.Create("grant_type", "client_credentials"),
  ])
{ }

internal record OAuthResponse(
  string? Access_Token = null,
  int? Expires_In = null,
  string? Token_Type = null,

  int? Status = null,
  string? Message = null
);