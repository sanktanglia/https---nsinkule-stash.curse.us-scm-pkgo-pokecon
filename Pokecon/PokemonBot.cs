using CommandLineParser.Arguments;
using Geocoding.Google;
using Google.Common.Geometry;
using Google.Protobuf;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using static RequestEnvelop.Types;
using static ResponseEnvelop.Types;

namespace Pokecon
{
    public enum LoginMethod
    {
        Google = 1,
        PokemonTrainerClub = 2
    }

    public enum MovementType
    {
        Static = 1,
        EdgeNeighbor = 2,
    }
    public class PokemonBot
    {
        const string API_URL = "https://pgorelease.nianticlabs.com/plfe/rpc";
        const string LOGIN_URL = "https://sso.pokemon.com/sso/login?service=https%3A%2F%2Fsso.pokemon.com%2Fsso%2Foauth2.0%2FcallbackAuthorize";
        const string LOGIN_OAUTH = "https://sso.pokemon.com/sso/oauth2.0/accessToken";

        const int AuthRetries = 5;
        const int APIRetries = 5;

        public string Login { get; set; }
        public string Password { get; set; }
        public double Latitude { get; set; }
        public double Longitutde { get; set; }
        public double Altitude { get; set; }
        public LoginMethod LoginMethod { get; set; }
        public string AuthToken { get; set; }
        public string RPCURL { get; set; }

        public bool LoggedIn { get; set; }

        public PokemonBot(string login, string password, LoginMethod method, Double lat, Double lon, double altitude = 0)
        {
            Login = login;
            Password = password;
            LoginMethod = method;
            Latitude = lat;
            Longitutde = lon;
            AuthToken = null;
            Altitude = altitude;
        }

        public void Authenticate()
        {
            for(int i = 0;i<=AuthRetries;i++)
            {
                if(!GetAuthToken())
                {
                    Console.WriteLine("Sleeping 2 seconds for auth retry");
                    Thread.Sleep(2000);
                }
                else
                {
                    break;
                }
            }
            if (AuthToken == null)
            {
                Console.WriteLine("No auth response after " + AuthRetries + " retries, exiting");
                return;
            }
            Console.WriteLine(Login + " authenticated");
            Thread.Sleep(2000);
            RPCURL = API_URL;
            for (int i = 0; i <= APIRetries; i++)
            {
                if (!GetEndpoint())
                {
                    Console.WriteLine("Sleeping 2 seconds for endpoint retry");
                    Thread.Sleep(2000);
                }
                else
                {
                    break;
                }
            }
            if (RPCURL == API_URL)
            {
                Console.WriteLine("No endpoint response after " + AuthRetries + " retries, exiting");
                return;
            }
            else
            {
                Console.WriteLine("Recieved RPC Endpoint " + RPCURL);
            }
            Thread.Sleep(2000);

            Thread mainThread = new Thread(LogicThread);
            mainThread.IsBackground = true;
            mainThread.Start();
        }

        public bool GetEndpoint()
        {
            four2stringauth authTest = new four2stringauth();
            authTest.Value = "4a2e9bc330dae60e7b74fc85b98868ab4700802e";

            var ret = MakeAPIRequest( new[] {
                new RequestEnvelop.Types.Requests { Type = 2 },
                new RequestEnvelop.Types.Requests { Type = 126 },
                new RequestEnvelop.Types.Requests { Type = 4 },
                new RequestEnvelop.Types.Requests { Type = 129 },
                new RequestEnvelop.Types.Requests { Type = 5, Message = authTest.ToByteString() },
            });
            if(ret == null)
            {
                return false;
            }
            RPCURL = "https://" + ret.ApiUrl + "/rpc";
            return true;
        }

        public ResponseEnvelop MakeAPIRequest(RequestEnvelop.Types.Requests[] reqs, int retries = AuthRetries)
        {
            try
            {
                var envelop = new RequestEnvelop
                {
                    Unknown1 = 2,
                    RpcId = 8145806132888207460,
                    Latitude = Latitude,
                    Longitude = Longitutde,
                    Altitude = Altitude,
                    Unknown12 = 989,
                    Auth = new RequestEnvelop.Types.AuthInfo
                    {
                        Provider = LoginMethod == LoginMethod.PokemonTrainerClub ? "ptc" : "google",
                        Token = new RequestEnvelop.Types.AuthInfo.Types.JWT { Contents = AuthToken, Unknown13 = 59 },
                    }
                };
                foreach (var r in reqs)
                    envelop.Requests.Add(r);
                for (int i = 0; i <= retries; i++)
                {
                    try
                    {
                        using (var client = new HttpClient())
                        {
                            client.DefaultRequestHeaders.Add("User-Agent", "Niantic App");
                            using (var ms = new MemoryStream())
                            {
                                envelop.WriteTo(ms);
                                ms.Position = 0;
                                var result = client.PostAsync(RPCURL, new ByteArrayContent(ms.ToArray())).Result;
                                var r = result.Content.ReadAsByteArrayAsync().Result;
                                var ret = ResponseEnvelop.Parser.ParseFrom(r);
                                return ret;
                            }
                        }
                    }
                    catch(Exception ex)
                    {
                        continue;
                    }
                }
            }
            catch (Exception e)
            {
                return null;
            }
            return null;
        }

