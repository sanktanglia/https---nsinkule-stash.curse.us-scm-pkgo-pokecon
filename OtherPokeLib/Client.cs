﻿using Newtonsoft.Json.Linq;
using PokemonGo.RocketAPI.Enums;
using PokemonGo.RocketAPI.GeneratedCode;
using PokemonGo.RocketAPI.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Google.Protobuf;
using PokemonGo.RocketAPI.Extensions;

namespace OtherPokeLib
{
    public class Client
    {
        private readonly HttpClient _httpClient;
        private AuthType _authType = AuthType.Google;
        private string _accessToken;
        private string _apiUrl;
        private Request.Types.UnknownAuth _unknownAuth;

        private double _currentLat;
        private double _currentLng;

        public Client(double lat, double lng)
        {
            SetCoordinates(lat, lng);

            //Setup HttpClient and create default headers
            HttpClientHandler handler = new HttpClientHandler()
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = false
            };
            _httpClient = new HttpClient(new RetryHandler(handler));
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Niantic App");
            //"Dalvik/2.1.0 (Linux; U; Android 5.1.1; SM-G900F Build/LMY48G)");
            _httpClient.DefaultRequestHeaders.ExpectContinue = false;
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Connection", "keep-alive");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Content-Type",
                "application/x-www-form-urlencoded");
        }

        private void SetCoordinates(double lat, double lng)
        {
            _currentLat = lat;
            _currentLng = lng;
        }



        private JToken poll(JToken json)
        {
            var handler = new HttpClientHandler()
            {
                AutomaticDecompression = DecompressionMethods.GZip,
                AllowAutoRedirect = false
            };

            String OAUTH_TOKEN_ENDPOINT = "https://www.googleapis.com/oauth2/v4/token";
            String SECRET = "NCjF1TLi2CcY6t5mt0ZveuL7";
            String CLIENT_ID = "848232511240-73ri3t7plvk96pj4f85uj8otdat2alem.apps.googleusercontent.com";

            using (var tempHttpClient = new HttpClient(handler))
            {
                var response = tempHttpClient.PostAsync(OAUTH_TOKEN_ENDPOINT,
                    new FormUrlEncodedContent(
                        new[]
                        {
                            new KeyValuePair<string, string>("client_id", CLIENT_ID),
                            new KeyValuePair<string, string>("client_secret", SECRET),
                            new KeyValuePair<string, string>("code", json.SelectToken("device_code").ToString()),
                            new KeyValuePair<string, string>("grant_type", "http://oauth.net/grant_type/device/1.0"),
                            new KeyValuePair<string, string>("scope", "openid email https://www.googleapis.com/auth/userinfo.email")
                        }));

                string content = response.Result.Content.ReadAsStringAsync().Result;
                JToken token = JObject.Parse(content);
                if (token.SelectToken("error") == null)
                {
                    return token;
                }
                else
                {
                    return null;
                }
            }

        }

        public async Task LoginPtc(string username, string password)
        {
            //Get session cookie
            var sessionResp = await _httpClient.GetAsync(Resources.PtcLoginUrl);
            var data = await sessionResp.Content.ReadAsStringAsync();
            var lt = JsonHelper.GetValue(data, "lt");
            var executionId = JsonHelper.GetValue(data, "execution");

            //Login
            var loginResp = await _httpClient.PostAsync(Resources.PtcLoginUrl,
                new FormUrlEncodedContent(
                    new[]
                    {
                        new KeyValuePair<string, string>("lt", lt),
                        new KeyValuePair<string, string>("execution", executionId),
                        new KeyValuePair<string, string>("_eventId", "submit"),
                        new KeyValuePair<string, string>("username", username),
                        new KeyValuePair<string, string>("password", password),
                    }));

            var ticketId = HttpUtility.ParseQueryString(loginResp.Headers.Location.Query)["ticket"];

            //Get tokenvar 
            var tokenResp = await _httpClient.PostAsync(Resources.PtcLoginOauth,
                new FormUrlEncodedContent(
                    new[]
                    {
                        new KeyValuePair<string, string>("client_id", "mobile-app_pokemon-go"),
                        new KeyValuePair<string, string>("redirect_uri", "https://www.nianticlabs.com/pokemongo/error"),
                        new KeyValuePair<string, string>("client_secret",
                            "w8ScCUXJQc6kXKw8FiOhd8Fixzht18Dq3PEVkUCP5ZPxtgyWsbTvWHFLm2wNY0JR"),
                        new KeyValuePair<string, string>("grant_type", "grant_type"),
                        new KeyValuePair<string, string>("code", ticketId),
                    }));

            var tokenData = await tokenResp.Content.ReadAsStringAsync();
            _accessToken = HttpUtility.ParseQueryString(tokenData)["access_token"];
            _authType = AuthType.Ptc;
        }

        public async Task<PlayerUpdateResponse> UpdatePlayerLocation(double lat, double lng)
        {
            this.SetCoordinates(lat, lng);
            var customRequest = new Request.Types.PlayerUpdateProto()
            {
                Lat = Utils.FloatAsUlong(_currentLat),
                Lng = Utils.FloatAsUlong(_currentLng)
            };

            var updateRequest = RequestBuilder.GetRequest(_unknownAuth, _currentLat, _currentLng, 10,
                new Request.Types.Requests()
                {
                    Type = (int)RequestType.PLAYER_UPDATE,
                    Message = customRequest.ToByteString()
                });
            var updateResponse =
                await _httpClient.PostProto<Request, PlayerUpdateResponse>($"https://{_apiUrl}/rpc", updateRequest);
            return updateResponse;
        }

        public async Task<ProfileResponse> GetServer()
        {
            var serverRequest = RequestBuilder.GetInitialRequest(_accessToken, _authType, _currentLat, _currentLng, 10,
                RequestType.GET_PLAYER, RequestType.GET_HATCHED_OBJECTS, RequestType.GET_INVENTORY,
                RequestType.CHECK_AWARDED_BADGES, RequestType.DOWNLOAD_SETTINGS);
            var serverResponse = await _httpClient.PostProto<Request, ProfileResponse>(Resources.RpcUrl, serverRequest);
            _apiUrl = serverResponse.ApiUrl;
            return serverResponse;
        }

        public async Task<ProfileResponse> GetProfile()
        {
            var profileRequest = RequestBuilder.GetInitialRequest(_accessToken, _authType, _currentLat, _currentLng, 10,
                new Request.Types.Requests() { Type = (int)RequestType.GET_PLAYER });
            var profileResponse =
                await _httpClient.PostProto<Request, ProfileResponse>($"https://{_apiUrl}/rpc", profileRequest);
            _unknownAuth = new Request.Types.UnknownAuth()
            {
                Unknown71 = profileResponse.Auth.Unknown71,
                Timestamp = profileResponse.Auth.Timestamp,
                Unknown73 = profileResponse.Auth.Unknown73,
            };
            return profileResponse;
        }

        public async Task<SettingsResponse> GetSettings()
        {
            var settingsRequest = RequestBuilder.GetRequest(_unknownAuth, _currentLat, _currentLng, 10,
                RequestType.DOWNLOAD_SETTINGS);
            return await _httpClient.PostProto<Request, SettingsResponse>($"https://{_apiUrl}/rpc", settingsRequest);
        }

        public async Task<MapObjectsResponse> GetMapObjects()
        {
            var nearByCellIDs = S2Helper.GetNearbyCellIds(_currentLng,
                            _currentLat);
            StringBuilder cellBuilder = new StringBuilder();
            for(int i = 0;i< nearByCellIDs.Count;i++)
            {
                cellBuilder.Append("\0");
            }
            var customRequest = new Request.Types.MapObjectsRequest()
            {
                
                CellIds =
                    ByteString.CopyFrom(
                        ProtoHelper.EncodeUlongList(nearByCellIDs)),
                Latitude = Utils.FloatAsUlong(_currentLat),
                Longitude = Utils.FloatAsUlong(_currentLng),
                Unknown14 = ByteString.CopyFromUtf8(cellBuilder.ToString())
            };

            var mapRequest = RequestBuilder.GetRequest(_unknownAuth, _currentLat, _currentLng, 10,
                new Request.Types.Requests()
                {
                    Type = (int)RequestType.GET_MAP_OBJECTS,
                    Message = customRequest.ToByteString()
                },
                new Request.Types.Requests() { Type = (int)RequestType.GET_HATCHED_OBJECTS },
                new Request.Types.Requests()
                {
                    Type = (int)RequestType.GET_INVENTORY,
                    Message = new Request.Types.Time() { Time_ = DateTime.UtcNow.ToUnixTime() }.ToByteString()
                },
                new Request.Types.Requests() { Type = (int)RequestType.CHECK_AWARDED_BADGES },
                new Request.Types.Requests()
                {
                    Type = (int)RequestType.DOWNLOAD_SETTINGS,
                    Message =
                        new Request.Types.SettingsGuid()
                        {
                            Guid = ByteString.CopyFromUtf8("4a2e9bc330dae60e7b74fc85b98868ab4700802e")
                        }.ToByteString()
                });

            return await _httpClient.PostProto<Request, MapObjectsResponse>($"https://{_apiUrl}/rpc", mapRequest);
        }

        public async Task<FortDetailResponse> GetFort(string fortId, double fortLat, double fortLng)
        {
            var customRequest = new Request.Types.FortDetailsRequest()
            {
                Id = ByteString.CopyFromUtf8(fortId),
                Latitude = Utils.FloatAsUlong(fortLat),
                Longitude = Utils.FloatAsUlong(fortLng),
            };

            var fortDetailRequest = RequestBuilder.GetRequest(_unknownAuth, _currentLat, _currentLng, 10,
                new Request.Types.Requests()
                {
                    Type = (int)RequestType.FORT_DETAILS,
                    Message = customRequest.ToByteString()
                });
            return await _httpClient.PostProto<Request, FortDetailResponse>($"https://{_apiUrl}/rpc", fortDetailRequest);
        }

        /*num Holoholo.Rpc.Types.FortSearchOutProto.Result {
         NO_RESULT_SET = 0;
         SUCCESS = 1;
         OUT_OF_RANGE = 2;
         IN_COOLDOWN_PERIOD = 3;
         INVENTORY_FULL = 4;
        }*/

        public async Task<FortSearchResponse> SearchFort(string fortId, double fortLat, double fortLng)
        {
            var customRequest = new Request.Types.FortSearchRequest()
            {
                Id = ByteString.CopyFromUtf8(fortId),
                FortLatDegrees = Utils.FloatAsUlong(fortLat),
                FortLngDegrees = Utils.FloatAsUlong(fortLng),
                PlayerLatDegrees = Utils.FloatAsUlong(_currentLat),
                PlayerLngDegrees = Utils.FloatAsUlong(_currentLng)
            };

            var fortDetailRequest = RequestBuilder.GetRequest(_unknownAuth, _currentLat, _currentLng, 30,
                new Request.Types.Requests()
                {
                    Type = (int)RequestType.FORT_SEARCH,
                    Message = customRequest.ToByteString()
                });
            return await _httpClient.PostProto<Request, FortSearchResponse>($"https://{_apiUrl}/rpc", fortDetailRequest);
        }

        public async Task<EncounterResponse> EncounterPokemon(ulong encounterId, string spawnPointGuid)
        {
            var customRequest = new Request.Types.EncounterRequest()
            {
                EncounterId = encounterId,
                SpawnpointId = spawnPointGuid,
                PlayerLatDegrees = Utils.FloatAsUlong(_currentLat),
                PlayerLngDegrees = Utils.FloatAsUlong(_currentLng)
            };

            var encounterResponse = RequestBuilder.GetRequest(_unknownAuth, _currentLat, _currentLng, 30,
                new Request.Types.Requests()
                {
                    Type = (int)RequestType.ENCOUNTER,
                    Message = customRequest.ToByteString()
                });
            return await _httpClient.PostProto<Request, EncounterResponse>($"https://{_apiUrl}/rpc", encounterResponse);
        }

        public async Task<CatchPokemonResponse> CatchPokemon(ulong encounterId, string spawnPointGuid, double pokemonLat,
            double pokemonLng)
        {
            var customRequest = new Request.Types.CatchPokemonRequest()
            {
                EncounterId = encounterId,
                Pokeball = (int)MiscEnums.Item.ITEM_POKE_BALL,
                SpawnPointGuid = spawnPointGuid,
                HitPokemon = 1,
                NormalizedReticleSize = Utils.FloatAsUlong(1.950),
                SpinModifier = Utils.FloatAsUlong(1),
                NormalizedHitPosition = Utils.FloatAsUlong(1)
            };

            var catchPokemonRequest = RequestBuilder.GetRequest(_unknownAuth, _currentLat, _currentLng, 30,
                new Request.Types.Requests()
                {
                    Type = (int)RequestType.CATCH_POKEMON,
                    Message = customRequest.ToByteString()
                });
            return
                await
                    _httpClient.PostProto<Request, CatchPokemonResponse>($"https://{_apiUrl}/rpc", catchPokemonRequest);
        }


        public async Task<InventoryResponse> GetInventory()
        {
            var inventoryRequest = RequestBuilder.GetRequest(_unknownAuth, _currentLat, _currentLng, 30, RequestType.GET_INVENTORY);
            return await _httpClient.PostProto<Request, InventoryResponse>($"https://{_apiUrl}/rpc", inventoryRequest);
        }
    }
}
