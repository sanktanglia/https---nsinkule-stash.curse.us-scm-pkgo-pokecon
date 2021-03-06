﻿using CommandLineParser.Arguments;
using Geocoding.Google;
using Google.Common.Geometry;
using Google.Protobuf;
using Newtonsoft.Json.Linq;
using OtherPokeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static RequestEnvelop.Types;
using static ResponseEnvelop.Types;

namespace Pokecon
{
    // https://www.reddit.com/r/pokemongodev/
    // https://github.com/tejado/pokemongo-api-demo

    public class Program
    {
        const string API_URL = "https://pgorelease.nianticlabs.com/plfe/rpc";
        const string LOGIN_URL = "https://sso.pokemon.com/sso/login?service=https%3A%2F%2Fsso.pokemon.com%2Fsso%2Foauth2.0%2FcallbackAuthorize";
        const string LOGIN_OAUTH = "https://sso.pokemon.com/sso/oauth2.0/accessToken";

        static bool _debug = true;
        static double COORDS_LATITUDE = 38.8791981;
        static double COORDS_LONGITUDE = -76.9818437;
        static ulong COORDS_ALTITUDE = 0;

        static void set_location(string locationName)
        {
            var geocoder = new GoogleGeocoder() { };
            var loc = geocoder.Geocode(locationName).FirstOrDefault();
            Console.WriteLine("[!] Your given location: {0}", loc.FormattedAddress);
            Console.WriteLine("[!] lat/long/alt: {0} {1} {2}", loc.Coordinates.Latitude, loc.Coordinates.Longitude, 0);
            set_location_coords(loc.Coordinates.Latitude, loc.Coordinates.Longitude, 0);
        }

        static ulong f2i(double value) { return BitConverter.ToUInt64(BitConverter.GetBytes(value), 0); }

        static void set_location_coords(double latitude, double longitude, double alt)
        {
            COORDS_LATITUDE = latitude;
            COORDS_LONGITUDE = longitude;

            COORDS_ALTITUDE = f2i(alt);
        }

