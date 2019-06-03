﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Runtime.Serialization;
using SyncSaberService.Web;

namespace SyncSaberService.Data
{
    public class ScoreSaberSong
    {
        [JsonIgnore]
        public bool Populated { get; private set; }

        public ScoreSaberSong()
        {

        }

        public static bool TryParseScoreSaberSong(JToken token, ref ScoreSaberSong song)
        {
            string songName = token["name"]?.Value<string>();
            if (songName == null)
                songName = "";
            bool successful = true;
            try
            {
                song = token.ToObject<ScoreSaberSong>(new JsonSerializer() {
                    NullValueHandling = NullValueHandling.Ignore,
                    MissingMemberHandling = MissingMemberHandling.Ignore
                });
                //Logger.Debug(song.ToString());
            }
            catch (Exception ex)
            {
                Logger.Exception($"Unable to create a ScoreSaberSong from the JSON for {songName}\n", ex);
                successful = false;
                song = null;
            }
            return successful;
        }


        [JsonProperty("uid")]
        public string uid { get; set; }
        [JsonProperty("id")]
        public string md5Hash { get; set; }
        [JsonProperty("name")]
        private string name { get; set; }
        [JsonProperty("songSubName")]
        private string songSubName { get; set; }
        [JsonProperty("author")]
        private string author { get; set; }
        [JsonProperty("bpm")]
        private float bpm { get; set; }
        [JsonProperty("diff")]
        public string difficulty { get; set; }
        [JsonProperty("scores")]
        public string scores { get; set; }
        [JsonProperty("24hr")]
        public int hr24 { get; set; }
        [JsonProperty("ranked")]
        public bool ranked { get; set; }
        [JsonProperty("stars")]
        public float stars { get; set; }
        [JsonProperty("image")]
        public string image { get; set; }

        public SongInfo ToSongInfo()
        {
            if (!Populated)
            {
                Logger.Warning("Trying to create SongInfo from an unpopulated ScoreSaberSong");
                return null;
            }
            SongInfo song = ScrapedDataProvider.GetSongByHash(md5Hash, true);

            if (song == null)
            {
                Logger.Info($"Couldn't find song {name} by {author}, generating new song info...");
                song = new SongInfo() {
                    songName = name,
                    songSubName = songSubName,
                    authorName = author,
                    bpm = bpm,
                    hash = md5Hash
                };
            }
            song.ScoreSaberInfo = this;
            return song;
        }


        [JsonIgnore]
        public SongInfo Song
        {
            get
            {
                //this.PopulateFields();
                //return this as SongInfoEnhanced;
                return ToSongInfo();
            }
        }

        [OnDeserialized]
        protected void OnDeserialized(StreamingContext context)
        {
            //if (!(this is ScoreSaberSong))
            //if (!this.GetType().IsSubclassOf(typeof(SongInfo)))
            //{
                //Logger.Warning("SongInfo OnDeserialized");
                Populated = true;
        }
        /*
        public SongInfo GetSongInfo()
        {
            try
            {
                song = BeatSaverReader.Search(md5Hash, BeatSaverReader.SearchType.hash).FirstOrDefault();
            } catch (JsonException ex)
            {
                Logger.Exception("Error trying to get SongInfo from Beat Saver.", ex);
            }
            return song;
        }
        */
    }
}

/// 198	
//uid	8497
//id	"44C9544A577E5B8DC3876F9F696A7F92"
//name	"Redo"
//songSubName	"Suzuki Konomi"
//author	"Splake"
//bpm	190
//diff	"_Expert_SoloStandard"
//scores	"1,702"
//24hr	8
//ranked	1
//stars	3.03
//image	"/imports/images/songs/44C9544A577E5B8DC3876F9F696A7F92.png"