        public bool GetAuthToken()
        {
            Console.WriteLine("[!] login for: {0}", Login);

            using (var clientHandler = new HttpClientHandler())
            {
                clientHandler.AllowAutoRedirect = false;
                using (var client = new HttpClient(clientHandler))
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "niantic");
                    var r = client.GetAsync(LOGIN_URL).Result.Content.ReadAsStringAsync().Result;
                    var jdata = JObject.Parse(r);
                    var data = new[]
                    {
                        new KeyValuePair<string, string>("lt", (string)jdata["lt"]),
                        new KeyValuePair<string, string>("execution", (string)jdata["execution"]),
                        new KeyValuePair<string, string>("_eventId", "submit"),
                        new KeyValuePair<string, string>("username", Login),
                        new KeyValuePair<string, string>("password", Password),
                    };
                    var result = client.PostAsync(LOGIN_URL, new FormUrlEncodedContent(data)).Result;
                    if (result.Headers.Location == null)
                    {
                        return false;
                    }
                    var location = result.Headers.Location.ToString();
                    var r1 = result.Content.ReadAsStringAsync().Result;

                    string ticket = null;
                    try { ticket = new Regex(".*ticket=").Split(location)[1]; }
                    catch
                    {
                        return false;
                    }

                    var data1 = new[]
                    {
                        new KeyValuePair<string, string>("client_id", "mobile-app_pokemon-go"),
                        new KeyValuePair<string, string>("redirect_uri", "https://www.nianticlabs.com/pokemongo/error"),
                        new KeyValuePair<string, string>("client_secret", "w8ScCUXJQc6kXKw8FiOhd8Fixzht18Dq3PEVkUCP5ZPxtgyWsbTvWHFLm2wNY0JR"),
                        new KeyValuePair<string, string>("grant_type", "refresh_token"),
                        new KeyValuePair<string, string>("code", ticket),
                    };
                    var r2 = client.PostAsync(LOGIN_OAUTH, new FormUrlEncodedContent(data1)).Result.Content.ReadAsStringAsync().Result;
                    var access_token = new Regex("&expires.*").Split(r2)[0];
                    access_token = new Regex(".*access_token=").Split(access_token)[1];
                    AuthToken = access_token;
                    return true;
                }
            }
        }
    

        public void LogicThread()
        {
            while(true)
            {

                four2stringgym fourString = new four2stringgym();

                S2CellId currentCell = S2CellId.FromLatLng(S2LatLng.FromDegrees(Latitude, Longitutde)).ParentForLevel(15);
                ulong cellID = currentCell.Id;

                fourString.Cells.Add(cellID);
                foreach (var sibling in currentCell.GetEdgeNeighbors())
                {
                    fourString.Cells.Add(sibling.Id);
                }
                fourString.Lat = Latitude;
                fourString.Lon = Longitutde;

                RequestEnvelop.Types.Requests request = new Requests();
                request.Type = 106;
                request.Message = fourString.ToByteString();
                ResponseEnvelop response = MakeAPIRequest(new[]
                {
                    request
                });
                if(response != null)
                {
                    foreach(var payload in response.Payload)
                    {
                        MapObjectsPayload mapObjects = MapObjectsPayload.Parser.ParseFrom(payload.PayloadData);
                        foreach(var cellEntry in mapObjects.Cells)
                        {
                            Console.WriteLine("Cell: " + cellEntry.S2CellId);
                            if(cellEntry.Fort != null)
                            {
                                foreach(var fort in cellEntry.Fort)
                                {
                                    Console.WriteLine("found pokestop/gym at " + fort.Latitude + "/" + fort.Longitude);
                                }
                            }
                            if(cellEntry.MapPokemon != null)
                            {
                                foreach(var pokemon in cellEntry.MapPokemon)
                                {
                                    Console.WriteLine("Found wild pokemon " + pokemon.PokedexTypeId + " at " + pokemon.Latitude + "/" + pokemon.Longitude);
                                }
                            }
                            if(cellEntry.NearbyPokemon != null)
                            {
                                foreach (var pokemon in cellEntry.NearbyPokemon)
                                {
                                    Console.WriteLine("Found nearby pokemon " + pokemon.PokedexNumber);
                                }
                            }
                            if(cellEntry.WildPokemon != null)
                            {
                                foreach (var pokemon in cellEntry.WildPokemon)
                                {
                                    Console.WriteLine("Found nearby pokemon " + pokemon.Pokemon + " at " + pokemon.Latitude + "/" + pokemon.Longitude + " " + pokemon.TimeTillHiddenMs + " ms left");
                                }
                            }
                        }
                    }
                }

                Thread.Sleep(1000 * 30);
            }
        }
    }
}