        static ResponseEnvelop api_req(string api_endpoint, string access_token, RequestEnvelop.Types.Requests[] reqs)
        {
            try
            {
                var envelop = new RequestEnvelop
                {
                    Unknown1 = 2,
                    RpcId = 8145806132888207460,
                    Latitude = COORDS_LATITUDE,
                    Longitude = COORDS_LONGITUDE,
                    Altitude = COORDS_ALTITUDE,
                    Unknown12 = 989,
                    Auth = new RequestEnvelop.Types.AuthInfo
                    {
                        Provider = "ptc",
                        Token = new RequestEnvelop.Types.AuthInfo.Types.JWT { Contents = access_token, Unknown13 = 59 },
                    }
                };
                foreach (var r in reqs)
                    envelop.Requests.Add(r);
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "Niantic App");
                    using (var ms = new MemoryStream())
                    {
                        envelop.WriteTo(ms);
                        ms.Position = 0;
                        var result = client.PostAsync(api_endpoint, new ByteArrayContent(ms.ToArray())).Result;
                        var r = result.Content.ReadAsByteArrayAsync().Result;
                        var ret = ResponseEnvelop.Parser.ParseFrom(r);
                        return ret;
                    }
                }
            }
            catch (Exception e)
            {
                if (_debug)
                    Console.WriteLine(e);
                return null;
            }
        }



        static string get_api_endpoint(string access_token)
        {
            four2stringauth authTest = new four2stringauth();
            authTest.Value = "4a2e9bc330dae60e7b74fc85b98868ab4700802e";

            var ret = api_req(API_URL, access_token, new[] {
                new RequestEnvelop.Types.Requests { Type = 2 },
                new RequestEnvelop.Types.Requests { Type = 126 },
                new RequestEnvelop.Types.Requests { Type = 4 },
                new RequestEnvelop.Types.Requests { Type = 129 },
                new RequestEnvelop.Types.Requests { Type = 5 },
            });
            try { return "https://" + ret.ApiUrl + "/rpc"; }
            catch { return null; }
        }

        static ResponseEnvelop get_profile(string api_endpoint, string access_token)
        {
            return api_req(api_endpoint, access_token, new[] { new RequestEnvelop.Types.Requests { Type = 2 } });
        }
        

        static string login_ptc(string username, string password)
        {
            Console.WriteLine("[!] login for: {0}", username);

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
                        new KeyValuePair<string, string>("username", username),
                        new KeyValuePair<string, string>("password", password),
                    };
                    var result = client.PostAsync(LOGIN_URL, new FormUrlEncodedContent(data)).Result;
                    if (result.Headers.Location == null)
                        return null;
                    var location = result.Headers.Location.ToString();
                    var r1 = result.Content.ReadAsStringAsync().Result;

                    string ticket = null;
                    try { ticket = new Regex(".*ticket=").Split(location)[1]; }
                    catch
                    {
                        if (_debug)
                            Console.WriteLine(r1);
                        return null;
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
                    return access_token;
                }
            }
        }

        public class Options
        {
            [ValueArgument(typeof(string), 'u', "username", Description = "PTC Username", Optional = false)]
            public string username { get; set; }
            [ValueArgument(typeof(string), 'p', "password", Description = "PTC Password", Optional = false)]
            public string password { get; set; }
            [ValueArgument(typeof(string), 'l', "location", Description = "Location", DefaultValue = "200 east force street valdosta ga")]
            public string location { get; set; }
            [ValueArgument(typeof(bool), 'd', "debug", Description = "Debug Mode", DefaultValue = true)]
            public bool debug { get; set; }
        }

        public static ResponseEnvelop GetGyms(string api_endpoint, string access_token)
        {
            four2stringgym fourString = new four2stringgym();

            S2CellId currentCell = S2CellId.FromLatLng(S2LatLng.FromDegrees(COORDS_LATITUDE, COORDS_LONGITUDE)).ParentForLevel(15);
            ulong cellID = currentCell.Id;

            fourString.Cells.Add(cellID);
            foreach(var sibling in currentCell.GetEdgeNeighbors())
            {
                fourString.Cells.Add(sibling.Id);
            }
            /*fourString.Cells.Add(9927817350306856960);
            fourString.Cells.Add(9927817361044275200);
            fourString.Cells.Add(9927817367486726144);
            fourString.Cells.Add(9927817352454340608);
            */
            fourString.Lat = COORDS_LATITUDE;
            fourString.Lon = COORDS_LONGITUDE;
            
            RequestEnvelop.Types.Requests request = new Requests();
            request.Type = 106;
            request.Message = fourString.ToByteString();
            return api_req(api_endpoint, access_token, new[] 
            {
                request
            });
        }

        static async void Execute(Client client)
        {
            var serverResponse = await client.GetServer();
            var profile = await client.GetProfile();
            var settings = await client.GetSettings();
            var mapObjects = await client.GetMapObjects();
            foreach(var entry in mapObjects.Payload)
            {
                foreach(var cell in entry.Profile)
                {
                    foreach(var stop in cell.Fort)
                    {
                        var fortDetails = await client.GetFort(stop.FortId, stop.Latitude, stop.Longitude);
                        Console.WriteLine(fortDetails.Payload[0].Name);
                        if(stop.FortId.Contains("9e530cd85faa44d5b1d4b6178e15524e.16"))
                        {

                        }
                    }

                }
            }
        }

        public static void Main(string[] args1)
        {
            var geocoder = new GoogleGeocoder() { };
            var loc = geocoder.Geocode("1700 Randolph Ave, Huntsville Al 35801").FirstOrDefault();
            Client client = new Client(loc.Coordinates.Latitude, loc.Coordinates.Longitude);
            client.LoginPtc("sanktanglia", "nic857462").Wait();
            Task.Run(() => Execute(client));
            System.Console.ReadLine();
            return;
            /*PokemonBot bot = new PokemonBot("sanktanglia", "nic857462", LoginMethod.PokemonTrainerClub, loc.Coordinates.Latitude, loc.Coordinates.Longitude);
            bot.Authenticate();
            Console.ReadLine();
            return;
            */

            var argsParser = new CommandLineParser.CommandLineParser();
            var args = new Options { };
            argsParser.ExtractArgumentAttributes(args);
            try { argsParser.ParseCommandLine(args1); }
            catch (Exception e) { Console.WriteLine(e.Message); return; }
            
            if (args.debug)
            {
                _debug = true;
                Console.WriteLine("[!] DEBUG mode on");
            }
            if (string.IsNullOrEmpty(args.username))
            {
                Console.WriteLine("[!] DEBUG mode on");
                return;
            }

            set_location(args.location);

            var access_token = login_ptc(args.username, args.password);
            if (access_token == null)
            {
                Console.WriteLine("[-] Wrong username/password");
                return;
            }
            Console.WriteLine("[+] RPC Session Token: {0} ...", access_token);

            var api_endpoint = get_api_endpoint(access_token);
            if (api_endpoint == null)
            {
                Console.WriteLine("[-] RPC server offline");
                return;
            }
            Console.WriteLine("[+] Received API endpoint: {0}", api_endpoint);

            var profile = get_profile(api_endpoint, access_token);
            if (profile != null)
            {
                Console.WriteLine("[+] Login successful");
                var profile2 = Profile.Parser.ParseFrom(profile.Payload[0].PayloadData);
                Console.WriteLine("[+] Username: {0}", profile2.Username);
                var creationTime = FromUnixTime(profile2.CreationTime);
                Console.WriteLine("[+] You are playing Pokemon Go since: {0}", creationTime);
                foreach (var curr in profile2.Currency)
                    Console.WriteLine("[+] {0}: {1}", curr.Type, curr.Amount);

                var gymTest = GetGyms(api_endpoint, access_token);
                var gymTest2 = GetGyms(api_endpoint, access_token);
            }
            else {
                Console.WriteLine("[-] Ooops...");
            }
        }



        public static DateTime FromUnixTime(long source)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(source);
        }
    }
}
